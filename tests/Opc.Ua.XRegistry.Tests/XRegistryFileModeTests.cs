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
    /// Verifies the <c>FileType</c> <c>Open</c> mode bits (OPC 10000-5 §C): Read = 1, Write = 2,
    /// EraseExisting = 4, Append = 8. The combinations the standard rejects have to be rejected, and
    /// a write that does not erase must start from the document already stored rather than from an
    /// empty buffer — otherwise a partial rewrite silently truncates the rest of it.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryFileModeTests
    {
        [TestCase((byte)0, TestName = "NeitherReadNorWrite")]
        [TestCase((byte)4, TestName = "EraseExistingWithoutWrite")]
        [TestCase((byte)8, TestName = "AppendWithoutWrite")]
        [TestCase((byte)3, TestName = "ReadAndWriteTogether")]
        [TestCase((byte)5, TestName = "EraseExistingWithReadOnly")]
        [TestCase((byte)14, TestName = "EraseExistingAndAppendTogether")]
        public async Task InvalidOpenModesAreRejectedAsync(byte mode)
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, mode, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(opened.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task WriteWithoutEraseExistingPreservesTheRestOfTheDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4, 5, 6]).ConfigureAwait(false);

            // Open for writing without EraseExisting and overwrite only the first two bytes.
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 9]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo(new byte[] { 9, 9, 3, 4, 5, 6 }),
                "A write that does not erase replaces only the bytes it covers.");
        }

        [Test]
        public async Task WriteWithEraseExistingReplacesTheDocumentAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4, 5, 6]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([9, 9]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo(new byte[] { 9, 9 }));
        }

        [Test]
        public async Task WriteWithAppendAddsToTheEndAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3]).ConfigureAwait(false);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kAppendMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([4, 5]), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            byte[] document = await ReadWholeDocumentAsync(nm, resource).ConfigureAwait(false);
            Assert.That(document, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public async Task OpenCountAndSizeTrackTheFileAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace();
            ResourceState resource = await CreateResourceAsync(nm).ConfigureAwait(false);
            await CommitAsync(nm, resource, [1, 2, 3, 4]).ConfigureAwait(false);

            ulong sizeAfterCommit = resource.Size!.Value;
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ushort openWhileOpen = resource.OpenCount!.Value;
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sizeAfterCommit, Is.EqualTo(4UL));
                Assert.That(openWhileOpen, Is.EqualTo((ushort)1));
                Assert.That(resource.OpenCount!.Value, Is.Zero);
            });
        }

        private static async Task<byte[]> ReadWholeDocumentAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                nm.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, int.MaxValue,
                CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            return read.Data.Span.ToArray();
        }

        /// <summary>
        /// Streams <paramref name="document"/> through the handle the create returned and commits it.
        /// </summary>
        private static async Task CommitAsync(
            XRegistryRegistrationNodeManager nm,
            ResourceState resource,
            byte[] document)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                nm.SystemContext, resource.Open, resource.NodeId,
                kWriteMode | kEraseExistingMode, CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                nm.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                nm.SystemContext, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<ResourceState> CreateResourceAsync(
            XRegistryRegistrationNodeManager nm)
        {
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "urn:doc", "1", false,
                CancellationToken.None).ConfigureAwait(false);
            return (ResourceState)nm.Find(created.ResourceNodeId)!;
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

        private const byte kReadMode = 1;
        private const byte kWriteMode = 2;
        private const byte kEraseExistingMode = 4;
        private const byte kAppendMode = 8;
    }
}
