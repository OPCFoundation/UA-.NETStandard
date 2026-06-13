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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

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
    }
}
