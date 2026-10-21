using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Loader
{
    // The live channels code mods offer each other while the game runs (docs/DLL Injection.md, "Live channels").
    //
    // A channel is a name, a version and a delegate. The mod that owns the data registers a delegate under the channel's name; any other mod asks for the
    // channel by name and calls the delegate whenever it needs the current answer, so the data never goes stale and neither mod names the
    // other. The channel is a contract owned by the mod that consumes it (as with file channels): its name, version and the delegate's type are
    // that mod's to document. The consumer states the version and the delegate type it expects; a provider that differs in either is reported
    // (log + status line) and the consumer is handed nothing, never a handler it would call wrongly. A mod that comes later in the load order takes
    // the channel over and is handed the handler it replaced, so it can build on the earlier mod's data (call the old handler, change the answer)
    // instead of only replacing it. Delegate types should be built from BCL types only (Func<short, IReadOnlyCollection<int>>), because each mod
    // compiles its own copies of any type of its own, and those are different types to the other mods.
    //
    // Instance-based so it can be unit tested; LiveChannels is the single shared instance mods reach by reflection.
    // A consumer and the provider of a live channel disagree on the contract. Text is the English sentence (log, observed file); the other
    // fields let the status line say it in the player's language.
    public sealed class ChannelMismatch
    {
        public const string Version = "version";
        public const string Type = "type";

        public string Kind;
        public string Consumer;
        public string Channel;
        public string Provider;
        // the version, or the delegate type, each side has
        public string ConsumerHas;
        public string ProviderHas;
        public string Text;
    }

    public sealed class LiveChannelRegistry
    {
        sealed class Registration
        {
            public string Provider;
            public int Version;
            public Delegate Handler;
        }

        sealed class Interest
        {
            public string Consumer;
            public string Channel;
            public int Version;
            public Type Expected;
        }

        readonly object _gate = new object();
        readonly Dictionary<string, Registration> _channels = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Interest> _consumers = new Dictionary<string, Interest>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _problems = new List<string>();
        readonly Action<string> _log;
        readonly Action<ChannelMismatch> _problem;

        // log receives a line whenever a provider is superseded (null: nothing is logged). problem receives the text of each contract
        // mismatch once (null: it is only recorded and logged); the loader raises it on the status widget.
        public LiveChannelRegistry(Action<string> log = null, Action<ChannelMismatch> problem = null)
        {
            _log = log;
            _problem = problem;
        }

        // Registers the provider's handler for the channel at the given contract version. As everywhere in the mod order, the last one wins: a
        // provider that arrives after another takes the channel over, and the line in the log says who was superseded by whom. The same provider
        // registering again just replaces its own handler. Returns the handler of the provider that was superseded (null if there was none, or it
        // was the same provider), so a later mod can call it from its own handler and publish a modified version of the same data.
        public Delegate Provide(string provider, string channel, int version, Delegate handler)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("a live channel needs the providing mod's name", nameof(provider));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("a live channel needs a name", nameof(channel));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string key = channel.Trim();
            string superseded = null;
            int supersededVersion = 0;
            Delegate previous = null;
            lock (_gate)
            {
                if (_channels.TryGetValue(key, out Registration existing) && !string.Equals(existing.Provider, provider, StringComparison.Ordinal))
                {
                    superseded = existing.Provider;
                    supersededVersion = existing.Version;
                    previous = existing.Handler;
                }
                _channels[key] = new Registration { Provider = provider, Version = version, Handler = handler };
            }
            _log?.Invoke("live channel '" + key + "' v" + version + ": provided by " + provider
                + (superseded == null ? "" : ", superseding " + superseded + " v" + supersededVersion + " (the later mod wins; its handler is handed to " + provider + ")"));
            return previous;
        }

        // The channel's handler for a consumer that expects the given contract version and delegate type, or null. Null means either that no mod
        // provides the channel (yet: providers register as they load, so ask when you need it, not at start-up), or that the provider's version or
        // delegate type is not the one the consumer expects; the second case is reported once as a problem naming both mods.
        public Delegate Get(string consumer, string channel, int version, Type expected)
        {
            if (string.IsNullOrWhiteSpace(consumer)) throw new ArgumentException("a live channel consumer needs the asking mod's name", nameof(consumer));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("a live channel needs a name", nameof(channel));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            string key = channel.Trim();
            ChannelMismatch mismatch = null;
            Delegate handler = null;
            lock (_gate)
            {
                _consumers[consumer + "|" + key] = new Interest { Consumer = consumer, Channel = key, Version = version, Expected = expected };
                if (!_channels.TryGetValue(key, out Registration r)) return null;
                if (r.Version != version)
                    mismatch = new ChannelMismatch
                    {
                        Kind = ChannelMismatch.Version,
                        Consumer = consumer,
                        Channel = key,
                        Provider = r.Provider,
                        ConsumerHas = version.ToString(),
                        ProviderHas = r.Version.ToString(),
                        Text = consumer + " uses live channel '" + key + "' v" + version + " but " + r.Provider + " provides v" + r.Version
                    };
                else if (r.Handler.GetType() != expected)
                    mismatch = new ChannelMismatch
                    {
                        Kind = ChannelMismatch.Type,
                        Consumer = consumer,
                        Channel = key,
                        Provider = r.Provider,
                        ConsumerHas = expected.ToString(),
                        ProviderHas = r.Handler.GetType().ToString(),
                        Text = consumer + " expects live channel '" + key + "' to be a " + expected + " but " + r.Provider + " provides a " + r.Handler.GetType()
                    };
                else handler = r.Handler;
                if (mismatch != null && _reported.Add(mismatch.Text)) _problems.Add(mismatch.Text);
                else mismatch = null;
            }
            if (mismatch != null)
            {
                _log?.Invoke("live channel mismatch: " + mismatch.Text);
                _problem?.Invoke(mismatch);
            }
            return handler;
        }

        // The name the providing mod registered under, or null when nobody provides the channel.
        public string ProviderOf(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            lock (_gate) { return _channels.TryGetValue(channel.Trim(), out Registration r) ? r.Provider : null; }
        }

        // What every mod provides and every mod asked for so far, for the observed-hooks file.
        public ObservedChannels Snapshot()
        {
            lock (_gate)
            {
                return new ObservedChannels
                {
                    Provided = _channels.Select(kv => new ObservedChannelProvision { Channel = kv.Key, Provider = kv.Value.Provider, Version = kv.Value.Version, DelegateType = kv.Value.Handler.GetType().ToString() })
                        .OrderBy(p => p.Channel, StringComparer.OrdinalIgnoreCase).ToList(),
                    Consumed = _consumers.Values.Select(c => new ObservedChannelUse { Channel = c.Channel, Consumer = c.Consumer, Version = c.Version, DelegateType = c.Expected.ToString() })
                        .OrderBy(c => c.Channel, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Consumer, StringComparer.Ordinal).ToList(),
                    Problems = _problems.ToList()
                };
            }
        }
    }

    public class ObservedChannels
    {
        public List<ObservedChannelProvision> Provided { get; set; } = new List<ObservedChannelProvision>();
        public List<ObservedChannelUse> Consumed { get; set; } = new List<ObservedChannelUse>();
        public List<string> Problems { get; set; } = new List<string>();
    }

    public class ObservedChannelProvision
    {
        public string Channel { get; set; }
        public string Provider { get; set; }
        public int Version { get; set; }
        public string DelegateType { get; set; }
    }

    public class ObservedChannelUse
    {
        public string Channel { get; set; }
        public string Consumer { get; set; }
        public int Version { get; set; }
        public string DelegateType { get; set; }
    }

    // The shared registry, with plain static methods and BCL-only signatures so mods bind to them by reflection without referencing this assembly
    // (dll/Shared/LiveChannel.cs in DW2-XL does so).
    public static class LiveChannels
    {
        internal static readonly LiveChannelRegistry Registry = new LiveChannelRegistry(Entry.Log,
            m => HookStatus.Warn("livechannel:" + m.Text,
                StatusStrings.Get(m.Kind == ChannelMismatch.Version ? "WidgetChannelVersionMismatch" : "WidgetChannelTypeMismatch", m.Consumer, m.Channel, m.ConsumerHas, m.Provider, m.ProviderHas), null));

        public static Delegate Provide(string provider, string channel, int version, Delegate handler) => Registry.Provide(provider, channel, version, handler);

        public static Delegate Get(string consumer, string channel, int version, Type expected) => Registry.Get(consumer, channel, version, expected);

        public static string ProviderOf(string channel) => Registry.ProviderOf(channel);
    }
}
