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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Stress.Tests.Channels.Integration
{
    /// <summary>
    /// L2 server-outage recovery tests for the shared channel manager path.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("ChannelManager")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ServerOutageRecoveryTests : IntegrationTestBase
    {
        [Test]
        [Order(100)]
        [CancelAfter(120_000)]
        public async Task SingleSessionRecoversAfterServerRestartAsync(
            CancellationToken ct)
        {
            ClientChannelManager manager = CreateChannelManager(CreateTightReconnectPolicy());
            try
            {
                using MetricsCollector collector = new();
                LeakCounters.Snapshot before = LeakCounters.Capture(manager);
                ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                    .ConfigureAwait(false);
                ManagedSessionType? session = null;
                ManagedChannelKey key = default;
                bool hasKey = false;

                try
                {
                    session = await ConnectManagedSessionAsync(endpoint, manager, "L2-SRV1", ct)
                        .ConfigureAwait(false);
                    session.KeepAliveInterval = KeepAliveIntervalMilliseconds;
                    key = GetManagedChannel(session).Key;
                    hasKey = true;

                    Assert.That(session.Connected, Is.True);
                    Assert.That(
                        await WaitForQuiescence.EntryRefcountReachesAsync(
                            manager,
                            key,
                            1,
                            DefaultWait,
                            ct).ConfigureAwait(false),
                        Is.True,
                        "The session should acquire one managed-channel reference.");
                    AssertSingleSharedEntry(manager, key, expectedRefcount: 1, ChannelState.Ready);

                    await ExerciseOutageAndRecoveryAsync(
                            manager,
                            [session],
                            key,
                            expectedRefcount: 1,
                            ct)
                        .ConfigureAwait(false);

                    await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
                    AssertSingleSharedEntry(manager, key, expectedRefcount: 1, ChannelState.Ready);
                    collector.RecordObservableInstruments();
                }
                finally
                {
                    await CloseAndDisposeAsync(session).ConfigureAwait(false);
                }

                if (hasKey)
                {
                    Assert.That(
                        await WaitForQuiescence.EntryGoneAsync(manager, key, DefaultWait, ct)
                            .ConfigureAwait(false),
                        Is.True,
                        "The managed-channel entry should be removed after the session is disposed.");
                }

                await AssertNoLeaksWithServerStoppedAsync(manager, before, "L2-SRV1").ConfigureAwait(false);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
        public async Task MultipleSessionsRecoverAfterServerRestartAsync(
            CancellationToken ct)
        {
            ClientChannelManager manager = CreateChannelManager(CreateTightReconnectPolicy());
            try
            {
                using MetricsCollector collector = new();
                LeakCounters.Snapshot before = LeakCounters.Capture(manager);
                ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                    .ConfigureAwait(false);
                var sessions = new List<ManagedSessionType>(SharedSessionCount);
                ManagedChannelKey key = default;
                bool hasKey = false;

                try
                {
                    for (int index = 0; index < SharedSessionCount; index++)
                    {
                        ManagedSessionType session = await ConnectManagedSessionAsync(
                                endpoint,
                                manager,
                                FormattableString.Invariant($"L2-SRV2-{index}"),
                                ct)
                            .ConfigureAwait(false);
                        session.KeepAliveInterval = KeepAliveIntervalMilliseconds;
                        sessions.Add(session);
                    }

                    key = GetManagedChannel(sessions[0]).Key;
                    hasKey = true;
                    Assert.That(
                        sessions.Select(session => GetManagedChannel(session).Key),
                        Is.All.EqualTo(key),
                        "All sessions should share the same managed-channel key.");
                    Assert.That(
                        await WaitForQuiescence.EntryRefcountReachesAsync(
                            manager,
                            key,
                            SharedSessionCount,
                            DefaultWait,
                            ct).ConfigureAwait(false),
                        Is.True,
                        "All sessions should share one managed-channel entry before the outage.");
                    AssertSingleSharedEntry(manager, key, SharedSessionCount, ChannelState.Ready);

                    await ExerciseOutageAndRecoveryAsync(
                            manager,
                            sessions,
                            key,
                            SharedSessionCount,
                            ct)
                        .ConfigureAwait(false);

                    foreach (ManagedSessionType session in sessions)
                    {
                        await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
                    }

                    AssertSingleSharedEntry(manager, key, SharedSessionCount, ChannelState.Ready);
                    collector.RecordObservableInstruments();
                }
                finally
                {
                    for (int index = sessions.Count - 1; index >= 0; index--)
                    {
                        await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
                    }
                }

                if (hasKey)
                {
                    Assert.That(
                        await WaitForQuiescence.EntryGoneAsync(manager, key, DefaultWait, ct)
                            .ConfigureAwait(false),
                        Is.True,
                        "The shared managed-channel entry should be removed after all sessions are disposed.");
                }

                await AssertNoLeaksWithServerStoppedAsync(manager, before, "L2-SRV2").ConfigureAwait(false);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ExponentialBackoffChannelReconnectPolicy CreateTightReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromMilliseconds(500),
                MaxAttempts = 3
            };
        }

        private static ManagedChannelDiagnostic AssertSingleSharedEntry(
            ClientChannelManager manager,
            ManagedChannelKey key,
            int expectedRefcount,
            ChannelState expectedState)
        {
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
            Assert.That(diagnostics, Has.Count.EqualTo(1), "Only one managed-channel entry should exist.");
            ManagedChannelDiagnostic diagnostic = diagnostics.Single(d => d.Key.Equals(key));
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Refcount, Is.EqualTo(expectedRefcount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(expectedRefcount));
                Assert.That(diagnostic.State, Is.EqualTo(expectedState));
            });
            return diagnostic;
        }

        private async Task ExerciseOutageAndRecoveryAsync(
            ClientChannelManager manager,
            List<ManagedSessionType> sessions,
            ManagedChannelKey key,
            int expectedRefcount,
            CancellationToken ct)
        {
            int serverPort = ServerFixturePort;
            Task<bool>[] reconnectingTasks = [.. sessions.Select(
                session => WaitForConnectionStateAsync(
                    session,
                    ConnectionState.Reconnecting,
                    DefaultWait,
                    ct))];
            Task<bool> faultedTask = WaitForManagedChannelStateAsync(
                GetManagedChannel(sessions[0]),
                ChannelState.Faulted,
                DefaultWait,
                ct);

            try
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);

                bool[] reconnectingResults = await Task.WhenAll(reconnectingTasks)
                    .ConfigureAwait(false);
                Assert.That(
                    reconnectingResults,
                    Is.All.True,
                    "Every ManagedSession should enter Reconnecting while the server is down.");
                Assert.That(
                    await faultedTask.ConfigureAwait(false),
                    Is.True,
                    "The tight channel-manager retry policy should exhaust while the server is down.");

                Task<bool>[] connectedTasks = [.. sessions.Select(
                    session => WaitForConnectionStateAsync(
                        session,
                        ConnectionState.Connected,
                        DefaultWait,
                        ct))];
                await RestartServerOnPreservedPortAsync(serverPort).ConfigureAwait(false);

                bool[] connectedResults = await Task.WhenAll(connectedTasks)
                    .ConfigureAwait(false);
                Assert.That(
                    connectedResults,
                    Is.All.True,
                    "Every ManagedSession should return to Connected after the server restarts.");
                Assert.That(
                    await WaitForChannelStateAsync(
                        manager,
                        key,
                        ChannelState.Ready,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The shared managed channel should return to Ready after recovery.");
            }
            finally
            {
                await EnsureServerRunningAsync(serverPort).ConfigureAwait(false);
            }
        }

        private async Task AssertNoLeaksWithServerStoppedAsync(
            ClientChannelManager manager,
            LeakCounters.Snapshot before,
            string scope)
        {
            int serverPort = ServerFixturePort;
            await ServerFixture.StopAsync().ConfigureAwait(false);
            try
            {
                // Allow a small leak tolerance: certificate disposal can lag
                // briefly behind the GC sweep during a server-restart cycle,
                // and a handful of transient channel-mgr entries may linger
                // for a couple of GC cycles before being fully torn down.
                LeakCounters.AssertNoLeaks(
                    before, CaptureLeaksAfterCollection(manager), scope, tolerance: 8);
            }
            finally
            {
                await RestartServerOnPreservedPortAsync(serverPort).ConfigureAwait(false);
            }
        }

        private static LeakCounters.Snapshot CaptureLeaksAfterCollection(
            ClientChannelManager manager)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return LeakCounters.Capture(manager);
        }

        private const int KeepAliveIntervalMilliseconds = 500;
        private const int SharedSessionCount = 5;
    }
}
