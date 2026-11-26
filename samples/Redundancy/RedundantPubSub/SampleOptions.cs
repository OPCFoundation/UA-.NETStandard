// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Opc.Ua.PubSub.Redundancy;

namespace RedundantPubSub
{
    /// <summary>
    /// Immutable, parsed configuration for the sample, sourced from command-line arguments and
    /// environment variables.
    /// </summary>
    /// <param name="Role">The role to run (publisher, subscriber, or demo).</param>
    /// <param name="HaMode">The PubSub redundancy mode (Cold, Warm, or Hot).</param>
    /// <param name="Election">The activation-election strategy (leader-election or lease-store).</param>
    /// <param name="OwnerId">Stable identifier of this instance within its redundant set.</param>
    /// <param name="Endpoint">The PubSub UDP transport endpoint (multicast or unicast).</param>
    /// <param name="PublisherId">The PublisherId advertised in network messages.</param>
    /// <param name="WriterGroupId">The WriterGroup id used by the writer and reader filter.</param>
    /// <param name="DataSetWriterId">The DataSetWriter id used by the writer and reader filter.</param>
    /// <param name="IntervalMs">The publishing interval in milliseconds.</param>
    /// <param name="Distributed">Whether a networked Raft cluster was requested.</param>
    /// <param name="Insecure">Whether unsigned loopback-only demo traffic was explicitly enabled.</param>
    /// <param name="LeaseDuration">The lease duration used by lease-store election.</param>
    /// <param name="DemoFirstActiveDuration">How long the first publisher stays active in the demo.</param>
    /// <param name="DemoSecondActiveDuration">How long the promoted publisher runs in the demo.</param>
    public sealed record SampleOptions(
        SampleRole Role,
        PubSubRedundancyMode HaMode,
        PubSubRedundancyElection Election,
        string OwnerId,
        string Endpoint,
        ushort PublisherId,
        ushort WriterGroupId,
        ushort DataSetWriterId,
        int IntervalMs,
        bool Distributed,
        bool Insecure,
        TimeSpan LeaseDuration,
        TimeSpan DemoFirstActiveDuration,
        TimeSpan DemoSecondActiveDuration)
    {
        /// <summary>
        /// Parses sample options from command-line arguments (which take precedence) and
        /// environment variables, falling back to sample defaults.
        /// </summary>
        /// <param name="args">The process command-line arguments.</param>
        /// <param name="getEnvironment">Callback used to read environment variables.</param>
        /// <returns>The parsed <see cref="SampleOptions"/>.</returns>
        public static SampleOptions Parse(string[] args, Func<string, string?> getEnvironment)
        {
            return Parse(CreateSettingsReader(args, getEnvironment));
        }

        /// <summary>
        /// Combines CLI settings and the environment for transport and application options.
        /// </summary>
        internal static Func<string, string?> CreateSettingsReader(string[] args, Func<string, string?> getEnvironment)
        {
            var cli = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = arg[2..].Replace('-', '_');
                cli[key] = ii + 1 < args.Length && !args[ii + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++ii]
                    : "true";
            }

            return key => cli.TryGetValue(key, out string? value) ? value : getEnvironment(key);
        }

        /// <summary>
        /// Parses the application settings from the common settings reader.
        /// </summary>
        internal static SampleOptions Parse(Func<string, string?> read)
        {
            string Read(string key, string fallback) => read(key) ?? fallback;
            return new SampleOptions(
                ParseRole(Read("ROLE", "demo")),
                ParseMode(Read("HA_MODE", "hot")),
                ParseElection(Read("HA_ELECTION", "leader-election")),
                Read("OWNER_ID", Dns.GetHostName()),
                Read("PUBSUB_ENDPOINT", SampleConstants.DefaultDemoEndpoint),
                ParseUShort(Read("PUBLISHER_ID", SampleConstants.DefaultPublisherId.ToString(CultureInfo.InvariantCulture))),
                ParseUShort(Read("WRITER_GROUP_ID", SampleConstants.DefaultWriterGroupId.ToString(CultureInfo.InvariantCulture))),
                ParseUShort(Read("DATA_SET_WRITER_ID", SampleConstants.DefaultDataSetWriterId.ToString(CultureInfo.InvariantCulture))),
                int.Parse(Read("PUBLISH_INTERVAL_MS", "1000"), CultureInfo.InvariantCulture),
                read("HA_RAFT_ID") != null || read("HA_RAFT_MEMBERS") != null ||
                    read("HA_RAFT_BIND") != null || read("HA_RAFT_PEERS") != null,
                bool.TryParse(Read("HA_INSECURE", "false"), out bool insecure) && insecure,
                TimeSpan.FromSeconds(double.Parse(Read("HA_LEASE_SECONDS", "15"), CultureInfo.InvariantCulture)),
                TimeSpan.FromSeconds(double.Parse(Read("DEMO_FIRST_SECONDS", "6"), CultureInfo.InvariantCulture)),
                TimeSpan.FromSeconds(double.Parse(Read("DEMO_SECOND_SECONDS", "6"), CultureInfo.InvariantCulture)));
        }

        private static SampleRole ParseRole(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "publisher" => SampleRole.Publisher,
                "subscriber" => SampleRole.Subscriber,
                "demo" => SampleRole.Demo,
                _ => throw new ArgumentException("ROLE must be publisher, subscriber, or demo.", nameof(value))
            };
        }

        private static PubSubRedundancyMode ParseMode(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "hot" => PubSubRedundancyMode.Hot,
                "warm" => PubSubRedundancyMode.Warm,
                "cold" => PubSubRedundancyMode.Cold,
                _ => throw new ArgumentException("HA_MODE must be hot, warm, or cold.", nameof(value))
            };
        }

        private static PubSubRedundancyElection ParseElection(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "leader" or "leader-election" or "leaderelection" => PubSubRedundancyElection.LeaderElection,
                "lease" or "lease-store" or "leasestore" => PubSubRedundancyElection.LeaseStore,
                _ => throw new ArgumentException("HA_ELECTION must be leader-election or lease-store.", nameof(value))
            };
        }

        private static ushort ParseUShort(string value)
        {
            return ushort.Parse(value, CultureInfo.InvariantCulture);
        }

    }

    /// <summary>
    /// The role the sample process runs as.
    /// </summary>
    public enum SampleRole
    {
        /// <summary>
        /// Runs a high-availability publisher instance.
        /// </summary>
        Publisher,

        /// <summary>
        /// Runs a subscriber instance (optionally part of a high-availability reader set).
        /// </summary>
        Subscriber,

        /// <summary>
        /// Runs the single-process demo that hosts two publishers and a subscriber.
        /// </summary>
        Demo
    }

    /// <summary>
    /// Shared constant values used across the sample publisher, subscriber, and demo roles.
    /// </summary>
    internal static class SampleConstants
    {
        /// <summary>
        /// The published data-set name.
        /// </summary>
        public const string DataSetName = "HaCounter";

        /// <summary>
        /// The data-set reader name.
        /// </summary>
        public const string ReaderName = "Reader 1";

        /// <summary>
        /// The default PubSub endpoint for the single-process local demo.
        /// </summary>
        public const string DefaultDemoEndpoint = "opc.udp://127.0.0.1:4840";

        /// <summary>
        /// The default PublisherId.
        /// </summary>
        public const ushort DefaultPublisherId = 1;

        /// <summary>
        /// The default WriterGroup id.
        /// </summary>
        public const ushort DefaultWriterGroupId = 100;

        /// <summary>
        /// The default DataSetWriter id.
        /// </summary>
        public const ushort DefaultDataSetWriterId = 1;
    }
}
