// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Exercises the sample executables' local topologies and fail-closed deployment checks.
    /// </summary>
    [TestFixture]
    [Category("SampleHaShortHaul")]
    [NonParallelizable]
    internal sealed class SampleHaShortHaulTests
    {
        /// <summary>
        /// The explicit isolated demo illustrates continuity or reset depending on mode.
        /// </summary>
        [TestCase("hot", "SIMULATED: HA OK: sequence continued")]
        [TestCase("cold", "SIMULATED: DATA LOSS: sequence reset")]
        [CancelAfter(45_000)]
        public async Task IsolatedPubSubDemoRunsAsync(
            string mode,
            string expected,
            CancellationToken ct)
        {
            await using var demo = new SampleAppProcess(
                "pubsub-demo",
                "Redundancy/RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", mode],
                SampleTestEnvironment.BuildFastDemo());
            await demo.WaitForLineAsync(expected, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            Assert.That(await demo.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Rejects unsigned external traffic even when the demo option was explicitly given.
        /// </summary>
        [TestCase("false", "opc.udp://127.0.0.1:4840")]
        [TestCase("true", "opc.udp://239.0.0.1:4840")]
        [CancelAfter(30_000)]
        public async Task PubSubRequiresExplicitLoopbackDemoAsync(
            string insecure,
            string endpoint,
            CancellationToken ct)
        {
            await using var app = new SampleAppProcess(
                "unsigned-pubsub",
                "Redundancy/RedundantPubSub",
                "RedundantPubSub",
                ["--role", "subscriber", "--ha-insecure", insecure, "--pubsub-endpoint", endpoint]);
            await AssertRefusedAsync(app, "Unsigned PubSub is limited to loopback demos", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Networked PubSub warm/hot cannot use native Raft election without an activation fence.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task ExternalPubSubHotRequiresLeaseFencingAsync(CancellationToken ct)
        {
            await using var app = new SampleAppProcess(
                "unfenced-pubsub",
                "Redundancy/RedundantPubSub",
                "RedundantPubSub",
                ["--role", "publisher", "--ha-mode", "hot", "--ha-election", "leader-election",
                    "--ha-raft-members", "3", "--ha-insecure", "false"]);
            await AssertRefusedAsync(app, "External warm/hot PubSub requires HA_ELECTION=lease-store", ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A legacy unsecured peer address cannot silently start networked Raft.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task StrongModeRejectsUnpinnedMembershipAsync(CancellationToken ct)
        {
            await using var server = StartServer(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["HA_CONSISTENCY"] = "strong",
                ["HA_RAFT_ID"] = "1",
                ["HA_RAFT_BIND"] = "127.0.0.1:6560",
                ["HA_RAFT_PEERS"] = "tcp://127.0.0.1:6561,tcp://127.0.0.1:6562"
            });
            await AssertRefusedAsync(server, "HA_RAFT_PEERS must contain unique ID|IP:port|SHA256 entries", ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Complete static membership and a configured secret cannot substitute for a freshness provider.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task StrongModeRejectsExternalStateWithoutAuthorityAsync(CancellationToken ct)
        {
            await using var server = StartServer(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["HA_CONSISTENCY"] = "strong",
                ["HA_RAFT_ID"] = "1",
                ["HA_RAFT_BIND"] = "127.0.0.1:6560",
                ["HA_RAFT_PEERS"] = $"2|127.0.0.1:6561|{new string('a', 64)},3|127.0.0.1:6562|{new string('b', 64)}",
                ["HA_RECORD_KEY_FILE"] = "not-used-without-an-authority"
            });
            await AssertRefusedAsync(server,
                "External shared state requires an independently scoped freshness authority",
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Strong inconsistent topologies fail rather than starting in eventual mode.
        /// </summary>
        [TestCase("strong", "aa", "hot", "Strong consistency requires active/passive redundancy")]
        [TestCase("strong", "ap", "none", "Strong consistency requires active/passive redundancy")]
        [TestCase("eventual", "aa", "hot", "HA_GOSSIP_PEERS")]
        [CancelAfter(30_000)]
        public async Task UnsupportedServerTopologyFailsClosedAsync(
            string consistency,
            string mode,
            string redundancy,
            string message,
            CancellationToken ct)
        {
            await using var server = StartServer(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["HA_MODE"] = mode,
                ["HA_CONSISTENCY"] = consistency,
                ["REDUNDANCY_MODE"] = redundancy
            });
            await AssertRefusedAsync(server, message, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The loopback strong demo really elects a Raft leader before starting UA.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task IsolatedStrongModeStartsRaftAsync(CancellationToken ct)
        {
            await using var server = StartServer(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["HA_CONSISTENCY"] = "strong",
                ["HA_LOCAL_DEMO"] = "true"
            });
            await server.WaitForLineAsync("Raft is ready (one in-process member)", TimeSpan.FromSeconds(30), ct)
                .ConfigureAwait(false);
            await server.WaitForLineAsync("became ACTIVE writer", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Neither insecure UA client flag alone enables permissive trust.
        /// </summary>
        [TestCase("--nosecurity")]
        [TestCase("--autoaccept")]
        [CancelAfter(30_000)]
        public async Task ClientRequiresExplicitInsecureDemoAsync(string flag, CancellationToken ct)
        {
            await using var client = new SampleAppProcess(
                "insecure-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                [flag],
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["HA_INSECURE"] = "false",
                    ["CLIENT_MODE"] = "independent"
                });
            await AssertRefusedAsync(client, "require HA_INSECURE=true and an independent loopback client", ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Checks that explicitly isolated active/active gossip replicas can still start.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task IsolatedActiveActiveReplicasStartAsync(CancellationToken ct)
        {
            await using RedundantServerCluster cluster = await RedundantServerCluster.StartActiveActiveAsync(
                count: 2, startupTimeout: TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
            Assert.That(cluster.LiveReplicas(), Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies the two applications can connect in the explicit local UA demo.
        /// </summary>
        [Test]
        [CancelAfter(90_000)]
        public async Task IsolatedServerAndClientConnectAsync(CancellationToken ct)
        {
            await using RedundantServerCluster cluster = await RedundantServerCluster.StartStandaloneAsync(
                startupTimeout: TimeSpan.FromSeconds(40), insecure: true, cancellationToken: ct).ConfigureAwait(false);
            await using var client = new SampleAppProcess(
                "client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                ["--server", cluster.BootstrapServerUrl, "--nosecurity", "--duration", "00:02:00"],
                SampleTestEnvironment.IndependentClient);
            await client.WaitForLineAsync("Monitoring ServerStatus.CurrentTime", TimeSpan.FromSeconds(45), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The default UA server does not advertise an unsecured session endpoint.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task StandaloneServerAdvertisesOnlySecuredEndpointsAsync(CancellationToken ct)
        {
            await using RedundantServerCluster cluster = await RedundantServerCluster.StartStandaloneAsync(
                startupTimeout: TimeSpan.FromSeconds(40), cancellationToken: ct).ConfigureAwait(false);
            ITelemetryContext telemetry = DefaultTelemetry.Create(_ => { });
            using var telemetryLifetime = telemetry as IDisposable;
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                new Uri(cluster.BootstrapServerUrl), telemetry, ct: ct).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);
            Assert.That(endpoints.IsEmpty, Is.False);
            foreach (EndpointDescription endpoint in endpoints)
            {
                Assert.That(endpoint.SecurityMode, Is.Not.EqualTo(MessageSecurityMode.None));
            }
        }

        private static SampleAppProcess StartServer(Dictionary<string, string?> settings)
        {
            settings["HA_HOST"] = "127.0.0.1";
            settings["HA_INSECURE"] = "false";
            return new SampleAppProcess(
                "server",
                "Redundancy/RedundantServer",
                "RedundantServer",
                ["--port", TestPorts.GetFreePort().ToString(CultureInfo.InvariantCulture)],
                settings);
        }

        private static async Task AssertRefusedAsync(SampleAppProcess app, string message, CancellationToken ct)
        {
            await app.WaitForLineAsync(message, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            Assert.That(await app.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.True);
        }
    }
}
