/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Opc.Ua.PubSub.Redundancy;

namespace RedundantPubSub
{
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
        ulong RaftId,
        int RaftMembers,
        string RaftBind,
        IReadOnlyList<string> RaftPeers,
        bool Insecure,
        string? RecordKeyBase64,
        TimeSpan LeaseDuration,
        TimeSpan DemoFirstActiveDuration,
        TimeSpan DemoSecondActiveDuration)
    {
        public static SampleOptions Parse(string[] args, Func<string, string?> getEnvironment)
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

            string Read(string key, string fallback) => cli.TryGetValue(key, out string? value) ? value : getEnvironment(key) ?? fallback;

            return new SampleOptions(
                ParseRole(Read("ROLE", "demo")),
                ParseMode(Read("HA_MODE", "hot")),
                ParseElection(Read("HA_ELECTION", "leader-election")),
                Read("OWNER_ID", Dns.GetHostName()),
                Read("PUBSUB_ENDPOINT", SampleConstants.DefaultEndpoint),
                ParseUShort(Read("PUBLISHER_ID", SampleConstants.DefaultPublisherId.ToString(CultureInfo.InvariantCulture))),
                ParseUShort(Read("WRITER_GROUP_ID", SampleConstants.DefaultWriterGroupId.ToString(CultureInfo.InvariantCulture))),
                ParseUShort(Read("DATA_SET_WRITER_ID", SampleConstants.DefaultDataSetWriterId.ToString(CultureInfo.InvariantCulture))),
                int.Parse(Read("PUBLISH_INTERVAL_MS", "1000"), CultureInfo.InvariantCulture),
                ulong.Parse(Read("HA_RAFT_ID", "1"), CultureInfo.InvariantCulture),
                int.Parse(Read("HA_RAFT_MEMBERS", "1"), CultureInfo.InvariantCulture),
                Read("HA_RAFT_BIND", "tcp://0.0.0.0:6560"),
                ReadList(Read("HA_RAFT_PEERS", string.Empty)),
                bool.TryParse(Read("HA_INSECURE", "false"), out bool insecure) && insecure,
                EmptyToNull(Read("HA_RECORD_KEY", string.Empty)),
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

        private static string? EmptyToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string[] ReadList(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? []
                : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public enum SampleRole
    {
        Publisher,
        Subscriber,
        Demo
    }

    internal static class SampleConstants
    {
        public const string DataSetName = "HaCounter";
        public const string ReaderName = "Reader 1";
        public const string DefaultEndpoint = "opc.udp://239.0.0.1:4840";
        public const ushort DefaultPublisherId = 1;
        public const ushort DefaultWriterGroupId = 100;
        public const ushort DefaultDataSetWriterId = 1;
    }
}
