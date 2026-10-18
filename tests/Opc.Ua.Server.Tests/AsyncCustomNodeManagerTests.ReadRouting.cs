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
        /// A single-node read through the dispatcher reads the node through the handle the
        /// permission check resolved, and returns the value a multi-node read returns.
        /// </summary>
        [Test]
        public async Task SingleNodeReadUsesTheHandleOfTheValidationAsync()
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
                // routed by the validation, for the node manager that owns the node.
                Assert.That(single.Handle, Is.InstanceOf<NodeHandle>());
                var handle = (NodeHandle)single.Handle;
                Assert.That(handle.Node, Is.SameAs(manager.PredefinedNodes[nodeId]));
                Assert.That(handle.RoutedBy, Is.SameAs(manager));
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

        private (MasterNodeManager Dispatcher, ITestNodeManager Manager, ISession Session)
            CreateReadDispatcher()
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            ITestNodeManager manager = CreateManager();
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(Mock.Of<IConfigurationNodeManager>());
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
