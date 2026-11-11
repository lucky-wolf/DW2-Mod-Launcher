# Game monitor and video card

Settings > How the game is started > **Game monitor** (Windows only). The choice is "Automatic" (the game places itself, as it always did) or one monitor of this machine, listed as `<monitor name> <width>x<height> on <video card>` (a second identical entry is numbered). It is stored as that text in `launcher_settings.json` (`GameMonitor`).

## Why this is in the launcher

Measured on 2026-11-11 with two monitors at 125% scale, the main one on one video card and the second on another:

- The game asks Windows for the **primary** monitor's size (`WindowsHelpers.GetCurrentDisplayMode`, `EnumDisplaySettingsW(null, ...)`) whatever monitor its window is on, and sizes its window from the answer. A window on a second monitor is stretched to the main monitor's size.
- Every display setup (`DWGame.SetDisplay`, at start and on a settings apply) moves a borderless/fullscreen window to 0,0, the main monitor's corner.
- The window is created and shown on the main monitor, so it flashes there before it moves.
- The game picks its video card by name, but Stride creates the graphics device on the first card regardless.
- The game's own DPI mode (system-aware, set at start; its `~ HIGHDPIAWARE` compatibility flag is deleted at every start) is not the cause, and setting per-monitor awareness from the loader does nothing (the process is already aware when the loader runs).

The launcher alone knows the machine's monitors and cards before the game starts, and the loader is the first code to run in the game, so both live here rather than in a mod.

## How it works

1. `DisplayEnumerator` lists the monitors (`EnumDisplayDevices`: each output, the video card behind it, its position and size in true pixels; the thread is switched to per-monitor-v2 awareness for the calls). `DisplayChoices` builds the labels and finds a stored label again.
2. `GameLauncher.WriteLoaderManifest` writes `DisplayRect` (`x,y,width,height`) and `DisplayAdapter` into the loader manifest. A choice that is no longer connected writes neither and adds a warning to `ChannelProblems`, which shows on the in-game status line; no other monitor is used instead.
3. `DisplayHooks` in the Loader (by reflection, the Loader is built without the game) installs, first of everything in `Entry.Init`:
   - `WindowsHelpers.GetCurrentDisplayMode` postfix: answers with the chosen monitor's size **and its own refresh rate** (the manifest's `DisplayRefresh`, read from that monitor, never the main monitor's).
   - `GameSettings.CheckApplyOverrides` postfix (runs once, right after the game loads its settings): the stored `GraphicsResolutionWidth/Height` become the chosen monitor's size, so the game starts at that monitor's resolution, not the one saved from another monitor. `GameSettings.WriteToFile` swaps the stored values back for the save (unless the player changed the resolution in game), so launching on Automatic later still finds the main monitor's resolution.
   - `GameForm` constructor prefix: the thread that makes the game's window becomes per-monitor-v2 DPI aware before the form is constructed (WinForms fixes a control's DPI context at construction; a window takes it at creation). Without it, on a monitor whose Windows scale differs from the main monitor's, the game's system-aware size and position calls are rescaled by the ratio (a 2560-wide window read as 3200 or acted on as 2048), so the first UI layout was for the wrong width and the top bar and notifications stayed at it while the rest re-laid out. Measured 2026-11-11, main monitor 125%, second 100%: window DPI 96, awareness per-monitor, UI correct. Switching the thread later (`CreateParams`, `CreateHandle`) did nothing.
   - `GameWindowWinforms.Position` setter prefix: a move to 0,0 becomes a move to the chosen monitor's corner (native `SetWindowPos` in true pixels).
   - `Form.CreateParams` getter postfix (for `GameForm`) and `GameWindowWinforms.Visible` setter prefix: the window is created on, and first shown on, the chosen rectangle.
   - `GraphicsDeviceManager.CreateDevice` prefix: the device is created on the card whose name contains `DisplayAdapter`.
   Each hook reports its own failure (status line and log) and never stops the others.

## Testing

The harness (`DW2-XL`, `docs/Testing in the Game.md`) starts the Copy through `--prepare-launch` with its own data root (`DW2-XL/temp/Launcher Data`); put `"GameMonitor": "<label>"` in its `launcher_settings.json`. Verified at 125% with the main monitor on an AMD card and the second on an NVIDIA card: the window is created at the second monitor's rectangle (no flash on the main one), stays there after a forced `SetDisplay`, renders at its size, and the device is created on the NVIDIA card (the default adapter is the AMD one).

Refresh rate (checked 2026-11-11, main monitor 60 Hz, second 144 Hz): the game's screen-mode question now answers 144 for the second monitor. The game's own saved refresh rate (`GraphicsResolutionRefreshRate`, 60) and its FPS limit setting (`GraphicsFpsLimit`, saved at 60 here, range 30 to 240) are the player's choices and are left alone: the swap chain's preferred rate is ignored in a borderless window (set to 144 and VSync off, the frame rate stayed 60), and the frame rate follows the FPS limit. So a high-refresh monitor needs the in-game FPS limit raised; the launcher does not change it.

Scales (checked 2026-11-11, owner): main/second 125/125, 125/100, 100/125 and 100/100 all work, and changing the main monitor's scale while the game ran on the second monitor did no harm. Not tested: changing the scale of the monitor the game is on, while it runs (the game has no DPI-change handling of its own); two monitors on one card.
