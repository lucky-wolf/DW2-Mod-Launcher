using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace DW2ModLauncher.Loader
{
    // The in-game status line and its expandable panel.
    //
    // One line of text in the lower left of the screen summarises every code mod (StatusText.Line); clicking it opens a dialog: the
    // screen is dimmed, and a panel above the line shows one block per mod. The dim layer and the blocks take the clicks, so nothing
    // underneath is reachable while it is open, and any click closes it. The widgets are the game's own DWButton controls registered as custom controls, so they are drawn with
    // the rest of the interface and hide with it.
    //
    // The whole thing runs on reflection. The loader is built on CI machines that do not have the game installed, so it cannot be
    // compiled against the game's assemblies; every game type, field and method is looked up by name once in Bind, and anything
    // missing throws there (one line in dw2modlauncher.log) instead of failing later on the render thread. A failed install changes nothing
    // for the mods or the game: the registry and dw2modlauncher.log still record everything.
    //
    // The game clears its custom controls whenever the interface is rebuilt (scaling change, new game), so a postfix on the
    // per-frame UserInterfaceController.Update adds the controls back whenever they are missing and keeps text and position current.
    internal static partial class StatusWidget
    {
        const string LineName = "DW2ML_StatusLine";
        const string RowPrefix = "DW2ML_StatusRow_";
        const string BackdropName = "DW2ML_StatusBackdrop";
        const float IndentX = 10f;
        const long RefreshMs = 500;

        static Action<string> _log;
        static Game _g;
        static object _line;
        static object _backdrop;
        static bool _wasExpanded;
        static readonly List<object> _rows = new List<object>();
        static bool _expanded;
        static long _shownRevision = -1;
        static long _lastRefresh;
        static int _lastWidth;
        static int _lastHeight;
        static bool _reported;

        public static void Install(Action<string> log)
        {
            _log = log;
            _g = Game.Bind();

            Assembly harmonyAssembly = Assembly.Load("0Harmony");
            Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true);
            Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true);

            MethodInfo target = _g.Controller.GetMethod("Update", BindingFlags.Public | BindingFlags.Static);
            if (target == null) throw new MissingMethodException("UserInterfaceController.Update not found");
            MethodInfo postfix = typeof(StatusWidget).GetMethod(nameof(UpdatePostfix), BindingFlags.Public | BindingFlags.Static);

            object harmony = Activator.CreateInstance(harmonyType, "DW2ModLauncher.Loader.StatusWidget");
            object postfixMethod = Activator.CreateInstance(harmonyMethodType, new object[] { postfix });

            // Patch(MethodBase original, HarmonyMethod prefix, postfix, transpiler, finalizer): the trailing parameters differ by Harmony
            // version, so fill the postfix by name and leave the rest null.
            MethodInfo patch = null;
            foreach (MethodInfo m in harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "Patch" && ps.Length > 0 && ps[0].ParameterType == typeof(MethodBase))
                {
                    patch = m;
                    break;
                }
            }
            if (patch == null) throw new MissingMethodException("Harmony.Patch not found");
            ParameterInfo[] parameters = patch.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "postfix") args[i] = postfixMethod;
            }
            args[0] = target;
            patch.Invoke(harmony, args);
            _log("OK status widget: hooked UserInterfaceController.Update.");
        }

        // Harmony fills the two ints from Update's own parameters of the same names.
        public static void UpdatePostfix(int screenWidth, int screenHeight)
        {
            if (_g == null) return;
            try
            {
                if (_line == null) _line = NewButton(LineName, Kind.Line);
                if (_backdrop == null) _backdrop = NewButton(BackdropName, Kind.Backdrop);

                StatusRegistry registry = ModStatus.Registry;
                long now = Environment.TickCount64;
                if (registry.Revision != _shownRevision || now - _lastRefresh >= RefreshMs
                    || screenWidth != _lastWidth || screenHeight != _lastHeight)
                {
                    _shownRevision = registry.Revision;
                    _lastRefresh = now;
                    _lastWidth = screenWidth;
                    _lastHeight = screenHeight;
                    Refresh(registry.Snapshot(), screenWidth, screenHeight);
                }

                // The dialog's controls exist only while it is open. Controls are drawn in the order they were added, so opening it
                // takes the line out first and puts it back last: dim layer, blocks, then the line on top.
                if (_expanded != _wasExpanded)
                {
                    _wasExpanded = _expanded;
                    EnsureAbsent(_line);
                }
                if (_expanded)
                {
                    EnsurePresent(_backdrop);
                    foreach (object row in _rows) EnsurePresent(row);
                }
                else
                {
                    EnsureAbsent(_backdrop);
                    foreach (object row in _rows) EnsureAbsent(row);
                }
                EnsurePresent(_line);

                MenuTick(screenWidth, screenHeight);
            }
            catch (Exception ex)
            {
                // never throw into the game's frame loop; report once and go quiet
                if (!_reported)
                {
                    _reported = true;
                    _log("ERROR status widget stopped: " + ex);
                }
                _g = null;
            }
        }

        static void Refresh(List<ModStatusSnapshot> mods, int screenWidth, int screenHeight)
        {
            Game g = _g;
            StatusLevel worst = StatusText.Worst(mods);

            string marker = _expanded ? "[-]" : "[+]";
            string text = marker + (worst == StatusLevel.Error ? " /!\\ " : " ") + StatusText.Line(mods);
            object color = worst == StatusLevel.Error ? g.Bad : worst == StatusLevel.Warn ? g.Warn : g.Dim;

            float lineTop = PlaceAboveEdge(_line, text, color, screenWidth * 0.8f, 0f, screenHeight - 1f);

            if (_expanded)
            {
                g.SetSizeAndPosition.Invoke(_backdrop, new object[] { g.Vec2(screenWidth, screenHeight), g.Vec2(0f, 0f) });
            }

            if (!_expanded)
            {
                return;
            }

            List<PanelRow> panel = StatusText.Rows(mods);
            panel.Insert(0, new PanelRow { Level = StatusLevel.Ok, Text = "DW2 Mod Launcher: " + mods.Count + " code mod" + (mods.Count == 1 ? "" : "s") + " loaded. Click anywhere to close." });
            if (_rows.Count != panel.Count)
            {
                foreach (object row in _rows) EnsureAbsent(row);
                _rows.Clear();
                for (int i = 0; i < panel.Count; i++) _rows.Add(NewButton(RowPrefix + i, Kind.Row));
            }

            // one common width so the blocks line up; wrap at 60% of the screen
            float maxWidth = screenWidth * 0.6f;
            float width = 0f;
            foreach (PanelRow row in panel)
            {
                width = Math.Max(width, g.MeasureWidth(row.Text, maxWidth));
            }

            // blocks are stacked with no gaps; the first (worst) is at the top and the last sits directly on the line
            float bottom = lineTop;
            for (int i = panel.Count - 1; i >= 0; i--)
            {
                object rowColor = panel[i].Level == StatusLevel.Error ? g.Bad : panel[i].Level == StatusLevel.Warn ? g.Warn : g.Dim;
                bottom = PlaceAboveEdge(_rows[i], panel[i].Text, rowColor, maxWidth, width, bottom);
            }
        }

        // Sets text and colour and puts the control's bottom edge at bottomY, IndentX in from the left of the screen. Returns the top edge.
        // fixedWidth 0 = fit the text.
        static float PlaceAboveEdge(object button, string text, object color, float maxWidth, float fixedWidth, float bottomY)
        {
            Game g = _g;
            float margin = g.Margin.Get<float>(button);
            g.MeasureSize(text, maxWidth, out float textWidth, out float textHeight);
            float w = (fixedWidth > 0f ? fixedWidth : textWidth) + margin * 4f + 8f;
            float h = textHeight + margin * 2f + 4f;
            if (!string.Equals((string)g.Text.Get(button), text, StringComparison.Ordinal))
            {
                g.Text.Set(button, text);
            }
            g.ForeColor.Set(button, color);
            g.ButtonColor.Set(button, g.PanelBack);
            g.ButtonColorHover.Set(button, g.PanelBackHover);
            float top = bottomY - h;
            g.SetSizeAndPosition.Invoke(button, new object[] { g.Vec2(w, h), g.Vec2(IndentX, top) });
            return top;
        }

        enum Kind { Line, Row, Backdrop }

        static object NewButton(string name, Kind kind)
        {
            Game g = _g;
            object b = Activator.CreateInstance(g.Button);
            g.Name.Set(b, name);
            g.Visible.Set(b, true);
            g.PostScene.Set(b, true);
            // above the game's own interface, including its Game Menu panel (layer 22000) and the Mods dialog (22500), which the status
            // dialog can be opened from: dim layer under the blocks under the line.
            g.Layer.Set(b, kind == Kind.Backdrop ? 23000f : kind == Kind.Row ? 23001f : 23002f);
            g.ClipToRegion.Set(b, false);
            g.Margin.Set(b, 3f);
            g.TextAlignment.Set(b, g.AlignLeft);
            g.Initialize.Invoke(b, new object[] { g.FontSmall, g.FontSmall, null });
            // the line is bare text (a backing shows only while hovered); blocks have a dark backing so text stays readable
            g.ShowTexture.Set(b, kind != Kind.Line);
            g.ShowTextureHovered.Set(b, true);
            switch (kind)
            {
                case Kind.Backdrop:
                    g.ButtonColor.Set(b, g.DimLayer);
                    g.ButtonColorHover.Set(b, g.DimLayer);
                    break;
                case Kind.Row:
                    g.ButtonColor.Set(b, g.PanelBack);
                    g.ButtonColorHover.Set(b, g.PanelBackHover);
                    break;
                default:
                    g.ButtonColorHover.Set(b, g.PanelBackHover);
                    break;
            }
            g.ClickEvent.Set(b, Delegate.CreateDelegate(g.ClickEvent.Type, typeof(StatusWidget).GetMethod(nameof(OnClick), BindingFlags.NonPublic | BindingFlags.Static)));
            return b;
        }

        // A click on the line toggles the dialog and a click anywhere else on it closes it; the next frame's Refresh lays it out.
        static void OnClick(object sender, object args)
        {
            string name = sender == null ? null : (string)_g?.Name.Get(sender);
            if (name != null && name.StartsWith(ModsPrefix, StringComparison.Ordinal))
            {
                OnMenuClick(name);
                return;
            }
            _expanded = !_expanded;
            _shownRevision = -1;
        }

        static bool IsPresent(object control)
        {
            Game g = _g;
            string name = (string)g.Name.Get(control);
            lock (g.CustomControlLock.GetStatic())
            {
                return ((IDictionary)g.CustomControls.GetStatic()).Contains(name);
            }
        }

        static void EnsurePresent(object control)
        {
            if (!IsPresent(control)) _g.AddCustomControl.Invoke(null, new[] { control });
        }

        static void EnsureAbsent(object control)
        {
            if (IsPresent(control)) _g.RemoveCustomControl.Invoke(null, new[] { control });
        }

        // A field or property looked up by name, so callers do not care which one the game declares.
        sealed class Slot
        {
            readonly FieldInfo _field;
            readonly PropertyInfo _property;

            Slot(FieldInfo f, PropertyInfo p)
            {
                _field = f;
                _property = p;
            }

            public Type Type => _field != null ? _field.FieldType : _property.PropertyType;

            public static Slot Find(Type owner, string name, BindingFlags scope)
            {
                FieldInfo f = owner.GetField(name, scope);
                if (f != null) return new Slot(f, null);
                PropertyInfo p = owner.GetProperty(name, scope);
                if (p != null) return new Slot(null, p);
                throw new MissingMemberException(owner.Name + "." + name + " not found");
            }

            public object Get(object o) => _field != null ? _field.GetValue(o) : _property.GetValue(o);

            public T Get<T>(object o) => (T)Convert.ChangeType(Get(o), typeof(T));

            public object GetStatic() => Get(null);

            public void Set(object o, object value)
            {
                if (_field != null) _field.SetValue(o, value); else _property.SetValue(o, value);
            }
        }

        // Every game member the widget touches, resolved once.
        sealed class Game
        {
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            const BindingFlags Stat = BindingFlags.Public | BindingFlags.Static;

            public Type Button;
            public Type Renderer;
            public Slot GameMenuPanel;
            Slot _themeText, _themeBack, _themeBackHover;
            public Type Controller;
            public Slot Name, Visible, PostScene, Layer, ClipToRegion, Margin, TextAlignment, ForeColor, Text;
            public Slot Size, Position, Enabled;
            public object FontNormalSize => Enum.Parse(Initialize.GetParameters()[0].ParameterType, "Normal");
            public object FontLargeSize => Enum.Parse(Initialize.GetParameters()[0].ParameterType, "Large");
            public Slot ShowTexture, ShowTextureHovered, ButtonColor, ButtonColorHover, ClickEvent, FontNormal;
            public Slot CustomControls, CustomControlLock;
            public MethodInfo Initialize, SetSizeAndPosition, AddCustomControl, RemoveCustomControl, Measure;
            public object FontSmall, AlignLeft;
            public object Warn, Bad, DimLayer;

            // the game's current theme, read each time so a changed colour theme shows up without a restart
            public object Dim => _themeText.GetStatic();
            public object PanelBack => _themeBack.GetStatic();
            public object PanelBackHover => _themeBackHover.GetStatic();

            Type _vec2;
            Slot _vx, _vy;
            ConstructorInfo _vec2Ctor;
            Func<int, int, int, int, object> _color;
            object _measureFont;

            public static Game Bind()
            {
                var g = new Game();
                Assembly ui = Assembly.Load("DistantWorlds.UI");
                Assembly types = Assembly.Load("DistantWorlds.Types");

                g.Button = ui.GetType("DistantWorlds.UI.DWButton", throwOnError: true);
                g.Controller = ui.GetType("DistantWorlds.UI.UserInterfaceController", throwOnError: true);
                Type drawing = types.GetType("DistantWorlds.Types.DrawingHelper", throwOnError: true);

                g.Name = Slot.Find(g.Button, "Name", Inst);
                g.Size = Slot.Find(g.Button, "Size", Inst);
                g.Position = Slot.Find(g.Button, "Position", Inst);
                g.Enabled = Slot.Find(g.Button, "Enabled", Inst);
                g.Renderer = FindType("DistantWorlds2.DWRendererBase");
                g.GameMenuPanel = Slot.Find(g.Renderer, "GameMenuPanel", Stat);
                g.Visible = Slot.Find(g.Button, "Visible", Inst);
                g.PostScene = Slot.Find(g.Button, "PostScene", Inst);
                g.Layer = Slot.Find(g.Button, "Layer", Inst);
                g.ClipToRegion = Slot.Find(g.Button, "ClipToRegion", Inst);
                g.Margin = Slot.Find(g.Button, "Margin", Inst);
                g.TextAlignment = Slot.Find(g.Button, "TextAlignment", Inst);
                g.ForeColor = Slot.Find(g.Button, "ForeColor", Inst);
                g.Text = Slot.Find(g.Button, "Text", Inst);
                g.ShowTexture = Slot.Find(g.Button, "ShowTexture", Inst);
                g.ShowTextureHovered = Slot.Find(g.Button, "ShowTextureHovered", Inst);
                g.ButtonColor = Slot.Find(g.Button, "ButtonColor", Inst);
                g.ButtonColorHover = Slot.Find(g.Button, "ButtonColorHover", Inst);
                g.ClickEvent = Slot.Find(g.Button, "ClickEvent", Inst);
                g.FontNormal = Slot.Find(g.Button, "FontNormal", Inst);
                g.CustomControls = Slot.Find(g.Controller, "CustomControls", Stat);
                g.CustomControlLock = Slot.Find(g.Controller, "CustomControlLock", Stat);

                g.SetSizeAndPosition = RequireMethod(g.Button, "SetSizeAndPosition", 2);
                g.Initialize = RequireMethod(g.Button, "Initialize", 3);
                g.AddCustomControl = g.Controller.GetMethod("AddCustomControl", Stat) ?? throw new MissingMethodException("AddCustomControl");
                g.RemoveCustomControl = g.Controller.GetMethod("RemoveCustomControl", Stat) ?? throw new MissingMethodException("RemoveCustomControl");

                // enums and structs are taken from the members that use them, so no assembly or namespace is hard-coded
                g.AlignLeft = Enum.Parse(g.TextAlignment.Type, "Left");
                g.FontSmall = Enum.Parse(g.Initialize.GetParameters()[0].ParameterType, "Small");
                g.BindStructs(g.ForeColor.Type, RequireField(g.Button, "Position").FieldType);

                // Measure(string, SpriteFont, float) -> Vector2
                foreach (MethodInfo m in drawing.GetMethods(Stat))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (m.Name == "MeasureStringDropShadowWordWrapWithSize" && ps.Length == 3 && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == g.FontNormal.Type && ps[2].ParameterType == typeof(float))
                    {
                        g.Measure = m;
                        break;
                    }
                }
                if (g.Measure == null) throw new MissingMethodException("DrawingHelper.MeasureStringDropShadowWordWrapWithSize(string, SpriteFont, float)");

                g.Warn = g._color(240, 190, 80, 255);
                g.Bad = g._color(255, 90, 90, 255);
                Type colors = types.GetType("DistantWorlds.Types.ColorHelper", throwOnError: true);
                g._themeText = Slot.Find(colors, "ForeColor", Stat);
                g._themeBack = Slot.Find(colors, "ButtonColor", Stat);
                g._themeBackHover = Slot.Find(colors, "ButtonColorHover", Stat);
                g.DimLayer = g._color(0, 0, 0, 150);
                return g;
            }

            void BindStructs(Type colorType, Type vectorType)
            {
                _vec2 = vectorType;
                _vec2Ctor = vectorType.GetConstructor(new[] { typeof(float), typeof(float) }) ?? throw new MissingMethodException("Vector2(float, float)");
                _vx = Slot.Find(vectorType, "X", BindingFlags.Public | BindingFlags.Instance);
                _vy = Slot.Find(vectorType, "Y", BindingFlags.Public | BindingFlags.Instance);

                ConstructorInfo byteCtor = colorType.GetConstructor(new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
                ConstructorInfo floatCtor = colorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
                if (byteCtor != null)
                {
                    _color = (r, gr, b, a) => byteCtor.Invoke(new object[] { (byte)r, (byte)gr, (byte)b, (byte)a });
                }
                else if (floatCtor != null)
                {
                    _color = (r, gr, b, a) => floatCtor.Invoke(new object[] { r / 255f, gr / 255f, b / 255f, a / 255f });
                }
                else
                {
                    throw new MissingMethodException("Color(byte, byte, byte, byte)");
                }
            }

            public float VecX(object v) => _vx.Get<float>(v);

            public float VecY(object v) => _vy.Get<float>(v);

            static Type FindType(string fullName)
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType(fullName);
                    if (t != null) return t;
                }
                throw new TypeLoadException(fullName + " not found in any loaded assembly");
            }

            public object Vec2(float x, float y) => _vec2Ctor.Invoke(new object[] { x, y });

            // the font is the one the buttons use (small), read from a live control the first time it is needed
            public void MeasureSize(string text, float maxWidth, out float width, out float height)
            {
                object size = Measure.Invoke(null, new[] { text, MeasureFont(), (object)maxWidth });
                width = _vx.Get<float>(size);
                height = _vy.Get<float>(size);
            }

            public float MeasureWidth(string text, float maxWidth)
            {
                MeasureSize(text, maxWidth, out float w, out _);
                return w;
            }

            object MeasureFont()
            {
                if (_measureFont == null)
                {
                    // a throwaway button initialised with the same font size as the real ones
                    object probe = Activator.CreateInstance(Button);
                    Initialize.Invoke(probe, new object[] { FontSmall, FontSmall, null });
                    _measureFont = FontNormal.Get(probe);
                }
                return _measureFont;
            }

            static MethodInfo RequireMethod(Type owner, string name, int parameterCount)
            {
                foreach (MethodInfo m in owner.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == name && m.GetParameters().Length == parameterCount) return m;
                }
                throw new MissingMethodException(owner.Name + "." + name + "(" + parameterCount + " parameters) not found");
            }

            static FieldInfo RequireField(Type owner, string name)
                => owner.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                   ?? throw new MissingFieldException(owner.Name + "." + name);
        }
    }
}
