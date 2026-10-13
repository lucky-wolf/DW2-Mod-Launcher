using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>The mod list's columns and their order. The user can drag the column headers into any order; the order is saved in the launcher settings.</summary>
    public static class ModListColumns
    {
        public const string State = "state";
        public const string Name = "name";
        public const string Source = "source";
        public const string Author = "author";

        /// <summary>The order a new user starts with.</summary>
        public static readonly IReadOnlyList<string> Default = new[] { State, Name, Source, Author };

        /// <summary>
        /// A usable order from a saved one: known columns once each, in the saved order, then any the saved list lacks (a column added by a
        /// later version) in their default order. Unknown and repeated entries are dropped; null or empty gives the default.
        /// </summary>
        public static List<string> Normalize(IEnumerable<string> saved)
        {
            List<string> order = new List<string>();
            foreach (string id in saved ?? Enumerable.Empty<string>())
            {
                string known = Default.FirstOrDefault(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
                if (known != null && !order.Contains(known)) order.Add(known);
            }
            foreach (string id in Default)
            {
                if (!order.Contains(id)) order.Add(id);
            }
            return order;
        }

        /// <summary>The order after dragging <paramref name="id"/> so that it has <paramref name="index"/> columns before it (counted without the dragged column itself).</summary>
        public static List<string> Move(IEnumerable<string> order, string id, int index)
        {
            List<string> result = Normalize(order);
            if (!result.Remove(id)) return result;
            result.Insert(Math.Clamp(index, 0, result.Count), id);
            return result;
        }
    }
}
