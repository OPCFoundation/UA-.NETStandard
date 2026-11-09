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
        private static readonly NodeId s_readerRoleId = new("ReadPermissionReader", 0);

        /// <summary>
        /// Verifies that a Read through the service dispatcher gives the same per-node
        /// permission verdicts and values whether a node is read alone (no per-request
        /// attribute cache) or together with other nodes (cached attributes), including a
        /// node read twice in one request.
        /// </summary>
        [Test]
        public async Task ReadPermissionVerdictsAreTheSameForSingleAndMultiNodeRequestsAsync()
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            ITestNodeManager manager = CreateManager();
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(CreateUnrestrictedConfigurationNodeManager().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(core.Object);
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            await using var dispatcher = new MasterNodeManager(m_mockServer.Object, m_configuration, null, manager);
            m_mockServer.Setup(server => server.NodeManager).Returns(dispatcher);

            NodeId allowed = await AddPermissionVariableAsync(manager, "ReadAllowed", 7, PermissionType.Browse | PermissionType.Read)
                .ConfigureAwait(false);
            NodeId denied = await AddPermissionVariableAsync(manager, "ReadDenied", 8, PermissionType.Browse)
                .ConfigureAwait(false);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.GrantedRoleIds).Returns([s_readerRoleId]);
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId("ReadPermissionSession", 0));
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.Identity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            DataValue singleAllowed = await ReadOneAsync(dispatcher, session.Object, allowed).ConfigureAwait(false);
            DataValue singleDenied = await ReadOneAsync(dispatcher, session.Object, denied).ConfigureAwait(false);

            using var context = new OperationContext(session.Object, DiagnosticsMasks.None);
            (ArrayOf<DataValue> many, _) = await dispatcher.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = allowed, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = denied, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = allowed, AttributeId = Attributes.Value }
                ]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(singleAllowed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That((int)singleAllowed.WrappedValue, Is.EqualTo(7));
                Assert.That(singleDenied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(many.Count, Is.EqualTo(3));
                Assert.That(many[0].StatusCode, Is.EqualTo(singleAllowed.StatusCode));
                Assert.That(many[0].WrappedValue, Is.EqualTo(singleAllowed.WrappedValue));
                Assert.That(many[1].StatusCode, Is.EqualTo(singleDenied.StatusCode));
                Assert.That(many[2].StatusCode, Is.EqualTo(singleAllowed.StatusCode));
                Assert.That(many[2].WrappedValue, Is.EqualTo(singleAllowed.WrappedValue));
            });
        }

        private static async Task<NodeId> AddPermissionVariableAsync(
            ITestNodeManager manager,
            string name,
            int value,
            PermissionType readerPermissions)
        {
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = value;
            variable.RolePermissions =
            [
                new RolePermissionType { RoleId = s_readerRoleId, Permissions = (uint)readerPermissions }
            ];
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);
            return variable.NodeId;
        }

        private static async Task<DataValue> ReadOneAsync(
            MasterNodeManager dispatcher,
            ISession session,
            NodeId nodeId)
        {
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            (ArrayOf<DataValue> values, _) = await dispatcher.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            Assert.That(values.Count, Is.EqualTo(1));
            return values[0];
        }
    }
}
