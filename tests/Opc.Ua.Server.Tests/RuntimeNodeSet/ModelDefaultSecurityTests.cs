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

using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// Imported model defaults protect actual Read and Write service dispatch on all manager implementations.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR09RuntimeImportedModelDefaultsAuthorizeInstanceValuesAsync()
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            ITestNodeManager manager = CreateManager();
            await using MasterNodeManager dispatcher = CreateModelPolicyDispatcher(manager);
            BaseVariableState variable = await ImportModelPolicyVariableAsync(manager, 0).ConfigureAwait(false);

            using OperationContext outsider = ModelPolicyActor(ObjectIds.WellKnownRole_Anonymous);
            DataValue denied = await ReadModelPolicyValueAsync(dispatcher, outsider, variable.NodeId)
                .ConfigureAwait(false);
            (ArrayOf<StatusCode> deniedWrites, _) = await dispatcher.WriteAsync(outsider,
                [new WriteValue
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(99))
                }]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(denied.WrappedValue.IsNull, Is.True);
                Assert.That(deniedWrites.Count, Is.EqualTo(1));
                Assert.That(deniedWrites[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(variable.WrappedValue.GetInt32(), Is.EqualTo(42));
            });

            using OperationContext administrator = ModelPolicyActor(ObjectIds.WellKnownRole_SecurityAdmin);
            DataValue allowed = await ReadModelPolicyValueAsync(dispatcher, administrator, variable.NodeId)
                .ConfigureAwait(false);
            Assert.That(allowed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(allowed.WrappedValue.GetInt32(), Is.EqualTo(42));
            (ArrayOf<StatusCode> allowedWrites, _) = await dispatcher.WriteAsync(administrator,
                [new WriteValue
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(73))
                }]).ConfigureAwait(false);
            Assert.That(allowedWrites.Count, Is.EqualTo(1));
            Assert.That(allowedWrites[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.WrappedValue.GetInt32(), Is.EqualTo(73));
        }

        /// <summary>
        /// Model channel policy survives publication and denies the actual service before a value callback.
        /// </summary>
        [TestCase((ushort)1, MessageSecurityMode.None, false)]
        [TestCase((ushort)1, MessageSecurityMode.Sign, true)]
        [TestCase((ushort)1, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase((ushort)2, MessageSecurityMode.None, false)]
        [TestCase((ushort)2, MessageSecurityMode.Sign, false)]
        [TestCase((ushort)2, MessageSecurityMode.SignAndEncrypt, true)]
        [Category("SecurityRegression")]
        public async Task SecurityR09RuntimeImportedModelRestrictionsProtectWeakChannelsAsync(
            ushort restriction,
            MessageSecurityMode mode,
            bool allowed)
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            ITestNodeManager manager = CreateManager();
            await using MasterNodeManager dispatcher = CreateModelPolicyDispatcher(manager);
            BaseVariableState variable = await ImportModelPolicyVariableAsync(manager, restriction)
                .ConfigureAwait(false);
            int reads = 0;
            variable.OnReadValue = (ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                ref Variant value, ref StatusCode status, ref DateTimeUtc timestamp) =>
            {
                reads++;
                value = new Variant(42);
                return ServiceResult.Good;
            };

            using OperationContext actor = ModelPolicyActor(ObjectIds.WellKnownRole_SecurityAdmin, mode);
            DataValue result = await ReadModelPolicyValueAsync(dispatcher, actor, variable.NodeId)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode,
                Is.EqualTo(allowed ? StatusCodes.Good : StatusCodes.BadSecurityModeInsufficient));
            Assert.That(reads, Is.EqualTo(allowed ? 1 : 0));
            if (allowed)
            {
                Assert.That(result.WrappedValue.GetInt32(), Is.EqualTo(42));
            }
            else
            {
                Assert.That(result.WrappedValue.IsNull, Is.True);
            }
        }

        /// <summary>
        /// Imports a readable and writable variable without any node-local security attributes.
        /// </summary>
        private async Task<BaseVariableState> ImportModelPolicyVariableAsync(
            ITestNodeManager manager,
            ushort restriction)
        {
            string xml =
                $"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                    xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris><Uri>{m_testNamespaceUri}</Uri></NamespaceUris>
                  <Models>
                    <Model ModelUri="{m_testNamespaceUri}" AccessRestrictions="{restriction}">
                      <RolePermissions>
                        <RolePermission Permissions="97">{ObjectIds.WellKnownRole_SecurityAdmin}</RolePermission>
                      </RolePermissions>
                    </Model>
                  </Models>
                  <UAVariable NodeId="ns=1;s=ModelProtected" BrowseName="1:ModelProtected"
                      DataType="i=6" AccessLevel="3" UserAccessLevel="3">
                    <References><Reference ReferenceType="i=40">i=63</Reference></References>
                    <Value><uax:Int32>42</uax:Int32></Value>
                  </UAVariable>
                </UANodeSet>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet nodeSet = UANodeSet.Read(stream)!;
            var nodes = new NodeStateCollection();
            nodeSet.Import(manager.SystemContext, nodes, linkParentChild: true);
            var variable = (BaseVariableState)nodes.Single();
            await manager.AddNodeAsync(manager.SystemContext, default, variable).ConfigureAwait(false);
            return variable;
        }

        /// <summary>
        /// Registers the real dispatcher without a listening endpoint, timer, certificate store or host.
        /// </summary>
        private MasterNodeManager CreateModelPolicyDispatcher(ITestNodeManager manager)
        {
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            var configuration = new Mock<IConfigurationNodeManager>();
            NamespaceMetadataState? noMetadata = null;
            configuration.As<INamespaceMetadataSnapshot>()
                .Setup(value => value.TryGetNamespaceMetadataState(It.IsAny<ushort>(), out noMetadata))
                .Returns(true);
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(configuration.Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(core.Object);
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            var dispatcher = new MasterNodeManager(m_mockServer.Object, m_configuration, null, manager);
            m_mockServer.Setup(server => server.NodeManager).Returns(dispatcher);
            return dispatcher;
        }

        /// <summary>
        /// Uses the real per-request identity and channel inputs to the server permission validators.
        /// </summary>
        private static OperationContext ModelPolicyActor(
            NodeId role,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(value => value.GrantedRoleIds).Returns([role]);
            var channel = new SecureChannelContext("model-policy",
                new EndpointDescription { SecurityMode = mode, EndpointUrl = "opc.tcp://localhost:4840" },
                RequestEncoding.Binary);
            return new OperationContext(new RequestHeader(), channel, RequestType.Read,
                RequestLifetime.None, identity.Object);
        }

        /// <summary>
        /// Reads through the actual service routing and returns its one per-node result.
        /// </summary>
        private static async Task<DataValue> ReadModelPolicyValueAsync(
            MasterNodeManager dispatcher,
            OperationContext context,
            NodeId nodeId)
        {
            (ArrayOf<DataValue> values, _) = await dispatcher.ReadAsync(context, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            Assert.That(values.Count, Is.EqualTo(1));
            return values[0];
        }
    }
}
