using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>One monitor of the desktop, in true pixels, with the video card that drives it.</summary>
    public sealed class DisplayInfo
    {
        /// <summary>The Windows device name of the output, such as \\.\DISPLAY2.</summary>
        public string DeviceName { get; set; }
        /// <summary>The monitor's own name as Windows lists it (for example "DELL U2720Q"; "Generic PnP Monitor" when the driver gives none).</summary>
        public string MonitorName { get; set; }
        /// <summary>The video card's name as Windows lists it (for example "NVIDIA GeForce RTX 4090"), which is what the game calls the adapter.</summary>
        public string AdapterName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        /// <summary>The monitor's own current refresh rate in hertz (0 when Windows does not say, or says 1 for "hardware default").</summary>
        public int RefreshRate { get; set; }
    }

    /// <summary>
    /// Lists the monitors attached to the desktop with their position and size in true pixels and the video card behind each (Windows only; empty elsewhere,
    /// where the game runs under Proton and its window is not ours to place). The numbers are real pixels whatever the Windows scale: the thread is switched to
    /// per-monitor-v2 DPI awareness for the calls, so a launcher that is not DPI-aware itself still reads the right values.
    /// </summary>
    public static class DisplayEnumerator
    {
        public static List<DisplayInfo> List()
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            if (!OperatingSystem.IsWindows()) return displays;
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            try
            {
                for (uint i = 0; ; i++)
                {
                    DisplayDevice adapter = NewDevice();
                    if (!EnumDisplayDevicesW(null, i, ref adapter, 0)) break;
                    if ((adapter.StateFlags & AttachedToDesktop) == 0) continue;
                    DevMode mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
                    if (!EnumDisplaySettingsW(adapter.DeviceName, CurrentSettings, ref mode)) continue;
                    DisplayDevice monitor = NewDevice();
                    string monitorName = EnumDisplayDevicesW(adapter.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : "";
                    displays.Add(new DisplayInfo
                    {
                        DeviceName = adapter.DeviceName,
                        MonitorName = monitorName,
                        AdapterName = adapter.DeviceString,
                        X = mode.PositionX,
                        Y = mode.PositionY,
                        Width = mode.PelsWidth,
                        Height = mode.PelsHeight,
                        IsPrimary = (adapter.StateFlags & PrimaryDevice) != 0,
                        RefreshRate = mode.DisplayFrequency > 1 ? mode.DisplayFrequency : 0
                    });
                }
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
            return displays;
        }

        private const int AttachedToDesktop = 0x1, PrimaryDevice = 0x4, CurrentSettings = -1;

        private static DisplayDevice NewDevice()
        {
            return new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public short SpecVersion, DriverVersion, Size, DriverExtra;
            public int Fields;
            public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public short LogPixels;
            public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
            public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice displayDevice, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string deviceName, int modeNumber, ref DevMode devMode);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
