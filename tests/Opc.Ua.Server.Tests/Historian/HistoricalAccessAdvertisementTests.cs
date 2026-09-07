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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class HistoricalAccessAdvertisementTests
    {
        private const string TestNamespaceUri = "urn:opcfoundation:server:tests:historical-access-advertisement";

        [Test]
        public async Task HistorizingNodeWithoutHistorianClearsHistoricalAdvertisementAsync()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = CreateVariable(
                h,
                "NoHistorian",
                AccessLevels.CurrentRead | AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
                AccessLevels.CurrentRead | AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
                historizing: true);

            await h.Manager.AddNodeAsync(h.Context, variable).ConfigureAwait(false);

            h.Manager.ReconcileHistoricalAccessAdvertisement();

            Assert.That(variable.Historizing, Is.False);
            Assert.That((byte)(variable.AccessLevel & AccessLevels.HistoryRead), Is.Zero);
            Assert.That((byte)(variable.AccessLevel & AccessLevels.HistoryWrite), Is.Zero);
            Assert.That((byte)(variable.UserAccessLevel & AccessLevels.HistoryRead), Is.Zero);
            Assert.That((byte)(variable.UserAccessLevel & AccessLevels.HistoryWrite), Is.Zero);
            Assert.That((byte)(variable.AccessLevel & AccessLevels.CurrentRead), Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That((byte)(variable.UserAccessLevel & AccessLevels.CurrentRead), Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public async Task HistorizingNodeWithHistorianPreservesHistoricalAdvertisementAsync()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = CreateVariable(
                h,
                "WithHistorian",
                AccessLevels.CurrentRead | AccessLevels.HistoryRead,
                AccessLevels.CurrentRead | AccessLevels.HistoryRead,
                historizing: true);

            await h.Manager.AddNodeAsync(h.Context, variable).ConfigureAwait(false);

            using var provider = new InMemoryHistorianProvider();
            h.Registry.RegisterForNode(variable.NodeId, provider);

            h.Manager.ReconcileHistoricalAccessAdvertisement();

            Assert.That(variable.Historizing, Is.True);
            Assert.That((byte)(variable.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(variable.UserAccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
        }

        [Test]
        public async Task NonHistorizingNodeIsUnaffectedAsync()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = CreateVariable(
                h,
                "CurrentOnly",
                AccessLevels.CurrentRead | AccessLevels.CurrentWrite,
                AccessLevels.CurrentRead,
                historizing: false);

            await h.Manager.AddNodeAsync(h.Context, variable).ConfigureAwait(false);

            h.Manager.ReconcileHistoricalAccessAdvertisement();

            Assert.That(variable.Historizing, Is.False);
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead | AccessLevels.CurrentWrite));
            Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public async Task HistoryReadAfterHistoricalAdvertisementClearedReportsUnsupportedAsync()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = CreateVariable(
                h,
                "ReadUnsupported",
                AccessLevels.CurrentRead | AccessLevels.HistoryRead,
                AccessLevels.CurrentRead | AccessLevels.HistoryRead,
                historizing: true);

            await h.Manager.AddNodeAsync(h.Context, variable).ConfigureAwait(false);

            h.Manager.ReconcileHistoricalAccessAdvertisement();

            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = variable.NodeId } };
            var results = new List<HistoryReadResult> { null };
            var errors = new List<ServiceResult> { null };

            await h.Manager.HistoryReadAsync(
                h.OperationContext,
                new ReadRawModifiedDetails
                {
                    StartTime = DateTime.UtcNow.AddMinutes(-1),
                    EndTime = DateTime.UtcNow
                },
                TimestampsToReturn.Source,
                releaseContinuationPoints: false,
                nodesToRead,
                results,
                errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
            Assert.That(results[0], Is.Not.Null);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task MasterReadClearsHistoricalBitsWhenReadCallbackAdvertisesHistoryAsync()
        {
            using MasterReadHarness h = CreateMasterReadHarness();

            await h.MasterNodeManager.StartupAsync().ConfigureAwait(false);

            (byte accessLevel, byte userAccessLevel, uint accessLevelEx, bool historizing) = await ReadHistoricalAttributesAsync(
                h,
                HistoricalAccessReadPathNodeManager.NoHistorianNodeId).ConfigureAwait(false);

            Assert.That(historizing, Is.False);
            Assert.That((byte)(accessLevel & AccessLevels.HistoryRead), Is.Zero);
            Assert.That((byte)(accessLevel & AccessLevels.HistoryWrite), Is.Zero);
            Assert.That((byte)(userAccessLevel & AccessLevels.HistoryRead), Is.Zero);
            Assert.That((byte)(userAccessLevel & AccessLevels.HistoryWrite), Is.Zero);
            Assert.That(accessLevelEx & AccessLevels.HistoryRead, Is.Zero);
            Assert.That(accessLevelEx & AccessLevels.HistoryWrite, Is.Zero);
            Assert.That((byte)(accessLevel & AccessLevels.CurrentRead), Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That((byte)(userAccessLevel & AccessLevels.CurrentRead), Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public async Task MasterReadPreservesHistoricalBitsWhenHistorianIsRegisteredAsync()
        {
            using MasterReadHarness h = CreateMasterReadHarness(registerHistorian: true);

            await h.MasterNodeManager.StartupAsync().ConfigureAwait(false);

            (byte accessLevel, byte userAccessLevel, uint accessLevelEx, bool historizing) = await ReadHistoricalAttributesAsync(
                h,
                HistoricalAccessReadPathNodeManager.HistorianNodeId).ConfigureAwait(false);

            Assert.That(historizing, Is.True);
            Assert.That((byte)(accessLevel & AccessLevels.HistoryRead), Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(userAccessLevel & AccessLevels.HistoryRead), Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(accessLevelEx & AccessLevels.HistoryRead, Is.EqualTo(AccessLevels.HistoryRead));
        }

        private static BaseDataVariableState CreateVariable(
            Harness h,
            string name,
            byte accessLevel,
            byte userAccessLevel,
            bool historizing)
        {
            ushort nsIdx = h.Manager.NamespaceIndex;
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(h.Context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DisplayName = new LocalizedText(name);
            variable.DataType = DataTypeIds.Boolean;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = accessLevel;
            variable.UserAccessLevel = userAccessLevel;
            variable.Historizing = historizing;
            return variable;
        }

        private static Harness CreateHarness()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            var registry = new HistorianProviderRegistry(namespaceTable);
            var mockServer = new Mock<IServerInternal>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();
            var mockTelemetry = new Mock<ITelemetryContext>();

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            var manager = new HistoricalAccessTestNodeManager(
                mockServer.Object,
                configuration,
                Mock.Of<ILogger>(),
                TestNamespaceUri);

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(Mock.Of<IUserIdentity>());
            mockSession.Setup(s => s.PreferredLocales).Returns([]);

            var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryRead,
                RequestLifetime.None,
                mockSession.Object);

            return new Harness(manager, serverSystemContext, operationContext, registry);
        }

        private static MasterReadHarness CreateMasterReadHarness(bool registerHistorian = false)
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            var registry = new HistorianProviderRegistry(namespaceTable);
            var mockServer = new Mock<IServerInternal>();
            var mockMainFactory = new Mock<IMainNodeManagerFactory>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();
            var mockCoreNodeManager = new Mock<ICoreNodeManager>();
            var mockTelemetry = new Mock<ITelemetryContext>();

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.Setup(s => s.MainNodeManagerFactory).Returns(mockMainFactory.Object);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);

            SetupEmptyNodeManager(mockConfigurationNodeManager.As<IAsyncNodeManager>());
            SetupEmptyNodeManager(mockCoreNodeManager.As<IAsyncNodeManager>());
            mockMainFactory.Setup(f => f.CreateConfigurationNodeManager())
                .Returns(mockConfigurationNodeManager.Object);
            mockMainFactory.Setup(f => f.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(mockCoreNodeManager.Object);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxBrowseContinuationPoints = 100,
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            var nodeManager = new HistoricalAccessReadPathNodeManager(
                mockServer.Object,
                configuration,
                Mock.Of<ILogger>(),
                TestNamespaceUri);

            var masterNodeManager = new MasterNodeManager(
                mockServer.Object,
                configuration,
                null,
                [nodeManager]);
            mockServer.Setup(s => s.NodeManager).Returns(masterNodeManager);

            if (registerHistorian)
            {
                registry.RegisterForNode(
                    HistoricalAccessReadPathNodeManager.HistorianNodeId,
                    new InMemoryHistorianProvider());
            }

            OperationContext operationContext = CreateOperationContext();

            return new MasterReadHarness(masterNodeManager, operationContext, registry);
        }

        private static void SetupEmptyNodeManager(Mock<IAsyncNodeManager> nodeManager)
        {
            nodeManager.Setup(n => n.NamespaceUris).Returns([]);
            nodeManager
                .Setup(n => n.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            nodeManager
                .Setup(n => n.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
        }

        private static OperationContext CreateOperationContext()
        {
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(Mock.Of<IUserIdentity>());
            mockSession.Setup(s => s.PreferredLocales).Returns([]);

            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                RequestLifetime.None,
                mockSession.Object);
        }

        private static async ValueTask<(
            byte AccessLevel,
            byte UserAccessLevel,
            uint AccessLevelEx,
            bool Historizing)>
            ReadHistoricalAttributesAsync(
                MasterReadHarness h,
                NodeId nodeId)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevel },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.UserAccessLevel },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevelEx },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Historizing }
            ];

            (ArrayOf<DataValue> values, _) = await h.MasterNodeManager.ReadAsync(
                h.OperationContext,
                maxAge: 0,
                TimestampsToReturn.Neither,
                nodesToRead).ConfigureAwait(false);

            Assert.That(values, Has.Count.EqualTo(4));
            Assert.That(StatusCode.IsGood(values[0].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(values[1].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(values[2].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(values[3].StatusCode), Is.True);
            Assert.That(values[0].WrappedValue.TryGetValue(out byte accessLevel), Is.True);
            Assert.That(values[1].WrappedValue.TryGetValue(out byte userAccessLevel), Is.True);
            Assert.That(values[2].WrappedValue.TryGetValue(out uint accessLevelEx), Is.True);
            Assert.That(values[3].WrappedValue.TryGetValue(out bool historizing), Is.True);
            return (accessLevel, userAccessLevel, accessLevelEx, historizing);
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                HistoricalAccessTestNodeManager manager,
                ServerSystemContext context,
                OperationContext operationContext,
                HistorianProviderRegistry registry)
            {
                Manager = manager;
                Context = context;
                OperationContext = operationContext;
                Registry = registry;
            }

            public HistoricalAccessTestNodeManager Manager { get; }

            public ServerSystemContext Context { get; }

            public OperationContext OperationContext { get; }

            public HistorianProviderRegistry Registry { get; }

            public void Dispose()
            {
                OperationContext.Dispose();
                Registry.Dispose();
                Manager.Dispose();
            }
        }

        private sealed class HistoricalAccessTestNodeManager : AsyncCustomNodeManager
        {
            public HistoricalAccessTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public ValueTask AddNodeAsync(ISystemContext context, NodeState node)
            {
                return AddPredefinedNodeAsync(context, node);
            }
        }

        private sealed class MasterReadHarness : IDisposable
        {
            public MasterReadHarness(
                MasterNodeManager masterNodeManager,
                OperationContext operationContext,
                HistorianProviderRegistry registry)
            {
                MasterNodeManager = masterNodeManager;
                OperationContext = operationContext;
                m_registry = registry;
            }

            public MasterNodeManager MasterNodeManager { get; }

            public OperationContext OperationContext { get; }

            private readonly HistorianProviderRegistry m_registry;

            public void Dispose()
            {
                OperationContext.Dispose();
                MasterNodeManager.Dispose();
                m_registry.Dispose();
            }
        }

        private sealed class HistoricalAccessReadPathNodeManager : AsyncCustomNodeManager
        {
            public static readonly NodeId NoHistorianNodeId = new("ReadPathNoHistorian", 1);
            public static readonly NodeId HistorianNodeId = new("ReadPathWithHistorian", 1);

            public HistoricalAccessReadPathNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await AddPredefinedNodeAsync(
                    SystemContext,
                    CreateReadPathVariable(NoHistorianNodeId, "ReadPathNoHistorian"),
                    cancellationToken).ConfigureAwait(false);

                await AddPredefinedNodeAsync(
                    SystemContext,
                    CreateReadPathVariable(HistorianNodeId, "ReadPathWithHistorian"),
                    cancellationToken).ConfigureAwait(false);
            }

            private BaseDataVariableState CreateReadPathVariable(NodeId nodeId, string name)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(SystemContext);
                variable.NodeId = nodeId;
                variable.BrowseName = new QualifiedName(name, NamespaceIndex);
                variable.DisplayName = new LocalizedText(name);
                variable.DataType = DataTypeIds.Boolean;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                variable.Historizing = true;
                variable.OnReadAccessLevel = (
                    ISystemContext context,
                    NodeState node,
                    ref byte value) =>
                {
                    value = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                    return ServiceResult.Good;
                };
                variable.OnReadUserAccessLevel = (
                    ISystemContext context,
                    NodeState node,
                    ref byte value) =>
                {
                    value = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                    return ServiceResult.Good;
                };
                variable.OnReadHistorizing = (
                    ISystemContext context,
                    NodeState node,
                    ref bool value) =>
                {
                    value = true;
                    return ServiceResult.Good;
                };
                variable.OnReadAccessLevelEx = (
                    ISystemContext context,
                    NodeState node,
                    ref uint value) =>
                {
                    value = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                    return ServiceResult.Good;
                };
                return variable;
            }
        }
    }
}
