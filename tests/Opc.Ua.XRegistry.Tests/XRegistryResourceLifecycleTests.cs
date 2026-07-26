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
    /// Verifies the resource lifecycle the model declares on <c>GroupType</c>: a resource version
    /// is created and optionally opened for writing, the document is transferred through the
    /// <c>FileType</c> Methods <c>ResourceType</c> inherits, and closing the write handle commits
    /// the document to the resource store and publishes the Opaque content-id fast path.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryResourceLifecycleTests
    {
        [Test]
        public async Task CreateResourceMaterializesAResourceFromTheModelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult result = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, false, CancellationToken.None)
                .ConfigureAwait(false);

            var resource = (ResourceState?)nm.Find(result.ResourceNodeId);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.AssignedVersionId, Is.EqualTo("1"),
                    "An empty VersionId lets the server assign the next one.");
                Assert.That(result.FileHandle, Is.Zero, "No handle unless RequestFileOpen is set.");
                Assert.That(resource, Is.Not.Null);
                Assert.That(resource, Is.InstanceOf<FileState>(),
                    "ResourceType is a FileType, so the document transfers over the file Methods.");
                Assert.That(resource!.ResourceId!.Value, Is.EqualTo("urn:doc"));
                Assert.That(resource.VersionId!.Value, Is.EqualTo("1"));
            });
        }

        [Test]
        public async Task CreateResourceReturnsAWriteHandleWhenRequestedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult result = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.FileHandle, Is.Not.Zero);
        }

        [Test]
        public async Task CreateResourceRejectsADuplicateVersionAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult first = await CreateResourceAsync(nm, group, "urn:doc", "7")
                .ConfigureAwait(false);
            CreateResourceMethodStateResult second = await CreateResourceAsync(nm, group, "urn:doc", "7")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(second.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdExists));
            });
        }

        [Test]
        public async Task CreateResourceRejectsAnUnknownGroupAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);

            CreateResourceMethodStateResult result = await CreateResourceAsync(
                nm, new NodeId(999999u, 1), "urn:doc", "1").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task CreateResourceRejectsAnEmptyResourceIdAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult result = await CreateResourceAsync(
                nm, group, string.Empty, "1").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task GetOrCreateResourceIsIdempotentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            GetOrCreateResourceMethodStateResult created = await nm.OnGetOrCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            GetOrCreateResourceMethodStateResult fetched = await nm.OnGetOrCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", "1", false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(created.Created, Is.True);
                Assert.That(fetched.Created, Is.False);
                Assert.That(fetched.ResourceNodeId, Is.EqualTo(created.ResourceNodeId));
            });
        }

        /// <summary>
        /// The full upload path: the write handle from CreateResource carries the document through
        /// the inherited FileType Write, and Close commits it and bootstraps the content-addressed
        /// fast path.
        /// </summary>
        [Test]
        public async Task ClosingAWriteHandleCommitsTheDocumentAndPublishesTheFastPathAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out IXRegistryResourceStore store);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            byte[] document = [0x10, 0x20, 0x30];

            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, created.ResourceNodeId, created.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            // The fake provider makes the content id the document itself.
            var fastPathNodeId = new NodeId(ByteString.From(document), NamespaceIndex(nm));
            var fastPath = (BaseDataVariableState?)nm.Find(fastPathNodeId);
            ByteString stored = await store
                .ReadAsync(created.ResourceNodeId.ToString()!, 0, int.MaxValue)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(written.ServiceResult), Is.True);
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
                Assert.That(stored.IsNull, Is.False, "The document is committed to the store.");
                Assert.That(stored.Span.ToArray(), Is.EqualTo(document));
                Assert.That(fastPath, Is.Not.Null, "Close bootstraps the Opaque content-id node.");
                Assert.That(resource.Epoch!.Value, Is.EqualTo(2u), "Close bumps the epoch.");
            });
        }

        [Test]
        public async Task WriteBeyondTheResourceByteLimitIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _, o => o.MaxResourceBytes = 4);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            WriteMethodStateResult ok = await WriteAsync(nm, resource, created, new byte[4])
                .ConfigureAwait(false);
            WriteMethodStateResult tooLarge = await WriteAsync(nm, resource, created, new byte[1])
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(ok.ServiceResult), Is.True);
                Assert.That(tooLarge.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadRequestTooLarge));
            });
        }

        [Test]
        public async Task WriteOnAnUnknownHandleIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await CreateResourceAsync(nm, group, "urn:doc", "1")
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            WriteMethodStateResult result = await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, created.ResourceNodeId, 4242u,
                ByteString.From([1]), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ReadStreamsTheCommittedDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            byte[] document = [1, 2, 3, 4, 5];

            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            await WriteAsync(nm, resource, created, document).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, created.ResourceNodeId, 1,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult first = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, created.ResourceNodeId, opened.FileHandle, 3,
                CancellationToken.None).ConfigureAwait(false);
            ReadMethodStateResult second = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, created.ResourceNodeId, opened.FileHandle, 3,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(opened.FileHandle, Is.Not.Zero);
                Assert.That(first.Data.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(second.Data.Span.ToArray(), Is.EqualTo(new byte[] { 4, 5 }),
                    "The cursor advances and the final read is short.");
            });
        }

        [Test]
        public async Task CloseWithoutAContentIdProviderIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out _, o => o.ContentIdProvider = null);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task RegisteringBeyondTheResourceLimitIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out _, o => o.MaxRegisteredResources = 1);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            CreateResourceMethodStateResult first = await CreateResourceAsync(nm, group, "a", "1")
                .ConfigureAwait(false);
            CreateResourceMethodStateResult second = await CreateResourceAsync(nm, group, "b", "1")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(second.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadTooManyOperations));
            });
        }

        [Test]
        public async Task OpeningBeyondTheConcurrentUploadLimitIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out _, o => o.MaxConcurrentUploads = 1);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            // The first create takes the only upload slot by asking for an open write handle.
            CreateResourceMethodStateResult first = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "a", "1", true, CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult second = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "b", "1", true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(second.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadTooManyOperations),
                    "A second concurrent upload exceeds MaxConcurrentUploads.");
            });
        }

        [Test]
        public async Task OpenBeyondTheConcurrentUploadLimitIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out _, o => o.MaxConcurrentUploads = 1);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "a", "1", true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, created.ResourceNodeId, kWriteMode,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(opened.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task ReadWithAnUnknownHandleIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            (ResourceState resource, CreateResourceMethodStateResult created) =
                await CreateOpenResourceAsync(nm).ConfigureAwait(false);

            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, created.ResourceNodeId, created.FileHandle + 999, 16,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(read.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ReadOnAWriteHandleIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            (ResourceState resource, CreateResourceMethodStateResult created) =
                await CreateOpenResourceAsync(nm).ConfigureAwait(false);

            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, created.ResourceNodeId, created.FileHandle, 16,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(read.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState),
                "A handle opened for writing cannot be read from.");
        }

        [Test]
        public async Task ReadPastTheEndOfTheDocumentReturnsAnEmptyChunkAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            (ResourceState resource, uint handle) =
                await WriteAndReopenForReadAsync(nm, s_document).ConfigureAwait(false);

            ReadMethodStateResult all = await ReadAsync(nm, resource, handle, s_document.Length)
                .ConfigureAwait(false);
            ReadMethodStateResult past = await ReadAsync(nm, resource, handle, 16)
                .ConfigureAwait(false);
            ReadMethodStateResult zero = await ReadAsync(nm, resource, handle, 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(all.Data.Span.ToArray(), Is.EqualTo(s_document));
                Assert.That(ServiceResult.IsGood(past.ServiceResult), Is.True);
                Assert.That(past.Data.Span.Length, Is.Zero, "Reading at EOF yields an empty chunk.");
                Assert.That(zero.Data.Span.Length, Is.Zero, "A zero-length read yields nothing.");
            });
        }

        [Test]
        public async Task CloseWithAnUnknownHandleIsRejectedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            (ResourceState resource, CreateResourceMethodStateResult created) =
                await CreateOpenResourceAsync(nm).ConfigureAwait(false);

            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle + 999,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(closed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ClosingAReadHandleDoesNotRepublishTheDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            (ResourceState resource, uint handle) =
                await WriteAndReopenForReadAsync(nm, s_document).ConfigureAwait(false);

            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True,
                "Closing a read handle just releases it; only a write handle commits.");
        }

        [Test]
        public async Task IdenticalDocumentsReuseTheSameFastPathNodeAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out _);
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);

            NodeId firstFastPath = await RegisterAsync(nm, group, "a", s_document).ConfigureAwait(false);
            NodeId secondFastPath = await RegisterAsync(nm, group, "b", s_document).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstFastPath.IsNull, Is.False);
                Assert.That(secondFastPath, Is.EqualTo(firstFastPath),
                    "Identity is derived from the bytes, so identical documents share one node.");
                Assert.That(nm.Find(firstFastPath), Is.Not.Null);
            });
        }

        private static async Task<NodeId> RegisterAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId,
            byte[] document)
        {
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, resourceId, "1", true, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            await WriteAsync(nm, resource, created, document).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            return new NodeId(
                new XRegistryServerTestHarness.FakeContentIdProvider()
                    .ComputeContentId("application/octet-stream", document),
                NamespaceIndex(nm));
        }

        private static ValueTask<ReadMethodStateResult> ReadAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            uint handle,
            int length)
        {
            return resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, handle, length,
                CancellationToken.None);
        }

        /// <summary>
        /// Creates a resource, streams <paramref name="document"/> into it, commits it and reopens
        /// it for reading, returning the read handle.
        /// </summary>
        private static async Task<(ResourceState Resource, uint Handle)> WriteAndReopenForReadAsync(
            XRegistryRegistrationNodeManager nm,
            byte[] document)
        {
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", "1", true, CancellationToken.None)
                .ConfigureAwait(false);

            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            await WriteAsync(nm, resource, created, document).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, created.ResourceNodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, created.ResourceNodeId, kReadMode,
                CancellationToken.None).ConfigureAwait(false);
            return (resource, opened.FileHandle);
        }

        private static async Task<(ResourceState, CreateResourceMethodStateResult)>
            CreateOpenResourceAsync(XRegistryRegistrationNodeManager nm)
        {
            NodeId group = await CreateGroupAsync(nm).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, "urn:doc", "1", true, CancellationToken.None)
                .ConfigureAwait(false);
            return ((ResourceState)nm.Find(created.ResourceNodeId)!, created);
        }

        private static ValueTask<WriteMethodStateResult> WriteAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            CreateResourceMethodStateResult created,
            byte[] data)
        {
            return resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, created.ResourceNodeId, created.FileHandle,
                ByteString.From(data), CancellationToken.None);
        }

        private static ValueTask<CreateResourceMethodStateResult> CreateResourceAsync(
            XRegistryRegistrationNodeManager nm,
            NodeId group,
            string resourceId,
            string versionId)
        {
            return nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group, resourceId, versionId, false, CancellationToken.None);
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

        private const byte kWriteMode = 2;
        private const byte kReadMode = 1;
        private static readonly byte[] s_document = [0x01, 0x02, 0x03, 0x04];
    }
}
