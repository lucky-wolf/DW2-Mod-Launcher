using System.Globalization;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Human-readable file sizes with three significant digits: "512 B", "120 KiB", "13.2 MiB", "3.29 MiB".</summary>
    public static class ByteSize
    {
        private static readonly string[] Units = { "B", "KiB", "MiB", "GiB", "TiB" };

        public static string Format(long bytes)
        {
            double value = bytes < 0 ? 0 : bytes;
            int unit = 0;
            // 1000 and up would need a fourth digit, so move to the next unit (1000KiB -> 0.98MiB).
            while (value >= 1000 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            string number = unit == 0 || value >= 100 ? value.ToString("0", CultureInfo.InvariantCulture)
                : value >= 10 ? value.ToString("0.0", CultureInfo.InvariantCulture)
                : value.ToString("0.00", CultureInfo.InvariantCulture);
            return number + " " + Units[unit];
        }
    }
}
