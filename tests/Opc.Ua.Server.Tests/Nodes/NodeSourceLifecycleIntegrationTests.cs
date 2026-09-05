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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Nodes
{
    [TestFixture]
    [Category("NodeSource")]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class NodeSourceLifecycleIntegrationTests
    {
        private const double kMaxAge = 10000;
        private const string kNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeSourceLifecycle";
        private const string kImportedNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeSourceLifecycle:Imported";

        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ILogger m_logger;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(NodeSourceLifecycleIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(
                telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create()
                .CreateLogger<NodeSourceLifecycleIntegrationTests>();
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(
                        m_secureChannelContext,
                        m_requestHeader,
                        true,
                        RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_server?.Dispose();
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public async Task SourceGraphSupportsServicesAndEveryLifecycleModeAsync()
        {
            var initial = new GraphSource(generation: 1);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(initial)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(registration.Generation, Is.EqualTo(1));
                Assert.That(registration.NodeManager, Is.TypeOf<NodeSourceNodeManager>());
                Assert.That(initial.BuildCount, Is.EqualTo(1));
                Assert.That(initial.NodeAddedCount, Is.EqualTo(1));
                Assert.That(initial.ExistingResolversSeeAuthoredGraph, Is.True);
                Assert.That(
                    initial.FolderReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(
                    initial.ObjectReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(
                    initial.VariableReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(
                    initial.MethodReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
            });
            await AssertGraphVisibleAsync(initial, expectedValue: 1)
                .ConfigureAwait(false);
            BrowseResponse inverseBrowse = await BrowseAsync(
                initial.FolderId,
                BrowseDirection.Inverse).ConfigureAwait(false);
            ReferenceDescription objectsReference = inverseBrowse.Results[0]
                .References.ToArray().Single(reference =>
                    ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        m_server.CurrentInstance.NamespaceUris) ==
                    ObjectIds.ObjectsFolder);
            Assert.Multiple(() =>
            {
                Assert.That(
                    objectsReference.BrowseName,
                    Is.EqualTo(new QualifiedName("Objects")));
                Assert.That(
                    objectsReference.DisplayName.Text,
                    Is.EqualTo("Objects"));
                Assert.That(
                    initial.FolderId.IdentifierAsString,
                    Is.EqualTo("NodeSourceRoot"));
            });
            await CallMethodAsync(initial.ObjectId, initial.MethodId).ConfigureAwait(false);
            Assert.That(initial.MethodCallCount, Is.EqualTo(1));

            var reloaded = new GraphSource(generation: 2);
            registration = await m_server.NodeManagerLifecycle
                .ReloadNodeSourceAsync(registration, reloaded)
                .ConfigureAwait(false);
            AssertStableNodeIds(initial, reloaded);
            Assert.That(registration.Generation, Is.EqualTo(2));
            await AssertValueAsync(reloaded.VariableId, 2).ConfigureAwait(false);

            var shadowReloaded = new GraphSource(generation: 3);
            registration = await m_server.NodeManagerLifecycle
                .ShadowReloadNodeSourceAsync(registration, shadowReloaded)
                .ConfigureAwait(false);
            AssertStableNodeIds(reloaded, shadowReloaded);
            Assert.That(registration.Generation, Is.EqualTo(3));
            await AssertValueAsync(shadowReloaded.VariableId, 3).ConfigureAwait(false);

            var immediateReloaded = new GraphSource(generation: 4);
            registration = await m_server.NodeManagerLifecycle
                .ImmediateReloadNodeSourceAsync(registration, immediateReloaded)
                .ConfigureAwait(false);
            AssertStableNodeIds(shadowReloaded, immediateReloaded);
            Assert.That(registration.Generation, Is.EqualTo(4));
            await AssertValueAsync(immediateReloaded.VariableId, 4).ConfigureAwait(false);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, callerContext: null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(CountRegistrations(registration.Id), Is.Zero);
                Assert.That(immediateReloaded.NodeRemovedCount, Is.EqualTo(1));
            });
            DataValue removedValue = await ReadValueAsync(immediateReloaded.VariableId)
                .ConfigureAwait(false);
            Assert.That(removedValue.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

            BrowseResponse objectsBrowse = await BrowseAsync(ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);
            Assert.That(
                objectsBrowse.Results[0].References.Contains(reference =>
                    reference.BrowseName == immediateReloaded.FolderBrowseName),
                Is.False);
        }

        [Test]
        public async Task SourceMonitoredItemCallbacksRunOnceAsync()
        {
            var source = new GraphSource(generation: 1);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source).ConfigureAwait(false);
            try
            {
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                CreateSubscriptionResponse subscription = await services
                    .CreateSubscriptionAsync(m_requestHeader, 100, 100, 10, 0, true, 0)
                    .ConfigureAwait(false);
                try
                {
                    m_requestHeader.Timestamp = DateTimeUtc.Now;
                    CreateMonitoredItemsResponse response = await services.CreateMonitoredItemsAsync(
                        m_requestHeader,
                        subscription.SubscriptionId,
                        TimestampsToReturn.Both,
                        [
                            new MonitoredItemCreateRequest
                            {
                                ItemToMonitor = new ReadValueId
                                {
                                    NodeId = source.VariableId,
                                    AttributeId = Attributes.Value
                                },
                                MonitoringMode = MonitoringMode.Reporting,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 0,
                                    QueueSize = 1,
                                    DiscardOldest = true
                                }
                            }
                        ]).ConfigureAwait(false);

                    Assert.That(response.Results, Has.Count.EqualTo(1));
                    Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(source.MonitoredItemCreatedCount, Is.EqualTo(1));
                }
                finally
                {
                    m_requestHeader.Timestamp = DateTimeUtc.Now;
                    await services.DeleteSubscriptionsAsync(
                        m_requestHeader,
                        [subscription.SubscriptionId]).ConfigureAwait(false);
                }
                Assert.That(source.MonitoredItemDeletedCount, Is.EqualTo(1));
            }
            finally
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(
                    registration, callerContext: null).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task BuildAsyncImportSupportsTypedBrowseReadAndCallAsync()
        {
            var source = new ImportedGraphSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                IServerInternal server = m_server.CurrentInstance;
                NodeState instance = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(source.ObjectId)
                    .ConfigureAwait(false);
                NodeState variable = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(source.VariableId)
                    .ConfigureAwait(false);
                NodeState method = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(source.MethodId)
                    .ConfigureAwait(false);
                NodeState authoredChild = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(source.AuthoredChildId)
                    .ConfigureAwait(false);
                BrowseResponse objectsBrowse = await BrowseAsync(ObjectIds.ObjectsFolder)
                    .ConfigureAwait(false);
                BrowseResponse objectBrowse = await BrowseAsync(source.ObjectId)
                    .ConfigureAwait(false);
                DataValue value = await ReadValueAsync(source.VariableId)
                    .ConfigureAwait(false);
                await CallMethodAsync(source.ObjectId, source.MethodId)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(source.TypedNodesResolvedDuringBuild, Is.True);
                    Assert.That(instance, Is.TypeOf<ImportedDeviceState>());
                    Assert.That(variable, Is.TypeOf<ImportedValueState>());
                    Assert.That(method, Is.TypeOf<ImportedResetMethodState>());
                    Assert.That(
                        ((BaseInstanceState)authoredChild).Parent,
                        Is.SameAs(instance));
                    Assert.That(
                        objectsBrowse.Results[0].References.Contains(reference =>
                            reference.BrowseName.Name == "ImportedDevice"),
                        Is.True);
                    Assert.That(
                        objectsBrowse.Results[0].References.Contains(reference =>
                            reference.BrowseName.Name == "AuthoredRoot"),
                        Is.True);
                    Assert.That(
                        objectBrowse.Results[0].References.Contains(reference =>
                            reference.BrowseName.Name == "Value"),
                        Is.True);
                    Assert.That(
                        objectBrowse.Results[0].References.Contains(reference =>
                            reference.BrowseName.Name == "Reset"),
                        Is.True);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(42));
                    Assert.That(source.MethodCallCount, Is.EqualTo(1));
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task GeneratedNodeSourceBuildsFreshGraphsWithTypedHelpersAsync()
        {
            var source = new GeneratedPhase5NodeSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                var manager = (NodeSourceNodeManager)registration.NodeManager;
                Assert.Multiple(() =>
                {
                    Assert.That(source.NamespaceUris.Count, Is.EqualTo(2));
                    Assert.That(
                        source.NamespaceUris[0],
                        Is.EqualTo(GeneratedNodeSetImportSource.NamespaceUri));
                    Assert.That(
                        source.NamespaceUris[1],
                        Is.EqualTo(GeneratedNodeSetImportSource.InstanceNamespaceUri));
                    Assert.That(source.UntypedConfigureCount, Is.EqualTo(1));
                    Assert.That(source.TypedConfigureCount, Is.EqualTo(1));
                    Assert.That(source.BehaviorRegistrationConfigureCount, Is.EqualTo(1));
                    Assert.That(source.MaterializedDevices, Has.Count.EqualTo(1));
                    Assert.That(
                        source.MaterializedDevices[0].Value!.NodeId,
                        Is.EqualTo(source.ImportedGeneratedValueId));
                    Assert.That(
                        manager.Find(source.ImportedGeneratedValueId),
                        Is.SameAs(source.MaterializedDevices[0].Value));
                    Assert.That(
                        source.GetNodeSetImportFactories().Count,
                        Is.GreaterThan(0));
                    Assert.That(
                        source.StringNamedBrowseName.NamespaceIndex,
                        Is.EqualTo(
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                                GeneratedNodeSetImportSource.NamespaceUri)));
                    Assert.That(
                        manager.Find(source.StringNamedObjectId),
                        Is.TypeOf<GeneratedNodeSourceModel.DeviceState>());
                    Assert.That(
                        manager.Find(source.StringNamedVariableId),
                        Is.TypeOf<GeneratedNodeSourceModel.CustomValueState>());
                    Assert.That(
                        manager.Find(source.StringNamedMethodId),
                        Is.TypeOf<GeneratedNodeSourceModel.CalibrateMethodState>());
                    Assert.That(
                        source.AuthoredObjectId.IdentifierAsString,
                        Does.StartWith("v1:"));
                    Assert.That(
                        source.AuthoredVariableId.IdentifierAsString,
                        Does.StartWith("v1:"));
                    Assert.That(
                        source.AuthoredMethodId.IdentifierAsString,
                        Does.StartWith("v1:"));
                    Assert.That(
                        manager.Find(source.AuthoredObjectId),
                        Is.TypeOf<GeneratedNodeSourceModel.DeviceState>());
                    Assert.That(
                        manager.Find(source.AuthoredVariableId),
                        Is.TypeOf<GeneratedNodeSourceModel.CustomValueState>());
                    Assert.That(
                        manager.Find(source.AuthoredMethodId),
                        Is.TypeOf<GeneratedNodeSourceModel.CalibrateMethodState>());
                    Assert.That(
                        source.ExternalServerDevice.NodeId,
                        Is.Not.EqualTo(source.ExternalCapabilitiesDevice.NodeId));
                    Assert.That(source.ExternalServerDevice.Parent, Is.Null);
                    Assert.That(source.ExternalCapabilitiesDevice.Parent, Is.Null);
                    Assert.That(
                        source.ExternalServerDevice.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            true,
                            ObjectIds.Server),
                        Is.True);
                    Assert.That(
                        source.ExternalCapabilitiesDevice.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            true,
                            ObjectIds.Server_ServerCapabilities),
                        Is.True);
                });

                GeneratedNodeSourceModel.DeviceState firstDevice =
                    source.MaterializedDevices[0];
                NodeId firstAuthoredObjectId = source.AuthoredObjectId;
                NodeId firstAuthoredVariableId = source.AuthoredVariableId;
                NodeId firstAuthoredMethodId = source.AuthoredMethodId;
                NodeId firstExternalServerId = source.ExternalServerDevice.NodeId;
                NodeId firstExternalCapabilitiesId =
                    source.ExternalCapabilitiesDevice.NodeId;
                registration = await m_server.NodeManagerLifecycle
                    .ReloadNodeSourceAsync(registration, source)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(source.UntypedConfigureCount, Is.EqualTo(2));
                    Assert.That(source.TypedConfigureCount, Is.EqualTo(2));
                    Assert.That(source.BehaviorRegistrationConfigureCount, Is.EqualTo(2));
                    Assert.That(source.MaterializedDevices, Has.Count.EqualTo(2));
                    Assert.That(
                        source.MaterializedDevices[1],
                        Is.Not.SameAs(firstDevice),
                        "Each BuildAsync invocation must materialize a fresh node graph.");
                    Assert.That(source.AuthoredObjectId, Is.EqualTo(firstAuthoredObjectId));
                    Assert.That(source.AuthoredVariableId, Is.EqualTo(firstAuthoredVariableId));
                    Assert.That(source.AuthoredMethodId, Is.EqualTo(firstAuthoredMethodId));
                    Assert.That(
                        source.ExternalServerDevice.NodeId,
                        Is.EqualTo(firstExternalServerId));
                    Assert.That(
                        source.ExternalCapabilitiesDevice.NodeId,
                        Is.EqualTo(firstExternalCapabilitiesId));
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ImportedTypedChildLinksToLaterAuthoredTypedParentAsync()
        {
            var source = new ImportedChildWithAuthoredParentSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                var manager = (NodeSourceNodeManager)registration.NodeManager;
                NodeState imported = manager.Find(source.ImportedChildId);
                var children = new List<BaseInstanceState>();
                source.Parent.GetChildren(manager.SystemContext, children);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        imported,
                        Is.TypeOf<GeneratedNodeSourceModel.CustomValueState>());
                    Assert.That(imported, Is.SameAs(source.Parent.Value));
                    Assert.That(
                        ((BaseInstanceState)imported).Parent,
                        Is.SameAs(source.Parent));
                    Assert.That(children, Has.Member(imported));
                    Assert.That(
                        children.Count(child => child.BrowseName.Name == "Value"),
                        Is.EqualTo(1));
                    Assert.That(
                        manager.Find(source.ReplacedPlaceholderId),
                        Is.Null);
                    Assert.That(
                        source.ReferenceOwner.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            source.ImportedChildId),
                        Is.True);
                    Assert.That(
                        source.ReferenceOwner.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            source.ReplacedPlaceholderId),
                        Is.False);
                    Assert.That(
                        ((NodeState)imported).ReferenceExists(
                            ReferenceTypeIds.Organizes,
                            false,
                            source.ImportedChildId),
                        Is.True);
                    Assert.That(
                        ((NodeState)imported).ReferenceExists(
                            ReferenceTypeIds.Organizes,
                            false,
                            source.ReplacedPlaceholderId),
                        Is.False);
                    Assert.That(source.ImportedLookupWonBeforeFinalization, Is.True);
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ImportedTypedChildCanReplacePlaceholderWithSameNodeIdAsync()
        {
            var source = new SameNodeIdImportedChildSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                var manager = (NodeSourceNodeManager)registration.NodeManager;
                NodeState imported = manager.Find(source.ImportedChildId);

                Assert.Multiple(() =>
                {
                    Assert.That(imported, Is.SameAs(source.Parent.Value));
                    Assert.That(imported, Is.Not.SameAs(source.OriginalPlaceholder));
                    Assert.That(
                        ((BaseInstanceState)imported).Parent,
                        Is.SameAs(source.Parent));
                    Assert.That(
                        ((BaseVariableState)imported).OnSimpleReadValue,
                        Is.Not.Null);
                    Assert.That(source.ImportedNodeAddedCount, Is.EqualTo(1));
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public void ImportedMissingOwnedParentFailsBeforeRegistration()
        {
            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(new MissingOwnedImportParentSource())
                        .ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ImportedReplacementOfConfiguredPlaceholderFailsBeforeRegistration()
        {
            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(
                            new ConfiguredPlaceholderImportSource())
                        .ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void ImportedReplacementWithConfiguredMissingDescendantFailsBeforeRegistration()
        {
            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(
                            new ConfiguredMissingDescendantImportSource())
                        .ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ImportedReplacementRemovesReferencesToOmittedDescendantsAsync()
        {
            var source = new OmittedDescendantImportSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                var manager = (NodeSourceNodeManager)registration.NodeManager;
                var references = new List<IReference>();
                source.ReferenceOwner.GetReferences(
                    manager.SystemContext,
                    references);

                Assert.Multiple(() =>
                {
                    Assert.That(manager.Find(source.OmittedNodeId), Is.Null);
                    Assert.That(
                        source.ReferenceOwner.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            source.OmittedNodeId),
                        Is.False);
                    Assert.That(
                        references.Exists(reference =>
                            ExpandedNodeId.ToNodeId(
                                reference.TargetId,
                                manager.SystemContext.NamespaceUris) ==
                            source.OmittedNodeId),
                        Is.False);
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task GeneratedImportProviderCreatesTypedStatesThroughGraphImportAsync()
        {
            ArrayOf<INodeSetImportFactory> factories =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeSetImportFactoryProvider
                    .Instance
                    .GetNodeSetImportFactories();
            Assert.That(
                factories.ToArray()
                    .Select(factory =>
                        $"{factory.NodeClass}|{factory.Discriminator}|" +
                        $"{factory.DiscriminatorId}")
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(factories.Count),
                "Generated registrations must not contain duplicate discriminator keys.");

            INodeSetImportFactory objectFactory = FindGeneratedImportFactory(
                factories,
                NodeClass.Object,
                NodeSetImportDiscriminator.TypeDefinition,
                1000u);
            INodeSetImportFactory methodFactory = FindGeneratedImportFactory(
                factories,
                NodeClass.Method,
                NodeSetImportDiscriminator.MethodDeclaration,
                1003u);
            var emptyObject =
                (GeneratedNodeSourceModel.DeviceState)objectFactory.CreateEmptyState();
            var secondEmptyObject =
                (GeneratedNodeSourceModel.DeviceState)objectFactory.CreateEmptyState();
            var emptyMethod =
                (GeneratedNodeSourceModel.CalibrateMethodState)methodFactory.CreateEmptyState();
            var emptyObjectChildren = new List<BaseInstanceState>();
            var emptyObjectReferences = new List<IReference>();
            var emptyMethodChildren = new List<BaseInstanceState>();
            var emptyMethodReferences = new List<IReference>();
            emptyObject.GetChildren(
                m_server.CurrentInstance.DefaultSystemContext,
                emptyObjectChildren);
            emptyObject.GetReferences(
                m_server.CurrentInstance.DefaultSystemContext,
                emptyObjectReferences);
            emptyMethod.GetChildren(
                m_server.CurrentInstance.DefaultSystemContext,
                emptyMethodChildren);
            emptyMethod.GetReferences(
                m_server.CurrentInstance.DefaultSystemContext,
                emptyMethodReferences);
            Assert.Multiple(() =>
            {
                Assert.That(emptyObject, Is.Not.SameAs(secondEmptyObject));
                Assert.That(emptyObject.IsCreated, Is.False);
                Assert.That(emptyObject.NodeId.IsNull, Is.True);
                Assert.That(emptyObject.Value, Is.Null);
                Assert.That(emptyObject.Calibrate, Is.Null);
                Assert.That(emptyObjectChildren, Is.Empty);
                Assert.That(emptyObjectReferences, Is.Empty);
                Assert.That(emptyMethod.IsCreated, Is.False);
                Assert.That(emptyMethod.NodeId.IsNull, Is.True);
                Assert.That(emptyMethod.InputArguments, Is.Null);
                Assert.That(emptyMethod.OutputArguments, Is.Null);
                Assert.That(emptyMethodChildren, Is.Empty);
                Assert.That(emptyMethodReferences, Is.Empty);
            });

            var source = new GeneratedNodeSetImportSource();
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            try
            {
                var manager = (NodeSourceNodeManager)registration.NodeManager;
                ushort namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                NodeId Id(uint identifier) => new(identifier, namespaceIndex);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        manager.Find(Id(1000u)),
                        Is.TypeOf<BaseObjectTypeState>());
                    Assert.That(
                        manager.Find(Id(1001u)),
                        Is.TypeOf<BaseDataVariableTypeState>());
                    Assert.That(
                        manager.Find(Id(1010u)),
                        Is.TypeOf<GeneratedNodeSourceModel.CalibrateMethodState>());
                    Assert.That(
                        manager.Find(Id(1002u)),
                        Is.TypeOf<GeneratedNodeSourceModel.CustomValueState>());
                    Assert.That(
                        manager.Find(Id(2000u)),
                        Is.TypeOf<GeneratedNodeSourceModel.DeviceState>());
                    Assert.That(
                        manager.Find(Id(2001u)),
                        Is.TypeOf<GeneratedNodeSourceModel.CustomValueState>());
                    Assert.That(
                        manager.Find(Id(2002u)),
                        Is.TypeOf<GeneratedNodeSourceModel.CalibrateMethodState>());
                    Assert.That(
                        manager.Find(Id(2003u)),
                        Is.InstanceOf<PropertyState<ArrayOf<Argument>>>());
                    Assert.That(
                        manager.Find(Id(2004u)),
                        Is.InstanceOf<PropertyState<ArrayOf<Argument>>>());
                });

                var device =
                    (GeneratedNodeSourceModel.DeviceState)manager.Find(Id(2000u));
                var method =
                    (GeneratedNodeSourceModel.CalibrateMethodState)manager.Find(Id(2002u));
                Assert.Multiple(() =>
                {
                    Assert.That(device.Value?.NodeId, Is.EqualTo(Id(2001u)));
                    Assert.That(device.Calibrate?.NodeId, Is.EqualTo(Id(2002u)));
                    Assert.That(method.InputArguments?.NodeId, Is.EqualTo(Id(2003u)));
                    Assert.That(method.OutputArguments?.NodeId, Is.EqualTo(Id(2004u)));
                });
            }
            finally
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public void BuildExceptionLeavesNoCommittedRegistration()
        {
            var source = new FailingSource();
            int registrationCount = m_server.NodeManagerLifecycle.Registrations.Count;

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_server.NodeManagerLifecycle
                    .AddNodeSourceAsync(source)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo(FailingSource.FailureMessage));
                Assert.That(source.BuildCount, Is.EqualTo(1));
                Assert.That(
                    m_server.NodeManagerLifecycle.Registrations.Count,
                    Is.EqualTo(registrationCount));
            });
        }

        [Test]
        public void BuildCancellationLeavesNoCommittedRegistration()
        {
            using var cancellation = new CancellationTokenSource();
            var source = new CancelingSource(cancellation);
            int registrationCount = m_server.NodeManagerLifecycle.Registrations.Count;

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddNodeSourceAsync(
                        source,
                        callerContext: null,
                        cancellation.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.Multiple(() =>
            {
                Assert.That(source.BuildCount, Is.EqualTo(1));
                Assert.That(
                    m_server.NodeManagerLifecycle.Registrations.Count,
                    Is.EqualTo(registrationCount));
            });
        }

        private async Task AssertGraphVisibleAsync(
            GraphSource source,
            int expectedValue)
        {
            IServerInternal server = m_server.CurrentInstance;
            NodeState folder = await server.NodeManager
                .FindNodeInAddressSpaceAsync(source.FolderId)
                .ConfigureAwait(false);
            NodeState instance = await server.NodeManager
                .FindNodeInAddressSpaceAsync(source.ObjectId)
                .ConfigureAwait(false);
            NodeState variable = await server.NodeManager
                .FindNodeInAddressSpaceAsync(source.VariableId)
                .ConfigureAwait(false);
            NodeState method = await server.NodeManager
                .FindNodeInAddressSpaceAsync(source.MethodId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(folder, Is.TypeOf<FolderState>());
                Assert.That(instance, Is.TypeOf<BaseObjectState>());
                Assert.That(variable, Is.TypeOf<BaseDataVariableState>());
                Assert.That(method, Is.TypeOf<MethodState>());
            });

            BrowseResponse objectsBrowse = await BrowseAsync(ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);
            Assert.That(
                objectsBrowse.Results[0].References.Contains(reference =>
                    reference.BrowseName == source.FolderBrowseName),
                Is.True);

            BrowseResponse folderBrowse = await BrowseAsync(source.FolderId)
                .ConfigureAwait(false);
            Assert.That(
                folderBrowse.Results[0].References.Contains(reference =>
                    reference.BrowseName == source.ObjectBrowseName),
                Is.True);

            BrowseResponse objectBrowse = await BrowseAsync(source.ObjectId)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(
                    objectBrowse.Results[0].References.Contains(reference =>
                        reference.BrowseName == source.VariableBrowseName),
                    Is.True);
                Assert.That(
                    objectBrowse.Results[0].References.Contains(reference =>
                        reference.BrowseName == source.MethodBrowseName),
                    Is.True);
            });

            await AssertValueAsync(source.VariableId, expectedValue)
                .ConfigureAwait(false);
        }

        private async Task AssertValueAsync(NodeId nodeId, int expectedValue)
        {
            DataValue value = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(expectedValue));
            });
        }

        private async Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
            ArrayOf<ReadValueId> nodesToRead =
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }];
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse response = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToRead,
                response.ResponseHeader.StringTable,
                m_logger);
            return response.Results[0];
        }

        private async Task<BrowseResponse> BrowseAsync(
            NodeId nodeId,
            BrowseDirection browseDirection = BrowseDirection.Forward)
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = browseDirection,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                    [nodeId],
                    template);

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await services
                .BrowseAsync(
                    m_requestHeader,
                    view: null,
                    requestedMaxReferencesPerNode: 0,
                    nodesToBrowse)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                nodesToBrowse);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToBrowse,
                response.ResponseHeader.StringTable,
                m_logger);
            return response;
        }

        private async Task CallMethodAsync(
            NodeId objectId,
            NodeId methodId)
        {
            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = objectId,
                    MethodId = methodId
                }
            ];
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse response = await m_server.CallAsync(
                m_secureChannelContext,
                m_requestHeader,
                methodsToCall,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                methodsToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                methodsToCall,
                response.ResponseHeader.StringTable,
                m_logger);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private static void AssertStableNodeIds(
            GraphSource expected,
            GraphSource actual)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.FolderId, Is.EqualTo(expected.FolderId));
                Assert.That(actual.ObjectId, Is.EqualTo(expected.ObjectId));
                Assert.That(actual.VariableId, Is.EqualTo(expected.VariableId));
                Assert.That(actual.MethodId, Is.EqualTo(expected.MethodId));
                Assert.That(actual.BuildCount, Is.EqualTo(1));
            });
        }

        private int CountRegistrations(Guid registrationId)
        {
            int count = 0;
            ArrayOf<NodeManagerRegistration> registrations =
                m_server.NodeManagerLifecycle.Registrations;
            for (int i = 0; i < registrations.Count; i++)
            {
                if (registrations[i].Id == registrationId)
                {
                    count++;
                }
            }
            return count;
        }

        private static INodeSetImportFactory FindGeneratedImportFactory(
            ArrayOf<INodeSetImportFactory> factories,
            NodeClass nodeClass,
            NodeSetImportDiscriminator discriminator,
            uint identifier)
        {
            var expectedId = new ExpandedNodeId(
                identifier,
                GeneratedNodeSetImportSource.NamespaceUri);
            return factories.ToArray().Single(factory =>
                factory.NodeClass == nodeClass &&
                factory.Discriminator == discriminator &&
                factory.DiscriminatorId == expectedId);
        }

        private sealed class GraphSource : INodeSource
        {
            public GraphSource(int generation)
            {
                m_generation = generation;
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public QualifiedName FolderBrowseName { get; private set; }

            public QualifiedName ObjectBrowseName { get; private set; }

            public QualifiedName VariableBrowseName { get; private set; }

            public QualifiedName MethodBrowseName { get; private set; }

            public NodeId FolderId { get; private set; }

            public NodeId ObjectId { get; private set; }

            public NodeId VariableId { get; private set; }

            public NodeId MethodId { get; private set; }

            public NodeId FolderReferenceTypeId { get; private set; }

            public NodeId ObjectReferenceTypeId { get; private set; }

            public NodeId VariableReferenceTypeId { get; private set; }

            public NodeId MethodReferenceTypeId { get; private set; }

            public int BuildCount { get; private set; }

            public int MethodCallCount { get; private set; }

            public int NodeAddedCount { get; private set; }

            public int NodeRemovedCount { get; private set; }

            public int MonitoredItemCreatedCount { get; private set; }

            public int MonitoredItemDeletedCount { get; private set; }

            public bool ExistingResolversSeeAuthoredGraph { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BuildCount++;

                INodeBuilder<FolderState> folder =
                    builder.AddFolder("NodeSourceRoot");
                FolderId = folder.Node.NodeId;
                FolderBrowseName = folder.Node.BrowseName;
                FolderReferenceTypeId = folder.Node.ReferenceTypeId;

                INodeBuilder<BaseObjectState> instance =
                    builder.AddObject("Device", FolderId);
                ObjectId = instance.Node.NodeId;
                ObjectBrowseName = instance.Node.BrowseName;
                ObjectReferenceTypeId = instance.Node.ReferenceTypeId;

                IVariableBuilder<int> variable =
                    builder.AddVariable<int>("Value", ObjectId);
                variable.Node.WrappedValue = new Variant(m_generation);
                variable.OnNodeAdded((_, _) => NodeAddedCount++);
                variable.OnNodeRemoved((_, _) => NodeRemovedCount++);
                variable.OnMonitoredItemCreated((_, _, _) => MonitoredItemCreatedCount++);
                variable.OnMonitoredItemDeleted((_, _, _, _) =>
                {
                    MonitoredItemDeletedCount++;
                    return default;
                });
                VariableId = variable.Node.NodeId;
                VariableBrowseName = variable.Node.BrowseName;
                VariableReferenceTypeId = variable.Node.ReferenceTypeId;

                INodeBuilder<MethodState> method =
                    builder.AddMethod("Reset", ObjectId);
                method.OnCall(
                    (_, _, _, _, _, _) =>
                    {
                        MethodCallCount++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });
                MethodId = method.Node.NodeId;
                MethodBrowseName = method.Node.BrowseName;
                MethodReferenceTypeId = method.Node.ReferenceTypeId;
                ExistingResolversSeeAuthoredGraph =
                    ReferenceEquals(
                        builder.Node<BaseObjectState>(ObjectId).Node,
                        instance.Node) &&
                    ReferenceEquals(
                        builder.Node<MethodState>(
                            "NodeSourceRoot/Device/Reset").Node,
                        method.Node);
                return default;
            }

            private readonly int m_generation;
        }

        private sealed class FailingSource : INodeSource
        {
            public const string FailureMessage = "Node source build failed.";

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public int BuildCount { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                BuildCount++;
                builder.AddFolder("Uncommitted");
                throw new InvalidOperationException(FailureMessage);
            }
        }

        private sealed class CancelingSource : INodeSource
        {
            public CancelingSource(CancellationTokenSource cancellation)
            {
                m_cancellation = cancellation;
            }

            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public int BuildCount { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                BuildCount++;
                builder.AddFolder("Canceled");
                m_cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            private readonly CancellationTokenSource m_cancellation;
        }

        private sealed class ImportedGraphSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris => [kImportedNamespaceUri];

            public NodeId ObjectId { get; private set; }

            public NodeId VariableId { get; private set; }

            public NodeId MethodId { get; private set; }

            public NodeId AuthoredChildId { get; private set; }

            public int MethodCallCount { get; private set; }

            public bool TypedNodesResolvedDuringBuild { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Import(ReadNodeSet());
                builder.AddFolder("AuthoredRoot");
                var namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    kImportedNamespaceUri);
                ObjectId = new NodeId(200u, namespaceIndex);
                VariableId = new NodeId(201u, namespaceIndex);
                MethodId = new NodeId(202u, namespaceIndex);
                AuthoredChildId = builder.AddObject(
                    "AuthoredChild",
                    ObjectId).Node.NodeId;

                ImportedDeviceState instance =
                    builder.Node<ImportedDeviceState>(ObjectId).Node;
                ImportedValueState variable =
                    builder.Node<ImportedValueState>(VariableId).Node;
                INodeBuilder<ImportedResetMethodState> method =
                    builder.Node<ImportedResetMethodState>(MethodId);
                variable.Value = new Variant(42);
                method.OnCall(
                    (_, _, _, _, _, _) =>
                    {
                        MethodCallCount++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });
                TypedNodesResolvedDuringBuild =
                    instance.TypeDefinitionId ==
                        new NodeId(100u, namespaceIndex) &&
                    variable.TypeDefinitionId ==
                        new NodeId(101u, namespaceIndex) &&
                    method.Node.MethodDeclarationId ==
                        new NodeId(102u, namespaceIndex) &&
                    ReferenceEquals(
                        builder.NodeFromTypeId<ImportedDeviceState>(
                            new NodeId(100u, namespaceIndex),
                            instance.BrowseName).Node,
                        instance) &&
                    ReferenceEquals(
                        builder.VariableFromDataTypeId<int>(
                            DataTypeIds.Int32,
                            variable.BrowseName).Node,
                        variable);
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return
                [
                    new ImportedNodeFactory(
                        NodeClass.Object,
                        new ExpandedNodeId(100u, kImportedNamespaceUri),
                        static () => new ImportedDeviceState(null)),
                    new ImportedNodeFactory(
                        NodeClass.Variable,
                        new ExpandedNodeId(101u, kImportedNamespaceUri),
                        static () => new ImportedValueState(null)),
                    new ImportedNodeFactory(
                        NodeClass.Method,
                        new ExpandedNodeId(102u, kImportedNamespaceUri),
                        static () => new ImportedResetMethodState(null))
                ];
            }

            public static UANodeSet ReadNodeSet()
            {
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                    "  <NamespaceUris>\r\n" +
                    $"    <Uri>{kImportedNamespaceUri}</Uri>\r\n" +
                    "  </NamespaceUris>\r\n" +
                    "  <UAObjectType NodeId=\"ns=1;i=100\" BrowseName=\"1:ImportedDeviceType\">\r\n" +
                    "    <DisplayName>ImportedDeviceType</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=45\" IsForward=\"false\">i=58</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAObjectType>\r\n" +
                    "  <UAVariableType NodeId=\"ns=1;i=101\" BrowseName=\"1:ImportedValueType\" " +
                    "DataType=\"i=6\">\r\n" +
                    "    <DisplayName>ImportedValueType</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=45\" IsForward=\"false\">i=63</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAVariableType>\r\n" +
                    "  <UAObject NodeId=\"ns=1;i=200\" BrowseName=\"1:ImportedDevice\">\r\n" +
                    "    <DisplayName>ImportedDevice</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=40\">ns=1;i=100</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=35\" IsForward=\"false\">i=85</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\">ns=1;i=201</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\">ns=1;i=202</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAObject>\r\n" +
                    "  <UAVariable NodeId=\"ns=1;i=201\" BrowseName=\"1:Value\" " +
                    "ParentNodeId=\"ns=1;i=200\" DataType=\"i=6\">\r\n" +
                    "    <DisplayName>Value</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=40\">ns=1;i=101</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">ns=1;i=200</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAVariable>\r\n" +
                    "  <UAMethod NodeId=\"ns=1;i=202\" BrowseName=\"1:Reset\" " +
                    "ParentNodeId=\"ns=1;i=200\" MethodDeclarationId=\"ns=1;i=102\">\r\n" +
                    "    <DisplayName>Reset</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">ns=1;i=200</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAMethod>\r\n" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream);
            }
        }

        private sealed class OmittedDescendantImportSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSetImportSource.NamespaceUri];

            public NodeId OmittedNodeId { get; private set; }

            public BaseObjectState ReferenceOwner { get; private set; } = null!;

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                GeneratedNodeSourceModel.DeviceState parent =
                    GeneratedNodeSourceModel.
                        GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                        AddDeviceType(
                            builder,
                            new QualifiedName(
                                "AuthoredTypedParent",
                                namespaceIndex)).Node;
                OmittedNodeId = parent.Calibrate!.InputArguments!.NodeId;
                ReferenceOwner = builder.AddObject("ReferenceOwner").Node;
                ReferenceOwner.AddReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    OmittedNodeId);
                ReferenceOwner.AddReference(
                    ReferenceTypeIds.HasProperty,
                    false,
                    new ExpandedNodeId(
                        OmittedNodeId.IdentifierAsString,
                        GeneratedNodeSetImportSource.NamespaceUri));
                builder.Import(
                    ConfiguredMissingDescendantImportSource.ReadNodeSet());
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }
        }

        private sealed class GeneratedNodeSetImportSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris => [NamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Import(ReadNodeSet());
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }

            public static UANodeSet ReadNodeSet()
            {
                using Stream stream = typeof(GeneratedNodeSetImportSource)
                    .Assembly
                    .GetManifestResourceStream(
                        "Opc.Ua.Server.Tests.Nodes.Assets." +
                        "GeneratedNodeSource.NodeSet2.xml");
                if (stream is null)
                {
                    throw new InvalidOperationException(
                        "The generated node-source test NodeSet resource was not found.");
                }
                return UANodeSet.Read(stream);
            }

            public const string NamespaceUri =
                "urn:opcfoundation.org:2026-09:GeneratedNodeSource";
            public const string InstanceNamespaceUri =
                "urn:opcfoundation.org:2026-09:GeneratedNodeSource:Instance";
        }

        private sealed class ImportedChildWithAuthoredParentSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSetImportSource.NamespaceUri];

            public GeneratedNodeSourceModel.DeviceState Parent { get; private set; } = null!;

            public NodeId ImportedChildId { get; private set; }

            public NodeId ReplacedPlaceholderId { get; private set; }

            public BaseObjectState ReferenceOwner { get; private set; } = null!;

            public bool ImportedLookupWonBeforeFinalization { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Import(ReadNodeSet());
                ushort namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                Parent = GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                        builder,
                        new QualifiedName(
                            "AuthoredTypedParent",
                            namespaceIndex)).Node;
                ReplacedPlaceholderId = Parent.Value!.NodeId;
                ReferenceOwner = builder.AddObject("ReferenceOwner").Node;
                ReferenceOwner.AddReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    ReplacedPlaceholderId);
                ImportedChildId = new NodeId(3100u, namespaceIndex);
                builder.Node(ImportedChildId).Node.AddReference(
                    ReferenceTypeIds.Organizes,
                    false,
                    ReplacedPlaceholderId);
                ImportedLookupWonBeforeFinalization = ReferenceEquals(
                    builder.VariableFromDataTypeId<int>(
                        DataTypeIds.Int32,
                        new QualifiedName("Value", namespaceIndex)).Node,
                    builder.Node(ImportedChildId).Node);
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }

            public static UANodeSet ReadNodeSet()
            {
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                    "  <NamespaceUris>\r\n" +
                    $"    <Uri>{GeneratedNodeSetImportSource.NamespaceUri}</Uri>\r\n" +
                    "  </NamespaceUris>\r\n" +
                    "  <UAVariable NodeId=\"ns=1;i=3100\" BrowseName=\"1:Value\" " +
                    "ParentNodeId=\"ns=1;s=AuthoredTypedParent\" DataType=\"i=6\">\r\n" +
                    "    <DisplayName>Value</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=40\">ns=1;i=1001</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">" +
                    "ns=1;s=AuthoredTypedParent</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAVariable>\r\n" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream);
            }
        }

        private sealed class ConfiguredPlaceholderImportSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSetImportSource.NamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                GeneratedNodeSourceModel.DeviceState parent =
                    GeneratedNodeSourceModel.
                        GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                        AddDeviceType(
                            builder,
                            new QualifiedName(
                                "AuthoredTypedParent",
                                namespaceIndex)).Node;
                builder.Variable<int>(parent.Value!.NodeId).OnRead(static () => 1);
                builder.Import(ImportedChildWithAuthoredParentSource.ReadNodeSet());
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }
        }

        private sealed class ConfiguredMissingDescendantImportSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSetImportSource.NamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                GeneratedNodeSourceModel.DeviceState parent =
                    GeneratedNodeSourceModel.
                        GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                        AddDeviceType(
                            builder,
                            new QualifiedName(
                                "AuthoredTypedParent",
                                namespaceIndex)).Node;
                builder.Node(parent.Calibrate!.InputArguments!.NodeId)
                    .OnNodeAdded(static (_, _) => { });
                builder.Import(ReadNodeSet());
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }

            public static UANodeSet ReadNodeSet()
            {
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                    "  <NamespaceUris>\r\n" +
                    $"    <Uri>{GeneratedNodeSetImportSource.NamespaceUri}</Uri>\r\n" +
                    "  </NamespaceUris>\r\n" +
                    "  <UAMethod NodeId=\"ns=1;i=3101\" BrowseName=\"1:Calibrate\" " +
                    "ParentNodeId=\"ns=1;s=AuthoredTypedParent\" " +
                    "MethodDeclarationId=\"ns=1;i=1010\">\r\n" +
                    "    <DisplayName>Calibrate</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">" +
                    "ns=1;s=AuthoredTypedParent</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAMethod>\r\n" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream);
            }
        }

        private sealed class SameNodeIdImportedChildSource :
            INodeSource,
            INodeSetImportFactoryProvider
        {
            public ArrayOf<string> NamespaceUris =>
                [GeneratedNodeSetImportSource.NamespaceUri];

            public GeneratedNodeSourceModel.DeviceState Parent { get; private set; } = null!;

            public BaseVariableState OriginalPlaceholder { get; private set; } = null!;

            public NodeId ImportedChildId { get; private set; }

            public int ImportedNodeAddedCount { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort namespaceIndex = (ushort)builder.Context.NamespaceUris.GetIndex(
                    GeneratedNodeSetImportSource.NamespaceUri);
                Parent = GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                        builder,
                        new QualifiedName(
                            "AuthoredSameIdParent",
                            namespaceIndex)).Node;
                OriginalPlaceholder = Parent.Value!;
                ImportedChildId = OriginalPlaceholder.NodeId;
                builder.Import(ReadNodeSet());
                IVariableBuilder<int> imported =
                    builder.Variable<int>(ImportedChildId);
                imported.OnRead(static () => 99);
                imported.OnNodeAdded((_, _) => ImportedNodeAddedCount++);
                return default;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return GeneratedNodeSourceModel.
                    GeneratedNodeSourceModelNodeSetImportFactoryProvider.Instance.
                    GetNodeSetImportFactories();
            }

            private UANodeSet ReadNodeSet()
            {
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                    "  <NamespaceUris>\r\n" +
                    $"    <Uri>{GeneratedNodeSetImportSource.NamespaceUri}</Uri>\r\n" +
                    "  </NamespaceUris>\r\n" +
                    $"  <UAVariable NodeId=\"ns=1;s={ImportedChildId.IdentifierAsString}\" " +
                    "BrowseName=\"1:Value\" " +
                    "ParentNodeId=\"ns=1;s=AuthoredSameIdParent\" DataType=\"i=6\">\r\n" +
                    "    <DisplayName>Value</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=40\">ns=1;i=1001</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">" +
                    "ns=1;s=AuthoredSameIdParent</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAVariable>\r\n" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream);
            }
        }

        private sealed class MissingOwnedImportParentSource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kImportedNamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Import(ReadNodeSet());
                return default;
            }

            private static UANodeSet ReadNodeSet()
            {
                const string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                    "  <NamespaceUris>\r\n" +
                    $"    <Uri>{kImportedNamespaceUri}</Uri>\r\n" +
                    "  </NamespaceUris>\r\n" +
                    "  <UAObject NodeId=\"ns=1;i=3200\" BrowseName=\"1:Orphan\" " +
                    "ParentNodeId=\"ns=1;i=3299\">\r\n" +
                    "    <DisplayName>Orphan</DisplayName>\r\n" +
                    "    <References>\r\n" +
                    "      <Reference ReferenceType=\"i=40\">i=58</Reference>\r\n" +
                    "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">" +
                    "ns=1;i=3299</Reference>\r\n" +
                    "    </References>\r\n" +
                    "  </UAObject>\r\n" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream);
            }
        }

        private sealed class ImportedNodeFactory : INodeSetImportFactory
        {
            public ImportedNodeFactory(
                NodeClass nodeClass,
                ExpandedNodeId discriminatorId,
                Func<NodeState> create)
            {
                NodeClass = nodeClass;
                DiscriminatorId = discriminatorId;
                m_create = create;
            }

            public NodeClass NodeClass { get; }

            public NodeSetImportDiscriminator Discriminator =>
                NodeClass switch
                {
                    NodeClass.Object or NodeClass.Variable =>
                        NodeSetImportDiscriminator.TypeDefinition,
                    NodeClass.Method =>
                        NodeSetImportDiscriminator.MethodDeclaration,
                    _ => NodeSetImportDiscriminator.NodeId
                };

            public ExpandedNodeId DiscriminatorId { get; }

            public NodeState CreateEmptyState()
            {
                return m_create();
            }

            private readonly Func<NodeState> m_create;
        }

        private sealed class ImportedDeviceState : BaseObjectState
        {
            public ImportedDeviceState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class ImportedValueState : BaseDataVariableState
        {
            public ImportedValueState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class ImportedResetMethodState : MethodState
        {
            public ImportedResetMethodState(NodeState parent)
                : base(parent)
            {
            }
        }
    }
}
