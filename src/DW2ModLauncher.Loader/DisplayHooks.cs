using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// Puts the game on the monitor and video card the player chose in the launcher (manifest DisplayRect and DisplayAdapter), in true pixels, and keeps it there.
    /// Measured on 2026-11-11 at 125% scale (docs/Display Choice.md in the launcher repo):
    /// - The game asks Windows for the PRIMARY monitor's size whatever monitor its window is on (WindowsHelpers.GetCurrentDisplayMode) and sizes its window
    ///   from the answer, so a window on a second monitor is stretched to the main monitor's size.
    /// - Each time the display is set up (DWGame.SetDisplay: at start and on a settings apply) a borderless/fullscreen window is moved to 0,0, the main monitor's corner.
    /// - The game's window is created and shown on the main monitor, which shows as a flash there before it moves.
    /// - The game picks its video card by name but Stride creates the device on the first card regardless.
    /// So: the size question is answered with the chosen monitor's size, a move to 0,0 becomes a move to the chosen monitor's corner, the window is created on the
    /// chosen rectangle, and the graphics device is created on the chosen card. Everything works by reflection because this DLL is built without the game's assemblies.
    /// All native calls run with the thread switched to per-monitor-v2 DPI awareness, so every number is a real pixel whatever the Windows scale.
    /// </summary>
    public static class DisplayHooks
    {
        private static int _x, _y, _width, _height, _refresh;
        private static string _adapter;
        private static Action<string> _log;
        private static string _logPath;
        private static bool _shown;
        private static object _chosenAdapter;

        /// <summary>Installs the hooks the manifest asks for. Returns false (and installs nothing) when it asks for none.</summary>
        public static bool Install(LoaderManifest manifest, Action<string> log, string logPath)
        {
            if (!OperatingSystem.IsWindows() || manifest == null) return false;
            _log = log;
            _logPath = logPath;
            _adapter = (manifest.DisplayAdapter ?? "").Trim();
            _refresh = manifest.DisplayRefresh;
            if (!ParseRect(manifest.DisplayRect, out _x, out _y, out _width, out _height) && _adapter.Length == 0) return false;
            bool placed = _width > 0;

            Harmony harmony = new Harmony("dw2modlauncher.loader.display");
            if (placed)
            {
                Patch(harmony, "the display-mode question", Type.GetType("WindowsHelpers, DistantWorlds.Core", false), "GetCurrentDisplayMode",
                    new[] { typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }, postfix: nameof(ModePostfix));
                Type settings = Type.GetType("DistantWorlds.Types.GameSettings, DistantWorlds.Types", false);
                Patch(harmony, "the stored resolution", settings, "CheckApplyOverrides", null, postfix: nameof(SettingsLoadedPostfix));
                Patch(harmony, "the stored resolution's save", settings, "WriteToFile", null, prefix: nameof(SettingsSavePrefix), postfix: nameof(SettingsSavedPostfix));
                Type window = Type.GetType("Stride.Games.GameWindowWinforms, Stride.Games", false);
                Patch(harmony, "the window's move to 0,0", window, "set_Position", null, prefix: nameof(PositionPrefix));
                Patch(harmony, "the window's first show", window, "set_Visible", null, prefix: nameof(VisiblePrefix));
                Type form = Type.GetType("System.Windows.Forms.Form, System.Windows.Forms", false);
                Patch(harmony, "the window's creation", form, "get_CreateParams", null, postfix: nameof(CreateParamsPostfix));
                PatchConstructors(harmony, "the window's construction", Type.GetType("Stride.Games.GameForm, Stride.Games", false), nameof(ConstructorPrefix));
                _log("Display: the game opens on " + _x + "," + _y + " " + _width + "x" + _height + (_refresh > 0 ? " at " + _refresh + " Hz" : "") + (_adapter.Length > 0 ? " on " + _adapter : "") + ".");
            }
            if (_adapter.Length > 0)
                Patch(harmony, "the graphics device's card", Type.GetType("Stride.Games.GraphicsDeviceManager, Stride.Games", false), "CreateDevice", null, prefix: nameof(CreateDevicePrefix));
            return true;
        }

        private static bool ParseRect(string text, out int x, out int y, out int width, out int height)
        {
            x = y = width = height = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] part = text.Split(',');
            return part.Length == 4 && int.TryParse(part[0], out x) && int.TryParse(part[1], out y) && int.TryParse(part[2], out width) && int.TryParse(part[3], out height) && width > 0 && height > 0;
        }

        // One missing method never stops the others, and is reported on the status line and in the log.
        private static void Patch(Harmony harmony, string what, Type type, string method, Type[] parameters, string prefix = null, string postfix = null)
        {
            try
            {
                MethodBase target = type == null ? null : parameters == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, parameters);
                if (target == null)
                {
                    Report("display" + method, "Display: " + what + " could not be hooked: " + (type == null ? "the type" : type.FullName + "." + method) + " was not found in this game version.");
                    return;
                }
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(DisplayHooks).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(DisplayHooks).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                Report("display" + method, "Display: " + what + " could not be hooked: " + ex.Message);
            }
        }

        private static void Report(string key, string text)
        {
            _log("ERROR " + text);
            HookStatus.Failed(key, StatusStrings.Get("WidgetChannelProblem", ("Problem", text)), _logPath);
        }

        // The screen-mode question is answered with the chosen monitor's own size and refresh rate, never the main monitor's.
        private static void ModePostfix(ref int width, ref int height, ref int refreshRateNum, ref int refreshRateDenom)
        {
            width = _width;
            height = _height;
            if (_refresh > 0)
            {
                refreshRateNum = _refresh;
                refreshRateDenom = 1;
            }
        }

        // The game's stored resolution belongs to whichever monitor it was last set on, so on the chosen monitor the game starts at that monitor's own
        // resolution. The stored values are swapped back for the save (unless the player changed them in game), so the main monitor's choice is not lost.
        private static int _storedWidth, _storedHeight;
        private static bool _swappedForSave;

        private static void SettingsLoadedPostfix(object __instance)
        {
            try
            {
                Type type = __instance.GetType();
                FieldInfo width = type.GetField("GraphicsResolutionWidth");
                FieldInfo height = type.GetField("GraphicsResolutionHeight");
                _storedWidth = (int)width.GetValue(__instance);
                _storedHeight = (int)height.GetValue(__instance);
                width.SetValue(__instance, _width);
                height.SetValue(__instance, _height);
                if (_storedWidth != _width || _storedHeight != _height)
                    _log("Display: the game's stored resolution " + _storedWidth + "x" + _storedHeight + " is replaced by the chosen monitor's " + _width + "x" + _height + ".");
            }
            catch (Exception ex)
            {
                Report("displayresolution", "Display: the stored resolution could not be set to the chosen monitor's: " + ex.Message);
            }
        }

        private static void SettingsSavePrefix(object gameSettings)
        {
            try
            {
                if (gameSettings == null || _storedWidth <= 0) return;
                Type type = gameSettings.GetType();
                FieldInfo width = type.GetField("GraphicsResolutionWidth");
                FieldInfo height = type.GetField("GraphicsResolutionHeight");
                if ((int)width.GetValue(gameSettings) != _width || (int)height.GetValue(gameSettings) != _height) return;
                width.SetValue(gameSettings, _storedWidth);
                height.SetValue(gameSettings, _storedHeight);
                _swappedForSave = true;
            }
            catch (Exception ex)
            {
                Report("displayresolutionsave", "Display: the main stored resolution could not be kept in the save: " + ex.Message);
            }
        }

        private static void SettingsSavedPostfix(object gameSettings)
        {
            if (!_swappedForSave) return;
            _swappedForSave = false;
            Type type = gameSettings.GetType();
            type.GetField("GraphicsResolutionWidth").SetValue(gameSettings, _width);
            type.GetField("GraphicsResolutionHeight").SetValue(gameSettings, _height);
        }

        // A move to (0,0) is a move to the corner of the chosen monitor. Done natively in true pixels, so the system DPI scale cannot change the numbers.
        private static bool PositionPrefix(object __instance, object[] __args)
        {
            try
            {
                object value = __args[0];
                int x = Convert.ToInt32(value.GetType().GetField("X").GetValue(value));
                int y = Convert.ToInt32(value.GetType().GetField("Y").GetValue(value));
                if (x != 0 || y != 0) return true;
                IntPtr handle = HandleOf(__instance);
                if (handle == IntPtr.Zero) return true;
                Place(handle, true, false);
                return false;
            }
            catch (Exception ex)
            {
                Report("displayposition", "Display: the window's move to 0,0 could not be redirected: " + ex.Message);
                return true;
            }
        }

        // The first time the window is shown it is put on the chosen rectangle first.
        private static void VisiblePrefix(object __instance, bool __0)
        {
            if (!__0 || _shown) return;
            try
            {
                IntPtr handle = HandleOf(__instance);
                if (handle == IntPtr.Zero) return;
                _shown = true;
                Place(handle, true, true);
                _log("Display: the window was placed before it was first shown.");
            }
            catch (Exception ex)
            {
                Report("displayshow", "Display: the window could not be placed before it was shown: " + ex.Message);
            }
        }

        // The window is created on the chosen rectangle, so it never exists anywhere else.
        private static void CreateParamsPostfix(object __instance, object __result)
        {
            if (__result == null || __instance == null || __instance.GetType().Name != "GameForm") return;
            Type type = __result.GetType();
            type.GetProperty("X").SetValue(__result, _x);
            type.GetProperty("Y").SetValue(__result, _y);
            type.GetProperty("Width").SetValue(__result, _width);
            type.GetProperty("Height").SetValue(__result, _height);
        }

        // The game is system-DPI-aware, so on a monitor whose Windows scale differs from the main monitor's, Windows rescales the window's size and position
        // calls, the game's first UI layout is made for the wrong size and the right-hand elements never move. The process awareness cannot be changed any more,
        // but WinForms takes a control's DPI context when it is constructed and a window takes the thread's awareness at creation, so the thread that makes the
        // game's window becomes per-monitor-v2 before the form is constructed (measured: switching later, at CreateParams or CreateHandle, had no effect).
        // Then window, size reads and input are all in real pixels. Only the window's own thread needs it.
        private static bool _threadAware;

        // WinForms takes a control's DPI context when the control is constructed and applies it when the window is made, so the switch has to come first.
        private static void ConstructorPrefix()
        {
            MakeWindowThreadPerMonitor();
        }

        private static void PatchConstructors(Harmony harmony, string what, Type type, string prefix)
        {
            try
            {
                if (type == null)
                {
                    Report("displayctor", "Display: " + what + " could not be hooked: the type was not found in this game version.");
                    return;
                }
                foreach (ConstructorInfo constructor in AccessTools.GetDeclaredConstructors(type, false))
                    harmony.Patch(constructor, prefix: new HarmonyMethod(typeof(DisplayHooks).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                Report("displayctor", "Display: " + what + " could not be hooked: " + ex.Message);
            }
        }

        private static void MakeWindowThreadPerMonitor()
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4));
                if (!_threadAware) _log("Display: the window's thread is per-monitor-v2 DPI aware.");
                _threadAware = true;
            }
            catch (Exception ex)
            {
                Report("displaydpi", "Display: the window's thread could not be made per-monitor DPI aware: " + ex.Message);
            }
        }

        // Stride creates the device on the first card whatever the game asks for, so the card is replaced in the information it is given.
        private static void CreateDevicePrefix(object[] __args)
        {
            try
            {
                object info = __args.FirstOrDefault(a => a != null && a.GetType().Name == "GraphicsDeviceInformation");
                if (info == null) return;
                PropertyInfo adapterProperty = info.GetType().GetProperty("Adapter");
                if (_chosenAdapter == null)
                {
                    Type factory = Type.GetType("Stride.Graphics.GraphicsAdapterFactory, Stride.Graphics", true);
                    foreach (object candidate in (System.Collections.IEnumerable)factory.GetProperty("Adapters").GetValue(null))
                    {
                        string description = candidate?.GetType().GetProperty("Description")?.GetValue(candidate) as string;
                        if (description != null && description.IndexOf(_adapter, StringComparison.OrdinalIgnoreCase) >= 0) { _chosenAdapter = candidate; break; }
                    }
                    if (_chosenAdapter == null)
                    {
                        Report("displaycard", "Display: no video card named \"" + _adapter + "\" was found, so the game uses its own choice.");
                        _adapter = "";
                        return;
                    }
                }
                if (!ReferenceEquals(adapterProperty.GetValue(info), _chosenAdapter))
                {
                    _log("Display: the graphics device is created on " + _adapter + ".");
                    adapterProperty.SetValue(info, _chosenAdapter);
                }
            }
            catch (Exception ex)
            {
                Report("displaycard", "Display: the video card could not be chosen: " + ex.Message);
            }
        }

        private static IntPtr HandleOf(object window)
        {
            object native = window.GetType().GetProperty("NativeWindow")?.GetValue(window);
            object handle = native?.GetType().GetProperty("Handle")?.GetValue(native);
            return handle is IntPtr pointer ? pointer : IntPtr.Zero;
        }

        private static void Place(IntPtr handle, bool move, bool size)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            try
            {
                uint flags = SwpNoZOrder | SwpNoActivate | (move ? 0 : SwpNoMove) | (size ? 0 : SwpNoSize);
                SetWindowPos(handle, IntPtr.Zero, _x, _y, _width, _height, flags);
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }

        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
