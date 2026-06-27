/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Tests for <see cref="RedundantManagedClient"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class RedundantManagedClientTests
    {
        [Test]
        public async Task ColdReconnectsAndResubscribesOnFailoverAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 100);
            FakeRedundantSession backup = new("urn:backup", 230);
            Mock<IServerRedundancyHandler> handler = CreateHandler(
                RedundancySupport.Cold,
                backup.Endpoint);
            handler.Setup(h => h.SelectFailoverTarget(
                    It.IsAny<ServerRedundancyInfo>(),
                    primary.Endpoint))
                .Returns(backup.Endpoint);
            RedundantManagedClient client = CreateClient(handler.Object, primary, backup);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                await client.FailoverAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(primary.ConnectCount, Is.EqualTo(1));
            Assert.That(backup.ConnectCount, Is.EqualTo(1));
            Assert.That(backup.AddedSubscriptions, Is.EqualTo(1));
            Assert.That(backup.LastMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(backup.LastPublishingEnabled, Is.True);
            Assert.That(client.CurrentRedundantSession, Is.SameAs(backup));
        }

        [Test]
        public async Task WarmTogglesSamplingAndPublishingOnFailoverAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 230);
            FakeRedundantSession backup = new("urn:backup", 100);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Warm, backup.Endpoint).Object,
                primary,
                backup);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                Assert.That(backup.LastMonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
                Assert.That(backup.LastPublishingEnabled, Is.False);
                primary.ServiceLevelValue = 100;
                backup.ServiceLevelValue = 240;
                await client.FailoverAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(primary.LastMonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
            Assert.That(primary.LastPublishingEnabled, Is.False);
            Assert.That(backup.LastMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(backup.LastPublishingEnabled, Is.True);
            Assert.That(client.CurrentRedundantSession, Is.SameAs(backup));
        }

        [Test]
        public async Task HotReportingHandoffMovesReportingToNextHighestAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                primary,
                backup);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                primary.ServiceLevelValue = 100;
                backup.ServiceLevelValue = 250;
                await client.FailoverAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(primary.LastMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
            Assert.That(primary.LastPublishingEnabled, Is.False);
            Assert.That(backup.LastMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(backup.LastPublishingEnabled, Is.True);
        }

        [Test]
        public async Task HotReportingMergeDeduplicatesSameSampleWithIndependentSequenceNumbersAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
                },
                primary,
                backup);
            var received = new List<RedundantManagedClientNotificationEventArgs>();
            client.NotificationReceived += (_, e) => received.Add(e);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                DateTime sourceTimestamp = new(2026, 6, 27, 9, 0, 0, DateTimeKind.Utc);
                primary.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 10, sourceTimestamp, 42);
                backup.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 27, sourceTimestamp, 42);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(primary.LastMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(backup.LastMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(received, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task HotReportingMergePreservesBufferedValuesWithSameSequenceNumberAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
                },
                primary,
                backup);
            var received = new List<RedundantManagedClientNotificationEventArgs>();
            client.NotificationReceived += (_, e) => received.Add(e);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                DateTime first = new(2026, 6, 27, 9, 0, 0, DateTimeKind.Utc);
                DateTime second = first.AddMilliseconds(100);
                primary.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 10, first, 42);
                primary.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 10, second, 43);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task HotReportingMergeDeliversDistinctValuesWithSameHandleAndTimestampAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
                },
                primary,
                backup);
            var received = new List<RedundantManagedClientNotificationEventArgs>();
            client.NotificationReceived += (_, e) => received.Add(e);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                DateTime sourceTimestamp = new(2026, 6, 27, 9, 0, 0, DateTimeKind.Utc);
                primary.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 10, sourceTimestamp, 42);
                backup.RaiseNotification("sub", clientHandle: 1, sequenceNumber: 27, sourceTimestamp, 43);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task HotReportingMergeDeduplicationIsConcurrentAndBoundedAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
                },
                primary,
                backup);
            int received = 0;
            object receivedLock = new();
            client.NotificationReceived += (_, _) =>
            {
                lock (receivedLock)
                {
                    received++;
                }
            };
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                DateTime sourceTimestamp = new(2026, 6, 27, 9, 0, 0, DateTimeKind.Utc);
                Task first = Task.Run(() =>
                {
                    for (int ii = 0; ii < 100; ii++)
                    {
                        primary.RaiseNotification(
                            "sub",
                            clientHandle: 1,
                            (uint)ii,
                            sourceTimestamp,
                            42);
                    }
                });
                Task second = Task.Run(() =>
                {
                    for (int ii = 0; ii < 100; ii++)
                    {
                        backup.RaiseNotification(
                            "sub",
                            clientHandle: 1,
                            (uint)(ii + 1000),
                            sourceTimestamp,
                            42);
                    }
                });
                await Task.WhenAll(first, second).ConfigureAwait(false);

                for (int ii = 0; ii < 5000; ii++)
                {
                    primary.RaiseNotification(
                        "sub",
                        clientHandle: 1,
                        (uint)(ii + 2000),
                        sourceTimestamp.AddTicks(ii + 1),
                        ii);
                }
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Is.EqualTo(5001));
            Assert.That(GetSeenNotificationCount(client), Is.LessThanOrEqualTo(4096));
        }

        [Test]
        public async Task StartSelectsHighestServiceLevelSessionAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 100);
            FakeRedundantSession backup = new("urn:backup", 250);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                primary,
                backup);
            try
            {
                await client.StartAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(client.CurrentRedundantSession, Is.SameAs(backup));
        }

        [Test]
        public async Task HotAndMirroredUsesSingleActiveSessionAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.HotAndMirrored, backup.Endpoint).Object,
                primary,
                backup);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(primary.ConnectCount, Is.EqualTo(1));
            Assert.That(backup.ConnectCount, Is.Zero);
            Assert.That(primary.AddedSubscriptions, Is.EqualTo(1));
            Assert.That(backup.AddedSubscriptions, Is.Zero);
        }

        [Test]
        public async Task DisposeDisposesOwnedSubscriptionTemplateAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            var subscription = new TrackingSubscription();
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.Hot, backup.Endpoint).Object,
                primary,
                backup);

            await client.StartAsync().ConfigureAwait(false);
            await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);

            Assert.That(subscription.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task HotAndMirroredFailoverActivatesBackupWithoutRecreatingSubscriptionsAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.HotAndMirrored, backup.Endpoint).Object,
                primary,
                backup);
            try
            {
                var subscription = new Subscription((ITelemetryContext)null!);
                await client.StartAsync().ConfigureAwait(false);
                await client.AddSubscriptionAsync("sub", subscription).ConfigureAwait(false);
                backup.ServiceLevelValue = 250;
                await client.FailoverAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(backup.ActivateMirroredCount, Is.EqualTo(1));
            Assert.That(primary.AddedSubscriptions, Is.EqualTo(1));
            Assert.That(backup.AddedSubscriptions, Is.Zero);
            Assert.That(client.CurrentRedundantSession, Is.SameAs(backup));
        }

        [Test]
        public async Task HotAndMirroredFailoverSelectsHighestServiceLevelBackupAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession lowBackup = new("urn:low", 100);
            FakeRedundantSession highBackup = new("urn:high", 110);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.HotAndMirrored, lowBackup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    EnableHotAndMirroredStatusChecks = true
                },
                primary,
                lowBackup,
                highBackup);
            try
            {
                await client.StartAsync().ConfigureAwait(false);
                highBackup.ServiceLevelValue = 250;
                await client.FailoverAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(lowBackup.ActivateMirroredCount, Is.Zero);
            Assert.That(highBackup.ActivateMirroredCount, Is.EqualTo(1));
            Assert.That(client.CurrentRedundantSession, Is.SameAs(highBackup));
        }

        [Test]
        public async Task HotAndMirroredStatusChecksPollBackupServiceLevelAsync()
        {
            FakeRedundantSession primary = new("urn:primary", 240);
            FakeRedundantSession backup = new("urn:backup", 220);
            RedundantManagedClient client = CreateClient(
                CreateHandler(RedundancySupport.HotAndMirrored, backup.Endpoint).Object,
                new RedundantManagedClientOptions
                {
                    EnableHotAndMirroredStatusChecks = true,
                    HotAndMirroredStatusCheckInterval = TimeSpan.FromMilliseconds(10)
                },
                primary,
                backup);
            try
            {
                await client.StartAsync().ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(backup.ConnectCount, Is.EqualTo(1));
            Assert.That(backup.ReadServiceLevelCount, Is.GreaterThan(1));
        }

        private static RedundantManagedClient CreateClient(
            IServerRedundancyHandler handler,
            params FakeRedundantSession[] sessions)
        {
            return CreateClient(handler, new RedundantManagedClientOptions(), sessions);
        }

        private static RedundantManagedClient CreateClient(
            IServerRedundancyHandler handler,
            RedundantManagedClientOptions options,
            params FakeRedundantSession[] sessions)
        {
            return new RedundantManagedClient(
                handler,
                new ArrayOf<IRedundantManagedClientSession>(sessions),
                options);
        }

        private static Mock<IServerRedundancyHandler> CreateHandler(
            RedundancySupport mode,
            ConfiguredEndpoint backupEndpoint)
        {
            var handler = new Mock<IServerRedundancyHandler>(MockBehavior.Strict);
            handler.Setup(h => h.FetchRedundancyInfoAsync(
                    It.IsAny<ISession>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateInfo(mode, backupEndpoint));
            handler.Setup(h => h.ShouldFailover(
                    It.IsAny<ServerRedundancyInfo>(),
                    It.IsAny<ConfiguredEndpoint>()))
                .Returns(new ServerFailoverDecision(true, DateTime.MinValue, "test"));
            handler.Setup(h => h.SelectFailoverTarget(
                    It.IsAny<ServerRedundancyInfo>(),
                    It.IsAny<ConfiguredEndpoint>()))
                .Returns(backupEndpoint);
            return handler;
        }

        private static ServerRedundancyInfo CreateInfo(
            RedundancySupport mode,
            ConfiguredEndpoint backupEndpoint)
        {
            return new ServerRedundancyInfo
            {
                Mode = mode,
                ServiceLevel = 100,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                RedundantServers =
                [
                    new RedundantServer
                    {
                        ServerUri = backupEndpoint.Description.Server.ApplicationUri!,
                        ServiceLevel = 230,
                        ServerState = ServerState.Running,
                        Endpoint = backupEndpoint
                    }
                ]
            };
        }

        private static ConfiguredEndpoint CreateEndpoint(string serverUri)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = $"opc.tcp://{serverUri[4..]}:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri
                }
            };

            return new ConfiguredEndpoint(null, description, configuration: null);
        }

        private static int GetSeenNotificationCount(RedundantManagedClient client)
        {
            FieldInfo field = typeof(RedundantManagedClient).GetField(
                "m_seenNotifications",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var seen = (System.Collections.IEnumerable)field.GetValue(client)!;
            int count = 0;
            foreach (object _ in seen)
            {
                count++;
            }
            return count;
        }

        private sealed class TrackingSubscription : Subscription
        {
            public TrackingSubscription()
                : base((ITelemetryContext)null!)
            {
            }

            public int DisposeCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }

                base.Dispose(disposing);
            }
        }

        private sealed class FakeRedundantSession : IRedundantManagedClientSession
        {
            public FakeRedundantSession(string serverUri, byte serviceLevel)
            {
                Endpoint = CreateEndpoint(serverUri);
                ServiceLevelValue = serviceLevel;
            }

            public event EventHandler<RedundantManagedClientNotificationEventArgs>? NotificationReceived;

            public ConfiguredEndpoint Endpoint { get; }

            public Opc.Ua.Client.ManagedSession? Session => null;

            public bool IsConnected { get; private set; }

            public byte ServiceLevel { get; private set; }

            public byte ServiceLevelValue { get; set; }

            public int ConnectCount { get; private set; }

            public int AddedSubscriptions { get; private set; }

            public int ActivateMirroredCount { get; private set; }

            public int ReadServiceLevelCount { get; private set; }

            public MonitoringMode LastMonitoringMode { get; private set; }

            public bool LastPublishingEnabled { get; private set; }

            public ValueTask ConnectAsync(CancellationToken ct = default)
            {
                IsConnected = true;
                ConnectCount++;
                ServiceLevel = ServiceLevelValue;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                IsConnected = false;
                return default;
            }

            public ValueTask ActivateMirroredSessionAsync(
                IRedundantManagedClientSession source,
                CancellationToken ct = default)
            {
                ActivateMirroredCount++;
                IsConnected = true;
                ServiceLevel = ServiceLevelValue;
                return default;
            }

            public ValueTask<byte> ReadServiceLevelAsync(CancellationToken ct = default)
            {
                ReadServiceLevelCount++;
                ServiceLevel = ServiceLevelValue;
                return new ValueTask<byte>(ServiceLevel);
            }

            public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
                IServerRedundancyHandler handler,
                CancellationToken ct = default)
            {
                return handler.FetchRedundancyInfoAsync(null!, ct);
            }

            public ValueTask AddSubscriptionAsync(
                string subscriptionKey,
                Subscription template,
                MonitoringMode monitoringMode,
                bool publishingEnabled,
                CancellationToken ct = default)
            {
                AddedSubscriptions++;
                LastMonitoringMode = monitoringMode;
                LastPublishingEnabled = publishingEnabled;
                return default;
            }

            public ValueTask SetSubscriptionStateAsync(
                MonitoringMode monitoringMode,
                bool publishingEnabled,
                CancellationToken ct = default)
            {
                LastMonitoringMode = monitoringMode;
                LastPublishingEnabled = publishingEnabled;
                return default;
            }

            public void RaiseNotification(
                string subscriptionKey,
                uint clientHandle,
                uint sequenceNumber)
            {
                RaiseNotification(
                    subscriptionKey,
                    clientHandle,
                    sequenceNumber,
                    DateTime.MinValue,
                    1);
            }

            public void RaiseNotification(
                string subscriptionKey,
                uint clientHandle,
                uint sequenceNumber,
                DateTime sourceTimestamp,
                int value)
            {
                NotificationReceived?.Invoke(
                    this,
                    new RedundantManagedClientNotificationEventArgs(
                        Endpoint.Description.Server.ApplicationUri!,
                        subscriptionKey,
                        clientHandle,
                        sequenceNumber,
                        new DataValue(
                            new Variant(value),
                            StatusCodes.Good,
                            sourceTimestamp)));
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
