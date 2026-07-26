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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies deletion, which the model expresses as <c>Delete(ExpectedEpoch)</c> on both
    /// <c>GroupType</c> and <c>ResourceType</c>. The epoch is an optimistic-concurrency check, so a
    /// caller holding a stale epoch must not be able to delete a newer version.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryDeleteTests
    {
        [Test]
        public async Task DeleteResourceRemovesTheNodeAndReleasesItsSlotAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out _, o => o.MaxRegisteredResources = 1);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult first = await CreateResourceAsync(nm, group, "a")
                .ConfigureAwait(false);
            CreateResourceMethodStateResult blocked = await CreateResourceAsync(nm, group, "b")
                .ConfigureAwait(false);

            var resource = (ResourceState)nm.Find(first.ResourceNodeId)!;
            DeleteMethodStateResult deleted = await nm
                .OnDeleteResourceAsync(resource, resource.Epoch!.Value).ConfigureAwait(false);

            CreateResourceMethodStateResult afterDelete = await CreateResourceAsync(nm, group, "c")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(blocked.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True);
                Assert.That(nm.Find(first.ResourceNodeId), Is.Null);
                Assert.That(ServiceResult.IsGood(afterDelete.ServiceResult), Is.True,
                    "Deleting frees a registration slot.");
            });
        }

        [Test]
        public async Task DeleteResourceRejectsAStaleEpochAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await CreateResourceAsync(nm, group, "a")
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            DeleteMethodStateResult result = await nm
                .OnDeleteResourceAsync(resource, resource.Epoch!.Value + 5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(nm.Find(created.ResourceNodeId), Is.Not.Null, "The resource survives.");
            });
        }

        [Test]
        public async Task DeleteResourceAlsoRemovesTheFastPathNodeAndStoredDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out IXRegistryResourceStore store);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            byte[] document = [7, 8, 9];

            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, created.ResourceNodeId, created.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            string storeKey = created.ResourceNodeId.ToString()!;
            var fastPathNodeId = new NodeId(ByteString.From(document), NamespaceIndex(nm));
            Assert.That(nm.Find(fastPathNodeId), Is.Not.Null, "Precondition: the fast path exists.");

            await nm.OnDeleteResourceAsync(resource, resource.Epoch!.Value).ConfigureAwait(false);
            ByteString stored = await store.ReadAsync(storeKey, 0, int.MaxValue).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(fastPathNodeId), Is.Null);
                Assert.That(stored.IsNull, Is.True, "The document is removed from the store.");
            });
        }

        [Test]
        public async Task DeleteGroupRemovesItsResourcesAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId groupNodeId = await CreateGroupAsync(nm).ConfigureAwait(false);
            var group = (GroupState)nm.Find(groupNodeId)!;

            CreateResourceMethodStateResult a = await CreateResourceAsync(nm, groupNodeId, "a")
                .ConfigureAwait(false);
            CreateResourceMethodStateResult b = await CreateResourceAsync(nm, groupNodeId, "b")
                .ConfigureAwait(false);

            DeleteMethodStateResult deleted = await nm
                .OnDeleteGroupAsync(group, group.Epoch!.Value).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True);
                Assert.That(nm.Find(groupNodeId), Is.Null);
                Assert.That(nm.Find(a.ResourceNodeId), Is.Null);
                Assert.That(nm.Find(b.ResourceNodeId), Is.Null);
            });
        }

        [Test]
        public async Task DeleteGroupRejectsAStaleEpochAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId groupNodeId = await CreateGroupAsync(nm).ConfigureAwait(false);
            var group = (GroupState)nm.Find(groupNodeId)!;

            DeleteMethodStateResult result = await nm
                .OnDeleteGroupAsync(group, group.Epoch!.Value + 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(nm.Find(groupNodeId), Is.Not.Null);
            });
        }

        [Test]
        public async Task DeletingAGroupFreesItsGroupIdAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId groupNodeId = await CreateGroupAsync(nm).ConfigureAwait(false);
            var group = (GroupState)nm.Find(groupNodeId)!;

            await nm.OnDeleteGroupAsync(group, group.Epoch!.Value).ConfigureAwait(false);
            CreateGroupMethodStateResult recreated = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(recreated.ServiceResult), Is.True);
        }

        private static ValueTask<CreateResourceMethodStateResult> CreateResourceAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId)
        {
            return nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, resourceId, string.Empty, false, CancellationToken.None);
        }

        private static async Task<NodeId> CreateGroupAsync(XRegistryRegistrationNodeManager nm)
        {
            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            return result.GroupNodeId;
        }

        private static ushort NamespaceIndex(XRegistryRegistrationNodeManager nm)
        {
            return (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace(
            out IXRegistryResourceStore store,
            System.Action<XRegistryServerOptions>? configure = null)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            configure?.Invoke(options);
            store = options.ResourceStore;

            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }
    }
}
