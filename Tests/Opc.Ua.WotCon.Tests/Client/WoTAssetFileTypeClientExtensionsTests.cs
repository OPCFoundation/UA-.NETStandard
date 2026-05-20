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

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Unit tests for <see cref="WoTAssetFileTypeClientExtensions"/>
    /// — the WoT-specific Open → Write → CloseAndUpdate flow,
    /// including the <see cref="Stream"/>-based overload that streams a
    /// Thing Description without buffering it in memory.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable(ParallelScope.All)]
    public class WoTAssetFileTypeClientExtensionsTests
    {
        [Test]
        public async Task UploadAndUpdateStreamRoundTripsTdAndCallsCloseAndUpdateAsync()
        {
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"Test\"}";
            byte[] tdBytes = Encoding.UTF8.GetBytes(td);

            var mock = new WotAssetFileTypeSessionMock();
            byte capturedMode = 0;
            byte[] writtenSoFar = Array.Empty<byte>();
            bool closeAndUpdateCalled = false;
            mock.OnOpen(mode => { capturedMode = mode; return 17; });
            mock.OnWrite((handle, data) =>
            {
                Assert.That(handle, Is.EqualTo(17u));
                byte[] joined = new byte[writtenSoFar.Length + data.Length];
                Array.Copy(writtenSoFar, 0, joined, 0, writtenSoFar.Length);
                Array.Copy(data, 0, joined, writtenSoFar.Length, data.Length);
                writtenSoFar = joined;
            });
            mock.OnCloseAndUpdate(handle =>
            {
                Assert.That(handle, Is.EqualTo(17u));
                closeAndUpdateCalled = true;
            });
            mock.OnClose(_ => { /* not expected on the happy path */ });

            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var stream = new MemoryStream(tdBytes);

            await file.UploadAndUpdateAsync(stream, chunkSize: 16, CancellationToken.None);

            Assert.That(capturedMode, Is.EqualTo((byte)6)); // Write | EraseExisting
            Assert.That(writtenSoFar, Is.EqualTo(tdBytes));
            Assert.That(closeAndUpdateCalled, Is.True);
            // No raw Close call on success — only CloseAndUpdate.
            Assert.That(mock.CountCallsTo(Opc.Ua.Methods.FileType_Close), Is.EqualTo(0));
            Assert.That(
                mock.CountCallsTo(Opc.Ua.WotCon.Methods.WoTAssetFileType_CloseAndUpdate),
                Is.EqualTo(1));
        }

        [Test]
        public async Task UploadAndUpdateStreamEmptyStreamStillCallsCloseAndUpdateAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            bool closeAndUpdateCalled = false;
            int writes = 0;
            mock.OnOpen(_ => 2);
            mock.OnWrite((_, _) => writes++);
            mock.OnCloseAndUpdate(_ => closeAndUpdateCalled = true);

            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var stream = new MemoryStream();

            await file.UploadAndUpdateAsync(stream, chunkSize: 32, CancellationToken.None);

            Assert.That(writes, Is.EqualTo(0));
            Assert.That(closeAndUpdateCalled, Is.True);
        }

        [Test]
        public async Task UploadAndUpdateStreamWriteFailureCallsCloseForCleanupAndRethrowsAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            bool closeCalled = false;
            int closeAndUpdateCalls = 0;
            mock.OnOpen(_ => 4);
            mock.OnWrite((_, _) => throw new InvalidOperationException("boom"));
            mock.OnClose(_ => closeCalled = true);
            mock.OnCloseAndUpdate(_ => closeAndUpdateCalls++);

            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var stream = new MemoryStream(new byte[8]);

            try
            {
                await file.UploadAndUpdateAsync(stream, chunkSize: 4, CancellationToken.None);
                Assert.Fail("expected exception");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
            Assert.That(closeCalled, Is.True);
            Assert.That(closeAndUpdateCalls, Is.EqualTo(0));
        }

        [Test]
        public void UploadAndUpdateStreamNullFileThrows()
        {
            using var stream = new MemoryStream();
            Assert.That(
                async () => await ((WoTAssetFileTypeClient)null!).UploadAndUpdateAsync(stream),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UploadAndUpdateStreamNullStreamThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            Assert.That(
                async () => await file.UploadAndUpdateAsync((Stream)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UploadAndUpdateStreamInvalidChunkSizeThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var stream = new MemoryStream();
            Assert.That(
                async () => await file.UploadAndUpdateAsync(stream, chunkSize: 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task UploadAndUpdateBytesOverloadStillWorksAsync()
        {
            // The byte-array overload must keep its existing semantics
            // alongside the new Stream overload.
            byte[] payload = Encoding.UTF8.GetBytes("{\"title\":\"x\"}");
            var mock = new WotAssetFileTypeSessionMock();
            byte[] writtenSoFar = Array.Empty<byte>();
            bool closeAndUpdateCalled = false;
            mock.OnOpen(_ => 1);
            mock.OnWrite((_, data) =>
            {
                byte[] joined = new byte[writtenSoFar.Length + data.Length];
                Array.Copy(writtenSoFar, 0, joined, 0, writtenSoFar.Length);
                Array.Copy(data, 0, joined, writtenSoFar.Length, data.Length);
                writtenSoFar = joined;
            });
            mock.OnCloseAndUpdate(_ => closeAndUpdateCalled = true);

            var file = new WoTAssetFileTypeClient(
                mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            await file.UploadAndUpdateAsync(payload.AsMemory(), ct: CancellationToken.None);

            Assert.That(writtenSoFar, Is.EqualTo(payload));
            Assert.That(closeAndUpdateCalled, Is.True);
        }
    }
}
