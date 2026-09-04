using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class ModOrderDocument
    {
        public List<string> order { get; set; }

        public ModOrderDocument()
        {
            order = new List<string>();
        }
    }
}
