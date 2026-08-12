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

using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Assets;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Tests OPC UA File Transfer method wiring for AAS file and blob content.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasElementFileManagerTests
    {
        [Test]
        public void OpenReadAndCloseServeInitialFileContent()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(file, ByteString.From(Encoding.UTF8.GetBytes("hello")),
                "text/plain");

            uint handle = 0;
            ServiceResult open = file.Open!.OnCall!(
                CreateContext(),
                file.Open,
                file.NodeId,
                AasElementFileManager.ReadMode,
                ref handle);
            ByteString data = ByteString.Empty;
            ServiceResult read = file.Read!.OnCall!(
                CreateContext(),
                file.Read,
                file.NodeId,
                handle,
                16,
                ref data);
            ServiceResult close = file.Close!.OnCall!(
                CreateContext(),
                file.Close,
                file.NodeId,
                handle);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(open), Is.True);
                Assert.That(ServiceResult.IsGood(read), Is.True);
                Assert.That(ServiceResult.IsGood(close), Is.True);
                Assert.That(Encoding.UTF8.GetString(data.Span.ToArray()), Is.EqualTo("hello"));
                Assert.That(file.OpenCount!.Value, Is.Zero);
            });
        }

        [Test]
        public void WriteRoundTripReplacesBlobContent()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(file, ByteString.From(Encoding.UTF8.GetBytes("old")),
                "application/octet-stream");

            uint writeHandle = 0;
            file.Open!.OnCall!(CreateContext(), file.Open, file.NodeId,
                AasElementFileManager.WriteEraseMode, ref writeHandle);
            ServiceResult write = file.Write!.OnCall!(
                CreateContext(),
                file.Write,
                file.NodeId,
                writeHandle,
                ByteString.From(Encoding.UTF8.GetBytes("new")));
            ServiceResult closeWrite = file.Close!.OnCall!(CreateContext(), file.Close, file.NodeId, writeHandle);
            uint readHandle = 0;
            file.Open.OnCall(CreateContext(), file.Open, file.NodeId, AasElementFileManager.ReadMode,
                ref readHandle);
            ByteString data = ByteString.Empty;
            ServiceResult read = file.Read!.OnCall!(CreateContext(), file.Read, file.NodeId, readHandle, 16,
                ref data);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(write), Is.True);
                Assert.That(ServiceResult.IsGood(closeWrite), Is.True);
                Assert.That(ServiceResult.IsGood(read), Is.True);
                Assert.That(Encoding.UTF8.GetString(data.Span.ToArray()), Is.EqualTo("new"));
                Assert.That(file.Size!.Value, Is.EqualTo((ulong)3));
            });
        }

        [Test]
        public void UnknownHandleReturnsBadInvalidArgument()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(file, ByteString.Empty, "text/plain");

            ByteString data = ByteString.Empty;
            ServiceResult result = file.Read!.OnCall!(
                CreateContext(),
                file.Read,
                file.NodeId,
                123,
                16,
                ref data);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void ReadOnWriteHandleReturnsBadInvalidArgument()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(file, ByteString.Empty, "text/plain");

            uint handle = 0;
            file.Open!.OnCall!(CreateContext(), file.Open, file.NodeId,
                AasElementFileManager.WriteEraseMode, ref handle);
            ByteString data = ByteString.Empty;
            ServiceResult result = file.Read!.OnCall!(
                CreateContext(),
                file.Read,
                file.NodeId,
                handle,
                16,
                ref data);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Handles are sequential, so a Session can name another Session's
        /// simply by counting up from its own. Nothing bound a handle to its
        /// opener, so any Session could read another's buffer, seek in it, or
        /// Close it and thereby publish it as the file's content.
        /// </summary>
        [Test]
        public void AHandleIsNotUsableFromAnotherSession()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(
                file, ByteString.From(Encoding.UTF8.GetBytes("secret")), "text/plain");
            ISystemContext owner = CreateSessionContext("session-a");
            ISystemContext other = CreateSessionContext("session-b");

            uint handle = 0;
            file.Open!.OnCall!(owner, file.Open, file.NodeId, AasElementFileManager.ReadMode, ref handle);

            ByteString stolen = ByteString.Empty;
            ServiceResult read = file.Read!.OnCall!(other, file.Read, file.NodeId, handle, 16, ref stolen);
            ServiceResult closed = file.Close!.OnCall!(other, file.Close, file.NodeId, handle);
            ByteString mine = ByteString.Empty;
            ServiceResult ownerRead = file.Read.OnCall(owner, file.Read, file.NodeId, handle, 16, ref mine);

            Assert.Multiple(() =>
            {
                Assert.That(read.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(stolen.Length, Is.Zero);
                Assert.That(closed.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(ServiceResult.IsGood(ownerRead), Is.True,
                    "The other Session's Close must not have closed the owner's handle.");
                Assert.That(Encoding.UTF8.GetString(mine.Span.ToArray()), Is.EqualTo("secret"));
            });
        }

        /// <summary>
        /// The handle count was bounded but the bytes behind a write handle
        /// were not, so a Session could grow server memory without limit by
        /// writing and never closing.
        /// </summary>
        [Test]
        public void AWriteBeyondTheConfiguredBoundIsRefused()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(
                file, ByteString.Empty, "application/octet-stream", maxOpenHandles: 4, maxWriteBytes: 8);
            ISystemContext context = CreateContext();

            uint handle = 0;
            file.Open!.OnCall!(context, file.Open, file.NodeId,
                AasElementFileManager.WriteEraseMode, ref handle);
            ServiceResult within = file.Write!.OnCall!(context, file.Write, file.NodeId, handle,
                ByteString.From(new byte[8]));
            ServiceResult beyond = file.Write.OnCall(context, file.Write, file.NodeId, handle,
                ByteString.From(new byte[1]));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(within), Is.True);
                Assert.That(beyond.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));
            });
        }

        /// <summary>
        /// The handlers run without the node manager lock, because the async
        /// call path - unlike the synchronous one - does not take it. Two
        /// concurrent Calls on one File therefore reach the handle table at the
        /// same time, which is undefined behaviour for a bare Dictionary.
        /// </summary>
        [Test]
        public void ConcurrentOpenAndCloseKeepTheHandleTableConsistent()
        {
            FileState file = CreateFile();
            using var manager = new AasElementFileManager(
                file, ByteString.From(Encoding.UTF8.GetBytes("body")), "text/plain", maxOpenHandles: 512);

            Assert.DoesNotThrow(() => Parallel.For(0, 200, _ =>
            {
                ISystemContext context = CreateContext();
                uint handle = 0;
                ServiceResult opened = file.Open!.OnCall!(
                    context, file.Open, file.NodeId, AasElementFileManager.ReadMode, ref handle);
                if (ServiceResult.IsGood(opened))
                {
                    ByteString data = ByteString.Empty;
                    file.Read!.OnCall!(context, file.Read, file.NodeId, handle, 4, ref data);
                    file.Close!.OnCall!(context, file.Close, file.NodeId, handle);
                }
            }));

            Assert.That(file.OpenCount!.Value, Is.Zero);
        }

        private static ISystemContext CreateSessionContext(string sessionId)
        {
            var context = new Mock<ISessionSystemContext>();
            context.Setup(c => c.SessionId).Returns(new NodeId(sessionId, 1));
            return context.Object;
        }

        private static FileState CreateFile()
        {
            var context = CreateContext();
            var file = new FileState(null)
            {
                NodeId = new NodeId("file", 1),
                BrowseName = new QualifiedName("file", 1),
                DisplayName = new LocalizedText("file")
            };
            file.Create(context, file.NodeId, file.BrowseName, file.DisplayName, true);
            return file;
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }
    }
}
