/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class SessionHistoryContinuationPersistenceTests
    {
        [Test]
        public async Task SavePersistsBeforePortableContinuationCanRestoreAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore();
            var codec = new RecordingCodec();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());

            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(store.Stored, Has.Count.EqualTo(1));
            Assert.That(store.Stored[0].Id, Is.EqualTo(continuation.Id));
            Assert.That(store.Stored[0].OwnerSessionId, Is.EqualTo(sessionId));
            IHistoryContinuationPoint? restored = await points.RestoreHistoryAsync(
                ByteString.From(continuation.Id.ToByteArray()),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(restored, Is.SameAs(continuation));
            Assert.That(store.TakeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task NonPortableCodecResultKeepsLocalContinuationAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore();
            var codec = new RecordingCodec
            {
                ReturnNullFromEncode = true
            };
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());

            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);
            IHistoryContinuationPoint? restored = points.RestoreHistory(
                ByteString.From(continuation.Id.ToByteArray()));

            Assert.That(restored, Is.SameAs(continuation));
            Assert.That(store.Stored, Is.Empty);
        }

        [Test]
        public void PersistenceFailureRemovesAndDisposesLocalState()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore
            {
                StoreException = new ServiceResultException(
                    StatusCodes.BadUnexpectedError)
            };
            var codec = new RecordingCodec();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());

            Assert.That(
                async () => await points.SaveHistoryAsync(
                    continuation,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(continuation.Disposed, Is.True);
            Assert.That(
                points.RestoreHistory(
                    ByteString.From(continuation.Id.ToByteArray())),
                Is.Null);
        }

        [Test]
        public async Task MirroredContinuationTransfersAtomicTakeToLocalOwnerAsync()
        {
            NodeId localSessionId = new(2000);
            NodeId ownerSessionId = new(1000);
            var id = Guid.NewGuid();
            var store = new RecordingStore();
            store.Stored.Add(new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = ownerSessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([1])
            });
            var mirroredPoint = new TrackingPoint(id);
            var codec = new RecordingCodec
            {
                DecodeResult = mirroredPoint
            };
            SessionContinuationPoints points = CreatePoints(
                localSessionId,
                store,
                codec);

            await points.LoadMirroredAsync(
                ownerSessionId,
                CancellationToken.None).ConfigureAwait(false);
            IHistoryContinuationPoint? restored = await points.RestoreHistoryAsync(
                ByteString.From(id.ToByteArray()),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(restored, Is.SameAs(mirroredPoint));
            Assert.That(store.LastTakeOwner, Is.EqualTo(localSessionId));
            Assert.That(store.TakeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task OriginalSessionCleanupCannotDeleteTransferredContinuationAsync()
        {
            NodeId ownerSessionId = new(1000);
            NodeId localSessionId = new(2000);
            var store = new RecordingStore();
            var ownerCodec = new RecordingCodec();
            SessionContinuationPoints owner = CreatePoints(
                ownerSessionId,
                store,
                ownerCodec);
            var original = new TrackingPoint(Guid.NewGuid());
            await owner.SaveHistoryAsync(
                original,
                CancellationToken.None).ConfigureAwait(false);

            var transferred = new TrackingPoint(original.Id);
            var localCodec = new RecordingCodec
            {
                DecodeResult = transferred
            };
            SessionContinuationPoints local = CreatePoints(
                localSessionId,
                store,
                localCodec);
            await local.LoadMirroredAsync(
                ownerSessionId,
                CancellationToken.None).ConfigureAwait(false);

            owner.Clear();
            IHistoryContinuationPoint? restored = await local.RestoreHistoryAsync(
                ByteString.From(original.Id.ToByteArray()),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(restored, Is.SameAs(transferred));
            Assert.That(store.LastTakeOwner, Is.EqualTo(localSessionId));
        }

        [Test]
        public async Task MirroredEnvelopeForAnotherOwnerIsIgnoredAsync()
        {
            NodeId localSessionId = new(2000);
            NodeId requestedOwner = new(1000);
            var id = Guid.NewGuid();
            var store = new RecordingStore();
            store.Stored.Add(new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = new NodeId(3000),
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([1])
            });
            var codec = new RecordingCodec
            {
                DecodeResult = new TrackingPoint(id)
            };
            SessionContinuationPoints points = CreatePoints(
                localSessionId,
                store,
                codec);

            await points.LoadMirroredAsync(
                requestedOwner,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(codec.DecodeCount, Is.Zero);
            Assert.That(
                await points.RestoreHistoryAsync(
                    ByteString.From(id.ToByteArray()),
                    CancellationToken.None).ConfigureAwait(false),
                Is.Null);
        }

        [Test]
        public async Task TransientTakeFailurePreservesLocalContinuationAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore();
            var codec = new RecordingCodec();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());
            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);
            store.TakeException = new ServiceResultException(
                StatusCodes.BadUnexpectedError);

            Assert.That(
                async () => await points.RestoreHistoryAsync(
                    ByteString.From(continuation.Id.ToByteArray()),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(continuation.Disposed, Is.False);

            store.TakeException = null;
            Assert.That(
                await points.RestoreHistoryAsync(
                    ByteString.From(continuation.Id.ToByteArray()),
                    CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(continuation));
        }

        [Test]
        public async Task ConcurrentRestoreCannotClaimPortablePointTwiceAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore
            {
                TakeGate = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var codec = new RecordingCodec();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());
            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);

            Task<IHistoryContinuationPoint?> first = points.RestoreHistoryAsync(
                ByteString.From(continuation.Id.ToByteArray()),
                CancellationToken.None).AsTask();
            await store.TakeStarted.Task.ConfigureAwait(false);
            IHistoryContinuationPoint? second = await points.RestoreHistoryAsync(
                ByteString.From(continuation.Id.ToByteArray()),
                CancellationToken.None).ConfigureAwait(false);
            store.TakeGate.SetResult(true);
            IHistoryContinuationPoint? firstResult =
                await first.ConfigureAwait(false);

            Assert.That(firstResult, Is.SameAs(continuation));
            Assert.That(second, Is.Null);
            Assert.That(store.TakeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PendingPersistenceCannotBeEvictedAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore
            {
                StoreGate = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var codec = new RecordingCodec();
            SessionContinuationPoints points = new(
                () => sessionId,
                maxBrowse: 1,
                maxHistory: 1,
                store: null,
                historyStore: store,
                historyCodec: codec,
                namespaceUris: new NamespaceTable());
            var first = new TrackingPoint(Guid.NewGuid());
            Task firstSave = points.SaveHistoryAsync(
                first,
                CancellationToken.None).AsTask();
            await store.StoreStarted.Task.ConfigureAwait(false);
            var second = new TrackingPoint(Guid.NewGuid());

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await points.SaveHistoryAsync(
                        second,
                        CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(second.Disposed, Is.True);
            store.StoreGate.SetResult(true);
            await firstSave.ConfigureAwait(false);
            Assert.That(
                await points.RestoreHistoryAsync(
                    ByteString.From(first.Id.ToByteArray()),
                    CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(first));
        }

        [Test]
        public async Task SynchronousReleaseRemovesPortableContinuationAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore();
            var codec = new RecordingCodec();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                codec);
            var continuation = new TrackingPoint(Guid.NewGuid());
            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);

            bool released = points.ReleaseHistory(
                ByteString.From(continuation.Id.ToByteArray()));

            Assert.That(released, Is.True);
            Assert.That(continuation.Disposed, Is.True);
            Assert.That(store.Stored, Is.Empty);
        }

        [Test]
        public void StoreCommitThenThrowSchedulesDurableCleanup()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore
            {
                StoreThenThrow = true
            };
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                new RecordingCodec());
            var continuation = new TrackingPoint(Guid.NewGuid());

            Assert.That(
                async () => await points.SaveHistoryAsync(
                    continuation,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(store.Stored, Is.Empty);
            Assert.That(continuation.Disposed, Is.True);
        }

        [Test]
        public async Task CleanupFailureDoesNotAbortSessionClearAsync()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore
            {
                ScheduleRemoveException = new InvalidOperationException(
                    "cleanup unavailable")
            };
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                new RecordingCodec());
            var continuation = new TrackingPoint(Guid.NewGuid());
            await points.SaveHistoryAsync(
                continuation,
                CancellationToken.None).ConfigureAwait(false);

            Assert.DoesNotThrow(points.Clear);
            Assert.That(continuation.Disposed, Is.True);
        }

        [Test]
        public void ClosedSessionRejectsAndDisposesLateHistoryContinuation()
        {
            NodeId sessionId = new(1000);
            var store = new RecordingStore();
            SessionContinuationPoints points = CreatePoints(
                sessionId,
                store,
                new RecordingCodec());
            points.Clear();
            var continuation = new TrackingPoint(Guid.NewGuid());

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await points.SaveHistoryAsync(
                        continuation,
                        CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(continuation.Disposed, Is.True);
            Assert.That(store.Stored, Is.Empty);
        }

        private static SessionContinuationPoints CreatePoints(
            NodeId sessionId,
            IHistoryContinuationPointStore store,
            IHistoryContinuationPointCodec codec)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:history-continuation");
            return new SessionContinuationPoints(
                () => sessionId,
                maxBrowse: 4,
                maxHistory: 4,
                store: null,
                historyStore: store,
                historyCodec: codec,
                namespaceUris: namespaceUris);
        }

        private sealed class RecordingStore : IHistoryContinuationPointStore
        {
            public List<HistoryContinuationPointEnvelope> Stored { get; } = [];

            public Exception? StoreException { get; init; }

            public bool StoreThenThrow { get; init; }

            public Exception? TakeException { get; set; }

            public Exception? ScheduleRemoveException { get; init; }

            public TaskCompletionSource<bool>? TakeGate { get; init; }

            public TaskCompletionSource<bool> TakeStarted { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool>? StoreGate { get; init; }

            public TaskCompletionSource<bool> StoreStarted { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public int TakeCount { get; private set; }

            public NodeId LastTakeOwner { get; private set; } = NodeId.Null;

            public async ValueTask StoreAsync(
                HistoryContinuationPointEnvelope envelope,
                CancellationToken cancellationToken = default)
            {
                StoreStarted.TrySetResult(true);
                if (StoreGate != null)
                {
                    await StoreGate.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                if (StoreException != null)
                {
                    throw StoreException;
                }
                Stored.Add(envelope);
                if (StoreThenThrow)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "Stored before simulated transport failure.");
                }
            }

            public async ValueTask<bool> TryTakeAsync(
                NodeId ownerSessionId,
                Guid id,
                CancellationToken cancellationToken = default)
            {
                TakeCount++;
                LastTakeOwner = ownerSessionId;
                TakeStarted.TrySetResult(true);
                if (TakeGate != null)
                {
                    await TakeGate.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                if (TakeException != null)
                {
                    throw TakeException;
                }
                int index = Stored.FindIndex(
                    envelope => envelope.OwnerSessionId == ownerSessionId &&
                        envelope.Id == id);
                if (index < 0)
                {
                    return false;
                }
                Stored.RemoveAt(index);
                return true;
            }

            public void ScheduleRemove(NodeId ownerSessionId, Guid id)
            {
                if (ScheduleRemoveException != null)
                {
                    throw ScheduleRemoveException;
                }
                int index = Stored.FindIndex(
                    envelope => envelope.OwnerSessionId == ownerSessionId &&
                        envelope.Id == id);
                if (index >= 0)
                {
                    Stored.RemoveAt(index);
                }
            }

            public ValueTask<ArrayOf<HistoryContinuationPointEnvelope>> LoadAsync(
                NodeId ownerSessionId,
                CancellationToken cancellationToken = default)
            {
                var matches = new List<HistoryContinuationPointEnvelope>();
                for (int i = 0; i < Stored.Count; i++)
                {
                    if (Stored[i].OwnerSessionId == ownerSessionId)
                    {
                        matches.Add(Stored[i]);
                    }
                }
                return new ValueTask<
                    ArrayOf<HistoryContinuationPointEnvelope>>(
                        matches.ToArrayOf());
            }
        }

        private sealed class RecordingCodec : IHistoryContinuationPointCodec
        {
            public IHistoryContinuationPoint? DecodeResult { get; init; }

            public bool ReturnNullFromEncode { get; init; }

            public int DecodeCount { get; private set; }

            public ValueTask<HistoryContinuationPointEnvelope?> EncodeAsync(
                NodeId ownerSessionId,
                IHistoryContinuationPoint continuationPoint,
                CancellationToken cancellationToken)
            {
                if (ReturnNullFromEncode)
                {
                    return new ValueTask<
                        HistoryContinuationPointEnvelope?>(
                        result: null);
                }
                return new ValueTask<
                    HistoryContinuationPointEnvelope?>(
                    new HistoryContinuationPointEnvelope
                    {
                        Id = continuationPoint.Id,
                        OwnerSessionId = ownerSessionId,
                        CodecId = "test",
                        CodecVersion = 1,
                        Payload = ByteString.From([1])
                    });
            }

            public ValueTask<IHistoryContinuationPoint?> DecodeAsync(
                HistoryContinuationPointEnvelope envelope,
                CancellationToken cancellationToken)
            {
                DecodeCount++;
                return new ValueTask<IHistoryContinuationPoint?>(
                    DecodeResult);
            }
        }

        private sealed class TrackingPoint : IHistoryContinuationPoint
        {
            public TrackingPoint(Guid id)
            {
                Id = id;
            }

            public Guid Id { get; }

            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
