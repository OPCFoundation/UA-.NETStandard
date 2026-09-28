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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.KeyCredential
{
    [TestFixture]
    [Category("KeyCredential")]
    public sealed class KeyCredentialRoutingRegressionTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out m_queueFactory, new FakeTimeProvider());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            TypeTable types = server.Object.TypeTree;
            types.AddReferenceSubtype(
                ReferenceTypeIds.References, NodeId.Null, new QualifiedName(BrowseNames.References));
            types.AddReferenceSubtype(
                ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References,
                new QualifiedName(BrowseNames.HierarchicalReferences));
            types.AddReferenceSubtype(
                ReferenceTypeIds.HasChild, ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName(BrowseNames.HasChild));
            types.AddReferenceSubtype(
                ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasChild, new QualifiedName(BrowseNames.Aggregates));
            types.AddReferenceSubtype(
                ReferenceTypeIds.HasComponent, ReferenceTypeIds.Aggregates,
                new QualifiedName(BrowseNames.HasComponent));
            types.AddReferenceSubtype(
                ReferenceTypeIds.HasProperty, ReferenceTypeIds.Aggregates, new QualifiedName(BrowseNames.HasProperty));
            var subscriptions = new Mock<ISubscriptionManager>();
            subscriptions.Setup(value => value.GetSubscriptions()).Returns([]);
            server.SetupGet(value => value.SubscriptionManager).Returns(subscriptions.Object);
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration(),
                SecurityConfiguration = new SecurityConfiguration()
            };
            m_manager = new ConfigurationNodeManager(server.Object, configuration);
            m_customManager = new CredentialNodeManager(server.Object, configuration);
            var core = new Mock<ICoreNodeManager>();
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(m_manager);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(core.Object);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            server.SetupGet(value => value.ConfigurationNodeManager).Returns(m_manager);
            server.SetupGet(value => value.CoreNodeManager).Returns(core.Object);
            m_master = new MasterNodeManager(server.Object, configuration, null, [m_customManager], null);
            server.SetupGet(value => value.NodeManager).Returns(m_master);

            m_folder = new KeyCredentialConfigurationFolderState(null)
            {
                NodeId = KeyCredentialPushSubject.StandardConfigurationFolderNodeId,
                BrowseName = new QualifiedName(BrowseNames.KeyCredentialConfiguration),
                DisplayName = LocalizedText.From(BrowseNames.KeyCredentialConfiguration),
                TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationFolderType
            };
            await m_manager.AddPredefinedNodeAsync(m_folder).ConfigureAwait(false);
            m_store = new InMemoryKeyCredentialStore();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_master != null)
            {
                await m_master.DisposeAsync().ConfigureAwait(false);
            }
            if (m_manager != null)
            {
                await m_manager.DisposeAsync().ConfigureAwait(false);
            }
            if (m_customManager != null)
            {
                await m_customManager.DisposeAsync().ConfigureAwait(false);
            }
            m_store?.Dispose();
            m_queueFactory?.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreatedCredentialSupportsDispatcherReadAndCall(bool customFolder)
        {
            AsyncCustomNodeManager owner = await BindSubjectAsync(customFolder).ConfigureAwait(false);
            using OperationContext context = CreateAdminContext();
            NodeId credentialId = await CreateCredentialAsync(context, "ServiceA", "urn:test:routed-resource")
                .ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            m_folder.GetChildren(owner.SystemContext, children);
            KeyCredentialConfigurationState credential = children.OfType<KeyCredentialConfigurationState>()
                .Single(child => child.NodeId == credentialId);

            (ArrayOf<DataValue> values, _) = await m_master.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = credentialId, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = credential.ResourceUri.NodeId, AttributeId = Attributes.Value }
                ]).ConfigureAwait(false);

            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant((int)NodeClass.Object)));
            Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant("urn:test:routed-resource")));
            Assert.That(credentialId.NamespaceIndex, Is.Not.Zero);
            Assert.That(owner.NamespaceIndexes, Does.Contain(credentialId.NamespaceIndex));
            if (customFolder)
            {
                Assert.That(credentialId.NamespaceIndex, Is.EqualTo(m_folder.NodeId.NamespaceIndex));
            }

            (ArrayOf<BrowseResult> browsed, _) = await m_master.BrowseAsync(
                context, new ViewDescription(), 0,
                [
                    new BrowseDescription
                    {
                        NodeId = credentialId,
                        BrowseDirection = BrowseDirection.Forward,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ]).ConfigureAwait(false);
            Assert.That(browsed[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(browsed[0].References.ToList().Select(reference => reference.NodeId),
                Does.Contain(new ExpandedNodeId(credential.ResourceUri.NodeId)));
            Assert.That(browsed[0].References.ToList().Select(reference => reference.NodeId),
                Does.Contain(new ExpandedNodeId(credential.UpdateCredential.NodeId)));

            (ArrayOf<CallMethodResult> updated, _) = await m_master.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = credentialId,
                        MethodId = credential.UpdateCredential.NodeId,
                        InputArguments =
                        [
                            "routed-credential",
                            ByteString.From([1, 3, 5, 7]),
                            string.Empty,
                            string.Empty
                        ]
                    }
                ]).ConfigureAwait(false);
            Assert.That(updated[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Server.KeyCredential stored = await m_store.GetAsync("routed-credential", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.Secret, Is.EqualTo(new byte[] { 1, 3, 5, 7 }));

            (ArrayOf<CallMethodResult> encryptingKey, _) = await m_master.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = credentialId,
                        MethodId = credential.GetEncryptingKey.NodeId,
                        InputArguments = ["routed-credential", SecurityPolicies.None]
                    }
                ]).ConfigureAwait(false);
            Assert.That(encryptingKey[0].StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            (ArrayOf<CallMethodResult> deleted, _) = await m_master.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = credentialId,
                        MethodId = credential.DeleteCredential.NodeId,
                        InputArguments = []
                    }
                ]).ConfigureAwait(false);
            Assert.That(deleted[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await m_store.GetAsync("routed-credential", CancellationToken.None).ConfigureAwait(false),
                Is.Null);

            (ArrayOf<DataValue> removed, _) = await m_master.ReadAsync(
                context, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = credentialId, AttributeId = Attributes.NodeClass }])
                .ConfigureAwait(false);
            Assert.That(removed[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task RestoredCredentialSupportsDispatcherBrowseAndRead()
        {
            await m_store.UpdateAsync(
                "restored-credential",
                new Server.KeyCredential([9, 7, 5, 3], DateTime.MaxValue),
                CancellationToken.None).ConfigureAwait(false);
            await m_manager.BindKeyCredentialPushAsync(new KeyCredentialPushSubject(m_store))
                .ConfigureAwait(false);
            using OperationContext context = CreateAdminContext();

            (ArrayOf<BrowseResult> browsed, _) = await m_master.BrowseAsync(
                context, new ViewDescription(), 0,
                [
                    new BrowseDescription
                    {
                        NodeId = m_folder.NodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ]).ConfigureAwait(false);
            Assert.That(browsed[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ReferenceDescription restored = browsed[0].References.ToList()
                .Single(reference => reference.BrowseName.Name == "restored-credential");
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(restored.TypeDefinition, Is.EqualTo(new ExpandedNodeId(
                ObjectTypeIds.KeyCredentialConfigurationType)));
            NodeId credentialId = restored.NodeId.InnerNodeId;
            Assert.That(m_manager.NamespaceIndexes, Does.Contain(credentialId.NamespaceIndex));

            var children = new List<BaseInstanceState>();
            m_folder.GetChildren(m_manager.SystemContext, children);
            KeyCredentialConfigurationState credential = children.OfType<KeyCredentialConfigurationState>()
                .Single(child => child.NodeId == credentialId);
            (ArrayOf<DataValue> values, _) = await m_master.ReadAsync(
                context, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = credentialId, AttributeId = Attributes.BrowseName },
                    new ReadValueId { NodeId = credential.CredentialId.NodeId, AttributeId = Attributes.Value }
                ]).ConfigureAwait(false);
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(
                new QualifiedName("restored-credential", credentialId.NamespaceIndex))));
            Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant("restored-credential")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreatedCredentialsHaveIndependentDispatcherPropertiesAndMethods(bool customFolder)
        {
            AsyncCustomNodeManager owner = await BindSubjectAsync(customFolder).ConfigureAwait(false);
            using OperationContext context = CreateAdminContext();
            NodeId firstId = await CreateCredentialAsync(context, "First", "urn:test:first").ConfigureAwait(false);
            NodeId secondId = await CreateCredentialAsync(context, "Second", "urn:test:second").ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            m_folder.GetChildren(owner.SystemContext, children);
            KeyCredentialConfigurationState first = children.OfType<KeyCredentialConfigurationState>()
                .Single(child => child.NodeId == firstId);
            KeyCredentialConfigurationState second = children.OfType<KeyCredentialConfigurationState>()
                .Single(child => child.NodeId == secondId);

            (ArrayOf<DataValue> values, _) = await m_master.ReadAsync(
                context, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = first.ResourceUri.NodeId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = second.ResourceUri.NodeId, AttributeId = Attributes.Value }
                ]).ConfigureAwait(false);

            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant("urn:test:first")));
            Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant("urn:test:second")));
            Assert.Multiple(() =>
            {
                Assert.That(first.ResourceUri.NodeId, Is.Not.EqualTo(second.ResourceUri.NodeId));
                Assert.That(first.UpdateCredential.NodeId, Is.Not.EqualTo(second.UpdateCredential.NodeId));
                Assert.That(first.GetEncryptingKey.NodeId, Is.Not.EqualTo(second.GetEncryptingKey.NodeId));
                Assert.That(first.DeleteCredential.NodeId, Is.Not.EqualTo(second.DeleteCredential.NodeId));
                Assert.That(first.ResourceUri.NodeId.NamespaceIndex, Is.Not.Zero);
                Assert.That(owner.NamespaceIndexes, Does.Contain(first.ResourceUri.NodeId.NamespaceIndex));
            });
        }

        private async Task<NodeId> CreateCredentialAsync(OperationContext context, string name, string resourceUri)
        {
            (ArrayOf<CallMethodResult> created, _) = await m_master.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = m_folder.NodeId,
                        MethodId = m_folder.CreateCredential.NodeId,
                        InputArguments =
                        [
                            name,
                            resourceUri,
                            KeyCredentialBridgeOptions.DefaultProfileUri,
                            ArrayOf<string>.Empty
                        ]
                    }
                ]).ConfigureAwait(false);
            Assert.That(created, Has.Count.EqualTo(1));
            Assert.That(created[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(created[0].OutputArguments, Has.Count.EqualTo(1));
            Assert.That(created[0].OutputArguments[0].TryGetValue(out NodeId credentialId), Is.True);
            return credentialId;
        }

        private async Task<AsyncCustomNodeManager> BindSubjectAsync(bool customFolder)
        {
            var subject = new KeyCredentialPushSubject(m_store);
            if (!customFolder)
            {
                await m_manager.BindKeyCredentialPushAsync(subject).ConfigureAwait(false);
                return m_manager;
            }

            ushort namespaceIndex = m_customManager.NamespaceIndexes[1];
            m_folder = new KeyCredentialConfigurationFolderState(null)
            {
                NodeId = new NodeId("CustomCredentials", namespaceIndex),
                BrowseName = new QualifiedName("CustomCredentials", namespaceIndex),
                DisplayName = LocalizedText.From("CustomCredentials"),
                TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationFolderType
            };
            await m_customManager.AddPredefinedNodeAsync(m_folder).ConfigureAwait(false);
            await subject.BindAsync(
                m_folder,
                m_customManager.SystemContext,
                m_customManager.AddPredefinedNodeAsync,
                async (node, ct) => await m_customManager.DeleteNodeAsync(
                    m_customManager.SystemContext, node.NodeId, ct).ConfigureAwait(false))
                .ConfigureAwait(false);
            return m_customManager;
        }

        private static OperationContext CreateAdminContext()
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.DisplayName).Returns("SecurityAdmin");
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_SecurityAdmin]);
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            return new OperationContext(
                new RequestHeader(),
                new SecureChannelContext("keycredential-routing", endpoint, RequestEncoding.Binary),
                RequestType.Call,
                RequestLifetime.None,
                identity.Object);
        }

        private sealed class CredentialNodeManager : AsyncCustomNodeManager
        {
            public CredentialNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, "urn:test:credentials:primary", "urn:test:credentials:secondary")
            {
            }
        }

        private MonitoredItemQueueFactory m_queueFactory = null!;
        private ConfigurationNodeManager m_manager = null!;
        private CredentialNodeManager m_customManager = null!;
        private MasterNodeManager m_master = null!;
        private KeyCredentialConfigurationFolderState m_folder = null!;
        private InMemoryKeyCredentialStore m_store = null!;
    }
}
