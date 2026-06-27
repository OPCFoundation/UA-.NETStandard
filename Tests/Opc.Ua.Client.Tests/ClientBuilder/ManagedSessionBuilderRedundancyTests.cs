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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Redundant client wiring tests for <see cref="ManagedSessionBuilder"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class ManagedSessionBuilderRedundancyTests
    {
        [Test]
        public void ConnectRedundantAsyncWithoutEndpointThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await builder.ConnectRedundantAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ConnectRedundantAsyncCreatesSessionsForResolvedPeersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            ConfiguredEndpoint backupEndpoint = CreateEndpoint("urn:backup");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        },
                        new RedundantServer
                        {
                            ServerUri = "urn:backup",
                            ServiceLevel = 230,
                            ServerState = ServerState.Running,
                            Endpoint = backupEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.CreatedEndpoints, Has.Count.EqualTo(2));
                Assert.That(factory.CreatedEndpoints[0], Is.SameAs(primaryEndpoint));
                Assert.That(factory.CreatedEndpoints[1], Is.SameAs(backupEndpoint));
                Assert.That(client.CurrentRedundantSession, Is.SameAs(factory.Sessions[1]));
                Assert.That(factory.Sessions[1].ConnectCount, Is.EqualTo(1));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncFiltersOutDuplicateEndpointsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        },
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.CreatedEndpoints, Has.Count.EqualTo(1));
                Assert.That(factory.CreatedEndpoints[0], Is.SameAs(primaryEndpoint));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncWithTokenReuseFailoverOptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .WithTokenReuseFailover(enable: true)
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.Sessions, Has.Count.GreaterThan(0));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncWithTransferSubscriptionsOptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            ConfiguredEndpoint backupEndpoint = CreateEndpoint("urn:backup");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        },
                        new RedundantServer
                        {
                            ServerUri = "urn:backup",
                            ServiceLevel = 230,
                            ServerState = ServerState.Running,
                            Endpoint = backupEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .WithTransferSubscriptionsOnRecreate(transferOnRecreate: true)
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.CreatedEndpoints, Has.Count.EqualTo(2));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncWithSessionNameOptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .WithSessionName("MyRedundantClient")
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.Sessions, Has.Count.GreaterThan(0));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncWithPreferredLocalesOptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .WithPreferredLocales("en", "de")
                .UseRedundantManagedClientSessionFactory(factory);

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync()
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.Sessions, Has.Count.GreaterThan(0));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectRedundantAsyncWithCustomRedundantOptionsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint("urn:primary");
            var factory = new RecordingRedundantSessionFactory(
                new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 100,
                    ServiceLevelAccessible = true,
                    ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = "urn:primary",
                            ServiceLevel = 100,
                            ServerState = ServerState.Running,
                            Endpoint = primaryEndpoint
                        }
                    ]
                });
            var handler = new Mock<IServerRedundancyHandler>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(primaryEndpoint)
                .WithServerRedundancy(handler.Object)
                .UseRedundantManagedClientSessionFactory(factory);

            var redundantOptions = new RedundantManagedClientOptions
            {
                HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
            };

            RedundantManagedClient client = await builder
                .ConnectRedundantAsync(redundantOptions)
                .ConfigureAwait(false);
            try
            {
                Assert.That(factory.Sessions, Has.Count.GreaterThan(0));
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ApplicationConfiguration CreateConfig(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
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

        private sealed class RecordingRedundantSessionFactory : IRedundantManagedClientSessionFactory
        {
            public RecordingRedundantSessionFactory(ServerRedundancyInfo redundancyInfo)
            {
                m_redundancyInfo = redundancyInfo;
            }

            public List<ConfiguredEndpoint> CreatedEndpoints { get; } = [];

            public List<RecordingRedundantSession> Sessions { get; } = [];

            public ValueTask<IRedundantManagedClientSession> CreateAsync(
                ConfiguredEndpoint endpoint,
                CancellationToken ct = default)
            {
                CreatedEndpoints.Add(endpoint);
                var session = new RecordingRedundantSession(endpoint, m_redundancyInfo);
                Sessions.Add(session);
                return new ValueTask<IRedundantManagedClientSession>(session);
            }

            private readonly ServerRedundancyInfo m_redundancyInfo;
        }

        private sealed class RecordingRedundantSession : IRedundantManagedClientSession
        {
            public RecordingRedundantSession(
                ConfiguredEndpoint endpoint,
                ServerRedundancyInfo redundancyInfo)
            {
                Endpoint = endpoint;
                m_redundancyInfo = redundancyInfo;
                ServiceLevel = endpoint.Description.Server.ApplicationUri == "urn:backup" ? (byte)230 : (byte)100;
            }

            public event EventHandler<RedundantManagedClientNotificationEventArgs>? NotificationReceived
            {
                add { }
                remove { }
            }

            public ConfiguredEndpoint Endpoint { get; }

            public Opc.Ua.Client.ManagedSession? Session => null;

            public bool IsConnected { get; private set; }

            public byte ServiceLevel { get; }

            public int ConnectCount { get; private set; }

            public ValueTask ConnectAsync(CancellationToken ct = default)
            {
                IsConnected = true;
                ConnectCount++;
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
                IsConnected = true;
                return default;
            }

            public ValueTask<byte> ReadServiceLevelAsync(CancellationToken ct = default)
            {
                return new ValueTask<byte>(ServiceLevel);
            }

            public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
                IServerRedundancyHandler handler,
                CancellationToken ct = default)
            {
                return new ValueTask<ServerRedundancyInfo>(m_redundancyInfo);
            }

            public ValueTask AddSubscriptionAsync(
                string subscriptionKey,
                Subscription template,
                MonitoringMode monitoringMode,
                bool publishingEnabled,
                CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask SetSubscriptionStateAsync(
                MonitoringMode monitoringMode,
                bool publishingEnabled,
                CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private readonly ServerRedundancyInfo m_redundancyInfo;
        }
    }
}
