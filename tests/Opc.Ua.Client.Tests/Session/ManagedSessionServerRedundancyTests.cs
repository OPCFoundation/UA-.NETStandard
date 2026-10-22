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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using UaManagedSession = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Runs a managed session against a Warm redundant Server set of two
    /// in-process reference servers (OPC 10000-4 §6.6.2).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Client")]
    [Category("Integration")]
    public sealed partial class ManagedSessionServerRedundancyTests
    {
        /// <summary>
        /// Without probing, the session stays on the Server it connected to and
        /// a member known only from ServerUriArray keeps an unknown ServiceLevel.
        /// </summary>
        [Test]
        public async Task ConnectWithoutProbingStaysOnConfiguredServerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(210);
            set.Backup.SetServiceLevel(250);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            ServerRedundancyInfo info = await WaitForResolvedMemberAsync(session, set.Backup, timeout.Token)
                .ConfigureAwait(false);
            RedundantServer backup = Member(info, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(info.Mode, Is.EqualTo(RedundancySupport.Warm));
                Assert.That(info.ServiceLevel, Is.EqualTo(210));
                Assert.That(backup.ServiceLevelKnown, Is.False);
                Assert.That(backup.ServiceLevel, Is.EqualTo(ServiceLevels.NoData));
                Assert.That(backup.ResolutionError, Is.Null);
            });
        }

        /// <summary>
        /// With probing, the initial connect switches to the member with the
        /// higher ServiceLevel.
        /// </summary>
        [Test]
        public async Task ConnectWithProbingSelectsMemberWithHigherServiceLevelAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(210);
            set.Backup.SetServiceLevel(250);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true }, timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            ServerRedundancyInfo? info = session.RedundancyInfo;
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.ServiceLevel, Is.EqualTo(250));
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(250));
        }

        /// <summary>
        /// A member that refuses the ServiceLevel read keeps an unknown
        /// ServiceLevel and reports the exact status.
        /// </summary>
        [Test]
        public async Task MemberRefusingProbeReportsResolutionErrorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(210);
            set.Backup.SetServiceLevel(250);
            set.Backup.RefuseRead(VariableIds.Server_ServiceLevel);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true }, timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            RedundantServer backup = Member(session.RedundancyInfo!, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(backup.Endpoint, Is.Not.Null);
                Assert.That(backup.ServiceLevelKnown, Is.False);
                Assert.That(backup.ResolutionError, Is.Not.Null);
                Assert.That(backup.ResolutionError?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotReadable));
            });
        }

        /// <summary>
        /// A redundancy node the Server refuses reports its exact status.
        /// </summary>
        [Test]
        public async Task RefusedRedundancyNodeReportsReadStatusAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.RefuseRead(VariableIds.Server_ServerRedundancy_RedundantServerArray);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);

            ServerRedundancyInfo? info = session.RedundancyInfo;
            Assert.That(info, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(info!.Mode, Is.EqualTo(RedundancySupport.Warm));
                Assert.That(
                    info.ReadStatuses[VariableIds.Server_ServerRedundancy_RedundantServerArray],
                    Is.EqualTo((StatusCode)StatusCodes.BadNotReadable));
                Assert.That(
                    info.ReadStatuses[VariableIds.Server_ServerRedundancy_ServerUriArray],
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(
                    info.ReadStatuses[VariableIds.Server_ServerRedundancy_RedundancySupport],
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(info.RedundantServers.ToList().Select(s => s.ServerUri),
                    Is.EqualTo(new[] { set.Backup.ApplicationUri }));
            });
        }

        /// <summary>
        /// A degraded ServiceLevel of the Server in use fails over to the
        /// healthy member while connected, without a connection loss, and the
        /// subscription continues on the new Server.
        /// </summary>
        [Test]
        public async Task DegradedServiceLevelFailsOverAndSubscriptionContinuesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            var options = new ServerRedundancyOptions
            {
                ProbeMemberServiceLevel = true,
                ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(250)
            };
            await using UaManagedSession session = await set.ConnectAsync(options, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);

            var recorder = new DataChangeRecorder();
            ISubscriptionManager manager = session.RequireSubscriptionManager();
            ISubscription subscription = manager.Add(
                recorder,
                OptionsFactory.Create(new Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 10,
                    LifetimeCount = 100
                }));
            Assert.That(subscription.MonitoredItems.TryAdd(
                "CurrentTime",
                OptionsFactory.Create(new Subscriptions.MonitoredItems.MonitoredItemOptions
                {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    SamplingInterval = TimeSpan.FromMilliseconds(100),
                    QueueSize = 1
                }),
                out _), Is.True);
            await WaitUntilAsync(() => recorder.Count >= 3, timeout.Token).ConfigureAwait(false);
            Assert.That(states, Is.Empty);

            set.Primary.SetServiceLevel(150);
            await WaitUntilAsync(
                () => ServerUriOf(session) == set.Backup.ApplicationUri &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);
            DateTime failedOverAt = DateTime.UtcNow;
            await WaitUntilAsync(() => recorder.LatestSourceTimestamp > failedOverAt, timeout.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(states.ToArray(), Is.EqualTo(new[] { ConnectionState.Failover, ConnectionState.Connected }));
                Assert.That(manager.Items.Single(), Is.SameAs(subscription));
                Assert.That(session.RedundancyInfo?.ServiceLevel, Is.EqualTo(230));
                Assert.That(recorder.Errors, Is.Empty);
            });
        }

        /// <summary>
        /// FailoverAsync switches a connected session to an explicit target
        /// without a connection loss.
        /// </summary>
        [Test]
        public async Task FailoverAsyncSwitchesToExplicitTargetAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);

            await session.FailoverAsync(set.Backup.Endpoint, timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(() => states.Contains(ConnectionState.Connected), timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            Assert.That(states.ToArray(), Is.EqualTo(new[] { ConnectionState.Failover, ConnectionState.Connected }));
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(230));
            Assert.That(session.RedundancyInfo?.ServiceLevel, Is.EqualTo(230));
        }

        /// <summary>
        /// FailoverAsync without a target fails over to the member the
        /// redundancy handler selects.
        /// </summary>
        [Test]
        public async Task FailoverAsyncSwitchesToHandlerSelectedTargetAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);

            await session.FailoverAsync(ct: timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
        }

        /// <summary>
        /// FailoverAsync requires a redundancy handler.
        /// </summary>
        [Test]
        public async Task FailoverAsyncWithoutRedundancyHandlerThrowsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token, withRedundancyHandler: false).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => session.FailoverAsync(set.Backup.Endpoint, timeout.Token),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported)).ConfigureAwait(false);

            Assert.That(session.RedundancyInfo, Is.Null);
            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
        }

        private static string? ServerUriOf(UaManagedSession session)
        {
            return session.ConfiguredEndpoint.Description.Server?.ApplicationUri;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }

        private static RedundantServer Member(ServerRedundancyInfo info, RedundantMember member)
        {
            return info.RedundantServers.ToList().Single(s => s.ServerUri == member.ApplicationUri);
        }

        private static async Task<ServerRedundancyInfo> WaitForResolvedMemberAsync(
            UaManagedSession session,
            RedundantMember member,
            CancellationToken ct)
        {
            while (true)
            {
                ServerRedundancyInfo? info = session.RedundancyInfo;
                if (info != null &&
                    info.RedundantServers.ToList().Any(s => s.ServerUri == member.ApplicationUri && s.Endpoint != null))
                {
                    return info;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Two reference servers that publish each other in ServerUriArray as a
        /// Warm redundant Server set, and the client configuration to reach them.
        /// </summary>
        private sealed class RedundantSet : IAsyncDisposable
        {
            private RedundantSet(string root, ITelemetryContext telemetry)
            {
                m_root = root;
                Telemetry = telemetry;
                Client = new ClientFixture(telemetry);
            }

            public RedundantMember Primary { get; private set; } = null!;

            public RedundantMember Backup { get; private set; } = null!;

            public ClientFixture Client { get; }

            public ITelemetryContext Telemetry { get; }

            public static async Task<RedundantSet> StartAsync(CancellationToken ct)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                string root = Path.Combine(Path.GetTempPath(), "redundancy-" + Guid.NewGuid().ToString("N")[..8]);
                var set = new RedundantSet(root, telemetry);
                try
                {
                    set.Primary = await RedundantMember.StartAsync(root, "Primary").ConfigureAwait(false);
                    set.Backup = await RedundantMember.StartAsync(root, "Backup").ConfigureAwait(false);
                    await set.Primary.PublishSetAsync([set.Backup.ApplicationUri], ct).ConfigureAwait(false);
                    await set.Backup.PublishSetAsync([set.Primary.ApplicationUri], ct).ConfigureAwait(false);
                    await set.Client.LoadClientConfigurationAsync(root, "RedundancyClient").ConfigureAwait(false);
                    set.Primary.Endpoint = await set.Client.GetEndpointAsync(
                        new Uri(set.Primary.Url), SecurityPolicies.None).ConfigureAwait(false);
                    set.Backup.Endpoint = await set.Client.GetEndpointAsync(
                        new Uri(set.Backup.Url), SecurityPolicies.None).ConfigureAwait(false);
                    return set;
                }
                catch
                {
                    await set.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public Task<UaManagedSession> ConnectAsync(
                ServerRedundancyOptions options,
                CancellationToken ct,
                bool withRedundancyHandler = true)
            {
                var resolver = new MemberEndpointResolver(new Dictionary<string, ConfiguredEndpoint>
                {
                    [Primary.ApplicationUri] = Primary.Endpoint,
                    [Backup.ApplicationUri] = Backup.Endpoint
                });
                return UaManagedSession.CreateAsync(
                    Client.Config,
                    Primary.Endpoint,
                    Client.SessionFactory,
                    redundancyHandler: withRedundancyHandler
                        ? new DefaultServerRedundancyHandler(resolver, options: options)
                        : null,
                    telemetry: Telemetry,
                    serverRedundancy: options,
                    ct: ct);
            }

            public async ValueTask DisposeAsync()
            {
                await Client.DisposeAsync().ConfigureAwait(false);
                if (Backup != null)
                {
                    await Backup.DisposeAsync().ConfigureAwait(false);
                }
                if (Primary != null)
                {
                    await Primary.DisposeAsync().ConfigureAwait(false);
                }
                try
                {
                    if (Directory.Exists(m_root))
                    {
                        Directory.Delete(m_root, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // A certificate store file may still be open; the temp folder is reclaimed later.
                }
            }

            private readonly string m_root;
        }

        /// <summary>
        /// One in-process reference server with its own ApplicationUri and an
        /// explicitly controlled ServiceLevel.
        /// </summary>
        private sealed class RedundantMember : IAsyncDisposable
        {
            private RedundantMember(ServerFixture<ReferenceServer> fixture, ReferenceServer server)
            {
                m_fixture = fixture;
                m_server = server;
                ApplicationUri = fixture.Config.ApplicationUri!;
                Url = $"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}";
                m_setServiceLevel = ((IServerServiceLevelControl)server.CurrentInstance).ClaimServiceLevelControl();
            }

            public string ApplicationUri { get; }

            public string Url { get; }

            public ConfiguredEndpoint Endpoint { get; set; } = null!;

            public static async Task<RedundantMember> StartAsync(string root, string name, int reverseConnectTimeout = 0)
            {
                var fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    AutoAccept = true,
                    SecurityNone = true,
                    ReverseConnectTimeout = reverseConnectTimeout
                };
                string pki = Path.Combine(root, name);
                await fixture.LoadConfigurationAsync(pki).ConfigureAwait(false);
                fixture.Config.ApplicationUri = Utils.ReplaceLocalhost("urn:localhost:UA:RedundantMember:" + name);
                ReferenceServer server = await fixture.StartAsync(pki).ConfigureAwait(false);
                return new RedundantMember(fixture, server);
            }

            public void SetServiceLevel(byte level)
            {
                m_setServiceLevel(level);
            }

            /// <summary>
            /// Makes the server open reverse connections to the client listener.
            /// </summary>
            public void DialClient(Uri clientUrl, int timeout)
            {
                m_server.AddReverseConnection(clientUrl, timeout);
            }

            /// <summary>
            /// Promotes Server.ServerRedundancy to NonTransparentRedundancyType and
            /// publishes Warm redundancy with the given peers.
            /// </summary>
            public async Task PublishSetAsync(string[] peerUris, CancellationToken ct)
            {
                IServerInternal server = m_server.CurrentInstance;
                ServerObjectState serverObject = server.ServerObject;
                ServerSystemContext context = server.DefaultSystemContext;
                var replacer = (IPredefinedNodeSubtypeReplacer)server.DiagnosticsNodeManager;
                NonTransparentRedundancyState subtype = context.CreateInstanceOfNonTransparentRedundancyType();
                subtype.AddRedundantServerArray(context);
                var replaced = (NonTransparentRedundancyState)await replacer.ReplacePredefinedInstanceSubtypeAsync(
                    context,
                    serverObject.ServerRedundancy!,
                    subtype,
                    new Dictionary<QualifiedName, NodeId>
                    {
                        [new QualifiedName(BrowseNames.ServerUriArray, 0)] =
                            VariableIds.Server_ServerRedundancy_ServerUriArray
                    },
                    node => serverObject.ServerRedundancy = (ServerRedundancyState)node,
                    ct).ConfigureAwait(false);
                replaced.RedundancySupport!.Value = RedundancySupport.Warm;
                replaced.ServerUriArray!.Value = peerUris;
                replaced.ClearChangeMasks(context, true);
            }

            /// <summary>
            /// Makes a variable of the Server object unreadable for every user.
            /// </summary>
            public void RefuseRead(NodeId variableId)
            {
                IServerInternal server = m_server.CurrentInstance;
                ServerObjectState serverObject = server.ServerObject;
                BaseVariableState variable = variableId == VariableIds.Server_ServiceLevel
                    ? serverObject.ServiceLevel!
                    : serverObject.ServerRedundancy!.RedundantServerArray!;
                variable.AccessLevel = AccessLevels.None;
                variable.UserAccessLevel = AccessLevels.None;
                variable.ClearChangeMasks(server.DefaultSystemContext, false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_server.Dispose();
            }

            private readonly ServerFixture<ReferenceServer> m_fixture;
            private readonly ReferenceServer m_server;
            private readonly Action<byte> m_setServiceLevel;
            private int m_disposed;
        }

        /// <summary>
        /// Records the source timestamps of the data changes of a subscription.
        /// </summary>
        private sealed class DataChangeRecorder : ISubscriptionNotificationHandler
        {
            public int Count => Volatile.Read(ref m_count);

            public DateTime LatestSourceTimestamp => new(Interlocked.Read(ref m_latestTicks), DateTimeKind.Utc);

            public ConcurrentQueue<StatusCode> Errors { get; } = new();

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                foreach (DataValueChange change in notification.Span)
                {
                    if (StatusCode.IsBad(change.Value.StatusCode))
                    {
                        Errors.Enqueue(change.Value.StatusCode);
                        continue;
                    }
                    long ticks = change.Value.GetValue(DateTime.MinValue).ToUniversalTime().Ticks;
                    long latest = Interlocked.Read(ref m_latestTicks);
                    while (ticks > latest)
                    {
                        long previous = Interlocked.CompareExchange(ref m_latestTicks, ticks, latest);
                        if (previous == latest)
                        {
                            break;
                        }
                        latest = previous;
                    }
                    Interlocked.Increment(ref m_count);
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private long m_latestTicks = DateTime.MinValue.Ticks;
            private int m_count;
        }

        /// <summary>
        /// Resolves the members of the set to the endpoints read from their
        /// discovery endpoints.
        /// </summary>
        private sealed class MemberEndpointResolver : IRedundantServerEndpointResolver
        {
            public MemberEndpointResolver(IReadOnlyDictionary<string, ConfiguredEndpoint> endpoints)
            {
                m_endpoints = endpoints;
            }

            public ValueTask<ConfiguredEndpoint?> ResolveAsync(
                string serverUri,
                ConfiguredEndpoint currentEndpoint,
                CancellationToken ct = default)
            {
                return new ValueTask<ConfiguredEndpoint?>(
                    m_endpoints.TryGetValue(serverUri, out ConfiguredEndpoint? endpoint) ? endpoint : null);
            }

            private readonly IReadOnlyDictionary<string, ConfiguredEndpoint> m_endpoints;
        }
    }
}
