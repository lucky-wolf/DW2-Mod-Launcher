using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // How bad a mod's own report is. Ordered, so the worst of several is just the largest.
    public enum StatusLevel
    {
        Ok = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    public sealed class StatusDetail
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public StatusLevel Level { get; set; }
    }

    // A point-in-time copy of one mod's status, safe to read from the render thread without any locking.
    public sealed class ModStatusSnapshot
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Built { get; set; }
        public string LogPath { get; set; }

        // null when the loader got the mod running; otherwise why it did not (missing DLL, exception from Init, ...)
        public string LoadError { get; set; }

        // worst of: the mod's own level, its errors, its details and the load result
        public StatusLevel Level { get; set; }
        public string Summary { get; set; }
        public List<string> Features { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<StatusDetail> Details { get; set; } = new List<StatusDetail>();
    }

    // The loader's record of every code mod: what the loader saw happen to it, plus whatever the mod chooses to tell us.
    //
    // Everything a mod reports is optional. A mod that reports nothing still appears (the loader registers it from the manifest and
    // records whether Init worked), so a mod that crashes on load shows in red without any cooperation.
    //
    // Instance-based so it can be unit tested with a fake clock; ModStatus is the single shared instance mods reach by reflection.
    public sealed class StatusRegistry
    {
        const int MaxErrors = 20;

        sealed class Entry
        {
            public string Id;
            public string Name;
            public string Version;
            public string Built;
            public string LogPath;
            public string LoadError;
            public StatusLevel Level;
            public string Summary;
            public readonly List<string> Features = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public readonly List<StatusDetail> Details = new List<StatusDetail>();
        }

        readonly object _gate = new object();
        readonly List<Entry> _entries = new List<Entry>();
        long _revision;

        // Bumps on every change a mod or the loader makes, so a renderer can skip rebuilding text when nothing changed.
        public long Revision
        {
            get { lock (_gate) { return _revision; } }
        }

        // Idempotent by id; non-empty values replace what is known. Mods that register themselves and the loader (which registers
        // every mod from the manifest) therefore agree on one row.
        public void Register(string id, string name, string version, string built)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                if (!string.IsNullOrWhiteSpace(name)) e.Name = name;
                if (!string.IsNullOrWhiteSpace(version)) e.Version = version;
                if (!string.IsNullOrWhiteSpace(built)) e.Built = built;
                _revision++;
            }
        }

        // What the loader saw when it ran the mod's entry point. loadError null means it ran.
        public void ReportLoad(string id, string name, string version, string built, string loadError)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                if (!string.IsNullOrWhiteSpace(name)) e.Name = name;
                if (!string.IsNullOrWhiteSpace(version)) e.Version = version;
                if (!string.IsNullOrWhiteSpace(built)) e.Built = built;
                e.LoadError = string.IsNullOrWhiteSpace(loadError) ? null : FirstLine(loadError);
                _revision++;
            }
        }

        public void SetLevel(string id, StatusLevel level, string summary)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                e.Level = level;
                e.Summary = string.IsNullOrWhiteSpace(summary) ? null : FirstLine(summary);
                _revision++;
            }
        }

        // A named line in the expanded panel. Setting the same key again replaces it in place; empty text removes it.
        public void Detail(string id, string key, string text, StatusLevel level)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                int at = e.Details.FindIndex(d => d.Key == key);
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (at >= 0) e.Details.RemoveAt(at);
                }
                else
                {
                    var d = new StatusDetail { Key = key, Text = FirstLine(text), Level = level };
                    if (at >= 0) e.Details[at] = d; else e.Details.Add(d);
                }
                _revision++;
            }
        }

        // First line only: exception text is long and the mod's own log has the full text. Repeats are kept once.
        public void Error(string id, string text)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                string line = FirstLine(text);
                if (e.Errors.Count < MaxErrors && !e.Errors.Contains(line))
                {
                    e.Errors.Add(line);
                    _revision++;
                }
            }
        }

        public void Installed(string id, string feature)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(feature)) return;
            lock (_gate)
            {
                Entry e = GetOrAdd(id);
                if (!e.Features.Contains(feature))
                {
                    e.Features.Add(feature);
                    _revision++;
                }
            }
        }

        public void SetLog(string id, string path)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_gate)
            {
                GetOrAdd(id).LogPath = string.IsNullOrWhiteSpace(path) ? null : path;
                _revision++;
            }
        }

        public List<ModStatusSnapshot> Snapshot()
        {
            var result = new List<ModStatusSnapshot>();
            lock (_gate)
            {
                foreach (Entry e in _entries)
                {
                    StatusLevel level = e.Level;
                    if (e.LoadError != null || e.Errors.Count > 0) level = StatusLevel.Error;
                    foreach (StatusDetail d in e.Details)
                    {
                        if (d.Level > level) level = d.Level;
                    }
                    result.Add(new ModStatusSnapshot
                    {
                        Id = e.Id,
                        Name = e.Name ?? e.Id,
                        Version = e.Version,
                        Built = e.Built,
                        LogPath = e.LogPath,
                        LoadError = e.LoadError,
                        Level = level,
                        Summary = e.Summary,
                        Features = new List<string>(e.Features),
                        Errors = new List<string>(e.Errors),
                        Details = e.Details.ConvertAll(d => new StatusDetail { Key = d.Key, Text = d.Text, Level = d.Level }),
                    });
                }
            }
            return result;
        }

        Entry GetOrAdd(string id)
        {
            foreach (Entry e in _entries)
            {
                if (e.Id == id) return e;
            }
            var created = new Entry { Id = id };
            _entries.Add(created);
            return created;
        }

        static string FirstLine(string text)
        {
            int eol = text.IndexOfAny(new[] { '\r', '\n' });
            return (eol >= 0 ? text.Substring(0, eol) : text).Trim();
        }
    }
}
