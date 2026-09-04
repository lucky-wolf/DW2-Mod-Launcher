using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class ModProfile
    {
        public string Name { get; set; }
        public List<string> Order { get; set; }
        public string ManualLaunchArguments { get; set; }
        public Dictionary<string, string> IniFiles { get; set; }
        public Dictionary<string, string> Versions { get; set; }

        public ModProfile()
        {
            Order = new List<string>();
            IniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
