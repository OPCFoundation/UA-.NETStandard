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

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the <see cref="WotResourceFileManager"/> OPC UA FileType
    /// primitives (Open/Read/Write/Close/GetPosition/SetPosition) in isolation,
    /// without a running server. The ThingDescriptionFileState is created via
    /// the source-generated factory; method handlers are invoked directly
    /// through the wired OnCall delegates.
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
        public void OpenSecondWriterWhileFirstOpenReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint first = 0;
            harness.Open(ModeWriteErase, ref first);
            uint second = 0;
            ServiceResult result = harness.Open(ModeWriteErase, ref second);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
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
        public void OpenCountIncreasesAndDecreasesWithHandles()
        {
            using var harness = new Harness();
            uint h1 = 0;
            uint h2 = 0;
            harness.Open(ModeRead, ref h1);
            Assert.That(harness.File.OpenCount!.Value, Is.EqualTo((ushort)1));
            harness.Open(ModeRead, ref h2);
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo((ushort)2));
            harness.Close(h1);
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo((ushort)1));
            harness.Close(h2);
            Assert.That(harness.File.OpenCount.Value, Is.Zero);
        }

        [Test]
        public void ReadOnValidReadHandleReturnsContent()
        {
            using var harness = new Harness();
            byte[] content = Encoding.UTF8.GetBytes("hello resource");
            harness.Manager.UpdatePersistedContent(content, null);

            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ByteString data = default;
            ServiceResult result = harness.Read(handle, 256, ref data);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.Span.ToArray(), Is.EqualTo(content));
        }

        [Test]
        public void ReadWithLengthZeroReturnsEmptyAndSuccess()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ByteString data = default;
            ServiceResult result = harness.Read(handle, 0, ref data);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.IsNull || data.Span.Length == 0, Is.True);
        }

        [Test]
        public void ReadPastEndOfFileReturnsEmptyAndSuccess()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ByteString first = default;
            harness.Read(handle, 2, ref first);
            ByteString data = default;
            ServiceResult result = harness.Read(handle, 256, ref data);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.IsNull || data.Span.Length == 0, Is.True);
        }

        [Test]
        public void ReadOnWriteHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ByteString data = default;
            ServiceResult result = harness.Read(handle, 16, ref data);
            harness.Close(handle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ReadWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ByteString data = default;
            ServiceResult result = harness.Read(9999, 16, ref data);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void WriteOnValidWriteHandleSucceeds()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3 }));
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteOnReadHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1 }));
            harness.Close(handle);

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
        public void WriteBeyondMaxSizeReturnsBadOutOfMemory()
        {
            using var harness = new Harness(maxDocumentSize: 4);
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2, 3, 4, 5 }));
            harness.Close(handle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfMemory));
        }

        [Test]
        public void WriteEmptyByteStringIsNoOp()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Write(handle, ByteString.Empty);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void WriteWhenAuthorizeFailsReturnsBadResult()
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
            harness.Close(handle);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void GetPositionOnReadHandleReturnsZeroInitially()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ulong position = 999;
            ServiceResult result = harness.GetPosition(handle, ref position);
            harness.Close(handle);

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
        public void GetPositionReflectsCurrentReadOffset()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3, 4, 5 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ByteString data = default;
            harness.Read(handle, 3, ref data);
            ulong position = 0;
            ServiceResult result = harness.GetPosition(handle, ref position);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(position, Is.EqualTo(3ul));
        }

        [Test]
        public void SetPositionOnReadHandleWithinBoundsSucceeds()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3, 4, 5 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.SetPosition(handle, 3);
            ulong pos = 0;
            harness.GetPosition(handle, ref pos);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(pos, Is.EqualTo(3ul));
        }

        [Test]
        public void SetPositionBeyondLengthReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            harness.Manager.UpdatePersistedContent(new byte[] { 1, 2, 3 }, null);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.SetPosition(handle, 100);
            harness.Close(handle);

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
        public void SetPositionOnWriteHandleCallsAuthorizeWrite()
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
            harness.Close(handle);

            Assert.That(setPosAuthCalled, Is.True);
        }

        [Test]
        public void SetPositionOnWriteHandleWhenAuthorizeFailsReturnsBadResult()
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
            harness.Close(handle);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void CloseReadHandleSucceedsWithoutCommit()
        {
            int commitCount = 0;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                commitCount++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(commitCount, Is.Zero);
        }

        [Test]
        public void CloseWriteHandleWithContentInvokesCommitCallback()
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
            ServiceResult result = harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.EqualTo(payload));
        }

        [Test]
        public void CloseWriteHandleWithoutContentIsNoOp()
        {
            int commitCount = 0;
            using var harness = new Harness(onCommit: (_, _, _) =>
            {
                commitCount++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            });
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            ServiceResult result = harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(commitCount, Is.Zero);
        }

        [Test]
        public void CloseWithInvalidHandleReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            ServiceResult result = harness.Close(9999);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void CloseWriteHandleWhenCommitFailsReturnsBadResult()
        {
            using var harness = new Harness(onCommit: (_, _, _) =>
                new ValueTask<ServiceResult>((ServiceResult)StatusCodes.BadInternalError));
            byte[] payload = Encoding.UTF8.GetBytes("doc");
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(payload));
            ServiceResult result = harness.Close(handle);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void CloseWriteHandleWhenCloseAuthorizeFailsRemovesHandle()
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
            ServiceResult result = harness.Close(handle);

            Assert.That(closeAuthCount, Is.EqualTo(1));
            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero,
                "Handle must be removed even when close-authorize fails.");
        }

        [Test]
        public void TryOpenWriteHandleSucceeds()
        {
            using var harness = new Harness();
            ServiceResult result = harness.Manager.TryOpenWriteHandle(null, out uint fileHandle);

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

            ServiceResult result = harness.Manager.TryOpenWriteHandle(null, out uint fileHandle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void TryOpenWriteHandleWhenWriterAlreadyOpenReturnsBadInvalidState()
        {
            using var harness = new Harness();
            harness.Manager.TryOpenWriteHandle(null, out uint _);

            ServiceResult second = harness.Manager.TryOpenWriteHandle(null, out uint fileHandle);

            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void UpdatePersistedContentSetsCurrentContentAndFileSize()
        {
            using var harness = new Harness();
            byte[] content = new byte[] { 10, 20, 30 };
            harness.Manager.UpdatePersistedContent(content, "application/td+json");

            Assert.That(harness.Manager.CurrentContent, Is.EqualTo(content));
            Assert.That(harness.File.Size!.Value, Is.EqualTo((ulong)3));
        }

        [Test]
        public void WriteThenReadRoundTripsContent()
        {
            using var harness = new Harness();
            byte[] payload = Encoding.UTF8.GetBytes("{\"title\":\"my-thing\"}");
            uint wh = 0;
            harness.Open(ModeWriteErase, ref wh);
            harness.Write(wh, ByteString.From(payload));
            harness.Close(wh);
            harness.Manager.UpdatePersistedContent(payload, null);

            uint rh = 0;
            harness.Open(ModeRead, ref rh);
            ByteString data = default;
            harness.Read(rh, payload.Length + 16, ref data);
            harness.Close(rh);

            Assert.That(data.Span.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void DisposeClosesAllOpenHandles()
        {
            var harness = new Harness();
            uint h1 = 0;
            uint h2 = 0;
            harness.Open(ModeRead, ref h1);
            harness.Open(ModeRead, ref h2);

            harness.Dispose();

            // After dispose the Manager should not throw and operations on old
            // handles now return BadInvalidArgument (handles dict was cleared).
            ByteString data = default;
            ServiceResult result = harness.Read(h1, 1, ref data);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "Old handles must be invalid after the manager is disposed.");
        }

        [Test]
        public void SecondDisposeIsIdempotent()
        {
            var harness = new Harness();
            harness.Dispose();

            Assert.That(() => harness.Dispose(), Throws.Nothing);
        }

        [Test]
        public void OperationsOnUnknownHandleReturnBadInvalidArgument()
        {
            using var harness = new Harness();
            ByteString data = default;
            ulong pos = 0;

            ServiceResult readResult = harness.Read(9999, 1, ref data);
            ServiceResult closeResult = harness.Close(9999);
            ServiceResult writeResult = harness.Write(9999, ByteString.From(new byte[] { 1 }));
            ServiceResult getPos = harness.GetPosition(9999, ref pos);
            ServiceResult setPos = harness.SetPosition(9999, 0);

            Assert.That(readResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(closeResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(writeResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(getPos.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(setPos.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        private sealed class Harness : IDisposable
        {
            private readonly NodeId m_objectId;

            public Harness(
                int maxOpenHandles = 8,
                int maxDocumentSize = 1024 * 1024,
                Func<ISystemContext, string, ServiceResult>? authorizeWrite = null,
                Func<byte[], NodeId?, CancellationToken, ValueTask<ServiceResult>>? onCommit = null)
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

                Manager = new WotResourceFileManager(
                    File,
                    maxOpenHandles,
                    maxDocumentSize,
                    authorizeWrite ?? ((_, _) => ServiceResult.Good),
                    onCommit ?? ((_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good)));

                m_objectId = File.NodeId;
            }

            public SystemContext Context { get; }

            public ThingDescriptionFileState File { get; }

            public WotResourceFileManager Manager { get; }

            public ServiceResult Open(byte mode, ref uint fileHandle)
                => File.Open!.OnCall!.Invoke(Context, File.Open, m_objectId, mode, ref fileHandle);

            public ServiceResult Close(uint fileHandle)
                => File.Close!.OnCall!.Invoke(Context, File.Close, m_objectId, fileHandle);

            public ServiceResult Read(uint fileHandle, int length, ref ByteString data)
                => File.Read!.OnCall!.Invoke(Context, File.Read, m_objectId, fileHandle, length, ref data);

            public ServiceResult Write(uint fileHandle, ByteString data)
                => File.Write!.OnCall!.Invoke(Context, File.Write, m_objectId, fileHandle, data);

            public ServiceResult GetPosition(uint fileHandle, ref ulong position)
                => File.GetPosition!.OnCall!.Invoke(
                    Context, File.GetPosition, m_objectId, fileHandle, ref position);

            public ServiceResult SetPosition(uint fileHandle, ulong position)
                => File.SetPosition!.OnCall!.Invoke(
                    Context, File.SetPosition, m_objectId, fileHandle, position);

            public void Dispose()
                => Manager.Dispose();
        }
    }
}
