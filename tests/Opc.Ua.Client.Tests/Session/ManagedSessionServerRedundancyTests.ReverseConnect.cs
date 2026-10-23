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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using UaManagedSession = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Runs a managed session against a Warm redundant Server set whose
    /// members open reverse connections to the client listener and are
    /// reached only through <see cref="ServerRedundancyOptions.MemberConnections"/>.
    /// </summary>
    public sealed partial class ManagedSessionServerRedundancyTests
    {
        /// <summary>
        /// The member probe reads the ServiceLevel of a member known only from
        /// ServerUriArray over a connection that member opened.
        /// </summary>
        [Test]
        public async Task ReverseConnectProbeReadsMemberOverItsConnectionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(true, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(250);
            set.Backup.SetServiceLevel(210);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true }, null, timeout.Token)
                .ConfigureAwait(false);

            ServerRedundancyInfo info = await WaitForMemberAsync(
                session, set.Backup, server => server.ServiceLevelKnown, timeout.Token).ConfigureAwait(false);
            RedundantServer backup = Member(info, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(backup.ServiceLevel, Is.EqualTo(210));
                Assert.That(backup.ResolutionError, Is.Null);
                Assert.That(backup.Endpoint?.ReverseConnect?.Enabled, Is.True);
                Assert.That(backup.Endpoint?.ReverseConnect?.ServerUri, Is.EqualTo(set.Backup.ApplicationUri));
                Assert.That(set.Source.RequestCount(set.Backup.ApplicationUri), Is.GreaterThanOrEqualTo(2),
                    "Endpoint resolution and the probe each wait for a connection of the member.");
            });
        }

        /// <summary>
        /// A degraded ServiceLevel of the Server in use fails over to the other
        /// member over a connection that member opened, and the session keeps
        /// running on that connection.
        /// </summary>
        [Test]
        public async Task ReverseConnectServiceLevelDropFailsOverOverMemberConnectionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(true, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            var options = new ServerRedundancyOptions
            {
                ProbeMemberServiceLevel = true,
                ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(500)
            };
            await using UaManagedSession session = await set.ConnectAsync(options, null, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);

            set.Primary.SetServiceLevel(150);
            await WaitUntilAsync(
                () => ServerUriOf(session) == set.Backup.ApplicationUri &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);

            Assert.That(states.ToArray(), Is.EqualTo(new[] { ConnectionState.Failover, ConnectionState.Connected }));
            Assert.That(session.ConfiguredEndpoint.ReverseConnect?.Enabled, Is.True);
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(230));

            // Closing the connection the source handed out for the member breaks the session,
            // which proves that the session runs on it, and recovery waits for the member again.
            ITransportWaitingConnection used = set.Source.LastConnection(set.Backup.ApplicationUri)!;
            int requests = set.Source.RequestCount(set.Backup.ApplicationUri);
            while (states.TryDequeue(out _))
            {
            }
            ((IUaSCByteTransport)used.Handle).Close();
            await WaitUntilAsync(
                () => states.Contains(ConnectionState.Reconnecting) &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            Assert.That(set.Source.RequestCount(set.Backup.ApplicationUri), Is.GreaterThan(requests));
            level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(230));
        }

        /// <summary>
        /// When the Server in use stops, the reconnect budget runs out and the
        /// session fails over to the other member over a connection that member
        /// opened.
        /// </summary>
        [Test]
        public async Task ReverseConnectLostServerFailsOverOverMemberConnectionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(true, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            var policy = new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromMilliseconds(200),
                MaxRetries = 0,
                JitterFactor = 0.0,
                MaxTotalReconnectTime = TimeSpan.FromSeconds(3)
            };
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), policy, timeout.Token).ConfigureAwait(false);
            await WaitForMemberAsync(session, set.Backup, server => server.Endpoint != null, timeout.Token)
                .ConfigureAwait(false);
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);

            await set.Primary.DisposeAsync().ConfigureAwait(false);
            await WaitUntilAsync(
                () => ServerUriOf(session) == set.Backup.ApplicationUri &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);

            ConnectionState[] observed = [.. states];
            Assert.That(observed, Does.Contain(ConnectionState.Reconnecting));
            Assert.That(observed, Does.Contain(ConnectionState.Failover));
            Assert.That(observed[^1], Is.EqualTo(ConnectionState.Connected));
            Assert.That(set.Source.LastConnection(set.Backup.ApplicationUri), Is.Not.Null);
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(230));
        }

        /// <summary>
        /// A member that never opens a connection is reported as not connected,
        /// is never waited for and is never selected.
        /// </summary>
        [Test]
        public async Task ReverseConnectMemberWithoutConnectionIsNeverSelectedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(false, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            var options = new ServerRedundancyOptions
            {
                ProbeMemberServiceLevel = true,
                ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(250)
            };
            await using UaManagedSession session = await set.ConnectAsync(options, null, timeout.Token)
                .ConfigureAwait(false);
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);

            ServerRedundancyInfo info = await WaitForMemberAsync(
                session, set.Backup, server => server.ResolutionError != null, timeout.Token).ConfigureAwait(false);
            RedundantServer backup = Member(info, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(backup.ResolutionError?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotConnected));
                Assert.That(backup.Endpoint, Is.Null);
                Assert.That(backup.ServiceLevelKnown, Is.False);
            });
            await Assert.ThatAsync(
                () => session.FailoverAsync(ct: timeout.Token),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNothingToDo)).ConfigureAwait(false);

            set.Primary.SetServiceLevel(150);
            await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(states, Is.Empty);
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(set.Source.RequestCount(set.Backup.ApplicationUri), Is.Zero);
                Assert.That(set.Source.GetAvailableEndpointUrls(set.Backup.ApplicationUri), Is.Empty);
            });
        }

        /// <summary>
        /// A member whose probe times out because it stopped connecting is
        /// reported as not connected and is not selected by the same refresh.
        /// </summary>
        [Test]
        public async Task ReverseConnectMemberThatStopsConnectingIsNotSelectedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(true, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(250);
            set.Backup.SetServiceLevel(210);
            var members = new ControlledMemberConnections(set.Source);
            var options = new ServerRedundancyOptions
            {
                ProbeMemberServiceLevel = true,
                PeerDiscoveryTimeout = TimeSpan.FromSeconds(3)
            };
            await using UaManagedSession session = await set.ConnectAsync(
                options, null, timeout.Token, members).ConfigureAwait(false);
            await WaitForMemberAsync(session, set.Backup, server => server.ServiceLevelKnown, timeout.Token)
                .ConfigureAwait(false);

            members.Stop(set.Backup.ApplicationUri);
            await Assert.ThatAsync(
                () => session.FailoverAsync(ct: timeout.Token),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNothingToDo)).ConfigureAwait(false);

            RedundantServer backup = Member(session.RedundancyInfo!, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                Assert.That(backup.Endpoint, Is.Null);
                Assert.That(backup.ResolutionError?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotConnected));
                Assert.That(members.StoppedWaits, Is.EqualTo(1), "Only the probe waits for the stopped member.");
            });
        }

        /// <summary>
        /// The probe and the failover wait for a member on the url its
        /// connections announce, also when no connection is held at the moment
        /// and the discovered endpoint has another url.
        /// </summary>
        [Test]
        public async Task ReverseConnectProbeAndFailoverWaitOnAnnouncedUrlAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(true, timeout.Token)
                .ConfigureAwait(false);
            set.Primary.SetServiceLevel(250);
            set.Backup.SetServiceLevel(210);
            var members = new ControlledMemberConnections(set.Source)
            {
                AnnounceAliases = true,
                HideAfterWait = true
            };
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true }, null, timeout.Token, members)
                .ConfigureAwait(false);

            ServerRedundancyInfo info = await WaitForMemberAsync(
                session, set.Backup, server => server.ServiceLevelKnown || server.ResolutionError != null,
                timeout.Token).ConfigureAwait(false);
            RedundantServer backup = Member(info, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(backup.ResolutionError, Is.Null);
                Assert.That(backup.ServiceLevel, Is.EqualTo(210));
                Assert.That(
                    backup.Endpoint?.EndpointUrl?.OriginalString,
                    Is.Not.EqualTo(ControlledMemberConnections.Alias(set.Backup.ApplicationUri)),
                    "The discovered endpoint has another url than the announced one.");
            });

            await session.FailoverAsync(ct: timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
                Assert.That(members.WrongUrlWaits, Is.Empty);
            });
        }

        /// <summary>
        /// With the primary reached over its own source or reverse connect
        /// manager and the members over another source, a failover and the
        /// recovery on the member take the member's connections.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        public async Task ReverseConnectFailoverUsesMemberSourceAsync(
            bool initialManager, bool initialDialed, bool unmarkedTarget)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using ReverseConnectSet set = await ReverseConnectSet.StartAsync(
                true, timeout.Token, separateMemberListener: true).ConfigureAwait(false);
            set.Primary.SetServiceLevel(250);
            set.Backup.SetServiceLevel(210);
            RecordingMemberConnections memberSource = set.MemberSource!;
            var primarySource = new SingleServerSource(set.Source, set.Primary.ApplicationUri);
            var policy = new ReconnectPolicyOptions { MaxTotalReconnectTime = TimeSpan.FromSeconds(10) };
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true },
                policy, timeout.Token, memberSource, primarySource, initialManager, initialDialed)
                .ConfigureAwait(false);
            ServerRedundancyInfo info = await WaitForMemberAsync(
                session, set.Backup, server => server.Endpoint != null, timeout.Token)
                .ConfigureAwait(false);
            int requests = memberSource.RequestCount(set.Backup.ApplicationUri);

            ConfiguredEndpoint? target = null;
            if (unmarkedTarget)
            {
                ConfiguredEndpoint resolved = info.RedundantServers.ToList()
                    .Single(server => server.ServerUri == set.Backup.ApplicationUri).Endpoint!;
                target = new ConfiguredEndpoint(null, resolved.Description, resolved.Configuration)
                {
                    UpdateBeforeConnect = false
                };
            }
            await session.FailoverAsync(target, timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            Assert.That(memberSource.RequestCount(set.Backup.ApplicationUri), Is.GreaterThan(requests));
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(210));

            // Closing the member's connection in use makes the recovery wait for the member again.
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) => states.Enqueue(e.NewState);
            requests = memberSource.RequestCount(set.Backup.ApplicationUri);
            ((IUaSCByteTransport)memberSource.LastConnection(set.Backup.ApplicationUri)!.Handle).Close();
            await WaitUntilAsync(
                () => states.Contains(ConnectionState.Reconnecting) &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
                Assert.That(memberSource.RequestCount(set.Backup.ApplicationUri), Is.GreaterThan(requests));
                Assert.That(primarySource.RefusedWaits, Is.Zero);
            });

            await WaitForMemberAsync(session, set.Primary, server => server.Endpoint != null, timeout.Token)
                .ConfigureAwait(false);
            await session.FailoverAsync(ct: timeout.Token).ConfigureAwait(false);
            level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(level.GetValue<byte>(0), Is.EqualTo(250));
                Assert.That(memberSource.RequestCount(set.Primary.ApplicationUri), Is.Zero);
                Assert.That(primarySource.RefusedWaits, Is.Zero);
            });
        }

        private static async Task<ServerRedundancyInfo> WaitForMemberAsync(
            UaManagedSession session,
            RedundantMember member,
            Func<RedundantServer, bool> condition,
            CancellationToken ct)
        {
            while (true)
            {
                ServerRedundancyInfo? info = session.RedundancyInfo;
                if (info != null &&
                    info.RedundantServers.ToList().Any(s => s.ServerUri == member.ApplicationUri && condition(s)))
                {
                    return info;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Two reference servers that publish each other as a Warm redundant
        /// Server set and open reverse connections to the client listener.
        /// </summary>
        private sealed class ReverseConnectSet : IAsyncDisposable
        {
            private const int ReverseConnectTimeoutMs = 6000;

            private ReverseConnectSet(string root, ITelemetryContext telemetry)
            {
                m_root = root;
                m_telemetry = telemetry;
                Client = new ClientFixture(telemetry);
            }

            public RedundantMember Primary { get; private set; } = null!;

            public RedundantMember Backup { get; private set; } = null!;

            public ClientFixture Client { get; }

            public RecordingMemberConnections Source { get; private set; } = null!;

            /// <summary>
            /// The second client listener the backup connects to, when started with one.
            /// </summary>
            public ReverseConnectManager? MemberListener { get; private set; }

            /// <summary>
            /// The connections of <see cref="MemberListener"/>.
            /// </summary>
            public RecordingMemberConnections? MemberSource { get; private set; }

            /// <summary>
            /// Starts both members and the client listener they connect to.
            /// </summary>
            /// <param name="backupDials">Whether the backup opens reverse connections.</param>
            /// <param name="ct">Cancellation token.</param>
            /// <param name="separateMemberListener">Whether the backup connects to a
            /// second client listener instead of the primary's.</param>
            public static async Task<ReverseConnectSet> StartAsync(
                bool backupDials,
                CancellationToken ct,
                bool separateMemberListener = false)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Assert.Ignore("Reverse connect fails on mac OS.");
                }

                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                string root = Path.Combine(Path.GetTempPath(), "redundancy-rc-" + Guid.NewGuid().ToString("N")[..8]);
                var set = new ReverseConnectSet(root, telemetry);
                try
                {
                    set.Primary = await RedundantMember.StartAsync(root, "Primary", ReverseConnectTimeoutMs)
                        .ConfigureAwait(false);
                    set.Backup = await RedundantMember.StartAsync(root, "Backup", ReverseConnectTimeoutMs)
                        .ConfigureAwait(false);
                    await set.Primary.PublishSetAsync([set.Backup.ApplicationUri], ct).ConfigureAwait(false);
                    await set.Backup.PublishSetAsync([set.Primary.ApplicationUri], ct).ConfigureAwait(false);

                    await set.Client.LoadClientConfigurationAsync(root, "RedundancyClient").ConfigureAwait(false);
                    ClientConfiguration clientConfiguration = set.Client.Config.ClientConfiguration ??
                        throw new InvalidOperationException("Client configuration is missing.");
                    ReverseConnectClientConfiguration reverseConnect =
                        clientConfiguration.ReverseConnect ??= new ReverseConnectClientConfiguration();
                    // Hold a member's connection for less than the member waits for the handshake.
                    reverseConnect.HoldTime = ReverseConnectTimeoutMs * 2 / 3;
                    await set.Client.StartReverseConnectHostAsync().ConfigureAwait(false);
                    set.Source = new RecordingMemberConnections(set.Client.ReverseConnectManager);

                    var clientUrl = new Uri(set.Client.ReverseConnectUri);
                    set.Primary.DialClient(clientUrl, ReverseConnectTimeoutMs);
                    if (separateMemberListener)
                    {
                        clientUrl = await set.StartMemberListenerAsync().ConfigureAwait(false);
                    }
                    if (backupDials)
                    {
                        set.Backup.DialClient(clientUrl, ReverseConnectTimeoutMs);
                    }
                    set.m_primaryEndpoint = await set.GetPrimaryEndpointAsync(ct).ConfigureAwait(false);
                    return set;
                }
                catch
                {
                    await set.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Connects over a connection of the primary; the members are
            /// reached only through <see cref="ServerRedundancyOptions.MemberConnections"/>.
            /// </summary>
            /// <param name="options">The redundancy options.</param>
            /// <param name="policy">The reconnect policy.</param>
            /// <param name="ct">Cancellation token.</param>
            /// <param name="members">The member connections, <see cref="Source"/> by default.</param>
            /// <param name="source">The source of the primary's connections, <see cref="Source"/>
            /// by default.</param>
            /// <param name="initialManager">Whether the primary is reached through the
            /// client's reverse connect manager instead of a source.</param>
            /// <param name="initialDialed">Whether the primary is reached by an outbound connection.</param>
            public Task<UaManagedSession> ConnectAsync(
                ServerRedundancyOptions options,
                ReconnectPolicyOptions? policy,
                CancellationToken ct,
                IReverseConnectionAvailability? members = null,
                IReverseConnectionSource? source = null,
                bool initialManager = false,
                bool initialDialed = false)
            {
                ConfiguredEndpoint endpoint = new(null, m_primaryEndpoint, EndpointConfiguration.Create(Client.Config))
                {
                    UpdateBeforeConnect = false,
                    ReverseConnect = initialDialed ? null : new ReverseConnectEndpoint
                    {
                        Enabled = true,
                        ServerUri = Primary.ApplicationUri
                    }
                };
                return UaManagedSession.CreateAsync(
                    new ManagedSessionOptions
                    {
                        Endpoint = endpoint,
                        SessionName = "ReverseConnectRedundancy",
                        ReverseConnectionSource = initialManager || initialDialed ? null : source ?? Source,
                        EnableServerRedundancy = true,
                        ServerRedundancy = options with { MemberConnections = members ?? Source },
                        ReconnectPolicy = policy ?? new ReconnectPolicyOptions()
                    },
                    Client.Config,
                    Client.SessionFactory,
                    telemetry: m_telemetry,
                    reverseConnectManager: initialManager ? Client.ReverseConnectManager : null,
                    ct: ct);
            }

            public async ValueTask DisposeAsync()
            {
                await Client.DisposeAsync().ConfigureAwait(false);
                if (MemberListener != null)
                {
                    await MemberListener.DisposeAsync().ConfigureAwait(false);
                }
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

            private async Task<Uri> StartMemberListenerAsync()
            {
                for (int attempt = 0; ; attempt++)
                {
                    var listener = new ReverseConnectManager(m_telemetry)
                    {
                        TransportBindings = TestTransportBindings.WithAllSchemes()
                    };
                    var url = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{ServerFixtureUtils.GetNextFreeIPPort()}");
                    try
                    {
                        listener.AddEndpoint(url, Client.Config);
                        await listener.StartServiceAsync(Client.Config).ConfigureAwait(false);
                        MemberListener = listener;
                        MemberSource = new RecordingMemberConnections(listener);
                        return url;
                    }
                    catch (ServiceResultException sre) when (
                        sre.StatusCode == StatusCodes.BadNoCommunication && attempt < 10)
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            private async Task<EndpointDescription> GetPrimaryEndpointAsync(CancellationToken ct)
            {
                var url = new Uri(Utils.ReplaceLocalhost(Primary.Url));
                ITransportWaitingConnection connection = await Client.ReverseConnectManager
                    .WaitForConnectionAsync(url, Primary.ApplicationUri, ct).ConfigureAwait(false);
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    Client.Config,
                    connection,
                    EndpointConfiguration.Create(Client.Config),
                    ct: ct).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(default, ct)
                    .ConfigureAwait(false);
                await client.CloseAsync(ct).ConfigureAwait(false);
                return ClientFixture.SelectEndpoint(Client.Config, endpoints, url, SecurityPolicies.None);
            }

            private readonly string m_root;
            private readonly ITelemetryContext m_telemetry;
            private EndpointDescription m_primaryEndpoint = null!;
        }

        /// <summary>
        /// Hands out the connections of a reverse connect manager and records
        /// every wait and the last connection handed out per server.
        /// </summary>
        private sealed class RecordingMemberConnections : IReverseConnectionAvailability
        {
            public RecordingMemberConnections(IReverseConnectionAvailability inner)
            {
                m_inner = inner;
            }

            public int RequestCount(string serverUri)
            {
                return m_requests.TryGetValue(serverUri, out int count) ? count : 0;
            }

            public ITransportWaitingConnection? LastConnection(string serverUri)
            {
                return m_connections.TryGetValue(serverUri, out ITransportWaitingConnection? connection)
                    ? connection
                    : null;
            }

            public IReadOnlyList<Uri> GetAvailableEndpointUrls(string serverUri)
            {
                return m_inner.GetAvailableEndpointUrls(serverUri);
            }

            public async ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                m_requests.AddOrUpdate(serverUri ?? string.Empty, 1, (_, count) => count + 1);
                ITransportWaitingConnection connection = await m_inner
                    .WaitForConnectionAsync(endpointUrl, serverUri, ct).ConfigureAwait(false);
                m_connections[connection.ServerUri] = connection;
                return connection;
            }

            private readonly IReverseConnectionAvailability m_inner;
            private readonly ConcurrentDictionary<string, int> m_requests = new(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, ITransportWaitingConnection> m_connections =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Member connections whose availability and waits the test controls:
        /// it can announce an alias url per member and hand out connections
        /// only for waits on that alias, report no held connection once a
        /// wait completed, and stop a member from connecting.
        /// </summary>
        private sealed class ControlledMemberConnections : IReverseConnectionAvailability
        {
            public ControlledMemberConnections(IReverseConnectionAvailability inner)
            {
                m_inner = inner;
            }

            public bool AnnounceAliases { get; init; }

            public bool HideAfterWait { get; init; }

            public int StoppedWaits => Volatile.Read(ref m_stoppedWaits);

            public ConcurrentQueue<Uri> WrongUrlWaits { get; } = new();

            public static string Alias(string serverUri)
            {
                return $"{Utils.UriSchemeOpcTcp}://announced.invalid:4840/{Uri.EscapeDataString(serverUri)}";
            }

            public void Stop(string serverUri)
            {
                m_stopped[serverUri] = 0;
            }

            public IReadOnlyList<Uri> GetAvailableEndpointUrls(string serverUri)
            {
                if (m_stopped.ContainsKey(serverUri) || m_hidden.ContainsKey(serverUri))
                {
                    return [];
                }
                IReadOnlyList<Uri> available = m_inner.GetAvailableEndpointUrls(serverUri);
                if (!AnnounceAliases || available.Count == 0)
                {
                    return available;
                }
                m_urls[serverUri] = available[0];
                return [new Uri(Alias(serverUri))];
            }

            public async ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                string key = serverUri ?? string.Empty;
                if (m_stopped.ContainsKey(key))
                {
                    Interlocked.Increment(ref m_stoppedWaits);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                if (AnnounceAliases)
                {
                    if (endpointUrl.OriginalString != Alias(key) || !m_urls.TryGetValue(key, out Uri? url))
                    {
                        WrongUrlWaits.Enqueue(endpointUrl);
                        throw new ServiceResultException(
                            StatusCodes.BadTcpEndpointUrlInvalid, "No connection announces this url.");
                    }
                    endpointUrl = url;
                }
                ITransportWaitingConnection connection = await m_inner
                    .WaitForConnectionAsync(endpointUrl, serverUri, ct).ConfigureAwait(false);
                if (HideAfterWait)
                {
                    m_hidden[key] = 0;
                }
                return connection;
            }

            private readonly IReverseConnectionAvailability m_inner;
            private readonly ConcurrentDictionary<string, Uri> m_urls = new(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, byte> m_hidden = new(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, byte> m_stopped = new(StringComparer.Ordinal);
            private int m_stoppedWaits;
        }

        /// <summary>
        /// Hands out only the connections of one server and refuses waits for
        /// any other server.
        /// </summary>
        private sealed class SingleServerSource : IReverseConnectionSource
        {
            public SingleServerSource(IReverseConnectionSource inner, string serverUri)
            {
                m_inner = inner;
                m_serverUri = serverUri;
            }

            public int RefusedWaits => Volatile.Read(ref m_refusedWaits);

            public ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                if (!string.Equals(serverUri, m_serverUri, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref m_refusedWaits);
                    throw new ServiceResultException(
                        StatusCodes.BadNotConnected, "The source holds no connection of this server.");
                }
                return m_inner.WaitForConnectionAsync(endpointUrl, serverUri, ct);
            }

            private readonly IReverseConnectionSource m_inner;
            private readonly string m_serverUri;
            private int m_refusedWaits;
        }
    }
}
