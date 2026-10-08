using System;
using System.Collections.Generic;
using System.Reflection;

namespace DW2ModLauncher.Loader
{
    // The Mods item on the game's Game Menu (the Esc panel) and the dialog it opens (docs/Mod Menu.md).
    //
    // The game's panel is a fixed list of hand-placed buttons with no extension point, so each frame (from the status widget's Update
    // postfix) while the panel is visible this adds two real game buttons to it: Mods, over the Random Seed label (which is hidden),
    // and a Random Seed button below it that copies the seed to the clipboard. The panel is made one row taller to fit. Mods opens a
    // modal Dialog (the game's own class, used for Settings) with one button per entry, centered like Settings.
    //
    // Like the status widget this is all reflection, and everything it needs is looked up once in MenuGame.Bind; a failure there
    // logs one line and leaves only this feature out.
    internal static partial class StatusWidget
    {
        const string ModsPrefix = "DW2ML_Mods";
        const string ModsButtonName = ModsPrefix + "Button";
        const string SeedButtonName = ModsPrefix + "SeedButton";
        const string ModsCloseName = ModsPrefix + "Close";
        const string ModsItemPrefix = ModsPrefix + "Item_";
        const long CopiedMs = 2000;

        static MenuGame _m;
        static bool _menuFailed;
        static bool _launcherEntryAdded;
        static object _menuPanel;
        static object _modsButton;
        static object _seedButton;
        static object _dialog;
        static List<MenuEntrySnapshot> _menuShown = new List<MenuEntrySnapshot>();
        static string _seedText;
        static long _copiedAt;
        static float _grownFor;

        // Called every frame from the Update postfix; cheap unless the Game Menu is up.
        static void MenuTick(int screenWidth, int screenHeight)
        {
            if (_menuFailed) return;
            try
            {
                if (_m == null) _m = MenuGame.Bind(_g);
                if (ModMenu.TakeShowRequest() && _g.GameMenuPanel.GetStatic() is object open && _g.Visible.Get<bool>(open) && _dialog == null)
                {
                    OpenDialog();
                }
                if (!_launcherEntryAdded)
                {
                    _launcherEntryAdded = true;
                    ModMenu.Registry.Add(MenuRegistry.LauncherId, "DW2 Mod Launcher", OpenStatusFromMenu);
                }

                object panel = _g.GameMenuPanel.GetStatic();
                if (panel == null || !_g.Visible.Get<bool>(panel))
                {
                    CloseDialog();
                    return;
                }
                if (!ReferenceEquals(panel, _menuPanel)) AttachToPanel(panel);
                LayoutPanel(panel);
            }
            catch (Exception ex)
            {
                _menuFailed = true;
                _log("ERROR mods menu stopped: " + ex);
            }
        }

        static void AttachToPanel(object panel)
        {
            MenuGame m = _m;
            _menuPanel = panel;
            _grownFor = 0f;
            _seedText = null;
            _modsButton = m.NewButton(ModsButtonName, "Mods");
            _seedButton = m.NewButton(SeedButtonName, "");
            m.AddControl.Invoke(panel, new[] { _modsButton });
            m.AddControl.Invoke(panel, new[] { _seedButton });
        }

        // The game lays the panel out again every time it is shown, so this puts things back each frame (it only writes what differs).
        static void LayoutPanel(object panel)
        {
            Game g = _g;
            MenuGame m = _m;
            object label = m.SeedLabel.Get(panel);
            if (label == null) return;

            // the label is the game's "Random Seed: N" text; it is replaced, but still the source of the number
            string labelText = (string)m.LabelText(label).Get(label);
            if (!string.IsNullOrEmpty(labelText) && labelText != _seedText) _seedText = labelText;
            g.Visible.Set(label, false);

            object pos = g.Position.Get(label);
            object size = g.Size.Get(label);
            float x = g.VecX(pos), y = g.VecY(pos), h = g.VecY(size);
            float gap = m.SmallGap;

            g.SetSizeAndPosition.Invoke(_modsButton, new object[] { size, pos });
            g.SetSizeAndPosition.Invoke(_seedButton, new object[] { size, g.Vec2(x, y + h + gap) });

            // one more row: the game sizes the panel when it is shown, so grow it once per showing
            object panelSize = g.Size.Get(panel);
            float panelWidth = g.VecX(panelSize), panelHeight = g.VecY(panelSize);
            if (_grownFor == 0f || panelHeight < _grownFor - 0.5f)
            {
                float grown = panelHeight + h + gap + m.BottomPadding;
                g.SetSizeAndPosition.Invoke(panel, new object[] { g.Vec2(panelWidth, grown), g.Position.Get(panel) });
                _grownFor = grown;
            }

            bool copied = _copiedAt != 0 && Environment.TickCount64 - _copiedAt < CopiedMs;
            if (_copiedAt != 0 && !copied) _copiedAt = 0;
            string seedButtonText = copied ? "Copied to clipboard" : string.IsNullOrEmpty(_seedText) ? "Random Seed" : _seedText;
            if (!string.Equals((string)g.Text.Get(_seedButton), seedButtonText, StringComparison.Ordinal))
            {
                g.Text.Set(_seedButton, seedButtonText);
            }
        }

        static void OnMenuClick(string name)
        {
            if (name == ModsButtonName)
            {
                OpenDialog();
            }
            else if (name == SeedButtonName)
            {
                CopySeed();
            }
            else if (name == ModsCloseName || name.EndsWith("_Close", StringComparison.Ordinal))
            {
                // closing the launcher's menu is the end of the stack: back to the game
                CloseDialog();
                ReturnToGame();
            }
            else if (name.StartsWith(ModsItemPrefix, StringComparison.Ordinal))
            {
                if (!int.TryParse(name.Substring(ModsItemPrefix.Length), out int index) || index < 0 || index >= _menuShown.Count) return;
                MenuEntrySnapshot entry = _menuShown[index];
                if (!entry.Enabled) return;
                CloseDialog();
                entry.OnClick();
            }
        }

        static void CopySeed()
        {
            // "Random Seed: 2141791" -> 2141791
            string text = _seedText ?? "";
            int colon = text.LastIndexOf(':');
            string seed = (colon >= 0 ? text.Substring(colon + 1) : text).Trim();
            if (seed.Length == 0) return;
            if (Clipboard.SetText(seed))
            {
                _copiedAt = Environment.TickCount64;
            }
            else
            {
                _log("WARN mods menu: could not copy the seed to the clipboard.");
            }
        }

        static void OpenDialog()
        {
            CloseDialog();
            _menuShown = ModMenu.Registry.Snapshot();
            _dialog = _m.BuildDialog(_menuShown);
            _g.AddCustomControl.Invoke(null, new[] { _dialog });
            _m.ShowCentered(_dialog, _m.ScreenWidth(), _m.ScreenHeight());
        }

        static void CloseDialog()
        {
            if (_dialog == null) return;
            object dialog = _dialog;
            _dialog = null;
            _m.Hide(dialog);
            _g.RemoveCustomControl.Invoke(null, new[] { dialog });
        }

        // The launcher's own entry: the game's own message dialog with the version, shown above the Game Menu. Its Ok button closes it
        // and leaves the Game Menu as it was. The status widget is still opened from the status line.
        static void OpenStatusFromMenu()
        {
            try
            {
                Assembly loader = typeof(StatusWidget).Assembly;
                string version = loader.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? loader.GetName().Version?.ToString() ?? "unknown";
                int plus = version.IndexOf('+');
                if (plus > 0) version = version.Substring(0, plus);
                int mods = ModStatus.Registry.Snapshot().Count;
                string text = "Version " + version + "\n\n" + mods + " code mod" + (mods == 1 ? "" : "s")
                    + " loaded. The line at the lower left of the screen lists them; click it for details.\n\nLog: data/Logs/dw2modlauncher.log";
                _m.ShowAbout("DW2 MOD LAUNCHER", text, _m.ScreenWidth(), _m.ScreenHeight());
            }
            catch (Exception ex)
            {
                _log("ERROR mods menu: could not show the launcher dialog: " + ex);
            }
        }

        // Ok goes back to the Mods dialog, like any mod's own dialog does (ModMenu.Show).
        static void OnAboutClose(object sender, object args)
        {
            try
            {
                _m?.HideAbout();
                OpenDialog();
            }
            catch (Exception ex)
            {
                _log("ERROR mods menu: could not return to the Mods dialog: " + ex);
            }
        }

        // Presses the Game Menu's own Return to Game button, which hides the menu and resumes the game.
        static void ReturnToGame()
        {
            try
            {
                _m.ReturnToGame(_g.GameMenuPanel.GetStatic());
            }
            catch (Exception ex)
            {
                _log("ERROR mods menu: could not close the Game Menu: " + ex);
            }
        }

        // The extra game members the menu needs, resolved once.
        sealed class MenuGame
        {
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            const BindingFlags Stat = BindingFlags.Public | BindingFlags.Static;

            public Slot SeedLabel;
            public MethodInfo AddControl;
            public float SmallGap;

            // room under the last row, which the game's own layout has no spare of once a row is added
            public float BottomPadding => Scaled(10f);

            Game _game;
            Type _dialogType;
            MethodInfo _setupButton, _showCentered, _setFeatureDefaults, _scaled, _dialogInitialize;
            Slot _headerText, _closeInHeader, _renderedBorder, _overlay, _modal, _backColor, _headerHeight, _padLeft, _padRight, _padTop;
            Slot _margin, _screenWidth, _screenHeight, _opacity, _targetOpacity, _opacitySpeed, _visible, _closeHandler;
            object _backColorValue;
            Type _clickType;
            MethodInfo _showMessage, _hideMessage;
            ConstructorInfo _buttonData;
            Slot _messageDialog, _controls;

            public static MenuGame Bind(Game g)
            {
                var m = new MenuGame { _game = g };
                Assembly ui = Assembly.Load("DistantWorlds.UI");
                Assembly types = Assembly.Load("DistantWorlds.Types");
                Type helper = types.GetType("DistantWorlds.Types.UserInterfaceHelper", throwOnError: true);
                Type colors = types.GetType("DistantWorlds.Types.ColorHelper", throwOnError: true);
                Type control = g.Button.BaseType;
                while (control != null && control.Name != "DWControl") control = control.BaseType;
                if (control == null) throw new MissingMemberException("DWControl");

                m._dialogType = ui.GetType("DistantWorlds.UI.Dialog", throwOnError: true);
                Type panelType = g.GameMenuPanel.Type;
                m.SeedLabel = Slot.Find(panelType, "GalaxyRandomSeedLabel", Inst);
                m.AddControl = control.GetMethod("AddControl", Inst) ?? throw new MissingMethodException("DWControl.AddControl");
                m.SmallGap = Slot.Find(helper, "SmallGap", Stat).Get<float>(null);
                m._scaled = helper.GetMethod("CalculateScaledValue", new[] { typeof(float) }) ?? throw new MissingMethodException("CalculateScaledValue(float)");
                m._backColorValue = Slot.Find(colors, "BackColorLightTransparent", Stat).GetStatic();

                // SetupButton(DWButton, name, size, position, text, image, gameTask, click, hoverStart, hoverEnd, addToUiController) -> DWButton
                foreach (MethodInfo mi in control.GetMethods(Stat))
                {
                    ParameterInfo[] ps = mi.GetParameters();
                    if (mi.Name == "SetupButton" && ps.Length == 11 && ps[0].ParameterType == g.Button && ps[10].ParameterType == typeof(bool))
                    {
                        m._setupButton = mi;
                        break;
                    }
                }
                if (m._setupButton == null) throw new MissingMethodException("DWControl.SetupButton(DWButton, ... 11 parameters)");
                m._clickType = g.ClickEvent.Type;

                Type d = m._dialogType;
                m._headerText = Slot.Find(d, "HeaderText", Inst);
                m._closeInHeader = Slot.Find(d, "AddCloseButtonToHeader", Inst);
                m._renderedBorder = Slot.Find(d, "UseRenderedBorder", Inst);
                m._overlay = Slot.Find(d, "UseOverlay", Inst);
                m._modal = Slot.Find(d, "IsModal", Inst);
                m._backColor = Slot.Find(d, "BackColor", Inst);
                m._headerHeight = Slot.Find(d, "HeaderHeight", Inst);
                m._padLeft = Slot.Find(d, "ContentPaddingLeft", Inst);
                m._padRight = Slot.Find(d, "ContentPaddingRight", Inst);
                m._padTop = Slot.Find(d, "ContentPaddingTop", Inst);
                m._margin = Slot.Find(d, "Margin", Inst);
                m._opacity = Slot.Find(d, "Opacity", Inst);
                m._targetOpacity = Slot.Find(d, "TargetOpacity", Inst);
                m._opacitySpeed = Slot.Find(d, "OpacityTransitionSpeed", Inst);
                // Dialog hides DWControl.Visible with its own, which adds and removes the modal layer (the dimmed, click-blocking state);
                // setting the base one on close would leave the screen grayed out
                m._visible = Slot.Find(d, "Visible", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                m._closeHandler = Slot.Find(d, "_CloseButtonClickHandler", BindingFlags.NonPublic | BindingFlags.Instance);
                m._setFeatureDefaults = d.GetMethod("SetFeatureDefaults", Inst) ?? throw new MissingMethodException("SetFeatureDefaults");
                m._showCentered = d.GetMethod("ShowCentered", Inst) ?? throw new MissingMethodException("Dialog.ShowCentered");
                m._dialogInitialize = RequireMethod(d, "Initialize", 3);

                Type controller = g.Controller;
                m._messageDialog = Slot.Find(controller, "MessageDialog", Stat);
                m._controls = Slot.Find(g.Button, "Controls", Inst);
                m._hideMessage = controller.GetMethod("HideMessageDialog", Stat, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("HideMessageDialog");
                foreach (MethodInfo mi in controller.GetMethods(Stat))
                {
                    ParameterInfo[] ps = mi.GetParameters();
                    if (mi.Name == "ShowMessageDialogCentered" && ps.Length == 9 && ps[1].ParameterType == typeof(string) && ps[3].ParameterType == typeof(bool))
                    {
                        m._showMessage = mi;
                        break;
                    }
                }
                if (m._showMessage == null) throw new MissingMethodException("UserInterfaceController.ShowMessageDialogCentered(image, title, text, isModal, yes, no, w, h, size)");
                Type buttonData = ui.GetType("DistantWorlds.UI.DWButtonData", throwOnError: true);
                m._buttonData = buttonData.GetConstructor(new[] { typeof(string), typeof(string), m._clickType, typeof(object) }) ?? throw new MissingMethodException("DWButtonData(name, label, click, extra)");

                m._screenWidth = Slot.Find(g.Controller, "ScreenWidth", Stat);
                m._screenHeight = Slot.Find(g.Controller, "ScreenHeight", Stat);
                return m;
            }

            // the seed label is a DWLabel, not a DWButton, so its Text is looked up on its own type
            Slot _labelText;

            public Slot LabelText(object label) => _labelText ?? (_labelText = Slot.Find(label.GetType(), "Text", Inst));

            public int ScreenWidth() => _screenWidth.Get<int>(null);

            public int ScreenHeight() => _screenHeight.Get<int>(null);

            public float Scaled(float v) => (float)_scaled.Invoke(null, new object[] { v });

            Delegate Click() => Delegate.CreateDelegate(_clickType, typeof(StatusWidget).GetMethod(nameof(OnClick), BindingFlags.NonPublic | BindingFlags.Static));

            // a button in the game's own style (SetupButton), not yet added to anything
            public object NewButton(string name, string text)
            {
                Game g = _game;
                object b = Activator.CreateInstance(g.Button);
                object zero = g.Vec2(0f, 0f);
                b = _setupButton.Invoke(null, new object[] { b, name, zero, zero, text, null, null, Click(), null, null, false });
                g.Visible.Set(b, true);
                g.PostScene.Set(b, true);
                return b;
            }

            // A Settings-style dialog: header, one full-width button per entry (greyed out when disabled), then Close.
            public object BuildDialog(List<MenuEntrySnapshot> entries)
            {
                Game g = _game;
                object d = Activator.CreateInstance(_dialogType);
                g.Name.Set(d, "DW2ML_ModsDialog");
                _renderedBorder.Set(d, true);
                _overlay.Set(d, true);
                _closeInHeader.Set(d, true);
                _headerText.Set(d, "MODS");
                _modal.Set(d, true);
                _setFeatureDefaults.Invoke(d, null);
                _backColor.Set(d, _backColorValue);
                _dialogInitialize.Invoke(d, new object[] { g.FontNormalSize, g.FontLargeSize, null });
                // Initialize puts a dialog at layer 20000; this goes above the Game Menu (22000) and below the status dialog (23000)
                g.Layer.Set(d, 22500f);
                _closeHandler.Set(d, Click());

                float rowHeight = Scaled(40f);
                float gap = SmallGap;
                float margin = _margin.Get<float>(d);
                float padLeft = _padLeft.Get<float>(d);
                float width = Scaled(375f);
                float inner = width - (padLeft + _padRight.Get<float>(d) + margin * 2f);
                float x = padLeft + margin;
                float y = _headerHeight.Get<float>(d) + _padTop.Get<float>(d) + margin;

                object innerSize = g.Vec2(inner, rowHeight);
                for (int i = 0; i < entries.Count; i++)
                {
                    object b = NewButton(ModsItemPrefix + i, entries[i].Label);
                    g.SetSizeAndPosition.Invoke(b, new object[] { innerSize, g.Vec2(x, y) });
                    g.Enabled.Set(b, entries[i].Enabled);
                    AddControl.Invoke(d, new[] { b });
                    y += rowHeight + gap;
                }
                y += gap * 4f;
                object close = NewButton(ModsCloseName, "Close");
                g.SetSizeAndPosition.Invoke(close, new object[] { innerSize, g.Vec2(x, y) });
                AddControl.Invoke(d, new[] { close });
                y += rowHeight + gap;

                float height = y + _padTop.Get<float>(d) + margin * 2f + Scaled(10f);
                g.SetSizeAndPosition.Invoke(d, new object[] { g.Vec2(width, height), g.Vec2(0f, 0f) });
                return d;
            }

            public void ShowCentered(object dialog, int screenWidth, int screenHeight)
            {
                object size = _game.Size.Get(dialog);
                _opacitySpeed.Set(dialog, 8f);
                _opacity.Set(dialog, 0f);
                _targetOpacity.Set(dialog, 1f);
                _showCentered.Invoke(dialog, new object[] { screenWidth, screenHeight, size });
            }

            // A message dialog with an Ok button. The game draws it at layer 20000, under the Game Menu (22000), so it and its children
            // are raised for as long as it is up (HideMessageDialog puts the dialog itself back).
            public void ShowAbout(string title, string text, int screenWidth, int screenHeight)
            {
                Game g = _game;
                Delegate close = Delegate.CreateDelegate(_clickType, typeof(StatusWidget).GetMethod(nameof(OnAboutClose), BindingFlags.NonPublic | BindingFlags.Static));
                object ok = _buttonData.Invoke(new object[] { "", "Ok", close, null });
                object size = g.Vec2(Scaled(400f), Scaled(220f));
                _showMessage.Invoke(null, new object[] { null, title, text, true, ok, null, screenWidth, screenHeight, size });
                object dialog = _messageDialog.GetStatic();
                g.Layer.Set(dialog, 22600f);
                RaiseChildren(dialog, 22600f);
            }

            // Presses the Game Menu's own Return to Game button, which hides the menu and resumes the game the way the game does.
            public void ReturnToGame(object panel)
            {
                if (panel == null) return;
                object button = Slot.Find(panel.GetType(), "ReturnToGameButton", Inst).Get(panel);
                if (button != null && _game.ClickEvent.Get(button) is Delegate click) click.DynamicInvoke(button, null);
            }

            public void HideAbout() => _hideMessage.Invoke(null, null);

            void RaiseChildren(object parent, float layer)
            {
                if (!(_controls.Get(parent) is System.Collections.IEnumerable children)) return;
                foreach (object c in new List<object>(Cast(children)))
                {
                    if (c == null) continue;
                    _game.Layer.Set(c, layer + 0.1f);
                    RaiseChildren(c, layer + 0.1f);
                }
            }

            static IEnumerable<object> Cast(System.Collections.IEnumerable e)
            {
                foreach (object o in e) yield return o;
            }

            public void Hide(object dialog) => _visible.Set(dialog, false);

            static MethodInfo RequireMethod(Type owner, string name, int parameterCount)
            {
                foreach (MethodInfo m in owner.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == name && m.GetParameters().Length == parameterCount) return m;
                }
                throw new MissingMethodException(owner.Name + "." + name + "(" + parameterCount + " parameters) not found");
            }
        }
    }
}
