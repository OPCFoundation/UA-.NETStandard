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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
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
            await CallMethodAsync(initial).ConfigureAwait(false);
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

        private async Task CallMethodAsync(GraphSource source)
        {
            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = source.ObjectId,
                    MethodId = source.MethodId
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

            public bool ExistingResolversSeeAuthoredGraph { get; private set; }

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BuildCount++;

                INodeBuilder<FolderState> folder =
                    builder.AddFolder(new QualifiedName("NodeSourceRoot"));
                FolderId = folder.Node.NodeId;
                FolderBrowseName = folder.Node.BrowseName;
                FolderReferenceTypeId = folder.Node.ReferenceTypeId;

                INodeBuilder<BaseObjectState> instance =
                    builder.AddObject(new QualifiedName("Device"), FolderId);
                ObjectId = instance.Node.NodeId;
                ObjectBrowseName = instance.Node.BrowseName;
                ObjectReferenceTypeId = instance.Node.ReferenceTypeId;

                IVariableBuilder<int> variable =
                    builder.AddVariable<int>(new QualifiedName("Value"), ObjectId);
                variable.Node.WrappedValue = new Variant(m_generation);
                variable.OnNodeAdded((_, _) => NodeAddedCount++);
                variable.OnNodeRemoved((_, _) => NodeRemovedCount++);
                VariableId = variable.Node.NodeId;
                VariableBrowseName = variable.Node.BrowseName;
                VariableReferenceTypeId = variable.Node.ReferenceTypeId;

                INodeBuilder<MethodState> method =
                    builder.AddMethod(new QualifiedName("Reset"), ObjectId);
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
                builder.AddFolder(new QualifiedName("Uncommitted"));
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
                builder.AddFolder(new QualifiedName("Canceled"));
                m_cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            private readonly CancellationTokenSource m_cancellation;
        }
    }
}
