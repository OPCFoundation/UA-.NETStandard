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
        public async Task DeleteNodesDeniedDeleteNodePermissionDoesNotMutateAsync()
        {
            using var harness = new AuthorizationHarness
            {
                SourcePermissionResult = new ServiceResult(StatusCodes.BadUserAccessDenied)
            };
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
                manager => manager.ValidateRolePermissionsAsync(
                    harness.Context,
                    harness.SourceNodeId,
                    PermissionType.DeleteNode,
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
        public async Task AddReferencesDeniedTargetPermissionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness
            {
                TargetPermissionResult = new ServiceResult(StatusCodes.BadUserAccessDenied)
            };
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
        public async Task AddReferencesGrantedPermissionsMutateBothSidesAsync()
        {
            using var harness = new AuthorizationHarness();
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
                        inverse.IsForward != item.IsForward),
                    It.IsAny<CancellationToken>()),
                Times.Once);
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
        public async Task DeleteReferencesDeniedTargetPermissionDoesNotHalfApplyAsync()
        {
            using var harness = new AuthorizationHarness
            {
                TargetPermissionResult = new ServiceResult(StatusCodes.BadUserAccessDenied)
            };
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
                    item,
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
        public async Task DeleteReferencesInverseFailureRestoresSourceAsync()
        {
            using var harness = new AuthorizationHarness();
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
                        rollback.IsForward == item.IsForward),
                    It.IsAny<CancellationToken>()),
                Times.Once);
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

        private sealed class AuthorizationHarness : IDisposable
        {
            private const string SourceNamespaceUri =
                "urn:opcfoundation:server:tests:node-management-source";
            private const string TargetNamespaceUri =
                "urn:opcfoundation:server:tests:node-management-target";

            private static readonly NodeId s_roleId = ObjectIds.WellKnownRole_SecurityAdmin;
            private readonly NamespaceMetadataState m_sourceNamespaceMetadata;

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

                SourceManager = CreateNodeManager(
                    SourceNamespaceUri,
                    SourceNodeId,
                    () => SourcePermissionResult);
                TargetManager = CreateNodeManager(
                    TargetNamespaceUri,
                    TargetNodeId,
                    () => TargetPermissionResult);

                var addedNodeId = new NodeId("Added", SourceNamespaceIndex);
                SourceManager
                    .Setup(manager => manager.AddNodeAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<AddNodesItem>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(ServiceResult result, NodeId addedNodeId)>(
                        (ServiceResult.Good, addedNodeId)));
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
                m_sourceNamespaceMetadata.DefaultRolePermissions =
                    new PropertyState<ArrayOf<RolePermissionType>>
                        .Implementation<StructureBuilder<RolePermissionType>>(
                            m_sourceNamespaceMetadata);
                configurationManager
                    .Setup(manager => manager.GetNamespaceMetadataStateAsync(
                        SourceNamespaceIndex,
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NamespaceMetadataState?>(
                        m_sourceNamespaceMetadata));

                var identity = new Mock<IUserIdentity>();
                identity.Setup(user => user.GrantedRoleIds)
                    .Returns(new NodeId[] { s_roleId }.ToArrayOf());
                var session = new Mock<ISession>();
                session.Setup(activeSession => activeSession.Id)
                    .Returns(new NodeId("NodeManagementSession", 0));
                session.Setup(activeSession => activeSession.EffectiveIdentity)
                    .Returns(identity.Object);
                session.Setup(activeSession => activeSession.PreferredLocales)
                    .Returns([]);
                Context = new OperationContext(
                    new RequestHeader(),
                    null!,
                    RequestType.AddNodes,
                    RequestLifetime.None,
                    session.Object);

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

            public ServiceResult SourcePermissionResult { get; set; } = ServiceResult.Good;

            public ServiceResult TargetPermissionResult { get; set; } = ServiceResult.Good;

            public AddNodesItem CreateAddNodesItem()
            {
                return new AddNodesItem
                {
                    ParentNodeId = SourceNodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Added", SourceNamespaceIndex),
                    NodeClass = NodeClass.Object,
                    RequestedNewNodeId = new NodeId("Added", SourceNamespaceIndex)
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
                    TargetNodeClass = NodeClass.Object
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
                m_sourceNamespaceMetadata.DefaultRolePermissions!.Value =
                [
                    new RolePermissionType
                    {
                        RoleId = s_roleId,
                        Permissions = (uint)permissions
                    }
                ];
            }

            public void Dispose()
            {
                Sut.Dispose();
            }

            private static Mock<IAsyncNodeManager> CreateNodeManager(
                string namespaceUri,
                NodeId nodeId,
                Func<ServiceResult> permissionResult)
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
                    .Setup(nodeManager => nodeManager.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        NodeId _,
                        PermissionType _,
                        CancellationToken _) =>
                        new ValueTask<ServiceResult>(permissionResult()));
                return manager;
            }
        }
    }
}
