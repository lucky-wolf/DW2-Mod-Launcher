using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class WorkshopUpdateCheckResult
    {
        public Dictionary<string, long> InstalledTimes { get; set; }
        public Dictionary<string, long> DetailTimes { get; set; }
        public Dictionary<string, long> RemoteTimes { get; set; }
        public string Error { get; set; }
        public Dictionary<string, WorkshopRemoteDetail> Details { get; set; }

        public WorkshopUpdateCheckResult()
        {
            InstalledTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            DetailTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            RemoteTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Error = "";
            Details = new Dictionary<string, WorkshopRemoteDetail>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
