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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end mock-based tests for <see cref="UaFileInfo.RefreshAsync"/>
    /// (the seven well-known FileType properties).
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientMetadataTests
    {
        [TestCase(false, false, 3u)]
        [TestCase(false, true, 3u)]
        [TestCase(true, false, 3u)]
        [TestCase(true, true, 3u)]
        [TestCase(false, false, null)]
        [TestCase(true, true, 0u)]
        public async Task PathWritesRefreshMetadataAndHonorChunkLimitAsync(
            bool createFile,
            bool writeText,
            uint? maxByteStringLength)
        {
            var harness = FileSystemSessionHarness.Create();
            var properties = new FileProperties
            {
                Writable = true,
                UserWritable = true,
                MaxByteStringLength = maxByteStringLength
            };
            properties.Realize();
            var fileId = new NodeId(8000u);
            if (!createFile)
            {
                harness.RegisterFile(harness.Root, new QualifiedName("data.txt"), fileId, properties);
            }
            var written = new List<byte>();
            var chunks = new List<int>();
            int creates = 0;
            int closes = 0;
            harness.CallHandler = request =>
            {
                switch (GetMethodId(request))
                {
                    case Methods.FileDirectoryType_CreateFile:
                        creates++;
                        Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From("data.txt")));
                        harness.RegisterFile(harness.Root, new QualifiedName("data.txt"), fileId, properties);
                        return new CallMethodResult { OutputArguments = [Variant.From(fileId), Variant.From(0u)] };
                    case Methods.FileType_Open:
                        Assert.That(request.ObjectId, Is.EqualTo(fileId));
                        return new CallMethodResult { OutputArguments = [Variant.From(7u)] };
                    case Methods.FileType_Write:
                        Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(7u)));
                        Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                        if (maxByteStringLength is > 0 && bytes.Length > maxByteStringLength)
                        {
                            return new CallMethodResult { StatusCode = StatusCodes.BadInvalidArgument };
                        }
                        chunks.Add(bytes.Length);
                        written.AddRange(bytes.ToArray()!);
                        return new CallMethodResult();
                    case Methods.FileType_Close:
                        closes++;
                        return new CallMethodResult();
                    default:
                        throw new InvalidOperationException("Unexpected file method.");
                }
            };
            var client = new FileSystemClient(
                harness.Session, harness.Root, new FileSystemClientOptions { ChunkSize = 8 });
            byte[] payload = [97, 98, 99, 100, 101, 102, 103];

            if (writeText)
            {
                await client.WriteAllTextAsync("/data.txt", "abcdefg", Encoding.UTF8).ConfigureAwait(false);
            }
            else
            {
                await client.WriteAllBytesAsync("/data.txt", payload).ConfigureAwait(false);
            }

            int[] expectedChunks = maxByteStringLength is > 0 ? [3, 3, 1] : [7];
            Assert.That(written, Is.EqualTo(payload));
            Assert.That(chunks, Is.EqualTo(expectedChunks));
            Assert.That(creates, Is.EqualTo(createFile ? 1 : 0));
            Assert.That(closes, Is.EqualTo(1));
        }

        [Test]
        public async Task RefreshAsyncPopulatesMandatoryPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var props = new FileProperties
            {
                Size = 12_345UL,
                Writable = true,
                UserWritable = false,
                OpenCount = 3
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("data.bin"), properties: props);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.bin").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            Assert.That(file.Size, Is.EqualTo(12_345UL));
            Assert.That(file.Writable, Is.True);
            Assert.That(file.UserWritable, Is.False);
            Assert.That(file.OpenCount, Is.EqualTo(3));
            Assert.That(file.MimeType, Is.Null);
            Assert.That(file.MaxByteStringLength, Is.Null);
            Assert.That(file.LastModifiedTime, Is.Null);
        }

        [Test]
        public async Task RefreshAsyncPopulatesOptionalPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var expectedModified = new DateTime(2024, 5, 12, 13, 0, 0, DateTimeKind.Utc);
            var props = new FileProperties
            {
                Size = 0UL,
                Writable = true,
                UserWritable = true,
                OpenCount = 0,
                MimeType = "application/json",
                MaxByteStringLength = 64_000u,
                LastModifiedTime = expectedModified
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("data.json"), properties: props);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.json").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            Assert.That(file.MimeType, Is.EqualTo("application/json"));
            Assert.That(file.MaxByteStringLength, Is.EqualTo(64_000u));
            Assert.That(file.LastModifiedTime, Is.EqualTo(expectedModified));
        }

        [Test]
        public async Task RefreshAsyncToleratesMissingOptionalPropertiesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            // No properties realised — the harness will return BadNoMatch
            // for every property lookup.
            harness.RegisterFile(harness.Root, new QualifiedName("data.bin"),
                properties: new FileProperties());
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("/data.bin").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            // Defaults for non-resolved mandatory properties.
            Assert.That(file.Size, Is.Zero);
            Assert.That(file.Writable, Is.False);
            Assert.That(file.UserWritable, Is.False);
            Assert.That(file.OpenCount, Is.Zero);
            // Optional properties remain null.
            Assert.That(file.MimeType, Is.Null);
            Assert.That(file.MaxByteStringLength, Is.Null);
            Assert.That(file.LastModifiedTime, Is.Null);
        }

        [Test]
        public async Task OpenAsyncRejectsEmptyModeAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("data.bin"));
            var client = new FileSystemClient(harness.Session, harness.Root);
            UaFileInfo file = await client.GetFileAsync("/data.bin").ConfigureAwait(false);

            Assert.That(
                async () => await file.OpenAsync(0).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task OpenAppendAsyncUsesServerPositionAndClosesHandleAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var props = new FileProperties
            {
                Size = 12UL,
                MaxByteStringLength = 4u
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("log.bin"), properties: props);
            var closedHandles = new List<uint>();
            harness.CallHandler = req =>
            {
                uint methodId = GetMethodId(req);
                if (methodId == Methods.FileType_Open)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(77u)]
                    };
                }
                if (methodId == Methods.FileType_GetPosition)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(20UL)]
                    };
                }
                if (methodId == Methods.FileType_Close)
                {
                    req.InputArguments[0].TryGetValue(out uint handle);
                    closedHandles.Add(handle);
                    return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] };
                }
                return new CallMethodResult { StatusCode = StatusCodes.BadNotSupported, OutputArguments = [] };
            };
            var client = new FileSystemClient(harness.Session, harness.Root);
            UaFileInfo file = await client.GetFileAsync("/log.bin").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            await using (UaFileStream stream = await file.OpenAppendAsync().ConfigureAwait(false))
            {
                Assert.That(stream.Position, Is.EqualTo(20));
                Assert.That(stream.Length, Is.EqualTo(20));
            }

            Assert.That(closedHandles, Has.Count.EqualTo(1));
            Assert.That(closedHandles[0], Is.EqualTo(77u));
        }

        [Test]
        public async Task ReadAllTextAndWriteAllTextUseFileTypeMethodsAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var props = new FileProperties
            {
                Size = 5UL,
                Writable = true
            };
            props.Realize();
            harness.RegisterFile(harness.Root, new QualifiedName("note.txt"), properties: props);
            var reads = new Queue<byte[]>(new[] { new byte[] { 72, 101, 108, 108, 111 }, [] });
            byte[] written = [];
            harness.CallHandler = req =>
            {
                uint methodId = GetMethodId(req);
                if (methodId == Methods.FileType_Open)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(11u)]
                    };
                }
                if (methodId == Methods.FileType_Read)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(reads.Dequeue().ToByteString())]
                    };
                }
                if (methodId == Methods.FileType_Write)
                {
                    req.InputArguments[1].TryGetValue(out ByteString payload);
                    written = payload.ToArray() ?? [];
                    return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] };
                }
                if (methodId == Methods.FileType_Close)
                {
                    return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] };
                }
                return new CallMethodResult { StatusCode = StatusCodes.BadNotSupported, OutputArguments = [] };
            };
            var client = new FileSystemClient(harness.Session, harness.Root);
            UaFileInfo file = await client.GetFileAsync("/note.txt").ConfigureAwait(false);
            await file.RefreshAsync().ConfigureAwait(false);

            string text = await file.ReadAllTextAsync().ConfigureAwait(false);
            await file.WriteAllTextAsync("Bye").ConfigureAwait(false);

            Assert.That(text, Is.EqualTo("Hello"));
            Assert.That(written, Is.EqualTo(new byte[] { 66, 121, 101 }));
        }

        private static uint GetMethodId(CallMethodRequest request)
        {
            return request.MethodId.TryGetValue(out uint methodId) ? methodId : 0;
        }
    }
}
