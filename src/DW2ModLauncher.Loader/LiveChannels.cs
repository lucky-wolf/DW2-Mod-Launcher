using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // The live channels code mods offer each other while the game runs (docs/DLL Injection.md, "Live channels").
    //
    // A channel is a name and a delegate. The mod that owns the data registers a delegate under the channel's name; any other mod asks for the
    // channel by name and calls the delegate whenever it needs the current answer, so the data never goes stale and neither mod names the
    // other. The channel is a contract owned by the mod that consumes it (as with file channels): its name and the delegate's type are
    // that mod's to document. A mod that comes later in the load order takes the channel over and is handed the handler it replaced, so it can
    // build on the earlier mod's data (call the old handler, change the answer) instead of only replacing it. Delegate types should be built from BCL types only (Func<short, IReadOnlyCollection<int>>), because each mod
    // compiles its own copies of any type of its own, and those are different types to the other mods.
    //
    // Instance-based so it can be unit tested; LiveChannels is the single shared instance mods reach by reflection.
    public sealed class LiveChannelRegistry
    {
        sealed class Registration
        {
            public string Provider;
            public Delegate Handler;
        }

        readonly object _gate = new object();
        readonly Dictionary<string, Registration> _channels = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
        readonly Action<string> _log;

        // log receives a line whenever a provider is superseded (null: nothing is logged).
        public LiveChannelRegistry(Action<string> log = null)
        {
            _log = log;
        }

        // Registers the provider's handler for the channel. As everywhere in the mod order, the last one wins: a provider that arrives after
        // another takes the channel over, and the line in the log says who was superseded by whom. The same provider registering again just
        // replaces its own handler. Returns the handler of the provider that was superseded (null if there was none, or it was the same
        // provider), so a later mod can call it from its own handler and publish a modified version of the same data.
        public Delegate Provide(string provider, string channel, Delegate handler)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("a live channel needs the providing mod's name", nameof(provider));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("a live channel needs a name", nameof(channel));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string key = channel.Trim();
            string superseded = null;
            Delegate previous = null;
            lock (_gate)
            {
                if (_channels.TryGetValue(key, out Registration existing) && !string.Equals(existing.Provider, provider, StringComparison.Ordinal))
                {
                    superseded = existing.Provider;
                    previous = existing.Handler;
                }
                _channels[key] = new Registration { Provider = provider, Handler = handler };
            }
            _log?.Invoke("live channel '" + key + "': provided by " + provider + (superseded == null ? "" : ", superseding " + superseded + " (the later mod wins; its handler is handed to " + provider + ")"));
            return previous;
        }

        // The channel's handler, or null when no mod provides it (yet: providers register as they load, so ask when you need it, not at start-up).
        public Delegate Get(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            lock (_gate) { return _channels.TryGetValue(channel.Trim(), out Registration r) ? r.Handler : null; }
        }

        // The name the providing mod registered under, or null when nobody provides the channel.
        public string ProviderOf(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            lock (_gate) { return _channels.TryGetValue(channel.Trim(), out Registration r) ? r.Provider : null; }
        }
    }

    // The shared registry, with plain static methods and BCL-only signatures so mods bind to them by reflection without referencing this assembly
    // (dll/Shared/LiveChannel.cs in DW2-XL does so).
    public static class LiveChannels
    {
        static readonly LiveChannelRegistry Registry = new LiveChannelRegistry(Entry.Log);

        public static Delegate Provide(string provider, string channel, Delegate handler) => Registry.Provide(provider, channel, handler);

        public static Delegate Get(string channel) => Registry.Get(channel);

        public static string ProviderOf(string channel) => Registry.ProviderOf(channel);
    }
}
