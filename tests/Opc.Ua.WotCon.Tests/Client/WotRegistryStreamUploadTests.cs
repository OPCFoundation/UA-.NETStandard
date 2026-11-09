/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Pending-version uploads must acquire the actual target before consuming a one-shot stream.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable]
    public sealed class WotRegistryStreamUploadTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingStreamUploadClaimsBeforeConsumptionAndInterleavesWritesAsync(bool legacyOverload)
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            NodeId create = CreateMethod(mock);
            using var source = new NonSeekableUploadSource([1, 2, 3, 4, 5]);
            source.BeforeRead = readIndex =>
            {
                Assert.That(Requests(mock, create), Has.Length.EqualTo(1), "Claim must precede even the first read.");
                Assert.That(Requests(mock, Ua.MethodIds.FileType_Write), Has.Length.EqualTo(readIndex),
                    "Each previous chunk must be sent before reading another; no full-source replay buffer.");
            };

            WotRegistryUploadResult result = legacyOverload
                ? await resource.UploadNewVersionAndGetResultAsync(source, chunkSize: 4).ConfigureAwait(false)
                : await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 5, chunkSize: 4)
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.VersionId, Is.EqualTo("1"));
                Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
                Assert.That(resource.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
                Assert.That(resource.VersionId, Is.EqualTo("1"));
                Assert.That(resource.HasContent, Is.True);
                Assert.That(mock.ContentFor(result.ResourceNodeId).Span.ToArray(),
                    Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
                Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [1, 2], [3, 4], [5] }));
                Assert.That(source.BytesRead, Is.EqualTo(5));
                Assert.That(source.ReadCalls, Is.EqualTo(4));
                Assert.That(source.IsDisposed, Is.False);
                Assert.That(mock.Capture.Select(r => r.MethodId), Is.EqualTo(new[]
                {
                    create, Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Write,
                    Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close
                }));
                Assert.That(Requests(mock, create)[0].InputArguments,
                    Is.EqualTo(new[] { Variant.From("sensor"), Variant.From("1"), Variant.From(true) }));
            });
            AssertFileCalls(mock, VersionNode(mock, "1"), 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingStreamUploadWithoutAtomicClaimAllocatesExactNewIdentityAsync(bool empty)
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync(supportsAtomicClaim: false).ConfigureAwait(false);
            using var source = new NonSeekableUploadSource(empty ? [] : [11, 12, 13]);
            source.BeforeRead = _ => Assert.That(Requests(mock, CreateMethod(mock)), Has.Length.EqualTo(2),
                "Both the refused claim and new-version allocation precede source consumption.");

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, maxBytes: 3, chunkSize: 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.VersionId, Is.EqualTo("2"));
                Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "2")));
                Assert.That(resource.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
                Assert.That(resource.VersionId, Is.EqualTo("1"));
                Assert.That(resource.HasContent, Is.Null,
                    "The returned new version is not the original proxy's version.");
                Assert.That(Requests(mock, CreateMethod(mock)).Select(r => r.InputArguments[1]),
                    Is.EqualTo(new[] { Variant.From("1"), Variant.From(string.Empty) }));
                Assert.That(mock.ContentFor(VersionNode(mock, "1")).Length, Is.Zero);
                Assert.That(mock.ContentFor(VersionNode(mock, "2")).Span.ToArray(),
                    Is.EqualTo(empty ? Array.Empty<byte>() : new byte[] { 11, 12, 13 }));
                Assert.That(WrittenChunks(mock),
                    Is.EqualTo(empty ? Array.Empty<byte[]>() : new byte[][] { [11, 12], [13] }));
                Assert.That(source.IsDisposed, Is.False);
                Assert.That(Requests(mock, Ua.MethodIds.FileType_Close), Has.Length.EqualTo(1));
            });
            AssertFileCalls(mock, VersionNode(mock, "2"), 1);
        }

        [Test]
        public async Task EmptyPendingStreamUploadRemainsFillableInSameVersionAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var empty = new NonSeekableUploadSource([]);

            WotRegistryUploadResult first = await resource.UploadNewVersionAndGetResultAsync(
                empty, maxBytes: 1, chunkSize: 4).ConfigureAwait(false);

            Assert.That(first.VersionId, Is.EqualTo("1"));
            Assert.That(first.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
            Assert.That(resource.HasContent, Is.False);
            Assert.That(Requests(mock, Ua.MethodIds.FileType_Write), Is.Empty);
            Assert.That(Requests(mock, Ua.MethodIds.FileType_Close), Has.Length.EqualTo(1));
            Assert.That(mock.ContentFor(VersionNode(mock, "1")).Length, Is.Zero);

            using var content = new NonSeekableUploadSource([21, 22]);
            WotRegistryUploadResult second = await resource.UploadNewVersionAndGetResultAsync(
                content, maxBytes: 2, chunkSize: 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(second.VersionId, Is.EqualTo("1"));
                Assert.That(second.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
                Assert.That(resource.HasContent, Is.True);
                Assert.That(Requests(mock, CreateMethod(mock)).Select(r => r.InputArguments[1]),
                    Is.EqualTo(new[] { Variant.From("1"), Variant.From("1") }));
                Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [21, 22] }));
                Assert.That(mock.ContentFor(VersionNode(mock, "1")).Span.ToArray(), Is.EqualTo(new byte[] { 21, 22 }));
                Assert.That(Requests(mock, Ua.MethodIds.FileType_Close), Has.Length.EqualTo(2));
                Assert.That(empty.IsDisposed || content.IsDisposed, Is.False);
            });
        }

        [Test]
        public async Task EmptyFallbackUploadClearsOldPendingClaimForFollowingUploadAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync(supportsAtomicClaim: false).ConfigureAwait(false);
            using var empty = new NonSeekableUploadSource([]);
            WotRegistryUploadResult emptyResult = await resource.UploadNewVersionAndGetResultAsync(
                empty, maxBytes: 1).ConfigureAwait(false);
            Assert.That(emptyResult.VersionId, Is.EqualTo("2"));
            mock.Capture.Clear();
            using var source = new NonSeekableUploadSource([31]);

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, maxBytes: 1).ConfigureAwait(false);

            Assert.That(result.VersionId, Is.EqualTo("3"));
            Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "3")));
            Assert.That(Requests(mock, CreateMethod(mock)).Select(r => r.InputArguments[1]),
                Is.EqualTo(new[] { Variant.From(string.Empty) }));
            Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [31] }));
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task PendingStreamUploadRefusalConsumesNothingAndPreservesStatusAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            mock.FailNextCallOn[CreateMethod(mock)] = StatusCodes.BadUserAccessDenied;
            using var source = new NonSeekableUploadSource([41, 42]);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 2).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(source.ReadCalls, Is.Zero);
            Assert.That(source.BytesRead, Is.Zero);
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(resource.HasContent, Is.False);
            Assert.That(mock.Capture.Select(r => r.MethodId), Is.EqualTo(new[] { CreateMethod(mock) }));
        }

        [Test]
        public async Task PendingStreamUploadEnforcesCumulativeLimitAndClosesPartialWriteAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var source = new NonSeekableUploadSource([51, 52, 53, 54]);

            await Assert.ThrowsAsync<IOException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 3, chunkSize: 4)
                    .ConfigureAwait(false));

            Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [51, 52] }));
            Assert.That(mock.Capture.Select(r => r.MethodId),
                Is.EqualTo(new[] { CreateMethod(mock), Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close }));
            Assert.That(source.ReadCalls, Is.EqualTo(2));
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(mock.ContentFor(VersionNode(mock, "1")).Span.ToArray(), Is.EqualTo(new byte[] { 51, 52 }),
                "Closing a partially written remote handle is cleanup, not a promise of rollback.");
            AssertFileCalls(mock, VersionNode(mock, "1"), 1);
        }

        [Test]
        public async Task PendingStreamUploadSeekableLimitIsCheckedBeforeClaimAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var source = new MemoryStream([0, 61, 62, 63]) { Position = 1 };

            await Assert.ThrowsAsync<IOException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 2, chunkSize: 4)
                    .ConfigureAwait(false));

            Assert.That(source.Position, Is.EqualTo(1));
            Assert.That(source.CanRead, Is.True);
            Assert.That(mock.Capture, Is.Empty);
            Assert.That(resource.HasContent, Is.False);
        }

        [TestCase(1)]
        [TestCase(1048576)]
        public async Task PendingStreamUploadHonorsSourcePositionAndChunkSizeBoundariesAsync(int chunkSize)
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var source = new MemoryStream([0, 61, 62, 63]) { Position = 1 };

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, maxBytes: 3, chunkSize: chunkSize).ConfigureAwait(false);

            Assert.That(result.VersionId, Is.EqualTo("1"));
            Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
            Assert.That(source.Position, Is.EqualTo(4));
            Assert.That(source.CanRead, Is.True);
            Assert.That(WrittenChunks(mock), Is.EqualTo(chunkSize == 1
                ? new byte[][] { [61], [62], [63] } : new byte[][] { [61, 62, 63] }));
            Assert.That(resource.HasContent, Is.True);
            AssertFileCalls(mock, VersionNode(mock, "1"), 1);
        }

        [Test]
        public async Task PendingStreamUploadLegacyOverloadCapsOversizedChunkAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var source = new NonSeekableUploadSource([64, 65]);

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, chunkSize: 1_048_577).ConfigureAwait(false);

            Assert.That(result.VersionId, Is.EqualTo("1"));
            Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
            Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [64, 65] }));
            Assert.That(source.IsDisposed, Is.False);
            AssertFileCalls(mock, VersionNode(mock, "1"), 1);
        }

        [TestCase(0L, 4, "maxBytes")]
        [TestCase(-1L, 4, "maxBytes")]
        [TestCase(8L, 0, "chunkSize")]
        [TestCase(8L, -1, "chunkSize")]
        [TestCase(8L, 1048577, "chunkSize")]
        public async Task PendingStreamUploadValidatesLimitsBeforeClaimAsync(long limit, int chunk, string parameter)
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var source = new NonSeekableUploadSource([71]);

            ArgumentOutOfRangeException exception = (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: limit, chunkSize: chunk)
                    .ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
            Assert.That(source.ReadCalls, Is.Zero);
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(mock.Capture, Is.Empty);
        }

        [Test]
        public async Task PendingStreamUploadRejectsNullAndUnreadableSourcesBeforeClaimAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using Stream unreadable = new Mock<Stream>().Object;

            ArgumentNullException nullException = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync((Stream)null!, maxBytes: 8).ConfigureAwait(false)))!;
            ArgumentException capabilityException = (await Assert.ThrowsAsync<ArgumentException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(unreadable, maxBytes: 8).ConfigureAwait(false)))!;

            Assert.That(nullException.ParamName, Is.EqualTo("content"));
            Assert.That(capabilityException.ParamName, Is.EqualTo("content"));
            Assert.That(mock.Capture, Is.Empty);
        }

        [TestCase("node")]
        [TestCase("version")]
        [TestCase("handle")]
        public async Task PendingStreamUploadRejectsMismatchedClaimBeforeConsumingAndClosesUnexpectedHandleAsync(
            string mismatch)
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            NodeId unexpectedNode = mismatch == "node"
                ? new NodeId("unexpected", mock.WotConNs) : resource.ResourceNodeId;
            string unexpectedVersion = mismatch == "version" ? "different" : "1";
            uint unexpectedHandle = mismatch == "handle" ? 0u : 77u;
            var assignedNode = new NodeId("server-selected-version", mock.WotConNs);
            int createCalls = 0;
            mock.OverrideMethod(CreateMethod(mock), _ =>
            {
                createCalls++;
                return createCalls == 1
                    ? [Variant.From(unexpectedNode), Variant.From(unexpectedVersion), Variant.From(unexpectedHandle)]
                    : [Variant.From(assignedNode), Variant.From("server-v9"), Variant.From(88u)];
            });
            mock.OverrideMethod(Ua.MethodIds.FileType_Write, _ => []);
            mock.OverrideMethod(Ua.MethodIds.FileType_Close, _ => []);
            using var source = new NonSeekableUploadSource([81, 82]);
            source.BeforeRead = _ =>
            {
                Assert.That(createCalls, Is.EqualTo(2));
                Assert.That(Requests(mock, Ua.MethodIds.FileType_Close),
                    Has.Length.EqualTo(unexpectedHandle == 0 ? 0 : 1));
            };

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, maxBytes: 2, chunkSize: 4).ConfigureAwait(false);

            Assert.That(result.ResourceNodeId, Is.EqualTo(assignedNode));
            Assert.That(result.VersionId, Is.EqualTo("server-v9"));
            Assert.That(resource.ResourceNodeId, Is.EqualTo(VersionNode(mock, "1")));
            Assert.That(resource.HasContent, Is.Null);
            Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [81, 82] }));
            Assert.That(Requests(mock, Ua.MethodIds.FileType_Write)[0].ObjectId, Is.EqualTo(assignedNode));
            Assert.That(Requests(mock, Ua.MethodIds.FileType_Write)[0].InputArguments[0],
                Is.EqualTo(Variant.From(88u)));
            CallMethodRequest[] closes = Requests(mock, Ua.MethodIds.FileType_Close);
            Assert.That(closes, Has.Length.EqualTo(unexpectedHandle == 0 ? 1 : 2));
            if (unexpectedHandle != 0)
            {
                Assert.That(closes[0].ObjectId, Is.EqualTo(unexpectedNode));
                Assert.That(closes[0].InputArguments[0], Is.EqualTo(Variant.From(77u)));
            }
            Assert.That(closes[closes.Length - 1].ObjectId, Is.EqualTo(assignedNode));
            Assert.That(closes[closes.Length - 1].InputArguments[0], Is.EqualTo(Variant.From(88u)));
            Assert.That(source.IsDisposed, Is.False);
        }

        [Test]
        public async Task FilledResourceStreamUploadAllocatesOneNewVersionWithoutOverwritingOriginalAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var initial = new NonSeekableUploadSource([91]);
            await resource.UploadNewVersionAndGetResultAsync(initial, maxBytes: 1).ConfigureAwait(false);
            mock.Capture.Clear();
            using var source = new NonSeekableUploadSource([92, 93, 94]);

            WotRegistryUploadResult result = await resource.UploadNewVersionAndGetResultAsync(
                source, maxBytes: 3, chunkSize: 4).ConfigureAwait(false);

            Assert.That(result.VersionId, Is.EqualTo("2"));
            Assert.That(result.ResourceNodeId, Is.EqualTo(VersionNode(mock, "2")));
            Assert.That(Requests(mock, CreateMethod(mock)).Select(r => r.InputArguments[1]),
                Is.EqualTo(new[] { Variant.From(string.Empty) }));
            Assert.That(WrittenChunks(mock), Is.EqualTo(new byte[][] { [92, 93], [94] }));
            Assert.That(mock.ContentFor(VersionNode(mock, "1")).Span.ToArray(), Is.EqualTo(new byte[] { 91 }));
            Assert.That(mock.ContentFor(VersionNode(mock, "2")).Span.ToArray(), Is.EqualTo(new byte[] { 92, 93, 94 }));
            Assert.That(source.IsDisposed, Is.False);
            AssertFileCalls(mock, VersionNode(mock, "2"), 2);
        }

        [Test]
        public async Task NewVersionStreamUploadWithoutRequestedHandleFailsBeforeConsumptionAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            using var initial = new NonSeekableUploadSource([101]);
            await resource.UploadNewVersionAndGetResultAsync(initial, maxBytes: 1).ConfigureAwait(false);
            mock.Capture.Clear();
            mock.OverrideMethod(CreateMethod(mock),
                _ => [Variant.From(VersionNode(mock, "2")), Variant.From("2"), Variant.From(0u)]);
            using var source = new NonSeekableUploadSource([102, 103]);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 2).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(source.ReadCalls, Is.Zero);
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(mock.Capture.Select(r => r.MethodId), Is.EqualTo(new[] { CreateMethod(mock) }));
        }

        [Test]
        public async Task PendingStreamUploadRetainsPrimarySourceFailureWhenCleanupAlsoFailsAsync()
        {
            (WotRegistrySessionMock mock, WotRegistryResourceClient resource) =
                await CreatePendingAsync().ConfigureAwait(false);
            var primary = new IOException("One-shot source failed.");
            mock.FailNextCallOn[Ua.MethodIds.FileType_Close] = StatusCodes.BadSessionClosed;
            using var source = new NonSeekableUploadSource([111, 112])
            {
                BeforeRead = _ => throw primary
            };

            IOException exception = (await Assert.ThrowsAsync<IOException>(async () =>
                await resource.UploadNewVersionAndGetResultAsync(source, maxBytes: 2).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(mock.Capture.Select(r => r.MethodId),
                Is.EqualTo(new[] { CreateMethod(mock), Ua.MethodIds.FileType_Close }));
            Assert.That(source.IsDisposed, Is.False);
            Assert.That(source.BytesRead, Is.Zero);
        }

        private static async Task<(WotRegistrySessionMock Mock, WotRegistryResourceClient Resource)> CreatePendingAsync(
            bool supportsAtomicClaim = true)
        {
            var mock = new WotRegistrySessionMock { SupportsAtomicContentlessFill = supportsAtomicClaim };
            WotRegistryClient registry = await WotRegistryClient.ForServerAsync(
                mock.Session, NUnitTelemetryContext.Create()).ConfigureAwait(false);
            WotRegistryGroupClient group = await registry.CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group.GetOrCreateResourceAsync("sensor", "1")
                .ConfigureAwait(false);
            mock.Capture.Clear();
            return (mock, resource);
        }

        private static NodeId CreateMethod(WotRegistrySessionMock mock)
        {
            return mock.ResolveMethodId(XRegistry.MethodIds.GroupType_CreateResource);
        }

        private static NodeId VersionNode(WotRegistrySessionMock mock, string version)
        {
            return new NodeId(
                "WoTRegistry/groups/thingdescriptions/resources/sensor/versions/" + version, mock.WotConNs);
        }

        private static CallMethodRequest[] Requests(WotRegistrySessionMock mock, NodeId method)
        {
            return mock.Capture.Where(r => r.MethodId == method).ToArray();
        }

        private static byte[][] WrittenChunks(WotRegistrySessionMock mock)
        {
            return Requests(mock, Ua.MethodIds.FileType_Write).Select(request =>
            {
                Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                return bytes.Span.ToArray();
            }).ToArray();
        }

        private static void AssertFileCalls(WotRegistrySessionMock mock, NodeId expectedNode, uint expectedHandle)
        {
            CallMethodRequest[] writesAndClose = mock.Capture.Where(r =>
                r.MethodId == Ua.MethodIds.FileType_Write || r.MethodId == Ua.MethodIds.FileType_Close).ToArray();
            Assert.That(writesAndClose, Is.Not.Empty);
            foreach (CallMethodRequest request in writesAndClose)
            {
                Assert.That(request.ObjectId, Is.EqualTo(expectedNode));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(expectedHandle)));
            }
        }
    }
}
