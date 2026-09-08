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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the <see cref="WotResourceFileManager"/> OPC UA FileType
    /// primitives (Open/Read/Write/Close/GetPosition/SetPosition) in isolation,
    /// without a running server. The ThingDescriptionFileState is created via
    /// the source-generated factory; method handlers are invoked directly
    /// through the wired method delegates.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotResourceFileManagerTests
    {
        private const byte ModeRead = WotResourceFileManager.ReadMode;
        private const byte ModeWriteErase = WotResourceFileManager.WriteEraseMode;

        [TestCase((byte)0)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        [TestCase((byte)4)]
        [TestCase((byte)5)]
        [TestCase((byte)7)]
        [TestCase((byte)8)]
        [TestCase((byte)10)]
        public void OpenWithUnsupportedModeReturnsBadNotSupported(byte mode)
        {
            using var harness = new Harness();
            uint handle = 0;
            ServiceResult result = harness.Open(mode, ref handle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public void OpenWithReadModeSucceeds()
        {
            using var harness = new Harness();
            uint handle = 0;
            ServiceResult result = harness.Open(ModeRead, ref handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(handle, Is.GreaterThan(0u));
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public void OpenWithWriteEraseModeSucceeds()
        {
            using var harness = new Harness();
            uint handle = 0;
            ServiceResult result = harness.Open(ModeWriteErase, ref handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(handle, Is.GreaterThan(0u));
        }

        [Test]
        public void OpenWriteModeCallsAuthorizeWrite()
        {
            bool called = false;
            using var harness = new Harness(authorizeWrite: (_, _) =>
            {
                called = true;
                return ServiceResult.Good;
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);

            Assert.That(called, Is.True);
        }

        [Test]
        public void OpenWriteModeWhenAuthorizeFailsReturnsBadResult()
        {
            using var harness = new Harness(authorizeWrite: (_, _) =>
                (ServiceResult)StatusCodes.BadUserAccessDenied);
            uint handle = 0;
            ServiceResult result = harness.Open(ModeWriteErase, ref handle);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public void OpenReadModeDoesNotCallAuthorizeWrite()
        {
            bool called = false;
            using var harness = new Harness(authorizeWrite: (_, _) =>
            {
                called = true;
                return ServiceResult.Good;
            });
            uint handle = 0;
            harness.Open(ModeRead, ref handle);

            Assert.That(called, Is.False);
        }

        [Test]
        public void OpenSecondWriterWhileFirstOpenReturnsBadNotWritable()
        {
            using var harness = new Harness();
            uint first = 0;
            harness.Open(ModeWriteErase, ref first);
            uint second = 0;
            ServiceResult result = harness.Open(ModeWriteErase, ref second);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(second, Is.Zero);
        }

        [Test]
        public void OpenBeyondMaxHandlesReturnsBadTooManyOperations()
        {
            using var harness = new Harness(maxOpenHandles: 2);
            uint h1 = 0;
            uint h2 = 0;
            harness.Open(ModeRead, ref h1);
            harness.Open(ModeRead, ref h2);
            uint h3 = 0;
            ServiceResult result = harness.Open(ModeRead, ref h3);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task OpenCountIncreasesAndDecreasesWithHandles()
        {
            using var harness = new Harness();
            uint h1 = 0;
            uint h2 = 0;
            harness.Open(ModeRead, ref h1);
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo((ushort)1));
            harness.Open(ModeRead, ref h2);
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo((ushort)2));
            await harness.CloseAsync(h1).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo((ushort)1));
            await harness.CloseAsync(h2).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount.Value, Is.Zero);
        }

        [Test]
        public async Task ReadOnValidReadHandleReturnsContent()
        {
            using var harness = new Harness();
            byte[] content = Encoding.UTF8.GetBytes("hello resource");
            harness.Manager.UpdatePersistedContent(content, null);

            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            (ServiceResult result, ByteString data) = await harness.ReadAsync(handle, 256)
                .ConfigureAwait(false);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.Span.ToArray(), Is.EqualTo(content));
        }

        [Test]
        public async Task ReadWithLengthZeroReturnsEmptyAndSuccess()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            (ServiceResult result, ByteString data) = await harness.ReadAsync(handle, 0)
                .ConfigureAwait(false);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.IsNull || data.Span.Length == 0, Is.True);
        }

        [Test]
        public async Task ReadPastEndOfFileReturnsEmptyAndSuccess()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            await harness.ReadAsync(handle, 2).ConfigureAwait(false);
            (ServiceResult result, ByteString data) = await harness.ReadAsync(handle, 256)
                .ConfigureAwait(false);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.IsNull || data.Span.Length == 0, Is.True);
        }

        [Test]
        public async Task ReadOnWriteHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            (ServiceResult result, _) = await harness.ReadAsync(handle, 16).ConfigureAwait(false);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ReadWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            (ServiceResult result, _) = await harness.ReadAsync(9999, 16).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task WriteOnValidWriteHandleSucceeds()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3 }));
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task WriteOnReadHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1 }));
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void WriteWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ServiceResult result = harness.Write(9999, ByteString.From(new byte[] { 1 }));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task WriteBeyondMaxSizeReturnsBadOutOfMemory()
        {
            bool committed = false;
            using var harness = new Harness(
                maxDocumentSize: 4,
                onCommit: (_, _, _) =>
                {
                    committed = true;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3, 4, 5 }));
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfMemory));
                Assert.That(committed, Is.False);
            });
        }

        [Test]
        public async Task WriteAfterSeekingBackOverwritesInPlaceAtMaxSize()
        {
            byte[]? committed = null;
            using var harness = new Harness(
                maxDocumentSize: 4,
                onCommit: (bytes, _, _) =>
                {
                    committed = bytes;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3, 4 }));
            ServiceResult setPosition = harness.SetPosition(handle, 2);

            ServiceResult write = harness.Write(handle, ByteString.From(new byte[] { 9, 8 }));
            ServiceResult close = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(setPosition), Is.True);
                Assert.That(ServiceResult.IsGood(write), Is.True);
                Assert.That(ServiceResult.IsGood(close), Is.True);
                Assert.That(committed, Is.EqualTo(new byte[] { 1, 2, 9, 8 }));
            });
        }

        [Test]
        public async Task WriteEmptyByteStringIsNoOp()
        {
            bool committed = false;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                committed = true;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.Empty);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(committed, Is.False);
            });
        }

        [Test]
        public async Task ByteIdenticalCloseDoesNotInvokeCommit()
        {
            byte[] document = [1, 2, 3];
            bool committed = false;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                committed = true;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            harness.Manager.UpdatePersistedContent(document, "application/json");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(document));

            ServiceResult close = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(close), Is.True);
                Assert.That(committed, Is.False);
            });
        }

        [Test]
        public async Task WriteWhenAuthorizeFailsReturnsBadResult()
        {
            using var harness = new Harness(authorizeWrite: (_, op) =>
            {
                if (op == "Write")
                {
                    return (ServiceResult)StatusCodes.BadUserAccessDenied;
                }

                return ServiceResult.Good;
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2 }));
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public async Task GetPositionOnReadHandleReturnsZeroInitially()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ulong position = 999;
            ServiceResult result = harness.GetPosition(handle, ref position);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(position, Is.Zero);
        }

        [Test]
        public void GetPositionWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ulong position = 0;
            ServiceResult result = harness.GetPosition(9999, ref position);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task GetPositionReflectsCurrentReadOffset()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3, 4, 5 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            await harness.ReadAsync(handle, 3).ConfigureAwait(false);
            ulong position = 0;
            ServiceResult result = harness.GetPosition(handle, ref position);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(position, Is.EqualTo(3ul));
        }

        [Test]
        public async Task SetPositionOnReadHandleWithinBoundsSucceeds()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3, 4, 5 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.SetPosition(handle, 3);
            ulong pos = 0;
            harness.GetPosition(handle, ref pos);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(pos, Is.EqualTo(3ul));
        }

        [Test]
        public async Task SetPositionBeyondLengthReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.SetPosition(handle, 100);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void SetPositionWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ServiceResult result = harness.SetPosition(9999, 0);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SetPositionOnWriteHandleCallsAuthorizeWrite()
        {
            bool setPosAuthCalled = false;
            using var harness = new Harness(authorizeWrite: (_, op) =>
            {
                if (op == "SetWritePosition")
                {
                    setPosAuthCalled = true;
                }

                return ServiceResult.Good;
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3 }));
            harness.SetPosition(handle, 1);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(setPosAuthCalled, Is.True);
        }

        [Test]
        public async Task SetPositionOnWriteHandleWhenAuthorizeFailsReturnsBadResult()
        {
            using var harness = new Harness(authorizeWrite: (_, op) =>
            {
                if (op == "SetWritePosition")
                {
                    return (ServiceResult)StatusCodes.BadUserAccessDenied;
                }

                return ServiceResult.Good;
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3 }));
            ServiceResult result = harness.SetPosition(handle, 1);
            await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public async Task CloseReadHandleSucceedsWithoutCommit()
        {
            int commitCount = 0;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                commitCount++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(commitCount, Is.Zero);
        }

        [Test]
        public async Task CloseWriteHandleWithContentInvokesCommitCallback()
        {
            byte[]? committed = null;
            using var harness = new Harness(onCommit: (bytes, _, _) =>
            {
                committed = bytes;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            byte[] payload = Encoding.UTF8.GetBytes("doc body");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(payload));
            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.EqualTo(payload));
        }

        [Test]
        public async Task CloseWriteHandleAwaitsCommitCallbackAsync()
        {
            var commitStarted = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCommit = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new Harness(onCommit: async (_, _, _) =>
            {
                commitStarted.SetResult(new object());
                return await releaseCommit.Task.ConfigureAwait(false);
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(Encoding.UTF8.GetBytes("doc body")));

            ValueTask<ServiceResult> close = harness.CloseAsync(handle);
            await commitStarted.Task.ConfigureAwait(false);

            Assert.That(close.IsCompleted, Is.False);

            releaseCommit.SetResult(ServiceResult.Good);
            ServiceResult result = await close.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task CloseWriteHandleWithoutContentIsNoOp()
        {
            int commitCount = 0;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                commitCount++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(commitCount, Is.Zero);
        }

        [Test]
        public async Task CloseWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ServiceResult result = await harness.CloseAsync(9999).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CloseWriteHandleWhenCommitFailsReturnsBadResult()
        {
            using var harness = new Harness(onCommit: (_, _, _) =>
                new ValueTask<ServiceResult>((ServiceResult)StatusCodes.BadInternalError));
            byte[] payload = Encoding.UTF8.GetBytes("doc");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(payload));
            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public async Task CloseWriteHandleWhenCloseAuthorizeFailsRemovesHandle()
        {
            int closeAuthCount = 0;
            using var harness = new Harness(authorizeWrite: (_, op) =>
            {
                if (op == "CloseWrite")
                {
                    closeAuthCount++;
                    return (ServiceResult)StatusCodes.BadUserAccessDenied;
                }

                return ServiceResult.Good;
            });
            byte[] payload = Encoding.UTF8.GetBytes("doc");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(payload));
            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.That(closeAuthCount, Is.EqualTo(1));
            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero,
                "Handle must be removed even when close-authorize fails.");
        }

        [Test]
        public void TryOpenWriteHandleSucceeds()
        {
            using var harness = new Harness();
            ServiceResult result = harness.Manager.TryOpenWriteHandle(NodeId.Null, out uint fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.GreaterThan(0u));
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public void TryOpenWriteHandleWhenMaxHandlesExceededReturnsBadTooManyOperations()
        {
            using var harness = new Harness(maxOpenHandles: 1);
            uint h1 = 0;
            harness.Open(ModeRead, ref h1);

            ServiceResult result = harness.Manager.TryOpenWriteHandle(NodeId.Null, out uint fileHandle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void TryOpenWriteHandleWhenWriterAlreadyOpenReturnsBadNotWritable()
        {
            using var harness = new Harness();
            harness.Manager.TryOpenWriteHandle(NodeId.Null, out uint _);

            ServiceResult second = harness.Manager.TryOpenWriteHandle(NodeId.Null, out uint fileHandle);

            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void UpdatePersistedContentSetsTheContentKeyAndFileSize()
        {
            using var harness = new Harness();
            byte[] content = new byte[] { 10, 20, 30 };
            harness.Manager.UpdatePersistedContent(content, "application/td+json");

            // The manager no longer holds the bytes: it holds the store key and
            // the length, and reads stream from the store on demand.
            Assert.That(
                harness.Manager.CurrentContentKey,
                Is.EqualTo(WotContentDigest.ToHex(WotContentDigest.Compute(content))));
            Assert.That(harness.Manager.CurrentContentLength, Is.EqualTo(3));
            Assert.That(harness.File.Size!.Value, Is.EqualTo((ulong)3));
        }

        [Test]
        public async Task WriteThenReadRoundTripsContent()
        {
            using var harness = new Harness();
            byte[] payload = Encoding.UTF8.GetBytes("{\"title\":\"my-thing\"}");
            uint wh = 0;
            harness.Open(ModeWriteErase, ref wh);
            harness.Write(wh, ByteString.From(payload));
            await harness.CloseAsync(wh).ConfigureAwait(false);
            harness.Manager.UpdatePersistedContent(payload, null);

            uint rh = 0;
            harness.Open(ModeRead, ref rh);
            (_, ByteString data) = await harness.ReadAsync(rh, payload.Length + 16)
                .ConfigureAwait(false);
            await harness.CloseAsync(rh).ConfigureAwait(false);

            Assert.That(data.Span.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task WriteHandlePassesStableBaselineIncarnationToCommit()
        {
            byte[] original = Encoding.UTF8.GetBytes("original");
            DateTime now = DateTime.UtcNow;
            var baselineVersion = new WotResourceVersion(
                "v1",
                WotContentDigest.Compute(original),
                original.Length,
                "application/json",
                string.Empty,
                now,
                now);
            Guid? observedIncarnation = null;
            WotResourceVersion? committedVersion = null;
            using var harness = new Harness(
                onVersionCommit: (bytes, baseline, incarnation, session, token) =>
                {
                    observedIncarnation = incarnation;
                    committedVersion = baselineVersion.With(
                        digest: WotContentDigest.Compute(bytes),
                        contentLength: bytes.Length);
                    return new ValueTask<WotResourceCommitResult>(
                        new WotResourceCommitResult(ServiceResult.Good, committedVersion));
                });
            harness.Manager.UpdatePersistedContent(baselineVersion, "application/json");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(original));
            harness.Manager.UpdatePersistedContent(
                baselineVersion.With(contentType: "application/updated"),
                "application/updated");

            ServiceResult result = await harness.CloseAsync(handle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(observedIncarnation, Is.EqualTo(baselineVersion.IncarnationId));
                Assert.That(committedVersion, Is.Not.Null);
                Assert.That(
                    harness.Manager.CurrentVersionIncarnation,
                    Is.EqualTo(committedVersion!.IncarnationId));
            });
        }

        [Test]
        public async Task SecondWriterImmediatelyUsesCommittedContentAndIncarnation()
        {
            byte[] original = Encoding.UTF8.GetBytes("original");
            DateTime now = DateTime.UtcNow;
            WotResourceVersion current = new(
                "v1",
                WotContentDigest.Compute(original),
                original.Length,
                "application/json",
                string.Empty,
                now,
                now);
            var baselines = new List<(string ContentKey, Guid? Incarnation)>();
            using var harness = new Harness(
                onVersionCommit: (bytes, baseline, incarnation, session, token) =>
                {
                    baselines.Add((baseline, incarnation));
                    current = current.With(
                        digest: WotContentDigest.Compute(bytes),
                        contentLength: bytes.Length,
                        modifiedAt: DateTime.UtcNow);
                    return new ValueTask<WotResourceCommitResult>(
                        new WotResourceCommitResult(ServiceResult.Good, current));
                });
            harness.Manager.UpdatePersistedContent(current, "application/json");
            byte[] first = Encoding.UTF8.GetBytes("first");
            uint firstHandle = 0;
            harness.Open(ModeWriteErase, ref firstHandle);
            harness.Write(firstHandle, ByteString.From(first));
            ServiceResult firstClose = await harness.CloseAsync(firstHandle).ConfigureAwait(false);
            string firstDigest = WotContentDigest.ToHex(WotContentDigest.Compute(first));

            byte[] second = Encoding.UTF8.GetBytes("second");
            uint secondHandle = 0;
            harness.Open(ModeWriteErase, ref secondHandle);
            harness.Write(secondHandle, ByteString.From(second));
            ServiceResult secondClose = await harness.CloseAsync(secondHandle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(firstClose), Is.True);
                Assert.That(ServiceResult.IsGood(secondClose), Is.True);
                Assert.That(baselines, Has.Count.EqualTo(2));
                Assert.That(baselines[0].ContentKey, Is.EqualTo(
                    WotContentDigest.ToHex(WotContentDigest.Compute(original))));
                Assert.That(baselines[1].ContentKey, Is.EqualTo(firstDigest));
                Assert.That(baselines[0].Incarnation, Is.EqualTo(current.IncarnationId));
                Assert.That(baselines[1].Incarnation, Is.EqualTo(current.IncarnationId));
                Assert.That(harness.Manager.CurrentContentKey, Is.EqualTo(current.DigestHex));
                Assert.That(harness.Manager.CurrentContentLength, Is.EqualTo(second.Length));
                Assert.That(
                    harness.Manager.CurrentVersionIncarnation,
                    Is.EqualTo(current.IncarnationId));
            });
        }

        [Test]
        public async Task DisposeClosesAllOpenHandlesAndResetsState()
        {
            var harness = new Harness();
            uint readHandle = 0;
            uint writeHandle = 0;
            harness.Open(ModeRead, ref readHandle);
            harness.Open(ModeWriteErase, ref writeHandle);

            harness.Dispose();

            (ServiceResult readResult, _) = await harness.ReadAsync(readHandle, 1)
                .ConfigureAwait(false);
            ushort openCountAfterDispose = harness.File.OpenCount!.Value;
            ServiceResult writeResult = harness.Manager.TryOpenWriteHandle(NodeId.Null, out uint newWriteHandle);

            Assert.Multiple(() =>
            {
                Assert.That(readResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                    "Old handles must be invalid after the manager is disposed.");
                Assert.That(openCountAfterDispose, Is.Zero);
                Assert.That(ServiceResult.IsGood(writeResult), Is.True,
                    "Disposal must clear the exclusive writer state.");
                Assert.That(newWriteHandle, Is.GreaterThan(0u));
            });

            harness.Dispose();
        }

        [Test]
        public void SecondDisposeIsIdempotent()
        {
            var harness = new Harness();
            harness.Dispose();

            Assert.That(() => harness.Dispose(), Throws.Nothing);
        }

        [Test]
        public async Task OperationsOnUnknownHandleReturnBadInvalidArgument()
        {
            using var harness = new Harness();
            ulong pos = 0;

            (ServiceResult readResult, _) = await harness.ReadAsync(9999, 1)
                .ConfigureAwait(false);
            ServiceResult closeResult = await harness.CloseAsync(9999).ConfigureAwait(false);
            ServiceResult writeResult = harness.Write(9999, ByteString.From(new byte[] { 1 }));
            ServiceResult getPos = harness.GetPosition(9999, ref pos);
            ServiceResult setPos = harness.SetPosition(9999, 0);

            Assert.That(readResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(closeResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(writeResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(getPos.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(setPos.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Scenario 5 (logical-Resource file-handle forwarding): a write handle
        /// opened through the <see cref="IXRegistryProjectedResourceFileHandleForwarder"/>
        /// entry points (the path a logical Resource's Open/Read/Write/Close
        /// delegate through) shares the SAME single-writer reservation as a
        /// handle opened directly on the manager's own bound FileState, because
        /// both paths are serviced by the one <see cref="WotResourceFileManager"/>
        /// instance. Opening a second writer via either path while the other
        /// path's writer is still open must be rejected.
        /// </summary>
        [Test]
        public void ForwardedOpenConflictsWithDirectWriterOnSameManager()
        {
            using var harness = new Harness();
            uint direct = 0;
            ServiceResult directOpen = harness.Open(ModeWriteErase, ref direct);
            Assert.That(ServiceResult.IsGood(directOpen), Is.True);

            uint forwarded = 0;
            ServiceResult forwardedOpen = harness.ForwardOpen(ModeWriteErase, ref forwarded);

            Assert.That(forwardedOpen.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(forwarded, Is.Zero);
        }

        [Test]
        public void DirectOpenConflictsWithForwardedWriterOnSameManager()
        {
            using var harness = new Harness();
            uint forwarded = 0;
            ServiceResult forwardedOpen = harness.ForwardOpen(ModeWriteErase, ref forwarded);
            Assert.That(ServiceResult.IsGood(forwardedOpen), Is.True);

            uint direct = 0;
            ServiceResult directOpen = harness.Open(ModeWriteErase, ref direct);

            Assert.That(directOpen.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(direct, Is.Zero);
        }

        [Test]
        public async Task ForwardedWriteThenForwardedCloseCommitsAndDirectReadSeesIt()
        {
            byte[]? committed = null;
            using var harness = new Harness(onCommit: (bytes, _, _) =>
            {
                committed = bytes;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });

            uint handle = 0;
            Assert.That(
                ServiceResult.IsGood(harness.ForwardOpen(ModeWriteErase, ref handle)),
                Is.True);

            byte[] content = Encoding.UTF8.GetBytes("forwarded content");
            ServiceResult write = harness.ForwardWrite(handle, ByteString.From(content));
            Assert.That(ServiceResult.IsGood(write), Is.True);

            ServiceResult close = await harness.ForwardCloseAsync(handle).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(close), Is.True);
            Assert.That(committed, Is.EqualTo(content));

            // The commit path updates the manager's served content; a direct
            // read handle (opened via the manager's own bound FileState) must
            // observe the same bytes just committed via the forwarding path,
            // proving both paths operate on one shared underlying state.
            harness.Manager.UpdatePersistedContent(content, null);
            uint readHandle = 0;
            harness.Open(ModeRead, ref readHandle);
            (ServiceResult readResult, ByteString data) = await harness
                .ReadAsync(readHandle, 256)
                .ConfigureAwait(false);
            await harness.CloseAsync(readHandle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(readResult), Is.True);
            Assert.That(data.Span.ToArray(), Is.EqualTo(content));
        }

        [Test]
        public async Task ForwardedReadReturnsSameContentAsDirectRead()
        {
            using var harness = new Harness();
            byte[] content = Encoding.UTF8.GetBytes("shared state content");
            harness.Manager.UpdatePersistedContent(content, null);

            uint handle = 0;
            Assert.That(
                ServiceResult.IsGood(harness.ForwardOpen(ModeRead, ref handle)),
                Is.True);
            (ServiceResult result, ByteString data) = await harness
                .ForwardReadAsync(handle, 256)
                .ConfigureAwait(false);
            await harness.ForwardCloseAsync(handle).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.Span.ToArray(), Is.EqualTo(content));
        }

        [Test]
        public void ForwardedGetPositionAndSetPositionOperateOnSharedHandleState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.ForwardOpen(ModeWriteErase, ref handle);
            harness.ForwardWrite(handle, ByteString.From(new byte[] { 1, 2, 3, 4, 5 }));

            ulong position = 0;
            ServiceResult setResult = harness.ForwardSetPosition(handle, 3);
            ServiceResult getResult = harness.ForwardGetPosition(handle, ref position);

            Assert.That(ServiceResult.IsGood(setResult), Is.True);
            Assert.That(ServiceResult.IsGood(getResult), Is.True);
            Assert.That(position, Is.EqualTo(3UL));
        }

        private sealed class Harness : IDisposable
        {
            private readonly NodeId m_objectId;

            public Harness(
                int maxOpenHandles = 8,
                int maxDocumentSize = 1024 * 1024,
                Func<ISystemContext, string, ServiceResult>? authorizeWrite = null,
                Func<byte[], NodeId, CancellationToken, ValueTask<ServiceResult>>? onCommit = null,
                Func<
                    byte[],
                    string,
                    Guid?,
                    NodeId,
                    CancellationToken,
                    ValueTask<WotResourceCommitResult>>? onVersionCommit = null)
            {
                Context = new SystemContext(null!)
                {
                    NamespaceUris = new NamespaceTable(),
                    EncodeableFactory = EncodeableFactory.Create()
                };
                Context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
                Context.NamespaceUris.GetIndexOrAppend("urn:test");

                File = Context.CreateInstanceOfThingDescriptionFileType(
                    parent: null!,
                    browseName: new QualifiedName("ResourceFile", 1));

                Manager = onVersionCommit is null
                    ? new WotResourceFileManager(
                        File,
                        maxOpenHandles,
                        maxDocumentSize,
                        authorizeWrite ?? ((_, _) => ServiceResult.Good),
                        onCommit ?? ((_, _, _) =>
                            new ValueTask<ServiceResult>(ServiceResult.Good)))
                    : new WotResourceFileManager(
                        File,
                        maxOpenHandles,
                        maxDocumentSize,
                        authorizeWrite ?? ((_, _) => ServiceResult.Good),
                        ReadEmptyAsync,
                        onVersionCommit);

                m_objectId = File.NodeId;
            }

            public SystemContext Context { get; }

            public ThingDescriptionFileState File { get; }

            public WotResourceFileManager Manager { get; }

            public ServiceResult Open(byte mode, ref uint fileHandle)
                => File.Open!.OnCall!.Invoke(Context, File.Open, m_objectId, mode, ref fileHandle);

            public async ValueTask<ServiceResult> CloseAsync(uint fileHandle)
            {
                CloseMethodStateResult result = await File.Close!.OnCallAsync!(
                        Context,
                        File.Close,
                        m_objectId,
                        fileHandle,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return result.ServiceResult;
            }

            public async ValueTask<(ServiceResult Status, ByteString Data)> ReadAsync(
                uint fileHandle,
                int length)
            {
                ReadMethodStateResult result = await File.Read!.OnCallAsync!(
                        Context,
                        File.Read,
                        m_objectId,
                        fileHandle,
                        length,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return (result.ServiceResult, result.Data);
            }

            public ServiceResult Write(uint fileHandle, ByteString data)
                => File.Write!.OnCall!.Invoke(Context, File.Write, m_objectId, fileHandle, data);

            private static ValueTask<ByteString> ReadEmptyAsync(
                string key,
                long offset,
                int count,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ByteString>(ByteString.Empty);
            }

            public ServiceResult GetPosition(uint fileHandle, ref ulong position)
                => File.GetPosition!.OnCall!.Invoke(
                    Context, File.GetPosition, m_objectId, fileHandle, ref position);

            public ServiceResult SetPosition(uint fileHandle, ulong position)
                => File.SetPosition!.OnCall!.Invoke(
                    Context, File.SetPosition, m_objectId, fileHandle, position);

            /// <summary>
            /// Invokes <see cref="IXRegistryProjectedResourceFileHandleForwarder.ForwardOpen"/>
            /// - the same entry point a logical Resource's own Open method
            /// forwards through - against this same manager instance.
            /// </summary>
            public ServiceResult ForwardOpen(byte mode, ref uint fileHandle)
                => Forwarder.ForwardOpen(Context, File.Open!, m_objectId, mode, ref fileHandle);

            public ValueTask<ServiceResult> ForwardCloseAsync(uint fileHandle)
                => Forwarder
                    .ForwardCloseAsync(Context, File.Close!, m_objectId, fileHandle, CancellationToken.None);

            public ValueTask<(ServiceResult Status, ByteString Data)> ForwardReadAsync(
                uint fileHandle,
                int length)
                => Forwarder
                    .ForwardReadAsync(
                        Context, File.Read!, m_objectId, fileHandle, length, CancellationToken.None);

            public ServiceResult ForwardWrite(uint fileHandle, ByteString data)
                => Forwarder.ForwardWrite(Context, File.Write!, m_objectId, fileHandle, data);

            public ServiceResult ForwardGetPosition(uint fileHandle, ref ulong position)
                => Forwarder.ForwardGetPosition(
                    Context, File.GetPosition!, m_objectId, fileHandle, ref position);

            public ServiceResult ForwardSetPosition(uint fileHandle, ulong position)
                => Forwarder.ForwardSetPosition(
                    Context, File.SetPosition!, m_objectId, fileHandle, position);

            private IXRegistryProjectedResourceFileHandleForwarder Forwarder =>
                (IXRegistryProjectedResourceFileHandleForwarder)Manager;

            public void Dispose()
                => Manager.Dispose();
        }
    }
}
