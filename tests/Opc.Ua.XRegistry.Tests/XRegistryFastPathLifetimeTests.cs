/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
    /// Verifies the lifetime of the content-addressed fast-path node. The node is <b>shared</b> by
    /// every resource whose document has the same bytes — that sharing is the whole point of a
    /// content key — so it must outlive any single Version that references it and must
    /// not linger once the last one is gone.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFastPathLifetimeTests
    {
        [Test]
        public async Task DeletingOneOfTwoResourcesKeepsTheSharedFastPathNodeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState first = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            ResourceState second = await RegisterAsync(nm, group, "b", s_document).ConfigureAwait(false);
            NodeId fastPath = FastPathNodeId(nm, s_document);

            Assert.That(nm.Find(fastPath), Is.Not.Null, "Precondition: the shared node exists.");

            await nm.OnDeleteResourceAsync(first, first.Epoch!.Value).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(fastPath), Is.Not.Null,
                    "The second resource still resolves to these bytes, so the shared node stays.");
                Assert.That(nm.Find(second.NodeId), Is.Not.Null);
                Assert.That(nm.Find(first.NodeId), Is.Null);
            });
        }

        [Test]
        public async Task DeletingTheLastResourceDropsTheSharedFastPathNodeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState first = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            ResourceState second = await RegisterAsync(nm, group, "b", s_document).ConfigureAwait(false);
            NodeId fastPath = FastPathNodeId(nm, s_document);

            await nm.OnDeleteResourceAsync(first, first.Epoch!.Value).ConfigureAwait(false);
            await nm.OnDeleteResourceAsync(second, second.Epoch!.Value).ConfigureAwait(false);

            Assert.That(nm.Find(fastPath), Is.Null,
                "Once the last referencing resource is gone the node must not linger.");
        }

        [Test]
        public async Task IdenticalBytesAcrossVersionsShareOneFastPathReferenceAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState first = await RegisterAsync(
                nm,
                group,
                "a",
                "1",
                s_document).ConfigureAwait(false);
            ResourceState second = await RegisterAsync(
                nm,
                group,
                "a",
                "2",
                s_document).ConfigureAwait(false);
            NodeId fastPath = FastPathNodeId(nm, s_document);

            await nm.OnDeleteResourceAsync(first, first.Epoch!.Value).ConfigureAwait(false);
            Assert.That(nm.Find(fastPath), Is.Not.Null);

            await nm.OnDeleteResourceAsync(second, second.Epoch!.Value).ConfigureAwait(false);
            Assert.That(nm.Find(fastPath), Is.Null);
        }

        [Test]
        public async Task DeletingAGroupReleasesEveryFastPathReferenceAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            await RegisterAsync(nm, group, "b", s_document).ConfigureAwait(false);
            var groupState = (GroupState)nm.Find(group)!;
            NodeId fastPath = FastPathNodeId(nm, s_document);

            await nm.OnDeleteGroupAsync(groupState, groupState.Epoch!.Value).ConfigureAwait(false);

            Assert.That(nm.Find(fastPath), Is.Null,
                "Deleting the owning group removes every resource, so the shared node goes too.");
        }

        [Test]
        public async Task RewritingAResourceRetiresItsPreviousFastPathNodeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState resource = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            NodeId original = FastPathNodeId(nm, s_document);
            Assert.That(nm.Find(original), Is.Not.Null, "Precondition.");

            byte[] revised = [0x09, 0x09, 0x09];
            await RewriteAsync(nm, resource, revised).ConfigureAwait(false);
            NodeId updated = FastPathNodeId(nm, revised);

            Assert.Multiple(() =>
            {
                Assert.That(nm.Find(updated), Is.Not.Null, "The new content is resolvable.");
                Assert.That(nm.Find(original), Is.Null,
                    "The superseded content id must not stay published forever.");
                Assert.That(resource.Xid!.Value,
                    Is.EqualTo("/groups/schemas/resources/a/versions/1"));
            });
        }

        [Test]
        public async Task RewritingAResourceKeepsAFastPathNodeAnotherResourceStillNeedsAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState first = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            await RegisterAsync(nm, group, "b", s_document).ConfigureAwait(false);
            NodeId shared = FastPathNodeId(nm, s_document);

            await RewriteAsync(nm, first, [0x09, 0x09, 0x09]).ConfigureAwait(false);

            Assert.That(nm.Find(shared), Is.Not.Null,
                "The other resource still resolves to the original bytes.");
        }

        [Test]
        public async Task RewritingWithIdenticalBytesKeepsTheFastPathNodeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            ResourceState resource = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            NodeId fastPath = FastPathNodeId(nm, s_document);

            await RewriteAsync(nm, resource, s_document).ConfigureAwait(false);

            Assert.That(nm.Find(fastPath), Is.Not.Null,
                "Re-writing the same content must not churn the node or drop the only reference.");

            // The reference count must still be exactly one, so deleting the resource cleans up.
            await nm.OnDeleteResourceAsync(resource, resource.Epoch!.Value).ConfigureAwait(false);
            Assert.That(nm.Find(fastPath), Is.Null);
        }

        /// <summary>
        /// Creates a resource, streams <paramref name="document"/> into it and commits it.
        /// </summary>
        private static Task<ResourceState> RegisterAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId,
            byte[] document)
        {
            return RegisterAsync(nm, group, resourceId, "1", document);
        }

        private static async Task<ResourceState> RegisterAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId,
            string versionId,
            byte[] document)
        {
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext,
                null!,
                group,
                resourceId,
                versionId,
                true,
                CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, created.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            return resource;
        }

        /// <summary>
        /// Opens an existing resource for writing, replaces its document and commits it.
        /// </summary>
        private static async Task RewriteAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            byte[] document)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static NodeId FastPathNodeId(XRegistryRegistrationNodeManager nm, byte[] document)
        {
            // The fake provider makes the content id the document itself.
            return new NodeId(
                ByteString.From(document),
                (ushort)nm.SystemContext.NamespaceUris.GetIndex(
                    XRegistryWellKnown.XRegistryNamespaceUri));
        }

        private static async Task<NodeId> CreateGroupAsync(XRegistryRegistrationNodeManager nm)
        {
            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            return result.GroupNodeId;
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace()
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }

        private const byte kWriteMode = 2;
        private const byte kEraseExistingMode = 4;
        private static readonly byte[] s_document = [0x01, 0x02, 0x03, 0x04];
    }
}
