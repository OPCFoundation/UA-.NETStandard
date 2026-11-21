// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Test doubles implement or inspect the legacy IAsyncNodeManager.SyncNodeManager view.

#pragma warning disable CA2000
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// A single-node read through the dispatcher returns the value a multi-node read
        /// returns, without a handle for a node manager with the default handle lookup.
        /// </summary>
        [Test]
        public async Task SingleNodeReadMatchesAMultiNodeReadAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddPermissionVariableAsync(
                manager, "RoutedRead", 11, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);

            var single = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
            DataValue alone = await ReadAsync(dispatcher, session, single).ConfigureAwait(false);
            (ArrayOf<DataValue> many, ArrayOf<DiagnosticInfo> diagnostics) = await ReadManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.None,
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(alone.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That((int)alone.WrappedValue, Is.EqualTo(11));
                Assert.That(many[0].StatusCode, Is.EqualTo(alone.StatusCode));
                Assert.That(many[0].WrappedValue, Is.EqualTo(alone.WrappedValue));
                Assert.That(many[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(diagnostics.IsNull, Is.True);
            });
            if (m_managerType != AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter)
            {
                // the default handle lookup finds the node again without a handle; a node
                // manager that overrides it gets the handle routed (see WriteRouting).
                Assert.That(single.Handle, Is.Null);
            }
        }

        /// <summary>
        /// A read value id that is read again does not read through the handle routed for an
        /// earlier read: neither for another node nor once its node was removed.
        /// </summary>
        [Test]
        public async Task RoutedHandleOfAnEarlierReadIsNotReusedAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId first = await AddPermissionVariableAsync(
                manager, "RoutedFirst", 1, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            NodeId second = await AddPermissionVariableAsync(
                manager, "RoutedSecond", 2, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);

            var readValueId = new ReadValueId { NodeId = first, AttributeId = Attributes.Value };
            DataValue value = await ReadAsync(dispatcher, session, readValueId).ConfigureAwait(false);
            Assert.That((int)value.WrappedValue, Is.EqualTo(1));

            readValueId.NodeId = second;
            value = await ReadAsync(dispatcher, session, readValueId).ConfigureAwait(false);
            Assert.That((int)value.WrappedValue, Is.EqualTo(2));

            // a read without a session validates no permissions and routes nothing.
            Assert.That(manager.PredefinedNodes.TryRemove(second, manager.PredefinedNodes[second]), Is.True);
            value = await ReadAsync(dispatcher, null, readValueId).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(readValueId.Handle, Is.Null);
        }

        /// <summary>
        /// The operation diagnostics of a read keep one entry per node, null for the nodes
        /// without one, and are not returned when the request asked for none.
        /// </summary>
        [Test]
        public async Task ReadDiagnosticsKeepOneEntryPerNodeAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId allowed = await AddPermissionVariableAsync(
                manager, "DiagnosticsAllowed", 5, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            var unknown = new NodeId("DiagnosticsUnknown", manager.NamespaceIndexes[0]);

            (ArrayOf<DataValue> values, ArrayOf<DiagnosticInfo> diagnostics) = await ReadManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.OperationAll,
                new ReadValueId { NodeId = allowed, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = unknown, AttributeId = Attributes.Value }).ConfigureAwait(false);
            (ArrayOf<DataValue> single, ArrayOf<DiagnosticInfo> singleDiagnostics) = await ReadManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.OperationAll,
                new ReadValueId { NodeId = unknown, AttributeId = Attributes.Value }).ConfigureAwait(false);
            (_, ArrayOf<DiagnosticInfo> none) = await ReadManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.None,
                new ReadValueId { NodeId = unknown, AttributeId = Attributes.Value }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(diagnostics.Count, Is.EqualTo(2));
                Assert.That(diagnostics[0], Is.Null);
                Assert.That(diagnostics[1], Is.Not.Null);
                Assert.That(single[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(singleDiagnostics.Count, Is.EqualTo(1));
                Assert.That(singleDiagnostics[0], Is.Not.Null);
                Assert.That(none.IsNull, Is.True);
            });
        }

        /// <summary>
        /// A read of several attributes of a node one after another reads the permission
        /// attributes of the node once; the node is read again after another node.
        /// </summary>
        [Test]
        public async Task ConsecutiveReadsOfANodeReadItsPermissionsOnceAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId counted = await AddPermissionVariableAsync(
                manager, "CountedPermissions", 3, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            NodeId other = await AddPermissionVariableAsync(
                manager, "OtherPermissions", 4, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            int permissionReads = 0;
            manager.PredefinedNodes[counted].OnReadRolePermissions =
                (ISystemContext context, NodeState node, ref ArrayOf<RolePermissionType> value) =>
            {
                permissionReads++;
                return ServiceResult.Good;
            };

            (ArrayOf<DataValue> values, _) = await ReadManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.None,
                new ReadValueId { NodeId = counted, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = counted, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = counted, AttributeId = Attributes.DisplayName },
                new ReadValueId { NodeId = other, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = counted, AttributeId = Attributes.Value }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(values.Count, Is.EqualTo(5));
                for (int ii = 0; ii < values.Count; ii++)
                {
                    Assert.That(values[ii].StatusCode, Is.EqualTo(StatusCodes.Good), $"value {ii}");
                }
                Assert.That((int)values[0].WrappedValue, Is.EqualTo(3));
                Assert.That((int)values[3].WrappedValue, Is.EqualTo(4));
                Assert.That(permissionReads, Is.EqualTo(2));
            });
        }

        private (MasterNodeManager Dispatcher, ITestNodeManager Manager, ISession Session)
            CreateReadDispatcher(ITestNodeManager? manager = null)
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            manager ??= CreateManager();
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(CreateUnrestrictedConfigurationNodeManager().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(core.Object);
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            var dispatcher = new MasterNodeManager(m_mockServer.Object, m_configuration, null, manager);
            m_mockServer.Setup(server => server.NodeManager).Returns(dispatcher);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.GrantedRoleIds).Returns([s_readerRoleId]);
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId("ReadRoutingSession", 0));
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.Identity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return (dispatcher, manager, session.Object);
        }

        private static async Task<DataValue> ReadAsync(
            MasterNodeManager dispatcher,
            ISession? session,
            ReadValueId readValueId)
        {
            using OperationContext context = session != null
                ? new OperationContext(session, DiagnosticsMasks.None)
                : new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            (ArrayOf<DataValue> values, _) = await dispatcher.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                [readValueId]).ConfigureAwait(false);
            Assert.That(values.Count, Is.EqualTo(1));
            return values[0];
        }

        private static async Task<(ArrayOf<DataValue>, ArrayOf<DiagnosticInfo>)> ReadManyAsync(
            MasterNodeManager dispatcher,
            ISession session,
            DiagnosticsMasks diagnosticsMask,
            params ReadValueId[] nodesToRead)
        {
            using var context = new OperationContext(session, diagnosticsMask);
            return await dispatcher.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                nodesToRead).ConfigureAwait(false);
        }
    }
}
