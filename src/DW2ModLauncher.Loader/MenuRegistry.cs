using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // A point-in-time copy of one entry under the in-game Mods menu.
    public sealed class MenuEntrySnapshot
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public bool Enabled { get; set; }
        public Action OnClick { get; set; }
    }

    // The entries mods (and the launcher itself) have put under the in-game Mods menu (docs/Mod Menu.md).
    //
    // Instance-based so it can be unit tested; ModMenu is the single shared instance mods reach by reflection.
    public sealed class MenuRegistry
    {
        // the launcher's own entry; always listed first
        public const string LauncherId = "DW2ModLauncher.Loader";

        sealed class Entry
        {
            public string Id;
            public string Label;
            public bool Enabled = true;
            public Action OnClick;
        }

        readonly object _gate = new object();
        readonly List<Entry> _entries = new List<Entry>();
        long _revision;

        // Bumps on every change, so a renderer can skip rebuilding when nothing changed.
        public long Revision
        {
            get { lock (_gate) { return _revision; } }
        }

        // Adds the entry, or replaces the label and handler of the one with the same id (its enabled state is kept).
        public void Add(string id, string label, Action onClick)
        {
            if (string.IsNullOrEmpty(id) || onClick == null) return;
            lock (_gate)
            {
                Entry e = Find(id);
                if (e == null)
                {
                    e = new Entry { Id = id };
                    _entries.Add(e);
                }
                e.Label = string.IsNullOrWhiteSpace(label) ? id : label.Trim();
                e.OnClick = onClick;
                _revision++;
            }
        }

        public void Remove(string id)
        {
            lock (_gate)
            {
                int i = _entries.FindIndex(e => e.Id == id);
                if (i < 0) return;
                _entries.RemoveAt(i);
                _revision++;
            }
        }

        public void SetEnabled(string id, bool enabled)
        {
            lock (_gate)
            {
                Entry e = Find(id);
                if (e == null || e.Enabled == enabled) return;
                e.Enabled = enabled;
                _revision++;
            }
        }

        // The launcher first, then the rest alphabetically by label (ties by id), so the order never depends on load order.
        public List<MenuEntrySnapshot> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<MenuEntrySnapshot>();
                foreach (Entry e in _entries)
                {
                    list.Add(new MenuEntrySnapshot { Id = e.Id, Label = e.Label, Enabled = e.Enabled, OnClick = e.OnClick });
                }
                list.Sort((a, b) =>
                {
                    bool al = a.Id == LauncherId, bl = b.Id == LauncherId;
                    if (al != bl) return al ? -1 : 1;
                    int c = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                });
                return list;
            }
        }

        Entry Find(string id) => _entries.Find(e => e.Id == id);
    }
}
