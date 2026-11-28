// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                new ServerRedundancyOptions { ProbeMemberServiceLevel = false }, timeout.Token).ConfigureAwait(false);

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
        /// Initial placement picks the higher ServiceLevel and keeps the seed on a tie.
        /// </summary>
        [TestCase(150, 250, true)]
        [TestCase(200, 255, true)]
        [TestCase(250, 250, false)]
        public async Task DefaultInitialSelectionRanksVerifiedServiceLevelsAsync(
            byte seedLevel,
            byte peerLevel,
            bool selectPeer)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(seedLevel);
            set.Backup.SetServiceLevel(peerLevel);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo((selectPeer ? set.Backup : set.Primary).ApplicationUri));
            ServerRedundancyInfo? info = session.RedundancyInfo;
            Assert.That(info, Is.Not.Null);
            byte expectedLevel = selectPeer ? peerLevel : seedLevel;
            Assert.That(info!.ServiceLevel, Is.EqualTo(expectedLevel));
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(expectedLevel));
            if (selectPeer)
            {
                Assert.That(
                    await EventuallyAsync(() => set.Primary.SessionCount == 0).ConfigureAwait(false),
                    Is.True,
                    "The session replaced on the Server connected to first must be closed there.");
            }
        }

        /// <summary>
        /// Ordinary connections can use URL-only discovery; redundancy requires independent URI authority.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task UrlDiscoveryDoesNotChooseRedundancyPeerIdentityAsync(
            bool enableRedundancy, bool configureExpectedUri)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            var builder = new ManagedSessionBuilder(set.Client.Config, set.Telemetry)
                .UseEndpoint(set.Primary.Url)
                .WithUserIdentity(new UserIdentity("user1", "password"u8))
                .WithServerRedundancyOptions(new ServerRedundancyOptions
                {
                    ExpectedServerApplicationUri = configureExpectedUri ? set.Primary.ApplicationUri : null
                });
            if (enableRedundancy)
            {
                builder.WithServerRedundancy();
            }

            if (enableRedundancy && !configureExpectedUri)
            {
                await Assert.ThatAsync(
                    () => builder.ConnectAsync(timeout.Token),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo((StatusCode)StatusCodes.BadCertificateUriInvalid)).ConfigureAwait(false);
                Assert.That(set.Primary.CreatedSessionCount, Is.Zero);
            }
            else
            {
                await using UaManagedSession session = await builder.ConnectAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                Assert.That(session.RedundancyInfo != null, Is.EqualTo(enableRedundancy));
            }
        }

        /// <summary>
        /// A fully populated discovery result is still routing metadata, not client trust configuration.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DiscoveredEndpointDoesNotAuthorizeCredentialedRedundancyAsync(
            bool configureExpectedUri, bool tokenReuseOnly)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            ConfiguredEndpoint discoveryResult = set.Primary.Endpoint;
            Assert.That(discoveryResult.Description.Server.ApplicationUri, Is.EqualTo(set.Primary.ApplicationUri));
            Assert.That(discoveryResult.Description.ServerCertificate.IsEmpty, Is.False);
            var builder = new ManagedSessionBuilder(set.Client.Config, set.Telemetry)
                .UseEndpoint(discoveryResult)
                .WithUserIdentity(new UserIdentity("user1", "password"u8));
            if (tokenReuseOnly)
            {
                builder.WithTokenReuseFailover();
            }
            else
            {
                builder.WithServerRedundancy();
            }

            if (!configureExpectedUri)
            {
                await Assert.ThatAsync(
                    () => builder.ConnectAsync(timeout.Token),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo((StatusCode)StatusCodes.BadCertificateUriInvalid)).ConfigureAwait(false);
                Assert.That(set.Primary.ActivatedIdentityTypes, Is.Empty);
                Assert.That(set.Primary.CreatedSessionCount, Is.Zero);
                return;
            }

            builder.WithServerRedundancyOptions(new ServerRedundancyOptions
            {
                ExpectedServerApplicationUri = set.Primary.ApplicationUri
            });
            await using UaManagedSession session = await builder.ConnectAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
        }

        /// <summary>
        /// A cached endpoint is not sufficient authority to resume a redundancy session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RedundantTakeoverRequiresIndependentPeerConfigurationAsync(bool supplyCachedEndpoint)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            await using UaManagedSession original = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token).ConfigureAwait(false);
            SessionHandoff handoff = await original.RetireAsync(timeout.Token).ConfigureAwait(false);
            int activations = set.Primary.ActivatedIdentityTypes.Count;
            var builder = new ManagedSessionBuilder(set.Client.Config, set.Telemetry)
                .WithServerRedundancy()
                .TakeOver(handoff);
            if (supplyCachedEndpoint)
            {
                builder.UseEndpoint(CoreUtils.Clone(handoff.Session.ConfiguredEndpoint)!);
            }

            await Assert.ThatAsync(
                () => builder.ConnectAsync(timeout.Token),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadCertificateUriInvalid)).ConfigureAwait(false);
            Assert.That(set.Primary.ActivatedIdentityTypes, Has.Count.EqualTo(activations));
        }

        /// <summary>
        /// A member whose resolved endpoint does not keep the security mode and
        /// policy of the session in use is not probed.
        /// </summary>
        [Test]
        public async Task ProbeSkipsMemberWithDifferentSecurityAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(150);
            set.Backup.SetServiceLevel(250);
            ConfiguredEndpoint unsecured = await set.Client.GetEndpointAsync(
                new Uri(set.Backup.Url), SecurityPolicies.None).ConfigureAwait(false);
            Assert.That(unsecured.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.None));

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true },
                timeout.Token,
                backupEndpoint: unsecured).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            RedundantServer backup = Member(session.RedundancyInfo!, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(
                    backup.ResolutionError?.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeRejected));
                Assert.That(backup.ServiceLevelKnown, Is.False);
                Assert.That(set.Backup.CreatedSessionCount, Is.Zero);
            });
        }

        /// <summary>
        /// A probe uses only the configured probe user, or anonymous by default.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProbeDoesNotForwardTheActiveIdentityAsync(bool useDedicatedIdentity)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(190);
            set.Backup.SetServiceLevel(180);
            set.Backup.RefuseRead(VariableIds.Server_NamespaceArray);
            IUserIdentity? probeIdentity = useDedicatedIdentity ? new UserIdentity("user2", "password1"u8) : null;
            set.Backup.Endpoint.UpdateBeforeConnect = true;
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeIdentity = probeIdentity, ServiceLevelMonitoringInterval = null },
                timeout.Token,
                identity: new UserIdentity("user1", "password"u8)).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            Assert.That(Member(session.RedundancyInfo!, set.Backup).ServiceLevel, Is.EqualTo(180));
            Assert.Multiple(() =>
            {
                Assert.That(set.Backup.Endpoint.UpdateBeforeConnect, Is.True);
                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                Assert.That(set.Backup.ActivatedIdentityTypes,
                    Is.EqualTo(new[] { useDedicatedIdentity ? UserTokenType.UserName : UserTokenType.Anonymous }));
                if (useDedicatedIdentity)
                {
                    Assert.That(set.Backup.ActivatedIdentityNames.Single(), Is.EqualTo("user2"));
                }
            });
        }

        /// <summary>
        /// A configured application URI must agree with the channel certificate before a switch can activate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailoverRejectsPeerCertificateWithDifferentApplicationUriAsync(bool reuseToken)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            const string expectedMemberUri = "urn:redundancy:misconfigured-member";
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token, expectedMemberUri)
                .ConfigureAwait(false);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token,
                identity: new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
            session.EnableTokenReuseFailover = reuseToken;

            var target = new ConfiguredEndpoint(
                null,
                CoreUtils.Clone(set.Backup.Endpoint.Description)!,
                set.Backup.Endpoint.Configuration);
            target.Description.Server.ApplicationUri = expectedMemberUri;

            await Assert.ThatAsync(
                () => session.FailoverAsync(target, timeout.Token),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadCertificateUriInvalid)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(set.Backup.CreatedSessionCount, Is.Zero);
                Assert.That(set.Backup.ActivatedIdentityTypes, Is.Empty);
            });
        }

        /// <summary>
        /// Resolution cannot substitute the configured URI of a different member.
        /// </summary>
        [Test]
        public async Task ProbeRequiresEndpointToIdentifyTheListedMemberAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(150);
            var target = new ConfiguredEndpoint(
                null,
                CoreUtils.Clone(set.Backup.Endpoint.Description)!,
                set.Backup.Endpoint.Configuration);
            target.Description.Server.ApplicationUri = "urn:redundancy:misconfigured-member";
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true },
                timeout.Token,
                backupEndpoint: target).ConfigureAwait(false);

            RedundantServer backup = Member(session.RedundancyInfo!, set.Backup);
            Assert.Multiple(() =>
            {
                Assert.That(backup.ResolutionError?.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadCertificateUriInvalid));
                Assert.That(backup.ServiceLevelKnown, Is.False);
                Assert.That(set.Backup.CreatedSessionCount, Is.Zero);
            });
        }

        /// <summary>
        /// While the Server in use is Healthy, ServiceLevel monitoring does not
        /// probe the other members.
        /// </summary>
        [Test]
        public async Task MonitoringOfHealthyServerDoesNotProbeMembersAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.HealthyMinimum);
            set.Backup.SetServiceLevel(ServiceLevels.HealthyMinimum);
            var options = new ServerRedundancyOptions
            {
                ProbeMemberServiceLevel = true,
                ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(100)
            };

            await using UaManagedSession session = await set.ConnectAsync(options, timeout.Token)
                .ConfigureAwait(false);
            int probesAfterConnect = set.Backup.CreatedSessionCount;
            set.Backup.SetServiceLevel(ServiceLevels.Maximum);
            await Task.Delay(1500, timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
                Assert.That(probesAfterConnect, Is.GreaterThan(0));
                Assert.That(set.Backup.CreatedSessionCount, Is.EqualTo(probesAfterConnect));
            });
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
            set.Primary.SetServiceLevel(150);
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
        /// A member that refuses the ServerStatus.State read reports the exact
        /// status and is not selected.
        /// </summary>
        [Test]
        public async Task MemberRefusingServerStateReportsResolutionErrorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(150);
            set.Backup.SetServiceLevel(250);
            set.Backup.RefuseRead(VariableIds.Server_ServerStatus_State);

            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ProbeMemberServiceLevel = true }, timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Primary.ApplicationUri));
            RedundantServer backup = Member(session.RedundancyInfo!, set.Backup);
            Assert.That(backup.ResolutionError?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotReadable));
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
        /// subscription continues and refreshes conditions on the new Server.
        /// </summary>
        [Test]
        public async Task DegradedServiceLevelFailsOverAndRefreshesConditionsAsync()
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
            var stateChanges = new ConcurrentQueue<ConnectionStateChangedEventArgs>();
            session.ConnectionStateChanged += (_, e) => stateChanges.Enqueue(e);

            var recorder = new FailoverNotificationRecorder();
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
            Assert.That(subscription.MonitoredItems.TryAdd(
                "Conditions",
                OptionsFactory.Create(new Subscriptions.MonitoredItems.MonitoredItemOptions
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    QueueSize = 10,
                    Filter = CreateRefreshEventFilter()
                }),
                out Subscriptions.MonitoredItems.IMonitoredItem? conditions), Is.True);
            await WaitUntilAsync(() => recorder.Count >= 3, timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(() => conditions?.Created == true, timeout.Token).ConfigureAwait(false);
            Assert.That(
                stateChanges.ToArray().Where(static change =>
                    change.PreviousState == ConnectionState.Connected),
                Is.Empty,
                "The connected session must not transition before the primary is degraded.");

            set.Primary.SetServiceLevel(150);
            await WaitUntilAsync(
                () => stateChanges.ToArray().Any(static change =>
                    change.PreviousState == ConnectionState.Failover &&
                    change.NewState == ConnectionState.Connected),
                timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(
                () => ServerUriOf(session) == set.Backup.ApplicationUri &&
                    session.StateMachine.State == ConnectionState.Connected,
                timeout.Token).ConfigureAwait(false);
            DateTime failedOverAt = DateTime.UtcNow;
            await WaitUntilAsync(() => recorder.LatestSourceTimestamp > failedOverAt, timeout.Token)
                .ConfigureAwait(false);
            await WaitUntilAsync(() => recorder.RefreshMarkers.Contains(ObjectTypeIds.RefreshEndEventType),
                timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    stateChanges.ToArray()
                        .Where(static change =>
                            change.PreviousState is ConnectionState.Connected or ConnectionState.Failover)
                        .Select(static change => change.NewState),
                    Is.EqualTo(new[] { ConnectionState.Failover, ConnectionState.Connected }));
                Assert.That(manager.Items.Single(), Is.SameAs(subscription));
                Assert.That(session.RedundancyInfo?.ServiceLevel, Is.EqualTo(230));
                Assert.That(recorder.Errors, Is.Empty);
                Assert.That(recorder.RefreshMarkers, Is.EqualTo(new[]
                {
                    ObjectTypeIds.RefreshStartEventType, ObjectTypeIds.RefreshEndEventType
                }));
            });
            Assert.That(
                await EventuallyAsync(() => set.Primary.SessionCount == 0).ConfigureAwait(false),
                Is.True,
                "The session on the Server left must be closed there.");
        }

        /// <summary>
        /// Classic event subscriptions receive one refresh sequence after failover.
        /// </summary>
        [Test]
        public async Task FailoverRefreshesClassicEventSubscriptionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(),
                timeout.Token,
                engineFactory: ClassicSubscriptionEngineFactory.Instance).ConfigureAwait(false);

            var markers = new ConcurrentQueue<NodeId>();
            using var subscription = new Subscription(set.Telemetry)
            {
                PublishingEnabled = true,
                PublishingInterval = 100
            };
            var item = new MonitoredItem(set.Telemetry)
            {
                NodeClass = NodeClass.Object,
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                QueueSize = 10,
                Filter = CreateRefreshEventFilter()
            };
            item.Notification += (_, notification) =>
            {
                if (notification.NotificationValue is EventFieldList fields)
                {
                    RecordRefreshMarker(fields.EventFields, markers);
                }
            };
            subscription.AddItem(item);
            Assert.That(session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync(timeout.Token).ConfigureAwait(false);

            await session.FailoverAsync(set.Backup.Endpoint, timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(() => markers.Contains(ObjectTypeIds.RefreshEndEventType), timeout.Token)
                .ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            Assert.That(markers, Is.EqualTo(new[]
            {
                ObjectTypeIds.RefreshStartEventType, ObjectTypeIds.RefreshEndEventType
            }));
        }

        /// <summary>
        /// The client retires its session when the only reachable member enters Maintenance.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MaintenanceWithNoEligiblePeerClosesSessionAsync(bool useChannelManager)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            await using ClientChannelManager? channels = useChannelManager
                ? new ClientChannelManager(set.Client.Config, set.Telemetry)
                : null;
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(ServiceLevels.NoData);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(100) },
                timeout.Token, channelManager: channels).ConfigureAwait(false);

            set.Primary.SetServiceLevel(ServiceLevels.Maintenance);
            await WaitUntilAsync(() => session.StateMachine.State == ConnectionState.Disconnected, timeout.Token)
                .ConfigureAwait(false);

            await Assert.ThatAsync(
                () => session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadServerHalted)).ConfigureAwait(false);
            Assert.That(await EventuallyAsync(() => set.Primary.SessionCount == 0).ConfigureAwait(false), Is.True);
            int createdSessions = set.Primary.CreatedSessionCount;
            session.StateMachine.TriggerReconnect();
            Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(set.Primary.CreatedSessionCount, Is.EqualTo(createdSessions));
        }

        /// <summary>
        /// A new session restores local subscriptions after the announced Maintenance interval.
        /// </summary>
        [Test]
        public async Task MaintenanceRecoveryRecreatesSubscriptionsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(ServiceLevels.NoData);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions { ServiceLevelMonitoringInterval = TimeSpan.FromMilliseconds(100) },
                timeout.Token).ConfigureAwait(false);

            var recorder = new FailoverNotificationRecorder();
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
            await WaitUntilAsync(() => recorder.Count > 0, timeout.Token).ConfigureAwait(false);

            set.Primary.SetEstimatedReturnTime(DateTime.UtcNow.AddSeconds(4));
            set.Primary.SetServiceLevel(ServiceLevels.Maintenance);
            await WaitUntilAsync(
                () => session.StateMachine.State == ConnectionState.Disconnected && set.Primary.SessionCount == 0,
                timeout.Token).ConfigureAwait(false);
            DateTime restoredAt = DateTime.UtcNow;
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);

            await WaitUntilAsync(() => session.StateMachine.IsConnected, timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(() => recorder.LatestSourceTimestamp > restoredAt, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(manager.Items.Single(), Is.SameAs(subscription));
            Assert.That(recorder.Errors, Is.Empty);
        }

        /// <summary>
        /// FailoverAsync switches a connected session to an explicit target
        /// without a connection loss and closes the session on the Server left.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailoverAsyncSwitchesToExplicitTargetAsync(bool useChannelManager)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using RedundantSet set = await RedundantSet.StartAsync(timeout.Token).ConfigureAwait(false);
            await using ClientChannelManager? channels = useChannelManager
                ? new ClientChannelManager(set.Client.Config, set.Telemetry)
                : null;
            set.Primary.SetServiceLevel(ServiceLevels.Maximum);
            set.Backup.SetServiceLevel(230);
            await using UaManagedSession session = await set.ConnectAsync(
                new ServerRedundancyOptions(), timeout.Token, channelManager: channels).ConfigureAwait(false);
            Assert.That(set.Primary.SessionCount, Is.EqualTo(1));
            ConcurrentQueue<ConnectionState> states = RecordStatesAfterConnect(session);

            await session.FailoverAsync(set.Backup.Endpoint, timeout.Token).ConfigureAwait(false);
            await WaitUntilAsync(
                () => states.SkipWhile(s => s != ConnectionState.Failover).Contains(ConnectionState.Connected),
                timeout.Token).ConfigureAwait(false);

            Assert.That(ServerUriOf(session), Is.EqualTo(set.Backup.ApplicationUri));
            // the Connected change of the initial connect can be raised after the handler is
            // attached; only the transitions from the failover on belong to this call.
            Assert.That(
                states.SkipWhile(s => s != ConnectionState.Failover).ToArray(),
                Is.EqualTo(new[] { ConnectionState.Failover, ConnectionState.Connected }));
            DataValue level = await session.ReadValueAsync(VariableIds.Server_ServiceLevel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(level.GetValue<byte>(0), Is.EqualTo(230));
            Assert.That(session.RedundancyInfo?.ServiceLevel, Is.EqualTo(230));
            Assert.That(
                await EventuallyAsync(() => set.Primary.SessionCount == 0).ConfigureAwait(false),
                Is.True,
                "The session on the Server left must be closed there.");
            Assert.That(set.Backup.SessionCount, Is.EqualTo(1));
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

        /// <summary>
        /// Records the connection states the session enters from now on. The connect
        /// call can return before the notification of the initial Connecting to Connected
        /// transition is delivered, so that late notification is not recorded.
        /// </summary>
        private static ConcurrentQueue<ConnectionState> RecordStatesAfterConnect(UaManagedSession session)
        {
            var states = new ConcurrentQueue<ConnectionState>();
            session.ConnectionStateChanged += (_, e) =>
            {
                if (e.PreviousState != ConnectionState.Connecting)
                {
                    states.Enqueue(e.NewState);
                }
            };
            return states;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }

        private static async Task<bool> EventuallyAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            return true;
        }

        private static RedundantServer Member(ServerRedundancyInfo info, RedundantMember member)
        {
            return info.RedundantServers.ToList().Single(s => s.ServerUri == member.ApplicationUri);
        }

        private static EventFilter CreateRefreshEventFilter()
        {
            return new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    }
                ]
            };
        }

        private static void RecordRefreshMarker(ArrayOf<Variant> fields, ConcurrentQueue<NodeId> markers)
        {
            if (fields.Count != 0 && fields[0].TryGetValue(out NodeId eventType) &&
                (eventType == ObjectTypeIds.RefreshStartEventType || eventType == ObjectTypeIds.RefreshEndEventType))
            {
                markers.Enqueue(eventType);
            }
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

            public static async Task<RedundantSet> StartAsync(CancellationToken ct, string? backupMemberUri = null)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                string root = Path.Combine(Path.GetTempPath(), "redundancy-" + Guid.NewGuid().ToString("N")[..8]);
                var set = new RedundantSet(root, telemetry);
                try
                {
                    set.Primary = await RedundantMember.StartAsync(root, "Primary").ConfigureAwait(false);
                    set.Backup = await RedundantMember.StartAsync(root, "Backup").ConfigureAwait(false);
                    await set.Primary.PublishSetAsync([backupMemberUri ?? set.Backup.ApplicationUri], ct)
                        .ConfigureAwait(false);
                    await set.Backup.PublishSetAsync([set.Primary.ApplicationUri], ct).ConfigureAwait(false);
                    await set.Client.LoadClientConfigurationAsync(root, "RedundancyClient").ConfigureAwait(false);
                    set.Primary.Endpoint = await set.Client.GetEndpointAsync(
                        new Uri(set.Primary.Url), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                    set.Backup.Endpoint = await set.Client.GetEndpointAsync(
                        new Uri(set.Backup.Url), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
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
                bool withRedundancyHandler = true,
                ConfiguredEndpoint? backupEndpoint = null,
                IUserIdentity? identity = null,
                IClientChannelManager? channelManager = null,
                ISubscriptionEngineFactory? engineFactory = null)
            {
                var resolver = new MemberEndpointResolver(new Dictionary<string, ConfiguredEndpoint>
                {
                    [Primary.ApplicationUri] = Primary.Endpoint,
                    [Backup.ApplicationUri] = backupEndpoint ?? Backup.Endpoint
                });
                return UaManagedSession.CreateAsync(
                    Client.Config,
                    Primary.Endpoint,
                    Client.SessionFactory,
                    identity: identity,
                    redundancyHandler: withRedundancyHandler
                        ? new DefaultServerRedundancyHandler(resolver, options: options)
                        : null,
                    telemetry: Telemetry,
                    channelManager: channelManager,
                    engineFactory: engineFactory,
                    serverRedundancy: options with { ExpectedServerApplicationUri = Primary.ApplicationUri },
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
                server.CurrentInstance.SessionManager.SessionCreated +=
                    (_, _) => Interlocked.Increment(ref m_createdSessions);
                server.CurrentInstance.SessionManager.SessionActivated +=
                    (session, _) =>
                    {
                        ActivatedIdentityTypes.Enqueue(session.EffectiveIdentity.TokenType);
                        ActivatedIdentityNames.Enqueue(session.EffectiveIdentity.DisplayName);
                    };
            }

            public string ApplicationUri { get; }

            public string Url { get; }

            public ConfiguredEndpoint Endpoint { get; set; } = null!;

            /// <summary>
            /// The sessions currently allocated on the server.
            /// </summary>
            public int SessionCount => m_server.CurrentInstance.SessionManager.GetSessions().Count;

            /// <summary>
            /// The sessions created on the server since it started.
            /// </summary>
            public int CreatedSessionCount => Volatile.Read(ref m_createdSessions);

            public ConcurrentQueue<UserTokenType> ActivatedIdentityTypes { get; } = new();

            public ConcurrentQueue<string> ActivatedIdentityNames { get; } = new();

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
                fixture.Config.ServerConfiguration!.UserTokenPolicies =
                    [new(UserTokenType.Anonymous), new(UserTokenType.UserName)];
                ReferenceServer server = await fixture.StartAsync(pki).ConfigureAwait(false);
                return new RedundantMember(fixture, server);
            }

            public void SetServiceLevel(byte level)
            {
                m_setServiceLevel(level);
            }

            public void SetEstimatedReturnTime(DateTime time)
            {
                m_server.CurrentInstance.ServerObject!.EstimatedReturnTime!.Value = time;
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
                    : variableId == VariableIds.Server_ServerStatus_State
                        ? serverObject.ServerStatus!.State!
                        : variableId == VariableIds.Server_NamespaceArray
                            ? serverObject.NamespaceArray!
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
            private int m_createdSessions;
        }

        /// <summary>
        /// Records data changes and condition refresh markers from a subscription.
        /// </summary>
        private sealed class FailoverNotificationRecorder : ISubscriptionNotificationHandler
        {
            public int Count => Volatile.Read(ref m_count);

            public DateTime LatestSourceTimestamp => new(Interlocked.Read(ref m_latestTicks), DateTimeKind.Utc);

            public ConcurrentQueue<StatusCode> Errors { get; } = new();

            public ConcurrentQueue<NodeId> RefreshMarkers { get; } = new();

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
                foreach (EventNotification value in notification.Span)
                {
                    RecordRefreshMarker(value.Fields, RefreshMarkers);
                }
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
