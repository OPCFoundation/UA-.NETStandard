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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for <see cref="DefaultServerRedundancyHandler"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class ServerRedundancyHandlerTests
    {
        private static readonly DateTime s_now = new(2026, 6, 27, 4, 0, 0, DateTimeKind.Utc);
        private Mock<IRedundantServerEndpointResolver> m_resolver = null!;
        private DefaultServerRedundancyHandler m_handler = null!;

        [SetUp]
        public void SetUp()
        {
            m_resolver = new Mock<IRedundantServerEndpointResolver>(MockBehavior.Strict);
            m_handler = new DefaultServerRedundancyHandler(m_resolver.Object, new FixedTimeProvider(s_now));
        }

        [TestCase(0, RedundancySupport.None)]
        [TestCase(1, RedundancySupport.Cold)]
        [TestCase(2, RedundancySupport.Warm)]
        [TestCase(3, RedundancySupport.Hot)]
        [TestCase(4, RedundancySupport.Transparent)]
        [TestCase(5, RedundancySupport.HotAndMirrored)]
        public async Task FetchRedundancyInfoMapsRedundancySupportAsync(
            int value,
            RedundancySupport expected)
        {
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: value,
                serviceLevel: ServiceLevels.HealthyMinimum);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(expected));
        }

        [Test]
        public void ShouldFailoverStaysWhileCurrentServerIsHealthy()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.HealthyMinimum,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Healthy,
                RedundantServers =
                [
                    CreateServerInfo("urn:backup", ServiceLevels.Maximum, ServerState.Running)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
            Assert.That(target, Is.Null);
        }

        [Test]
        public void ShouldFailoverSwitchesFromDegradedToHealthyPeer()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.DegradedMaximum,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                RedundantServers =
                [
                    CreateServerInfo("urn:degraded", 150, ServerState.Running),
                    CreateServerInfo("urn:healthy", 230, ServerState.Running)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.True);
            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Description.Server.ApplicationUri, Is.EqualTo("urn:healthy"));
        }

        [Test]
        public void ShouldFailoverDoesNotSwitchFromDegradedWithoutHealthyPeer()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = 100,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Degraded,
                RedundantServers =
                [
                    CreateServerInfo("urn:degraded", 150, ServerState.Running)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
        }

        [Test]
        public void ShouldFailoverSwitchesFromMaintenanceToOperationalPeer()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.Maintenance,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Maintenance,
                EstimatedReturnTime = s_now.AddMinutes(10),
                RedundantServers =
                [
                    CreateServerInfo("urn:healthy", 230, ServerState.Running)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.True);
            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Description.Server.ApplicationUri, Is.EqualTo("urn:healthy"));
        }

        [Test]
        public void ShouldFailoverHonorsMaintenanceEstimatedReturnTime()
        {
            DateTime estimatedReturnTime = s_now.AddMinutes(10);
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.Maintenance,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Maintenance,
                EstimatedReturnTime = estimatedReturnTime,
                RedundantServers =
                [
                    CreateServerInfo("urn:shutdown", 230, ServerState.Shutdown)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
            Assert.That(decision.RetryAfter, Is.EqualTo(estimatedReturnTime));
        }

        [Test]
        public void ShouldFailoverUsesBackoffWhenMaintenanceReturnTimeLapsed()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.Maintenance,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Maintenance,
                EstimatedReturnTime = s_now.AddMinutes(-1),
                RedundantServers =
                [
                    CreateServerInfo("urn:shutdown", 230, ServerState.Shutdown)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
            Assert.That(
                decision.RetryAfter,
                Is.EqualTo(s_now.Add(DefaultServerRedundancyHandler.DefaultMaintenanceBackoff)));
        }

        [Test]
        public void ShouldFailoverUsesBackoffWhenMaintenanceReturnTimeIsAbsent()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.Maintenance,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.Maintenance,
                EstimatedReturnTime = DateTime.MinValue,
                RedundantServers =
                [
                    CreateServerInfo("urn:shutdown", 230, ServerState.Shutdown)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
            Assert.That(
                decision.RetryAfter,
                Is.EqualTo(s_now.Add(DefaultServerRedundancyHandler.DefaultMaintenanceBackoff)));
        }

        [Test]
        public void ShouldFailoverReturnsNoFailoverWhenAllPeersAreDown()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:shutdown", ServiceLevels.Maximum, ServerState.Shutdown),
                    CreateServerInfo("urn:suspended", ServiceLevels.Maximum, ServerState.Suspended)
                ]
            };

            ServerFailoverDecision decision = m_handler.ShouldFailover(
                info, CreateCurrentEndpoint("urn:current"));
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(decision.IsFailoverWarranted, Is.False);
            Assert.That(target, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetReturnsNullForTransparentMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Transparent,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:backup", 230, ServerState.Running)
                ]
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetReturnsNullForNoneMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.None,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = false,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:backup", 230, ServerState.Running)
                ]
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetSkipsCurrentEndpoint()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:current", ServiceLevels.Maximum, ServerState.Running),
                    CreateServerInfo("urn:backup", 230, ServerState.Running)
                ]
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Description.Server.ApplicationUri, Is.EqualTo("urn:backup"));
        }

        [Test]
        public void SelectFailoverTargetSkipsNonRunningServers()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:suspended", ServiceLevels.Maximum, ServerState.Suspended),
                    CreateServerInfo("urn:shutdown", 240, ServerState.Shutdown),
                    CreateServerInfo("urn:running", 230, ServerState.Running)
                ]
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Description.Server.ApplicationUri, Is.EqualTo("urn:running"));
        }

        [Test]
        public void SelectFailoverTargetReturnsNullWhenNoViableServers()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancySupport.Hot,
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelAccessible = true,
                ServiceLevelSubrange = ServiceLevelSubrange.NoData,
                RedundantServers =
                [
                    CreateServerInfo("urn:current", 230, ServerState.Running),
                    CreateServerInfo("urn:down", 230, ServerState.Shutdown)
                ]
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task FetchRedundancyInfoReadsRedundantServerArrayAsync()
        {
            var serverData = new RedundantServerDataType
            {
                ServerId = "urn:server1",
                ServiceLevel = 230,
                ServerState = ServerState.Running
            };
            ConfiguredEndpoint resolvedEndpoint = CreateEndpoint("urn:server1", "opc.tcp://server1:4840");
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:server1",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedEndpoint);

            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: 100,
                redundantServers: [serverData]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancySupport.Hot));
            Assert.That(info.ServiceLevelSubrange, Is.EqualTo(ServiceLevelSubrange.Degraded));
            Assert.That(info.RedundantServers, Has.Count.EqualTo(1));
            Assert.That(info.RedundantServers[0].ServerUri, Is.EqualTo("urn:server1"));
            Assert.That(info.RedundantServers[0].Endpoint, Is.SameAs(resolvedEndpoint));
        }

        [Test]
        public async Task FetchRedundancyInfoResolvesServerUriArrayToEndpointsAsync()
        {
            ConfiguredEndpoint resolvedEndpoint = CreateEndpoint("urn:server-uri", "opc.tcp://server-uri:4840");
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:server-uri",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedEndpoint);

            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: ServiceLevels.NoData,
                serverUris: ["urn:server-uri"]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.RedundantServers, Has.Count.EqualTo(1));
            Assert.That(info.RedundantServers[0].ServerUri, Is.EqualTo("urn:server-uri"));
            Assert.That(info.RedundantServers[0].Endpoint, Is.SameAs(resolvedEndpoint));
            Assert.That(
                info.RedundantServers[0].Endpoint!.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://server-uri:4840"));
            VerifyBatchedRedundancyRead(mockSession);
        }

        [Test]
        public async Task FetchRedundancyInfoExcludesUnresolvedServerUriFromSelectionAsync()
        {
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:unresolved",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfiguredEndpoint?)null);
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: ServiceLevels.NoData,
                serverUris: ["urn:unresolved"]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(info.RedundantServers, Has.Count.EqualTo(1));
            Assert.That(info.RedundantServers[0].Endpoint, Is.Null);
            Assert.That(target, Is.Null);
        }

        [Test]
        public async Task FetchRedundancyInfoSelectsServerUriOnlyPeerWithUnknownServiceLevelAsync()
        {
            ConfiguredEndpoint resolvedEndpoint = CreateEndpoint("urn:server-uri", "opc.tcp://server-uri:4840");
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:server-uri",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedEndpoint);
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: ServiceLevels.NoData,
                serverUris: ["urn:server-uri"]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(info.RedundantServers[0].ServiceLevel, Is.EqualTo(ServiceLevels.NoData));
            Assert.That(target, Is.SameAs(resolvedEndpoint));
        }

        [Test]
        public async Task FetchRedundancyInfoFallsBackToUnknownServerUriOnlyPeerAsync()
        {
            var serverData = new RedundantServerDataType
            {
                ServerId = "urn:nodata",
                ServiceLevel = ServiceLevels.NoData,
                ServerState = ServerState.Running
            };
            ConfiguredEndpoint noDataEndpoint = CreateEndpoint("urn:nodata", "opc.tcp://nodata:4840");
            ConfiguredEndpoint unknownEndpoint = CreateEndpoint("urn:server-uri", "opc.tcp://server-uri:4840");
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:nodata",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(noDataEndpoint);
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:server-uri",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(unknownEndpoint);
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: ServiceLevels.DegradedMaximum,
                redundantServers: [serverData],
                serverUris: ["urn:nodata", "urn:server-uri"]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);
            ConfiguredEndpoint? target = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(info.RedundantServers, Has.Count.EqualTo(2));
            Assert.That(target, Is.SameAs(unknownEndpoint));
        }

        [Test]
        public async Task FetchRedundancyInfoCachesResolvedEndpointsAsync()
        {
            var serverData = new RedundantServerDataType
            {
                ServerId = "urn:server1",
                ServiceLevel = 230,
                ServerState = ServerState.Running
            };
            ConfiguredEndpoint resolvedEndpoint = CreateEndpoint("urn:server1", "opc.tcp://server1:4840");
            m_resolver.Setup(r => r.ResolveAsync(
                    "urn:server1",
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedEndpoint);
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: 100,
                redundantServers: [serverData]);

            await m_handler.FetchRedundancyInfoAsync(mockSession.Object).ConfigureAwait(false);
            await m_handler.FetchRedundancyInfoAsync(mockSession.Object).ConfigureAwait(false);

            m_resolver.Verify(r => r.ResolveAsync(
                "urn:server1",
                It.IsAny<ConfiguredEndpoint>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task FetchRedundancyInfoReadsTransparentCurrentServerIdAsync()
        {
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Transparent,
                serviceLevel: ServiceLevels.HealthyMinimum,
                currentServerId: "server-a");

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.CurrentServerId, Is.EqualTo("server-a"));
        }

        [Test]
        public async Task FetchRedundancyInfoHandlesReadErrorsAsync()
        {
            Mock<ISession> mockSession = CreateMockSessionWithBadStatus();

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancySupport.None));
            Assert.That(info.ServiceLevel, Is.Zero);
            Assert.That(info.ServiceLevelAccessible, Is.False);
            Assert.That(info.RedundantServers, Is.Empty);
        }

        [Test]
        public async Task FetchRedundancyInfoHandlesMalformedOptionalNodesAsync()
        {
            Mock<ISession> mockSession = CreateMockSessionWithThrowingOptionalReads();

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancySupport.Hot));
            Assert.That(info.RedundantServers, Is.Empty);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var info = new ServerRedundancyInfo();
            ConfiguredEndpoint endpoint = CreateCurrentEndpoint("urn:current");

            Assert.That(
                async () => await m_handler.FetchRedundancyInfoAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
            Assert.That(() => m_handler.ShouldFailover(null!, endpoint), Throws.ArgumentNullException);
            Assert.That(() => m_handler.ShouldFailover(info, null!), Throws.ArgumentNullException);
            Assert.That(() => m_handler.SelectFailoverTarget(null!, endpoint), Throws.ArgumentNullException);
            Assert.That(() => m_handler.SelectFailoverTarget(info, null!), Throws.ArgumentNullException);
        }

        private static RedundantServer CreateServerInfo(
            string uri,
            byte serviceLevel,
            ServerState state)
        {
            return new RedundantServer
            {
                ServerUri = uri,
                ServiceLevel = serviceLevel,
                ServerState = state,
                Endpoint = CreateEndpoint(uri, $"opc.tcp://{uri[4..]}:4840")
            };
        }

        private static ConfiguredEndpoint CreateCurrentEndpoint(string applicationUri)
        {
            return CreateEndpoint(applicationUri, "opc.tcp://current:4840");
        }

        private static ConfiguredEndpoint CreateEndpoint(string applicationUri, string endpointUrl)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = applicationUri,
                    DiscoveryUrls = new ArrayOf<string>(new[] { endpointUrl })
                }
            };

            return new ConfiguredEndpoint(null, description, configuration: null);
        }

        private static Mock<ISession> CreateMockSession(
            int redundancySupport,
            byte serviceLevel,
            RedundantServerDataType[]? redundantServers = null,
            string[]? serverUris = null,
            string currentServerId = "")
        {
            var mock = new Mock<ISession>();
            mock.SetupGet(s => s.ConfiguredEndpoint)
                .Returns(CreateCurrentEndpoint("urn:current"));

            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 5),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant(redundancySupport), StatusCodes.Good),
                        new DataValue(new Variant(serviceLevel), StatusCodes.Good),
                        redundantServers == null
                            ? new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                            : CreateRedundantServerArrayValue(redundantServers),
                        serverUris == null
                            ? new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                            : new DataValue(new Variant(new ArrayOf<string>(serverUris)), StatusCodes.Good),
                        new DataValue(new Variant(DateTime.MinValue), StatusCodes.Good)
                    ],
                    DiagnosticInfos = []
                });

            SetupSingleNodeRead(
                mock,
                VariableIds.Server_ServerRedundancy_CurrentServerId,
                string.IsNullOrEmpty(currentServerId)
                    ? new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                    : new DataValue(new Variant(currentServerId), StatusCodes.Good));

            return mock;
        }

        private static void SetupSingleNodeRead(
            Mock<ISession> mock,
            NodeId nodeId,
            DataValue result)
        {
            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 1 && r[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [result],
                    DiagnosticInfos = []
                });
        }

        private static void VerifyBatchedRedundancyRead(Mock<ISession> mock)
        {
            mock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<ReadValueId>>(r => r.Count == 5),
                It.IsAny<CancellationToken>()), Times.Once);
            mock.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<ReadValueId>>(r =>
                    r.Count == 1 &&
                    (r[0].NodeId == VariableIds.Server_ServerRedundancy_RedundantServerArray ||
                        r[0].NodeId == VariableIds.Server_ServerRedundancy_ServerUriArray)),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static DataValue CreateRedundantServerArrayValue(
            RedundantServerDataType[] redundantServers)
        {
            ArrayOf<ExtensionObject> extensionObjects =
                Array.ConvertAll(redundantServers, s => new ExtensionObject(s));

            return new DataValue(new Variant(extensionObjects), StatusCodes.Good);
        }

        private static Mock<ISession> CreateMockSessionWithBadStatus()
        {
            var mock = new Mock<ISession>();

            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 5),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                    ],
                    DiagnosticInfos = []
                });

            return mock;
        }

        private static Mock<ISession> CreateMockSessionWithThrowingOptionalReads()
        {
            return CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: ServiceLevels.NoData);
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            public FixedTimeProvider(DateTime utcNow)
            {
                m_utcNow = utcNow;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return m_utcNow;
            }

            private readonly DateTimeOffset m_utcNow;
        }
    }
}
