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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Direct tests for <see cref="WotAssetFileManager"/> that exercise the
    /// Spec §6.3.10 file primitives without spinning up a full server. The
    /// generated <c>WoTAssetFileState</c> is created in isolation via the
    /// source-generated factory; method handlers are invoked directly.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotAssetFileManagerTests
    {
        private const byte ModeRead = 1;
        private const byte ModeWriteErase = 6;

        // ----------------------------------------------------------------
        // Open: mode restriction (§6.3.10) — Read (1) and Write|EraseExisting (6) only.
        // ----------------------------------------------------------------

        [TestCase((byte)0)]
        [TestCase((byte)2)]   // Write only
        [TestCase((byte)3)]   // Read+Write
        [TestCase((byte)4)]   // EraseExisting only
        [TestCase((byte)5)]   // Read+EraseExisting
        [TestCase((byte)7)]   // Read+Write+EraseExisting
        [TestCase((byte)8)]   // Append only
        [TestCase((byte)10)]  // Write+Append
        [TestCase((byte)16)]  // unknown high bit
        public void OpenWithUnsupportedModeReturnsBadNotSupported(byte mode)
        {
            using var harness = new Harness();
            uint handle = 0;
            ServiceResult result = harness.Open(mode, ref handle);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(handle, Is.EqualTo(0u), "No handle is allocated for a rejected mode.");
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
        public void OpenSecondWriterWhileFirstStillOpenReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint first = 0;
            harness.Open(ModeWriteErase, ref first);
            uint second = 0;
            ServiceResult result = harness.Open(ModeWriteErase, ref second);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(second, Is.EqualTo(0u));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));
        }

        // ----------------------------------------------------------------
        // Read / Write / position primitives.
        // ----------------------------------------------------------------

        [Test]
        public void WriteThenReadRoundTripsBytes()
        {
            using var harness = new Harness();
            byte[] payload = Encoding.UTF8.GetBytes("hello world");

            harness.Upload(payload);
            byte[] downloaded = harness.Download();

            Assert.That(downloaded, Is.EqualTo(payload));
        }

        [Test]
        public void ReadOnEmptyFileReturnsEmpty()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ByteString data = default;
            ServiceResult result = harness.Read(handle, 256, ref data);
            harness.Close(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.IsNull || data.Span.Length == 0, Is.True);
        }

        [Test]
        public void GetPositionAndSetPositionWorkOnReadHandle()
        {
            using var harness = new Harness();
            harness.Upload([1, 2, 3, 4, 5]);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            try
            {
                ulong pos = 999;
                ServiceResult getResult = harness.GetPosition(handle, ref pos);
                Assert.That(ServiceResult.IsGood(getResult), Is.True);
                Assert.That(pos, Is.EqualTo(0ul));

                Assert.That(ServiceResult.IsGood(harness.SetPosition(handle, 3)), Is.True);

                pos = 0;
                harness.GetPosition(handle, ref pos);
                Assert.That(pos, Is.EqualTo(3ul));
            }
            finally
            {
                harness.Close(handle);
            }
        }

        [Test]
        public void SetPositionBeyondLengthReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            harness.Upload([1, 2, 3]);
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            try
            {
                ServiceResult result = harness.SetPosition(handle, 100);
                Assert.That(result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            }
            finally
            {
                harness.Close(handle);
            }
        }

        [Test]
        public void ReadOnWriteHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            try
            {
                ByteString data = default;
                ServiceResult result = harness.Read(handle, 16, ref data);
                Assert.That(result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
            finally
            {
                harness.Close(handle);
            }
        }

        [Test]
        public void WriteOnReadHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            try
            {
                ServiceResult result = harness.Write(handle, ByteString.From(new byte[] { 1, 2 }));
                Assert.That(result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
            finally
            {
                harness.Close(handle);
            }
        }

        [Test]
        public void WriteBeyondMaxSizeReturnsBadOutOfMemory()
        {
            using var harness = new Harness(maxThingDescriptionSize: 4);
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            try
            {
                ServiceResult result = harness.Write(
                    handle, ByteString.From(new byte[] { 1, 2, 3, 4, 5 }));
                Assert.That(result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadOutOfMemory));
            }
            finally
            {
                harness.Close(handle);
            }
        }

        [Test]
        public void OperationsOnUnknownHandleReturnBadInvalidArgument()
        {
            using var harness = new Harness();
            ByteString data = default;
            ServiceResult readResult = harness.Read(fileHandle: 9999, length: 1, data: ref data);
            ServiceResult closeResult = harness.Close(9999);
            ServiceResult writeResult = harness.Write(9999, ByteString.From(new byte[] { 1 }));

            Assert.That(readResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(closeResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(writeResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        // ----------------------------------------------------------------
        // Close vs CloseAndUpdate semantics.
        // ----------------------------------------------------------------

        [Test]
        public void CloseAfterWriteDiscardsPendingContent()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(Encoding.UTF8.GetBytes("draft")));
            harness.Close(handle);

            // Persistent content remains unchanged (was empty).
            Assert.That(harness.File.Size!.Value, Is.EqualTo(0ul));
            Assert.That(harness.MaterialiseCallCount, Is.EqualTo(0));
            Assert.That(harness.LastMaterialisedTd, Is.Null);
        }

        [Test]
        public async Task CloseAndUpdateWithValidTdInvokesMaterialiseCallbackAndPersistsContent()
        {
            using var harness = new Harness();
            byte[] tdBytes = Encoding.UTF8.GetBytes("""
                {"name":"asset-001","base":"sim://example/asset/1"}
                """);

            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(tdBytes));
            ServiceResult result = harness.CloseAndUpdate(handle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(harness.MaterialiseCallCount, Is.EqualTo(1));
            Assert.That(harness.LastMaterialisedTd!.Name, Is.EqualTo("asset-001"));
            Assert.That(harness.LastMaterialisedTd.Base,
                Is.EqualTo("sim://example/asset/1"));
            Assert.That(harness.File.Size!.Value, Is.EqualTo((ulong)tdBytes.Length));

            // Subsequent Read returns the persisted TD bytes.
            byte[] downloaded = harness.Download();
            Assert.That(downloaded, Is.EqualTo(tdBytes));
            await Task.CompletedTask;
        }

        [Test]
        public void CloseAndUpdateWithMalformedJsonReturnsBadDecodingError()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(Encoding.UTF8.GetBytes("{not json")));
            ServiceResult result = harness.CloseAndUpdate(handle);

            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
            Assert.That(harness.MaterialiseCallCount, Is.EqualTo(0));
            // Bad TD must not become the new persisted content.
            Assert.That(harness.File.Size!.Value, Is.EqualTo(0ul));
        }

        [Test]
        public void CloseAndUpdateOnReadHandleReturnsBadInvalidState()
        {
            using var harness = new Harness();
            uint handle = 0;
            harness.Open(ModeRead, ref handle);
            ServiceResult result = harness.CloseAndUpdate(handle);

            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseAndUpdatePropagatesCallbackFailure()
        {
            using var harness = new Harness(
                materialise: (_, _) => new ValueTask<ServiceResult>(
                    (ServiceResult)StatusCodes.BadConfigurationError));
            uint handle = 0;
            harness.Open(ModeWriteErase, ref handle);
            harness.Write(handle, ByteString.From(Encoding.UTF8.GetBytes("""{"name":"x"}""")));
            ServiceResult result = harness.CloseAndUpdate(handle);

            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            // When the callback fails, the new bytes must not become persistent content.
            Assert.That(harness.File.Size!.Value, Is.EqualTo(0ul));
        }

        // ----------------------------------------------------------------
        // Open-count bookkeeping.
        // ----------------------------------------------------------------

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
            Assert.That(harness.File.OpenCount.Value, Is.EqualTo((ushort)0));
        }

        // ----------------------------------------------------------------
        // Test harness — owns the WoTAssetFileState and the file manager.
        // ----------------------------------------------------------------

        private sealed class Harness : IDisposable
        {
            private readonly NodeId _objectId;
            private readonly List<ThingDescription> _materialisedTds = new();
            private readonly Func<ThingDescription, CancellationToken, ValueTask<ServiceResult>> _materialiseDelegate;

            public Harness(
                int maxOpenHandles = 4,
                int maxThingDescriptionSize = 1024 * 1024,
                Func<ThingDescription, CancellationToken, ValueTask<ServiceResult>>? materialise = null)
            {
                Context = new SystemContext(null!)
                {
                    NamespaceUris = new NamespaceTable(),
                    EncodeableFactory = EncodeableFactory.Create()
                };
                Context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
                Context.NamespaceUris.GetIndexOrAppend("urn:test");

                _materialiseDelegate = materialise ?? DefaultMaterialise;

                File = Context.CreateInstanceOfWoTAssetFileType(
                    parent: null!,
                    browseName: new QualifiedName("WoTFile", 1));

                // The manager is internal so InternalsVisibleTo gives us access.
                Manager = new WotAssetFileManager(
                    File,
                    maxOpenHandles,
                    maxThingDescriptionSize,
                    _materialiseDelegate,
                    NullLogger.Instance);

                _objectId = File.NodeId;
            }

            public SystemContext Context { get; }

            public WoTAssetFileState File { get; }

            public WotAssetFileManager Manager { get; }

            public int MaterialiseCallCount { get; private set; }

            public ThingDescription? LastMaterialisedTd
                => _materialisedTds.Count == 0 ? null : _materialisedTds[^1];

            public ServiceResult Open(byte mode, ref uint fileHandle)
                => File.Open!.OnCall!.Invoke(Context, File.Open, _objectId, mode, ref fileHandle);

            public ServiceResult Close(uint fileHandle)
                => File.Close!.OnCall!.Invoke(Context, File.Close, _objectId, fileHandle);

            public ServiceResult Read(uint fileHandle, int length, ref ByteString data)
                => File.Read!.OnCall!.Invoke(Context, File.Read, _objectId, fileHandle, length, ref data);

            public ServiceResult Write(uint fileHandle, ByteString data)
                => File.Write!.OnCall!.Invoke(Context, File.Write, _objectId, fileHandle, data);

            public ServiceResult GetPosition(uint fileHandle, ref ulong position)
                => File.GetPosition!.OnCall!.Invoke(
                    Context, File.GetPosition, _objectId, fileHandle, ref position);

            public ServiceResult SetPosition(uint fileHandle, ulong position)
                => File.SetPosition!.OnCall!.Invoke(
                    Context, File.SetPosition, _objectId, fileHandle, position);

            public ServiceResult CloseAndUpdate(uint fileHandle)
                => File.CloseAndUpdate!.OnCall!.Invoke(
                    Context, File.CloseAndUpdate, _objectId, fileHandle);

            public void Upload(byte[] content)
            {
                uint handle = 0;
                Open(ModeWriteErase, ref handle);
                try
                {
                    if (content.Length > 0)
                    {
                        Assert.That(
                            ServiceResult.IsGood(Write(handle, ByteString.From(content))),
                            Is.True,
                            "Write inside Upload helper failed.");
                    }
                }
                finally
                {
                    Close(handle);
                }
                // Close discards pending writes, so seed persistent content explicitly.
                Manager.UpdatePersistedContent(content);
            }

            public byte[] Download()
            {
                uint handle = 0;
                Open(ModeRead, ref handle);
                try
                {
                    var buffer = new List<byte>();
                    while (true)
                    {
                        ByteString chunk = default;
                        Read(handle, 1024, ref chunk);
                        if (chunk.IsNull || chunk.Span.Length == 0)
                        {
                            break;
                        }
                        buffer.AddRange(chunk.Span.ToArray());
                        if (chunk.Span.Length < 1024)
                        {
                            break;
                        }
                    }
                    return [.. buffer];
                }
                finally
                {
                    Close(handle);
                }
            }

            public void Dispose() => Manager.Dispose();

            private ValueTask<ServiceResult> DefaultMaterialise(
                ThingDescription td,
                CancellationToken ct)
            {
                MaterialiseCallCount++;
                _materialisedTds.Add(td);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }
    }
}
