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
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelSendQueueTests
    {
        [Test]
        public void ControlFramesAreDequeuedBeforePayloadFrames()
        {
            object queue = CreateQueue(out _);
            Enqueue(queue, [1, 2, 3], DataChannelFrameFlags.MessageStart, 0);
            DataChannelFrame control = DataChannelFrame.Credit(7, 42, 100, 200);

            EnqueueControl(queue, control);

            Assert.That(TryDequeueControl(queue, out DataChannelFrame dequeuedControl), Is.True);
            Assert.That(TryPeekPayloadLength(queue, out int payloadLength), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(dequeuedControl.FrameType, Is.EqualTo(DataChannelFrameType.Credit));
                Assert.That(dequeuedControl.FrameSequenceNumber, Is.EqualTo(42u));
                Assert.That(payloadLength, Is.EqualTo(3));
            });
        }

        [Test]
        public void TryPeekPayloadLengthReportsWithoutConsuming()
        {
            object queue = CreateQueue(out _);
            Enqueue(queue, [10, 20, 30, 40], DataChannelFrameFlags.None, 0);

            Assert.That(TryPeekPayloadLength(queue, out int firstLength), Is.True);
            Assert.That(TryPeekPayloadLength(queue, out int secondLength), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(firstLength, Is.EqualTo(4));
                Assert.That(secondLength, Is.EqualTo(4));
                Assert.That(GetProperty<int>(queue, "PayloadCount"), Is.EqualTo(1));
            });
        }

        [Test]
        public void TryDequeuePayloadDoesNotApplyDeficitInsideQueue()
        {
            object queue = CreateQueue(out _);
            SetProperty(queue, "Deficit", 3L);
            Enqueue(queue, [1, 2, 3, 4], DataChannelFrameFlags.None, 0);

            Assert.That(TryDequeuePayload(queue, 9, out DataChannelFrame frame, out byte[]? buffer), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(frame.Payload.Length, Is.EqualTo(4));
                Assert.That(GetProperty<long>(queue, "Deficit"), Is.EqualTo(3L));
                Assert.That(buffer, Is.Not.Null);
            });

            ReleaseFrame(queue, buffer);
        }

        [Test]
        public void ExpireDroppableCoalescesContiguousExpiredRuns()
        {
            object queue = CreateQueue(out _);
            Enqueue(queue, [1], DataChannelFrameFlags.Droppable, 10);
            Enqueue(queue, [2], DataChannelFrameFlags.Droppable, 10);
            Enqueue(queue, [3], DataChannelFrameFlags.None, 0);
            Enqueue(queue, [4], DataChannelFrameFlags.Droppable, 10);
            Enqueue(queue, [5], DataChannelFrameFlags.Droppable, 10);

            var runs = new List<DataChannelGapRun>();
            int expired = ExpireDroppable(queue, 10, runs);

            Assert.Multiple(() =>
            {
                Assert.That(expired, Is.EqualTo(4));
                Assert.That(runs, Has.Count.EqualTo(2));
                Assert.That(runs[0].First, Is.EqualTo(1u));
                Assert.That(runs[0].Last, Is.EqualTo(2u));
                Assert.That(runs[1].First, Is.EqualTo(4u));
                Assert.That(runs[1].Last, Is.EqualTo(5u));
                Assert.That(GetProperty<int>(queue, "PayloadCount"), Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Dequeuing from an empty queue reports nothing rather than
        /// inventing a frame.
        /// </summary>
        /// <remarks>
        /// The scheduler asks every ready channel for work each round, so
        /// this is the ordinary answer for a channel whose queue has just
        /// drained, not an error path.
        /// </remarks>
        [Test]
        public void DequeuingFromAnEmptyQueueYieldsNothing()
        {
            object queue = CreateQueue(out _);

            Assert.That(
                TryDequeuePayload(queue, 9, out DataChannelFrame frame, out byte[]? buffer),
                Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(frame.Payload.Length, Is.Zero);
                Assert.That(buffer, Is.Null, "An empty queue handed out a buffer to release.");
            });
        }

        /// <summary>
        /// Expiry does no work when nothing has expired.
        /// </summary>
        /// <remarks>
        /// The scan runs on every scheduler round for every channel with a
        /// deadline, so the common case has to leave the queue untouched
        /// rather than rebuild it.
        /// </remarks>
        [Test]
        public void ExpiryLeavesTheQueueUntouchedWhenNothingHasExpired()
        {
            object queue = CreateQueue(out _);
            Enqueue(queue, [1], DataChannelFrameFlags.Droppable, 1000);
            Enqueue(queue, [2], DataChannelFrameFlags.Droppable, 1000);

            var runs = new List<DataChannelGapRun>();
            int expired = ExpireDroppable(queue, 10, runs);

            Assert.Multiple(() =>
            {
                Assert.That(expired, Is.Zero);
                Assert.That(runs, Is.Empty);
                Assert.That(GetProperty<int>(queue, "PayloadCount"), Is.EqualTo(2));
            });
        }

        [Test]
        public void TakeSequenceNumberWrapsOverExcludedZero()
        {
            object queue = CreateQueue(out _);
            SetPrivateField(queue, "m_nextSequenceNumber", uint.MaxValue - 1);

            uint first = TakeSequenceNumber(queue);
            uint second = TakeSequenceNumber(queue);
            uint third = TakeSequenceNumber(queue);
            uint fourth = TakeSequenceNumber(queue);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(uint.MaxValue - 1));
                Assert.That(second, Is.EqualTo(uint.MaxValue));
                Assert.That(third, Is.EqualTo(1u));
                Assert.That(fourth, Is.EqualTo(2u));
                Assert.That(new[] { first, second, third, fourth }, Does.Not.Contain(0u));
                Assert.That(GetProperty<uint>(queue, "NextSequenceNumber"), Is.EqualTo(3u));
            });
        }

        [Test]
        public void ReleaseFrameReturnsBufferForReuse()
        {
            object queue = CreateQueue(out TrackingBufferManager buffers);
            Enqueue(queue, [1, 2, 3, 4], DataChannelFrameFlags.None, 0);

            Assert.That(TryDequeuePayload(queue, 1, out _, out byte[]? firstBuffer), Is.True);
            ReleaseFrame(queue, firstBuffer);

            Enqueue(queue, [5, 6, 7, 8], DataChannelFrameFlags.None, 0);
            Assert.That(TryDequeuePayload(queue, 1, out _, out byte[]? secondBuffer), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(buffers.ReturnedCount, Is.EqualTo(1));
                Assert.That(secondBuffer, Is.SameAs(firstBuffer));
            });
        }

        [Test]
        public void ClearEmptiesQueuesAndReleasesPayloadBuffers()
        {
            object queue = CreateQueue(out TrackingBufferManager buffers);
            SetProperty(queue, "Deficit", 1024L);
            Enqueue(queue, [1, 2], DataChannelFrameFlags.None, 0);
            Enqueue(queue, [3, 4, 5], DataChannelFrameFlags.None, 0);
            EnqueueControl(queue, DataChannelFrame.End(1, 3));

            Clear(queue);

            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<bool>(queue, "HasPayload"), Is.False);
                Assert.That(GetProperty<bool>(queue, "HasControlFrames"), Is.False);
                Assert.That(GetProperty<int>(queue, "PayloadCount"), Is.Zero);
                Assert.That(GetProperty<long>(queue, "Deficit"), Is.Zero);
                Assert.That(buffers.ReturnedCount, Is.EqualTo(2));
            });
        }

        private static object CreateQueue(out TrackingBufferManager tracking)
        {
            tracking = new TrackingBufferManager();
            var bufferManager = new BufferManager(tracking);
            Type queueType = typeof(DataChannelFrame).Assembly.GetType(
                "Opc.Ua.Bindings.DataChannelSendQueue",
                throwOnError: true)!;
            return Activator.CreateInstance(queueType, bufferManager)!;
        }

        private static uint Enqueue(
            object queue,
            ReadOnlySpan<byte> payload,
            DataChannelFrameFlags flags,
            long deadline)
        {
            var enqueue = CreateDelegate<EnqueueDelegate>(queue, "Enqueue");
            return enqueue(payload, flags, deadline);
        }

        private static void EnqueueControl(object queue, in DataChannelFrame frame)
        {
            var enqueue = CreateDelegate<EnqueueControlDelegate>(queue, "EnqueueControl");
            enqueue(frame);
        }

        private static bool TryDequeueControl(object queue, out DataChannelFrame frame)
        {
            var dequeue = CreateDelegate<TryDequeueControlDelegate>(queue, "TryDequeueControl");
            return dequeue(out frame);
        }

        private static bool TryPeekPayloadLength(object queue, out int payloadLength)
        {
            var peek = CreateDelegate<TryPeekPayloadLengthDelegate>(queue, "TryPeekPayloadLength");
            return peek(out payloadLength);
        }

        private static bool TryDequeuePayload(
            object queue,
            uint channelId,
            out DataChannelFrame frame,
            out byte[]? buffer)
        {
            var dequeue = CreateDelegate<TryDequeuePayloadDelegate>(queue, "TryDequeuePayload");
            return dequeue(channelId, out frame, out buffer);
        }

        private static int ExpireDroppable(
            object queue,
            long nowTicks,
            List<DataChannelGapRun> runs)
        {
            var expire = CreateDelegate<ExpireDroppableDelegate>(queue, "ExpireDroppable");
            return expire(nowTicks, runs);
        }

        private static void ReleaseFrame(object queue, byte[]? buffer)
        {
            var release = CreateDelegate<ReleaseFrameDelegate>(queue, "ReleaseFrame");
            release(buffer);
        }

        private static uint TakeSequenceNumber(object queue)
        {
            var take = CreateDelegate<TakeSequenceNumberDelegate>(queue, "TakeSequenceNumber");
            return take();
        }

        private static void Clear(object queue)
        {
            var clear = CreateDelegate<ClearDelegate>(queue, "Clear");
            clear();
        }

        private static T GetProperty<T>(object queue, string name)
        {
            object? value = queue.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(queue);
            return (T)value!;
        }

        private static void SetProperty<T>(object queue, string name, T value)
        {
            queue.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(queue, value);
        }

        private static void SetPrivateField<T>(object queue, string name, T value)
        {
            queue.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(queue, value);
        }

        private static T CreateDelegate<T>(object queue, string methodName)
            where T : Delegate
        {
            MethodInfo method = queue.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
#if NET8_0_OR_GREATER
            return method.CreateDelegate<T>(queue);
#else
            return (T)method.CreateDelegate(typeof(T), queue);
#endif
        }

        private delegate uint EnqueueDelegate(
            ReadOnlySpan<byte> payload,
            DataChannelFrameFlags flags,
            long deadline);

        private delegate void EnqueueControlDelegate(in DataChannelFrame frame);

        private delegate bool TryDequeueControlDelegate(out DataChannelFrame frame);

        private delegate bool TryPeekPayloadLengthDelegate(out int payloadLength);

        private delegate bool TryDequeuePayloadDelegate(
            uint channelId,
            out DataChannelFrame frame,
            out byte[]? buffer);

        private delegate int ExpireDroppableDelegate(long nowTicks, List<DataChannelGapRun> runs);

        private delegate void ReleaseFrameDelegate(byte[]? buffer);

        private delegate uint TakeSequenceNumberDelegate();

        private delegate void ClearDelegate();

        public sealed class TrackingBufferManager : IBufferManager
        {
            private readonly Queue<byte[]> m_available = new();

            public string Name => "tracking";

            public int MaxSuggestedBufferSize => 65536;

            public int ReturnedCount { get; private set; }

            public int GetSuggestedBufferSize(int size)
            {
                return size;
            }

            public int GetExpectedBufferSize(int size)
            {
                return size;
            }

            public byte[] TakeBuffer(int size, string owner)
            {
                if (m_available.Count > 0)
                {
                    byte[] buffer = m_available.Dequeue();
                    if (buffer.Length >= size)
                    {
                        return buffer;
                    }
                }

                return new byte[size];
            }

            public byte[] TakeBuffer(int size, string owner, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return TakeBuffer(size, owner);
            }

            public void TransferBuffer(byte[]? buffer, string owner)
            {
            }

            public void Lock(byte[] buffer)
            {
            }

            public void Unlock(byte[] buffer)
            {
            }

            public void ReturnBuffer(byte[]? buffer, string owner)
            {
                if (buffer == null)
                {
                    return;
                }

                ReturnedCount++;
                m_available.Enqueue(buffer);
            }
        }
    }
}
