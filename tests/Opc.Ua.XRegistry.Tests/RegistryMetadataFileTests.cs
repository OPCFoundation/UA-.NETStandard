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

using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryMetadataFileTests
    {
        [Test]
        public void HandlesBelongToTheirSessionAndTheirFile()
        {
            ServerSystemContext owner = Context("owner");
            ServerSystemContext other = Context("other");
            using var binding = new RegistryMetadataFileBinding();
            FileState first = File(owner, "first");
            FileState second = File(owner, "second");
            binding.Bind(first, () => ByteString.From("first"u8.ToArray()), (_, _) => ServiceResult.Good);
            binding.Bind(second, () => ByteString.From("second"u8.ToArray()), (_, _) => ServiceResult.Good);
            uint handle = Open(first, owner);
            ByteString bytes = default;

            Assert.That(Read(first, other, handle, ref bytes).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Read(second, owner, handle, ref bytes).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Read(first, owner, handle, ref bytes).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(bytes, Is.EqualTo(ByteString.From("first"u8.ToArray())));
        }

        [Test]
        public void ReadRechecksAuthorizationAfterOpen()
        {
            ServerSystemContext caller = Context("caller");
            using var binding = new RegistryMetadataFileBinding();
            FileState file = File(caller, "metadata");
            bool allowed = true;
            binding.Bind(file, () => ByteString.From("private"u8.ToArray()),
                (_, _) => allowed ? ServiceResult.Good : StatusCodes.BadUserAccessDenied);
            uint handle = Open(file, caller);
            allowed = false;
            ByteString bytes = default;

            Assert.That(Read(file, caller, handle, ref bytes).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(bytes.IsNull, Is.True);
        }

        [Test]
        public void PinnedReadsCountsAndSessionCleanupShareTheHandleLifetime()
        {
            ServerSystemContext caller = Context("caller");
            using var binding = new RegistryMetadataFileBinding(maxHandlesPerSession: 1, maxHandles: 2);
            FileState file = File(caller, "metadata");
            ByteString document = ByteString.From("before"u8.ToArray());
            binding.Bind(file, () => document, (_, _) => ServiceResult.Good);
            uint handle = Open(file, caller);
            Assert.That(file.OpenCount!.Value, Is.EqualTo(1));
            document = ByteString.From("after"u8.ToArray());
            ByteString bytes = default;
            Assert.That(Read(file, caller, handle, ref bytes).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(bytes, Is.EqualTo(ByteString.From("before"u8.ToArray())));
            uint excess = 0;
            Assert.That(file.Open!.OnCall!(caller, file.Open, file.NodeId, 1, ref excess).StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
            binding.ReleaseSession(caller.SessionId!.Value);
            Assert.That(file.OpenCount.Value, Is.Zero);
            Assert.That(Read(file, caller, handle, ref bytes).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Open(file, caller), Is.Not.Zero);
        }

        [Test]
        public void RetainedByteBudgetIsReleasedOnClose()
        {
            ServerSystemContext caller = Context("caller");
            using var binding = new RegistryMetadataFileBinding(maxRetainedBytes: 3);
            FileState file = File(caller, "metadata");
            binding.Bind(file, () => ByteString.From("abc"u8.ToArray()), (_, _) => ServiceResult.Good);
            uint handle = Open(file, caller);
            uint excess = 0;
            Assert.That(file.Open!.OnCall!(caller, file.Open, file.NodeId, 1, ref excess).StatusCode,
                Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(excess, Is.Zero);
            Assert.That(file.Close!.OnCall!(caller, file.Close, file.NodeId, handle).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(file.OpenCount!.Value, Is.Zero);
            Assert.That(Open(file, caller), Is.Not.Zero);
        }

        private static ServiceResult Read(FileState file, ISystemContext context, uint handle, ref ByteString bytes)
        {
            return file.Read!.OnCall!(context, file.Read, file.NodeId, handle, 256, ref bytes);
        }

        private static uint Open(FileState file, ISystemContext context)
        {
            uint handle = 0;
            Assert.That(file.Open!.OnCall!(context, file.Open, file.NodeId, 1, ref handle).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            return handle;
        }

        private static FileState File(ISystemContext context, string id)
        {
            var file = new FileState(null);
            file.Create(context, new NodeId(id, 1), new QualifiedName("Metadata", 1),
                new LocalizedText("Metadata"), assignNodeIds: false);
            return file;
        }

        private static ServerSystemContext Context(string session)
        {
            return new ServerSystemContext(XRegistryServerTestHarness.CreateServer(
                XRegistryWellKnown.XRegistryNamespaceUri).Object)
            {
                SessionId = new NodeId(session, 1)
            };
        }
    }
}
