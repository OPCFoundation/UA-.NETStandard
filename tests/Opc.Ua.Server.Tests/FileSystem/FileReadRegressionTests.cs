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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileReadRegressionTests
    {
        [TestCase(-1)]
        [TestCase(0)]
        public async Task FileReadRequiresPositiveLengthWithoutIoAsync(int length)
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            FileObjectState file = CreateFile(manager);
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            (ServiceResult result, List<Variant> output) = await CallAsync(
                file.Read!, manager.SystemContext, file.NodeId, [handle, length]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(output, Is.Empty);
                Assert.That(stream.ReadCalls, Is.Zero);
                Assert.That(stream.Position, Is.Zero);
            });
        }

        [TestCase(1, 1)]
        [TestCase(7, 7)]
        [TestCase(8, 8)]
        [TestCase(9, 8)]
        public async Task FileReadHonorsEffectiveLimitBoundaryAsync(int requested, int expected)
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            FileObjectState file = CreateFile(manager);
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            ByteString data = await ReadAsync(file, manager.SystemContext, handle, requested).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(data.ToArray(), Is.EqualTo(kContents.AsSpan(0, expected).ToArray()));
                Assert.That(stream.RequestedCount, Is.EqualTo(expected));
                Assert.That(stream.Position, Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task FileReadLargeRequestedLengthUsesBoundedBufferAsync()
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            FileObjectState file = CreateFile(manager);
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            ByteString first = await ReadAsync(file, manager.SystemContext, handle, 9).ConfigureAwait(false);
            Assert.That(first.Length, Is.EqualTo(8), "Establish the allocation bound before testing Int32.MaxValue.");
            Assert.That(stream.RequestedCount, Is.EqualTo(8));

            stream.Position = 0;
            ByteString large = await ReadAsync(file, manager.SystemContext, handle, int.MaxValue).ConfigureAwait(false);
            Assert.That(large.ToArray(), Is.EqualTo(kContents.AsSpan(0, 8).ToArray()));
            Assert.That(stream.RequestedCount, Is.EqualTo(8));
            Assert.That(stream.Position, Is.EqualTo(8));
        }

        [Test]
        public async Task FileReadAtEndReturnsEmptyAndAdvancesOnlyByReturnedBytesAsync()
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            FileObjectState file = CreateFile(manager);
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);
            stream.Position = kContents.Length - 2;

            ByteString tail = await ReadAsync(file, manager.SystemContext, handle, 8).ConfigureAwait(false);
            ByteString eof = await ReadAsync(file, manager.SystemContext, handle, 8).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(tail.ToArray(), Is.EqualTo(new byte[] { 10, 11 }));
                Assert.That(eof.IsEmpty, Is.True);
                Assert.That(stream.Position, Is.EqualTo(kContents.Length));
            });
        }

        [Test]
        public async Task BoundFileAdvertisesAndEnforcesServerReadLimitAsync()
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            var root = new FileDirectoryState(null)
            {
                NodeId = new NodeId("BoundFiles", manager.NamespaceIndex),
                BrowseName = new QualifiedName("BoundFiles", manager.NamespaceIndex)
            };
            var binder = new FileDirectoryBinder();
            await using IFileDirectoryBinding binding = await binder.BindAsync(
                root, manager.Provider, manager.SystemContext).ConfigureAwait(false);
            var file = (FileState)root.FindChild(
                manager.SystemContext, new QualifiedName("data.bin", manager.NamespaceIndex))!;
            Assert.That(file, Is.Not.Null);
            Assert.That(file.MaxByteStringLength, Is.Not.Null);
            Assert.That(file.MaxByteStringLength!.Value, Is.EqualTo(8u));
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            ByteString data = await ReadAsync(file, manager.SystemContext, handle, 9).ConfigureAwait(false);

            Assert.That(data.ToArray(), Is.EqualTo(kContents.AsSpan(0, 8).ToArray()));
            Assert.That(stream.RequestedCount, Is.EqualTo(8));
        }

        [Test]
        public async Task FileSpecificReadLimitCanOnlyNarrowServerLimitAsync(
            [Values(4u, 8u, 16u)] uint fileLimit)
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream);
            FileObjectState file = CreateFile(manager);
            file.MaxByteStringLength = PropertyState<uint>.With<VariantBuilder>(file);
            file.MaxByteStringLength.Value = fileLimit;
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            ByteString data = await ReadAsync(file, manager.SystemContext, handle, 9).ConfigureAwait(false);
            int expected = fileLimit == 4 ? 4 : 8;

            Assert.That(data.ToArray(), Is.EqualTo(kContents.AsSpan(0, expected).ToArray()));
            Assert.That(stream.RequestedCount, Is.EqualTo(expected));
        }

        [Test]
        public async Task FileReadAccountsForEncodedResponseMessageBudgetAsync()
        {
            using var stream = new ObservedReadStream();
            using FileSystemNodeManager manager = CreateManager(stream, byteLimit: 1024, messageLimit: 64);
            FileObjectState file = CreateFile(manager);
            Assert.That(file.MaxByteStringLength, Is.Not.Null);
            Assert.That(file.MaxByteStringLength!.Value, Is.InRange(1u, 63u));
            uint handle = await OpenAsync(file, manager.SystemContext).ConfigureAwait(false);

            ByteString data = await ReadAsync(file, manager.SystemContext, handle, 1024).ConfigureAwait(false);
            var response = new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new CallMethodResult { OutputArguments = [data] }]
            };
            byte[] encoded = BinaryEncoder.EncodeMessage(response, manager.SystemContext.Server.MessageContext);

            Assert.That(encoded, Has.Length.LessThanOrEqualTo(64));
            Assert.That(stream.RequestedCount, Is.LessThanOrEqualTo((int)file.MaxByteStringLength.Value));
        }

        internal static async ValueTask<(ServiceResult Result, List<Variant> Output)> CallAsync(
            MethodState method,
            ISystemContext context,
            NodeId objectId,
            ArrayOf<Variant> input,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await method.CallAsync(
                context, objectId, input, errors, output, cancellationToken).ConfigureAwait(false);
            Assert.That(errors.TrueForAll(ServiceResult.IsGood), Is.True);
            return (result, output);
        }

        internal static async ValueTask<uint> OpenAsync(FileState file, ISystemContext context, byte mode = 1)
        {
            (ServiceResult result, List<Variant> output) = await CallAsync(
                file.Open!, context, file.NodeId, [mode]).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].TryGetValue(out uint handle), Is.True);
            return handle;
        }

        private static async ValueTask<ByteString> ReadAsync(
            FileState file, ISystemContext context, uint handle, int length)
        {
            (ServiceResult result, List<Variant> output) = await CallAsync(
                file.Read!, context, file.NodeId, [handle, length]).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].TryGetValue(out ByteString data), Is.True);
            return data;
        }

        private static FileSystemNodeManager CreateManager(
            Stream stream, int byteLimit = 8, int messageLimit = 4096)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out _);
            server.SetupGet(s => s.Telemetry).Returns(telemetry);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.MaxByteStringLength = byteLimit;
            messageContext.MaxMessageSize = messageLimit;
            server.SetupGet(s => s.MessageContext).Returns(messageContext);
            var provider = new Mock<IFileSystemProvider>();
            provider.SetupGet(p => p.MountName).Returns("ReadRegression");
            provider.SetupGet(p => p.IsWritable).Returns(true);
            provider.Setup(p => p.OpenReadAsync("data.bin", It.IsAny<CancellationToken>())).ReturnsAsync(stream);
            provider.Setup(p => p.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) => EnumerateAsync(ct));
            var manager = new FileSystemNodeManager(server.Object, new ApplicationConfiguration(), provider.Object);
            manager.SystemContext.SessionId = new NodeId("read-regression-session", 0);
            return manager;
        }

        private static FileObjectState CreateFile(FileSystemNodeManager manager)
        {
            return new FileObjectState(manager.SystemContext,
                FileSystemNodeId.BuildFile("data.bin", manager.NamespaceIndex), "data.bin", "data.bin");
        }

        private static async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileSystemEntry("data.bin", "data.bin", false, kContents.Length, true,
                DateTime.MinValue, "application/octet-stream");
        }

        private static readonly byte[] kContents = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

        private sealed class ObservedReadStream : MemoryStream
        {
            public ObservedReadStream()
                : base(kContents, writable: false)
            {
            }

            public int RequestedCount { get; private set; }

            public int ReadCalls { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                RequestedCount = count;
                ReadCalls++;
                return base.Read(buffer, offset, count);
            }
        }
    }
}
