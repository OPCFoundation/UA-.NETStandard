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
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for the NodeManagement dispatcher methods on
    /// <see cref="MasterNodeManager"/>: per-item validation paths
    /// (BadNothingToDo / BadBrowseNameInvalid / BadParentNodeIdInvalid /
    /// BadReferenceTypeIdInvalid / BadReferenceNotAllowed /
    /// BadSourceNodeIdInvalid / BadTargetNodeIdInvalid /
    /// BadNodeIdInvalid / BadUserAccessDenied) and per-item routing.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [Category("NodeManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MasterNodeManagerNodeManagementTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/MasterNodeManagement/";

        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task AddNodesAsync_NullItem_ReturnsBadNothingToDoAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                ctx,
                new AddNodesItem[] { null! }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task AddNodesAsync_NullBrowseName_ReturnsBadBrowseNameInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                ctx, new AddNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public async Task AddNodesAsync_NullParentNodeId_ReturnsBadParentNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddNodesItem
            {
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Test", 2),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                ctx, new AddNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadParentNodeIdInvalid));
        }

        [Test]
        public async Task AddNodesAsync_NullReferenceTypeId_ReturnsBadReferenceTypeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.ObjectsFolder,
                BrowseName = new QualifiedName("Test", 2),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                ctx, new AddNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Test]
        public async Task AddNodesAsync_NonHierarchicalReferenceType_ReturnsBadReferenceNotAllowedAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                BrowseName = new QualifiedName("Test", 2),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                ctx, new AddNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        [Test]
        public async Task AddNodesAsyncRequestedIdInUnownedNamespaceReturnsBadUserAccessDeniedAsync()
        {
            Mock<INodeManagerWithNodeManagement> manager = CreateNodeManagementManager(true);
            using MasterNodeManager sut = CreateMasterNodeManager(manager.Object);
            ushort namespaceIndex = GetTestNamespaceIndex();
            NodeId parentNodeId = ConfigureParent(manager, namespaceIndex);
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = new NodeId("Requested", ushort.MaxValue),
                BrowseName = new QualifiedName("Requested", ushort.MaxValue),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                CreateContext(),
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddNodesAsyncRequestedIdInOptedOutNamespaceReturnsBadUserAccessDeniedAsync()
        {
            Mock<INodeManagerWithNodeManagement> manager = CreateNodeManagementManager(false);
            using MasterNodeManager sut = CreateMasterNodeManager(manager.Object);
            ushort namespaceIndex = GetTestNamespaceIndex();
            NodeId parentNodeId = ConfigureParent(manager, namespaceIndex);
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = new NodeId("Requested", namespaceIndex),
                BrowseName = new QualifiedName("Requested", namespaceIndex),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                CreateContext(),
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddNodesAsyncRequestedIdInSupportedNamespaceIsDispatchedAsync()
        {
            Mock<INodeManagerWithNodeManagement> manager = CreateNodeManagementManager(true);
            using MasterNodeManager sut = CreateMasterNodeManager(manager.Object);
            ushort namespaceIndex = GetTestNamespaceIndex();
            NodeId parentNodeId = ConfigureParent(manager, namespaceIndex);
            var requestedNodeId = new NodeId("Requested", namespaceIndex);
            manager
                .Setup(m => m.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceResult.Good, requestedNodeId));
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = requestedNodeId,
                BrowseName = new QualifiedName("Requested", namespaceIndex),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                CreateContext(),
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].AddedNodeId, Is.EqualTo(requestedNodeId));
        }

        [Test]
        public async Task AddNodesAsyncAuthorizationFailureFromOwnerIsPreservedAsync()
        {
            Mock<INodeManagerWithNodeManagement> manager = CreateNodeManagementManager(true);
            using MasterNodeManager sut = CreateMasterNodeManager(manager.Object);
            ushort namespaceIndex = GetTestNamespaceIndex();
            NodeId parentNodeId = ConfigureParent(manager, namespaceIndex);
            manager
                .Setup(m => m.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    new ServiceResult(StatusCodes.BadUserAccessDenied),
                    NodeId.Null));
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = new NodeId("Requested", namespaceIndex),
                BrowseName = new QualifiedName("Requested", namespaceIndex),
                NodeClass = NodeClass.Object
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                CreateContext(),
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddNodesAsyncHasSubtypeWithIncompatibleNodeClassesReturnsBadReferenceNotAllowedAsync()
        {
            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.RootFolder,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                BrowseName = new QualifiedName("Variable", 2),
                NodeClass = NodeClass.Variable
            };

            (ArrayOf<AddNodesResult> results, _) =
                await m_server.CurrentInstance.NodeManager.AddNodesAsync(
                    CreateContext(),
                    new AddNodesItem[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        [TestCase(ValidReferenceKind.Organizes, NodeClass.Object)]
        [TestCase(ValidReferenceKind.HasComponent, NodeClass.Variable)]
        [TestCase(ValidReferenceKind.HasProperty, NodeClass.Variable)]
        public async Task AddNodesAsyncValidReferenceNodeClassesAreNotRejectedAsync(
            ValidReferenceKind referenceKind,
            NodeClass nodeClass)
        {
            Mock<INodeManagerWithNodeManagement> manager = CreateNodeManagementManager(true);
            using MasterNodeManager sut = CreateMasterNodeManager(manager.Object);
            ushort namespaceIndex = GetTestNamespaceIndex();
            NodeId parentNodeId = ConfigureParent(manager, namespaceIndex);
            var addedNodeId = new NodeId($"Added{referenceKind}", namespaceIndex);
            manager
                .Setup(m => m.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceResult.Good, addedNodeId));
            NodeId referenceTypeId = referenceKind switch
            {
                ValidReferenceKind.Organizes => ReferenceTypeIds.Organizes,
                ValidReferenceKind.HasComponent => ReferenceTypeIds.HasComponent,
                _ => ReferenceTypeIds.HasProperty
            };
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = referenceTypeId,
                BrowseName = new QualifiedName($"Added{referenceKind}", namespaceIndex),
                NodeClass = nodeClass
            };

            (ArrayOf<AddNodesResult> results, _) = await sut.AddNodesAsync(
                CreateContext(),
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task DeleteNodesAsync_NullItem_ReturnsBadNothingToDoAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.DeleteNodesAsync(
                ctx,
                new DeleteNodesItem[] { null! }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task DeleteNodesAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new DeleteNodesItem
            {
                DeleteTargetReferences = false
            };

            (ArrayOf<StatusCode> results, _) = await sut.DeleteNodesAsync(
                ctx, new DeleteNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task DeleteNodesAsync_UnknownNodeId_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new DeleteNodesItem
            {
                NodeId = new NodeId("DoesNotExist", 999),
                DeleteTargetReferences = false
            };

            (ArrayOf<StatusCode> results, _) = await sut.DeleteNodesAsync(
                ctx, new DeleteNodesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task AddReferencesAsync_NullItem_ReturnsBadNothingToDoAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.AddReferencesAsync(
                ctx,
                new AddReferencesItem[] { null! }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task AddReferencesAsync_NullSourceNodeId_ReturnsBadSourceNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddReferencesItem
            {
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, _) = await sut.AddReferencesAsync(
                ctx, new AddReferencesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSourceNodeIdInvalid));
        }

        [Test]
        public async Task AddReferencesAsync_NullTargetNodeId_ReturnsBadTargetNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddReferencesItem
            {
                SourceNodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = ReferenceTypeIds.Organizes
            };

            (ArrayOf<StatusCode> results, _) = await sut.AddReferencesAsync(
                ctx, new AddReferencesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadTargetNodeIdInvalid));
        }

        [Test]
        public async Task AddReferencesAsync_NullReferenceTypeId_ReturnsBadReferenceTypeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new AddReferencesItem
            {
                SourceNodeId = ObjectIds.ObjectsFolder,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, _) = await sut.AddReferencesAsync(
                ctx, new AddReferencesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Test]
        public async Task DeleteReferencesAsync_NullItem_ReturnsBadNothingToDoAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.DeleteReferencesAsync(
                ctx,
                new DeleteReferencesItem[] { null! }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task DeleteReferencesAsync_NullSourceNodeId_ReturnsBadSourceNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new DeleteReferencesItem
            {
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, _) = await sut.DeleteReferencesAsync(
                ctx, new DeleteReferencesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSourceNodeIdInvalid));
        }

        [Test]
        public async Task DeleteReferencesAsync_NullReferenceTypeId_ReturnsBadReferenceTypeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var item = new DeleteReferencesItem
            {
                SourceNodeId = ObjectIds.ObjectsFolder,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, _) = await sut.DeleteReferencesAsync(
                ctx, new DeleteReferencesItem[] { item }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Test]
        public async Task DeleteReferencesAsync_BadItem_OmitsDiagnostics_WhenNotRequestedAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext(DiagnosticsMasks.None);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = ObjectIds.ObjectsFolder,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnostics) =
                await sut.DeleteReferencesAsync(
                    ctx, new DeleteReferencesItem[] { item }.ToArrayOf(), CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
            Assert.That(diagnostics.IsNull || diagnostics.Count == 0, Is.True);
        }

        [Test]
        public async Task DeleteReferencesAsync_BadItem_ReturnsDiagnostics_WhenRequestedAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext(DiagnosticsMasks.OperationAll);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = ObjectIds.ObjectsFolder,
                TargetNodeId = ObjectIds.Server
            };

            (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnostics) =
                await sut.DeleteReferencesAsync(
                    ctx, new DeleteReferencesItem[] { item }.ToArrayOf(), CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task AddNodesAsync_BadItem_OmitsDiagnostics_WhenNotRequestedAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext(DiagnosticsMasks.None);

            (ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnostics) =
                await sut.AddNodesAsync(
                    ctx,
                    new AddNodesItem[] { null! }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
            Assert.That(diagnostics.IsNull || diagnostics.Count == 0, Is.True);
        }

        [Test]
        public async Task AddNodesAsync_BadItem_ReturnsDiagnostics_WhenRequestedAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext(DiagnosticsMasks.OperationAll);

            (ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnostics) =
                await sut.AddNodesAsync(
                    ctx,
                    new AddNodesItem[] { null! }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task AddNodesAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnostics) = await sut.AddNodesAsync(
                ctx,
                System.Array.Empty<AddNodesItem>().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
            Assert.That(diagnostics.IsNull || diagnostics.Count == 0, Is.True);
        }

        [Test]
        public void AddNodesAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.AddNodesAsync(
                    null!,
                    System.Array.Empty<AddNodesItem>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void DeleteNodesAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.DeleteNodesAsync(
                    null!,
                    System.Array.Empty<DeleteNodesItem>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void AddReferencesAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.AddReferencesAsync(
                    null!,
                    System.Array.Empty<AddReferencesItem>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void DeleteReferencesAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.DeleteReferencesAsync(
                    null!,
                    System.Array.Empty<DeleteReferencesItem>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public async Task AddNodesDeniedAddNodePermissionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(PermissionType.Browse);
            AddNodesItem item = harness.CreateAddNodesItem();

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                harness.Context,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SourceManager.Verify(
                manager => manager.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddNodesGrantedAddNodePermissionMutatesAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(PermissionType.AddNode);
            AddNodesItem item = harness.CreateAddNodesItem();

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                harness.Context,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.AddNodeAsync(
                    harness.Context,
                    item,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task AddNodesDeniedParentAddReferencePermissionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(PermissionType.AddNode);
            harness.SetSourcePermissions(PermissionType.Browse);
            AddNodesItem item = harness.CreateAddNodesItem();

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                harness.Context,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SourceManager.Verify(
                manager => manager.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddNodesParentAccessRestrictionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(PermissionType.AddNode);
            harness.SourceMetadata.AccessRestrictions = AccessRestrictionType.SigningRequired;
            AddNodesItem item = harness.CreateAddNodesItem();
            OperationContext insecureContext = harness.CreateContext(MessageSecurityMode.None);

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                insecureContext,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.SourceManager.Verify(
                manager => manager.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddNodesUsesRequestedTargetNamespacePolicyAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(
                harness.SourceNamespaceIndex,
                PermissionType.Browse);
            harness.SetAddNodePermissions(
                harness.TargetNamespaceIndex,
                PermissionType.AddNode);
            AddNodesItem item = harness.CreateAddNodesItem(harness.TargetNamespaceIndex);

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                harness.Context,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.TargetManager.Verify(
                manager => manager.AddNodeAsync(
                    harness.Context,
                    item,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task AddNodesNamespaceAccessRestrictionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetAddNodePermissions(
                harness.TargetNamespaceIndex,
                PermissionType.AddNode);
            harness.SetNamespaceAccessRestrictions(
                harness.TargetNamespaceIndex,
                AccessRestrictionType.SigningRequired);
            AddNodesItem item = harness.CreateAddNodesItem(harness.TargetNamespaceIndex);
            OperationContext insecureContext = harness.CreateContext(MessageSecurityMode.None);

            (ArrayOf<AddNodesResult> results, _) = await harness.Sut.AddNodesAsync(
                insecureContext,
                new AddNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.TargetManager.Verify(
                manager => manager.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteNodesDeniedDeleteNodePermissionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetSourcePermissions(PermissionType.Browse);
            var item = new DeleteNodesItem
            {
                NodeId = harness.SourceNodeId
            };

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteNodesAsync(
                harness.Context,
                new DeleteNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SourceManager.Verify(
                manager => manager.GetPermissionMetadataAsync(
                    harness.Context,
                    It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(),
                    It.IsAny<Dictionary<NodeId, Variant[]>>(),
                    true,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.SourceManager.Verify(
                manager => manager.DeleteNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteNodesGrantedDeleteNodePermissionMutatesAsync()
        {
            using var harness = new AuthorizationHarness();
            var item = new DeleteNodesItem
            {
                NodeId = harness.SourceNodeId
            };

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteNodesAsync(
                harness.Context,
                new DeleteNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.DeleteNodeAsync(
                    harness.Context,
                    item,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task DeleteNodesAccessRestrictionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SourceMetadata.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            var item = new DeleteNodesItem
            {
                NodeId = harness.SourceNodeId
            };
            OperationContext signedContext = harness.CreateContext(MessageSecurityMode.Sign);

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteNodesAsync(
                signedContext,
                new DeleteNodesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.SourceManager.Verify(
                manager => manager.DeleteNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteNodesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesDeniedTargetPermissionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetTargetPermissions(PermissionType.Browse);
            AddReferencesItem item = harness.CreateAddReferencesItem();

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SourceManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesTargetAccessRestrictionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.TargetMetadata.AccessRestrictions = AccessRestrictionType.SigningRequired;
            AddReferencesItem item = harness.CreateAddReferencesItem();
            OperationContext insecureContext = harness.CreateContext(MessageSecurityMode.None);

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                insecureContext,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.SourceManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesGrantedPermissionsMutateBothSidesAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.UseOptimizedMetadataWithoutNodeClasses();
            AddReferencesItem item = harness.CreateAddReferencesItem();

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.AddReferenceAsync(
                    harness.Context,
                    item,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    harness.Context,
                    It.Is<AddReferencesItem>(inverse =>
                        inverse.SourceNodeId == harness.TargetNodeId &&
                        inverse.TargetNodeId == harness.SourceNodeId &&
                        inverse.ReferenceTypeId == item.ReferenceTypeId &&
                        inverse.IsForward != item.IsForward &&
                        inverse.TargetServerUri == string.Empty &&
                        inverse.TargetNodeClass == harness.SourceMetadata.NodeClass),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task AddReferencesTargetServerUriSkipsLocalTargetProcessingAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetTargetPermissions(PermissionType.Browse);
            AddReferencesItem item = harness.CreateAddReferencesItem();
            item.TargetServerUri = "urn:remote:server";

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.AddReferenceAsync(
                    harness.Context,
                    item,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.TargetManager.Verify(
                manager => manager.GetPermissionMetadataAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(),
                    It.IsAny<Dictionary<NodeId, Variant[]>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesRemoteServerIndexSkipsLocalTargetProcessingAsync()
        {
            using var harness = new AuthorizationHarness();
            AddReferencesItem item = harness.CreateAddReferencesItem();
            item.TargetNodeId = new ExpandedNodeId(
                harness.TargetNodeId,
                "urn:remote:namespace",
                serverIndex: 1);

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesNamespaceUriTargetSkipsLocalTargetProcessingAsync()
        {
            using var harness = new AuthorizationHarness();
            AddReferencesItem item = harness.CreateAddReferencesItem();
            item.TargetNodeId = new ExpandedNodeId(
                harness.TargetNodeId,
                "urn:remote:namespace");

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.TargetManager.Verify(
                manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferencesInverseFailureRollsBackSourceAsync()
        {
            using var harness = new AuthorizationHarness();
            AddReferencesItem item = harness.CreateAddReferencesItem();
            harness.TargetManager
                .Setup(manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadDuplicateReferenceNotAllowed)));

            (ArrayOf<StatusCode> results, _) = await harness.Sut.AddReferencesAsync(
                harness.Context,
                new AddReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                results[0],
                Is.EqualTo(StatusCodes.BadDuplicateReferenceNotAllowed));
            harness.SourceManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    harness.Context,
                    It.Is<DeleteReferencesItem>(rollback =>
                        rollback.SourceNodeId == item.SourceNodeId &&
                        rollback.TargetNodeId == item.TargetNodeId &&
                        rollback.ReferenceTypeId == item.ReferenceTypeId &&
                        rollback.IsForward == item.IsForward &&
                        !rollback.DeleteBidirectional),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void AddReferencesCancellationUsesIndependentRollbackToken()
        {
            using var harness = new AuthorizationHarness();
            AddReferencesItem item = harness.CreateAddReferencesItem();
            using var requestCts = new CancellationTokenSource();
            var cleanupToken = default(CancellationToken);
            harness.SourceManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Callback((
                    OperationContext _,
                    DeleteReferencesItem _,
                    CancellationToken token) => cleanupToken = token)
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            harness.TargetManager
                .Setup(manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    OperationContext _,
                    AddReferencesItem _,
                    CancellationToken token) =>
                {
                    requestCts.Cancel();
                    throw new OperationCanceledException(token);
                });

            Assert.That(
                async () => await harness.Sut.AddReferencesAsync(
                    harness.Context,
                    new AddReferencesItem[] { item }.ToArrayOf(),
                    requestCts.Token).ConfigureAwait(false),
                Throws.TypeOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(requestCts.IsCancellationRequested, Is.True);
                Assert.That(cleanupToken.CanBeCanceled, Is.True);
                Assert.That(cleanupToken, Is.Not.EqualTo(requestCts.Token));
                Assert.That(cleanupToken.IsCancellationRequested, Is.False);
            });
        }

        [Test]
        public async Task DeleteReferencesDeniedTargetPermissionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.SetTargetPermissions(PermissionType.Browse);
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteReferencesAsync(
                harness.Context,
                new DeleteReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SourceManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.TargetManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteReferencesTargetAccessRestrictionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.TargetMetadata.AccessRestrictions = AccessRestrictionType.SigningRequired;
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();
            OperationContext insecureContext = harness.CreateContext(MessageSecurityMode.None);

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteReferencesAsync(
                insecureContext,
                new DeleteReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.SourceManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.TargetManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteReferencesGrantedPermissionsMutateBothSidesAsync()
        {
            using var harness = new AuthorizationHarness();
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteReferencesAsync(
                harness.Context,
                new DeleteReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    harness.Context,
                    It.Is<DeleteReferencesItem>(source =>
                        source.SourceNodeId == item.SourceNodeId &&
                        source.TargetNodeId == item.TargetNodeId &&
                        source.ReferenceTypeId == item.ReferenceTypeId &&
                        source.IsForward == item.IsForward &&
                        !source.DeleteBidirectional),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.TargetManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    harness.Context,
                    It.Is<DeleteReferencesItem>(inverse =>
                        inverse.SourceNodeId == harness.TargetNodeId &&
                        inverse.TargetNodeId == harness.SourceNodeId &&
                        inverse.ReferenceTypeId == item.ReferenceTypeId &&
                        inverse.IsForward != item.IsForward &&
                        !inverse.DeleteBidirectional),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task DeleteReferencesRemoteTargetSkipsReciprocalProcessingAsync()
        {
            using var harness = new AuthorizationHarness();
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();
            item.TargetNodeId = new ExpandedNodeId(
                harness.TargetNodeId,
                "urn:remote:namespace",
                serverIndex: 1);

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteReferencesAsync(
                harness.Context,
                new DeleteReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            harness.SourceManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    harness.Context,
                    It.Is<DeleteReferencesItem>(source =>
                        source.SourceNodeId == item.SourceNodeId &&
                        source.TargetNodeId == item.TargetNodeId &&
                        !source.DeleteBidirectional),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.TargetManager.Verify(
                manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteReferencesInverseFailureRestoresSourceAsync()
        {
            using var harness = new AuthorizationHarness();
            harness.UseOptimizedMetadataWithoutNodeClasses();
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();
            harness.TargetManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNoMatch)));

            (ArrayOf<StatusCode> results, _) = await harness.Sut.DeleteReferencesAsync(
                harness.Context,
                new DeleteReferencesItem[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNoMatch));
            harness.SourceManager.Verify(
                manager => manager.AddReferenceAsync(
                    harness.Context,
                    It.Is<AddReferencesItem>(rollback =>
                        rollback.SourceNodeId == item.SourceNodeId &&
                        rollback.TargetNodeId == item.TargetNodeId &&
                        rollback.ReferenceTypeId == item.ReferenceTypeId &&
                        rollback.IsForward == item.IsForward &&
                        rollback.TargetServerUri == string.Empty &&
                        rollback.TargetNodeClass == harness.TargetMetadata.NodeClass),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void DeleteReferencesUnexpectedFailureRestoresSourceWithIndependentToken()
        {
            using var harness = new AuthorizationHarness();
            DeleteReferencesItem item = harness.CreateDeleteReferencesItem();
            var cleanupToken = default(CancellationToken);
            harness.SourceManager
                .Setup(manager => manager.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Callback((
                    OperationContext _,
                    AddReferencesItem _,
                    CancellationToken token) => cleanupToken = token)
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            harness.TargetManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Target mutation failed."));

            Assert.That(
                async () => await harness.Sut.DeleteReferencesAsync(
                    harness.Context,
                    new DeleteReferencesItem[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
            Assert.Multiple(() =>
            {
                Assert.That(cleanupToken.CanBeCanceled, Is.True);
                Assert.That(cleanupToken.IsCancellationRequested, Is.False);
            });
        }

        private MasterNodeManager CreateMasterNodeManager(params INodeManager[] additional)
        {
            var nodeManagers = new List<INodeManager>(additional);
            return new MasterNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                null,
                [.. nodeManagers]);
        }

        private ushort GetTestNamespaceIndex()
        {
            int namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(TestNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return checked((ushort)namespaceIndex);
        }

        private static Mock<INodeManagerWithNodeManagement> CreateNodeManagementManager(
            bool allowNodeManagement)
        {
            var manager = new Mock<INodeManagerWithNodeManagement>();
            manager.Setup(m => m.NamespaceUris).Returns([TestNamespaceUri]);
            manager.Setup(m => m.AllowNodeManagement).Returns(allowNodeManagement);
            return manager;
        }

        private static NodeId ConfigureParent(
            Mock<INodeManagerWithNodeManagement> manager,
            ushort namespaceIndex)
        {
            var parentNodeId = new NodeId("Parent", namespaceIndex);
            var parentHandle = new object();
            manager.Setup(m => m.GetManagerHandle(parentNodeId)).Returns(parentHandle);
            manager
                .Setup(m => m.GetNodeMetadata(
                    It.IsAny<OperationContext>(),
                    parentHandle,
                    BrowseResultMask.NodeClass))
                .Returns(new NodeMetadata(parentHandle, parentNodeId)
                {
                    NodeClass = NodeClass.Object
                });
            return parentNodeId;
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(), null!, RequestType.AddNodes, RequestLifetime.None, session.Object);
        }

        private static OperationContext CreateContext(DiagnosticsMasks diagnosticsMask)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            var requestHeader = new RequestHeader { ReturnDiagnostics = (uint)diagnosticsMask };
            return new OperationContext(
                requestHeader, null!, RequestType.AddNodes, RequestLifetime.None, session.Object);
        }

        /// <summary>
        /// Combines the legacy manager contract with the asynchronous NodeManagement facet.
        /// </summary>
        public interface INodeManagerWithNodeManagement : INodeManager, INodeManagementAsyncNodeManager;

        /// <summary>
        /// Standard concrete hierarchical references used by the valid-semantics test.
        /// </summary>
        public enum ValidReferenceKind
        {
            Organizes,
            HasComponent,
            HasProperty
        }

        private sealed class AuthorizationHarness : IDisposable
        {
            private const string SourceNamespaceUri =
                "urn:opcfoundation:server:tests:node-management-source";
            private const string TargetNamespaceUri =
                "urn:opcfoundation:server:tests:node-management-target";

            private static readonly NodeId s_roleId = ObjectIds.WellKnownRole_SecurityAdmin;
            private readonly Mock<ISession> m_session;
            private readonly NamespaceMetadataState m_sourceNamespaceMetadata;
            private readonly NamespaceMetadataState m_targetNamespaceMetadata;

            public AuthorizationHarness()
            {
                var namespaceTable = new NamespaceTable();
                namespaceTable.Append("urn:opcfoundation:server:tests");
                SourceNamespaceIndex = (ushort)namespaceTable.Append(SourceNamespaceUri);
                TargetNamespaceIndex = (ushort)namespaceTable.Append(TargetNamespaceUri);

                var typeTree = new TypeTable(namespaceTable);
                typeTree.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
                typeTree.AddSubtype(
                    ReferenceTypeIds.HierarchicalReferences,
                    ReferenceTypeIds.References);
                typeTree.AddSubtype(
                    ReferenceTypeIds.Organizes,
                    ReferenceTypeIds.HierarchicalReferences);

                SourceNodeId = new NodeId("Source", SourceNamespaceIndex);
                TargetNodeId = new NodeId("Target", TargetNamespaceIndex);
                SourceMetadata = new NodeMetadata(SourceNodeId, SourceNodeId)
                {
                    NodeClass = NodeClass.Object
                };
                TargetMetadata = new NodeMetadata(TargetNodeId, TargetNodeId)
                {
                    NodeClass = NodeClass.Variable
                };

                var configurationManager = new Mock<IConfigurationNodeManager>();
                var coreNodeManager = new Mock<ICoreNodeManager>();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(managerFactory => managerFactory.CreateConfigurationNodeManager())
                    .Returns(configurationManager.Object);
                factory.Setup(managerFactory =>
                        managerFactory.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(coreNodeManager.Object);

                var server = new Mock<IServerInternal>();
                server.Setup(instance => instance.Telemetry)
                    .Returns(NUnitTelemetryContext.Create());
                server.Setup(instance => instance.NamespaceUris).Returns(namespaceTable);
                server.Setup(instance => instance.ServerUris).Returns(new StringTable());
                server.Setup(instance => instance.TypeTree).Returns(typeTree);
                server.Setup(instance => instance.Factory).Returns(EncodeableFactory.Create());
                server.Setup(instance => instance.MainNodeManagerFactory).Returns(factory.Object);
                var defaultSystemContext = new ServerSystemContext(server.Object);
                server.Setup(instance => instance.DefaultSystemContext)
                    .Returns(defaultSystemContext);

                SourceManager = CreateNodeManager(
                    SourceNamespaceUri,
                    SourceNodeId,
                    SourceMetadata);
                TargetManager = CreateNodeManager(
                    TargetNamespaceUri,
                    TargetNodeId,
                    TargetMetadata);

                var addedNodeId = new NodeId("Added", SourceNamespaceIndex);
                SourceManager
                    .Setup(manager => manager.AddNodeAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<AddNodesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(ServiceResult result, NodeId addedNodeId)>(
                        (ServiceResult.Good, addedNodeId)));
                TargetManager
                    .Setup(manager => manager.AddNodeAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<AddNodesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(ServiceResult result, NodeId addedNodeId)>(
                        (ServiceResult.Good, new NodeId("Added", TargetNamespaceIndex))));
                SourceManager
                    .Setup(manager => manager.DeleteNodeAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<DeleteNodesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                SourceManager
                    .Setup(manager => manager.AddReferenceAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<AddReferencesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                SourceManager
                    .Setup(manager => manager.DeleteReferenceAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<DeleteReferencesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                TargetManager
                    .Setup(manager => manager.AddReferenceAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<AddReferencesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                TargetManager
                    .Setup(manager => manager.DeleteReferenceAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<DeleteReferencesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

                m_sourceNamespaceMetadata = new NamespaceMetadataState(null)
                {
                    NodeId = new NodeId("SourceNamespaceMetadata", 0)
                };
                m_sourceNamespaceMetadata
                    .AddDefaultAccessRestrictions(defaultSystemContext)
                    .AddDefaultRolePermissions(defaultSystemContext)
                    .AddDefaultUserRolePermissions(defaultSystemContext);
                m_targetNamespaceMetadata = new NamespaceMetadataState(null)
                {
                    NodeId = new NodeId("TargetNamespaceMetadata", 0)
                };
                m_targetNamespaceMetadata
                    .AddDefaultAccessRestrictions(defaultSystemContext)
                    .AddDefaultRolePermissions(defaultSystemContext)
                    .AddDefaultUserRolePermissions(defaultSystemContext);
                configurationManager
                    .Setup(manager => manager.GetNamespaceMetadataStateAsync(
                        It.IsAny<ushort>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((ushort namespaceIndex, CancellationToken _) =>
                        new ValueTask<NamespaceMetadataState?>(
                            namespaceIndex == SourceNamespaceIndex
                                ? m_sourceNamespaceMetadata
                                : namespaceIndex == TargetNamespaceIndex
                                    ? m_targetNamespaceMetadata
                                    : null));

                var identity = new Mock<IUserIdentity>();
                identity.Setup(user => user.GrantedRoleIds)
                    .Returns(new NodeId[] { s_roleId }.ToArrayOf());
                m_session = new Mock<ISession>();
                m_session.Setup(activeSession => activeSession.Id)
                    .Returns(new NodeId("NodeManagementSession", 0));
                m_session.Setup(activeSession => activeSession.EffectiveIdentity)
                    .Returns(identity.Object);
                m_session.Setup(activeSession => activeSession.PreferredLocales)
                    .Returns([]);
                Context = CreateContext(MessageSecurityMode.SignAndEncrypt);

                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                };
                Sut = new MasterNodeManager(
                    server.Object,
                    configuration,
                    null,
                    new IAsyncNodeManager[]
                    {
                        SourceManager.Object,
                        TargetManager.Object
                    });
            }

            public MasterNodeManager Sut { get; }

            public OperationContext Context { get; }

            public Mock<IAsyncNodeManager> SourceManager { get; }

            public Mock<IAsyncNodeManager> TargetManager { get; }

            public ushort SourceNamespaceIndex { get; }

            public ushort TargetNamespaceIndex { get; }

            public NodeId SourceNodeId { get; }

            public NodeId TargetNodeId { get; }

            public NodeMetadata SourceMetadata { get; }

            public NodeMetadata TargetMetadata { get; }

            public AddNodesItem CreateAddNodesItem()
            {
                return CreateAddNodesItem(SourceNamespaceIndex);
            }

            public AddNodesItem CreateAddNodesItem(ushort namespaceIndex)
            {
                return new AddNodesItem
                {
                    ParentNodeId = SourceNodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Added", namespaceIndex),
                    NodeClass = NodeClass.Object,
                    RequestedNewNodeId = new NodeId("Added", namespaceIndex)
                };
            }

            public AddReferencesItem CreateAddReferencesItem()
            {
                return new AddReferencesItem
                {
                    SourceNodeId = SourceNodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,
                    TargetNodeId = TargetNodeId,
                    TargetNodeClass = TargetMetadata.NodeClass
                };
            }

            public DeleteReferencesItem CreateDeleteReferencesItem()
            {
                return new DeleteReferencesItem
                {
                    SourceNodeId = SourceNodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,
                    TargetNodeId = TargetNodeId,
                    DeleteBidirectional = true
                };
            }

            public void SetAddNodePermissions(PermissionType permissions)
            {
                SetAddNodePermissions(SourceNamespaceIndex, permissions);
            }

            public void SetAddNodePermissions(
                ushort namespaceIndex,
                PermissionType permissions)
            {
                NamespaceMetadataState namespaceMetadata =
                    namespaceIndex == SourceNamespaceIndex
                        ? m_sourceNamespaceMetadata
                        : m_targetNamespaceMetadata;
                namespaceMetadata.DefaultRolePermissions!.Value =
                [
                    new RolePermissionType
                    {
                        RoleId = s_roleId,
                        Permissions = (uint)permissions
                    }
                ];
            }

            public void SetNamespaceAccessRestrictions(
                ushort namespaceIndex,
                AccessRestrictionType restrictions)
            {
                NamespaceMetadataState namespaceMetadata =
                    namespaceIndex == SourceNamespaceIndex
                        ? m_sourceNamespaceMetadata
                        : m_targetNamespaceMetadata;
                namespaceMetadata.DefaultAccessRestrictions!.Value = (ushort)restrictions;
            }

            public void SetSourcePermissions(PermissionType permissions)
            {
                SourceMetadata.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = s_roleId,
                        Permissions = (uint)permissions
                    }
                ];
            }

            public void SetTargetPermissions(PermissionType permissions)
            {
                TargetMetadata.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = s_roleId,
                        Permissions = (uint)permissions
                    }
                ];
            }

            public OperationContext CreateContext(MessageSecurityMode securityMode)
            {
                var endpoint = new EndpointDescription
                {
                    SecurityMode = securityMode,
                    SecurityPolicyUri = securityMode == MessageSecurityMode.None
                        ? SecurityPolicies.None
                        : SecurityPolicies.Basic256Sha256,
                    TransportProfileUri = Profiles.UaTcpTransport
                };
                var channelContext = new SecureChannelContext(
                    "NodeManagementChannel",
                    endpoint,
                    RequestEncoding.Binary);
                return new OperationContext(
                    new RequestHeader(),
                    channelContext,
                    RequestType.AddNodes,
                    RequestLifetime.None,
                    m_session.Object);
            }

            public void UseOptimizedMetadataWithoutNodeClasses()
            {
                SetupOptimizedPermissionMetadata(SourceManager, SourceMetadata);
                SetupOptimizedPermissionMetadata(TargetManager, TargetMetadata);
            }

            public void Dispose()
            {
                Sut.Dispose();
            }

            private static void SetupOptimizedPermissionMetadata(
                Mock<IAsyncNodeManager> manager,
                NodeMetadata fullMetadata)
            {
                var permissionMetadata = new NodeMetadata(
                    fullMetadata.Handle,
                    fullMetadata.NodeId)
                {
                    AccessRestrictions = fullMetadata.AccessRestrictions,
                    DefaultAccessRestrictions = fullMetadata.DefaultAccessRestrictions,
                    RolePermissions = fullMetadata.RolePermissions,
                    DefaultRolePermissions = fullMetadata.DefaultRolePermissions,
                    UserRolePermissions = fullMetadata.UserRolePermissions,
                    DefaultUserRolePermissions = fullMetadata.DefaultUserRolePermissions,
                    IsPartOfTypeHierarchy = fullMetadata.IsPartOfTypeHierarchy
                };
                manager
                    .Setup(nodeManager => nodeManager.GetPermissionMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<Dictionary<NodeId, Variant[]>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata?>(permissionMetadata));
            }

            private static Mock<IAsyncNodeManager> CreateNodeManager(
                string namespaceUri,
                NodeId nodeId,
                NodeMetadata metadata)
            {
                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(nodeManager => nodeManager.NamespaceUris)
                    .Returns(new[] { namespaceUri });
                manager.Setup(nodeManager => nodeManager.AllowNodeManagement)
                    .Returns(true);
                manager
                    .Setup(nodeManager => nodeManager.GetManagerHandleAsync(
                        It.IsAny<NodeId>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((NodeId requestedNodeId, CancellationToken _) =>
                        new ValueTask<object>(
                            requestedNodeId == nodeId ? nodeId : null!));
                manager
                    .Setup(nodeManager => nodeManager.GetPermissionMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<Dictionary<NodeId, Variant[]>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        object _,
                        BrowseResultMask _,
                        Dictionary<NodeId, Variant[]> _,
                        bool _,
                        CancellationToken _) =>
                        new ValueTask<NodeMetadata?>(metadata));
                manager
                    .Setup(nodeManager => nodeManager.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata>(metadata));
                return manager;
            }
        }
    }
}
