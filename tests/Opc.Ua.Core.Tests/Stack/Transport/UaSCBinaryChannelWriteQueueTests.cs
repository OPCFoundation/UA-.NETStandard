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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// The queue that sends a channel's chunks one after the other, in the order
    /// they were issued, off the caller's stack.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [Parallelizable]
    public sealed class UaSCBinaryChannelWriteQueueTests
    {
        private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();

        /// <summary>
        /// Writes queued while the first one is still sending are sent in order,
        /// never two at a time, and each is reported once.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task QueuedWritesAreSentOneAtATimeInOrderAsync()
        {
            var transport = new RecordingTransport();
            using var channel = new WriteProbeChannel(m_telemetry, transport);
            const int count = 200;

            channel.Write(0, useCollection: true);
            await transport.FirstSendStarted.Task.ConfigureAwait(false);
            for (int ii = 1; ii < count; ii++)
            {
                channel.Write(ii, useCollection: ii % 2 == 0);
            }
            transport.ReleaseFirstSend.SetResult(true);

            await channel.WaitForCompletionsAsync(count).ConfigureAwait(false);

            Assert.That(transport.Sent, Is.EqualTo(Range(count)));
            Assert.That(transport.MaxConcurrentSends, Is.EqualTo(1));
            Assert.That(channel.Completed, Is.EquivalentTo(Range(count)));
            Assert.That(channel.HasPendingWrites, Is.False);
        }

        /// <summary>
        /// Issuing a write returns before the send starts: the caller may hold the
        /// channel gate that completion reporting enters.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task IssuingAWriteDoesNotSendOnTheCallersStackAsync()
        {
            var transport = new RecordingTransport();
            using var channel = new WriteProbeChannel(m_telemetry, transport);

            int callerThread = Environment.CurrentManagedThreadId;
            channel.Write(0, useCollection: false);
            Assert.That(transport.Sent, Is.Empty, "the first send blocks until released");

            await transport.FirstSendStarted.Task.ConfigureAwait(false);
            Assert.That(transport.FirstSendThread, Is.Not.EqualTo(callerThread));
            transport.ReleaseFirstSend.SetResult(true);
            await channel.WaitForCompletionsAsync(1).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes issued from many threads are all sent and reported, one at a
        /// time, and the queue drains completely.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ConcurrentWritersAreAllSentAsync()
        {
            var transport = new RecordingTransport { BlockFirstSend = false };
            using var channel = new WriteProbeChannel(m_telemetry, transport);
            const int writers = 8;
            const int perWriter = 250;

            await Task.WhenAll(Enumerate(writers, w => Task.Run(() =>
            {
                for (int ii = 0; ii < perWriter; ii++)
                {
                    channel.Write((w * perWriter) + ii, useCollection: ii % 3 == 0);
                }
            }))).ConfigureAwait(false);

            await channel.WaitForCompletionsAsync(writers * perWriter).ConfigureAwait(false);

            Assert.That(transport.Sent, Is.EquivalentTo(Range(writers * perWriter)));
            Assert.That(transport.MaxConcurrentSends, Is.EqualTo(1));
            Assert.That(channel.HasPendingWrites, Is.False);
        }

        /// <summary>
        /// A failed send is reported with its error and does not stop the writes
        /// queued behind it.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task AFailedSendIsReportedAndLaterWritesStillGoOutAsync()
        {
            var transport = new RecordingTransport { BlockFirstSend = false, FailIndex = 3 };
            using var channel = new WriteProbeChannel(m_telemetry, transport);

            for (int ii = 0; ii < 6; ii++)
            {
                channel.Write(ii, useCollection: true);
            }
            await channel.WaitForCompletionsAsync(6).ConfigureAwait(false);

            Assert.That(transport.Sent, Is.EqualTo(s_sentAroundTheFailure));
            Assert.That(channel.Failed, Is.EqualTo(s_failed));
            Assert.That(channel.HasPendingWrites, Is.False);
        }

        private static readonly int[] s_sentAroundTheFailure = [0, 1, 2, 4, 5];
        private static readonly int[] s_failed = [3];

        private static int[] Range(int count)
        {
            int[] values = new int[count];
            for (int ii = 0; ii < count; ii++)
            {
                values[ii] = ii;
            }
            return values;
        }

        private static List<Task> Enumerate(int count, Func<int, Task> start)
        {
            var tasks = new List<Task>(count);
            for (int ii = 0; ii < count; ii++)
            {
                tasks.Add(start(ii));
            }
            return tasks;
        }

        /// <summary>
        /// A channel that issues writes of a 4-byte index and records how they
        /// are reported.
        /// </summary>
        private sealed class WriteProbeChannel : UaSCUaBinaryChannel
        {
            public WriteProbeChannel(ITelemetryContext telemetry, IUaSCByteTransport transport)
                : base(
                    "writes",
                    new BufferManager("writes", TcpMessageLimits.DefaultMaxBufferSize, telemetry),
                    new ChannelQuotas(ServiceMessageContext.Create(telemetry)),
                    serverCertificate: null,
                    endpoints: new List<EndpointDescription>(),
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: telemetry)
            {
                Transport = transport;
            }

            public ConcurrentQueue<int> Completed { get; } = new();

            public ConcurrentQueue<int> Failed { get; } = new();

            public void Write(int index, bool useCollection)
            {
                byte[] buffer = BufferManager.TakeBuffer(sizeof(int), "WriteProbeChannel");
                BitConverter.GetBytes(index).CopyTo(buffer, 0);
                if (useCollection)
                {
                    BeginWriteMessage(
                        new BufferCollection { new ArraySegment<byte>(buffer, 0, sizeof(int)) },
                        index);
                }
                else
                {
                    BeginWriteMessage(new ArraySegment<byte>(buffer, 0, sizeof(int)), index);
                }
            }

            public async Task WaitForCompletionsAsync(int count)
            {
                while (Completed.Count + Failed.Count < count)
                {
                    await Task.Delay(5).ConfigureAwait(false);
                }
            }

            protected override void HandleWriteComplete(
                BufferCollection? buffers,
                object? state,
                int bytesWritten,
                ServiceResult result)
            {
                base.HandleWriteComplete(buffers, state, bytesWritten, result);
                (ServiceResult.IsGood(result) ? Completed : Failed).Enqueue((int)state!);
            }
        }

        /// <summary>
        /// Records the index carried by each sent chunk and how many sends overlap.
        /// The first send can be held until released.
        /// </summary>
        private sealed class RecordingTransport : IUaSCByteTransport
        {
            public bool BlockFirstSend { get; set; } = true;

            public int FailIndex { get; set; } = -1;

            public TaskCompletionSource<bool> FirstSendStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseFirstSend { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int FirstSendThread { get; private set; }

            public List<int> Sent
            {
                get
                {
                    lock (m_sentLock)
                    {
                        return new List<int>(m_sent);
                    }
                }
            }

            public int MaxConcurrentSends => Volatile.Read(ref m_maxConcurrent);

            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => TransportChannelFeatures.None;

            public string Implementation => "UA-TEST";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return SendAsync(BitConverter.ToInt32(chunk.ToArray(), 0));
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                ArraySegment<byte> segment = buffers[0];
                return SendAsync(BitConverter.ToInt32(segment.Array!, segment.Offset));
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return new ValueTask<ArraySegment<byte>>(Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => default(ArraySegment<byte>), TaskScheduler.Default));
            }

            public void Close()
            {
            }

            private async ValueTask SendAsync(int index)
            {
                int concurrent = Interlocked.Increment(ref m_concurrent);
                int max;
                while (concurrent > (max = Volatile.Read(ref m_maxConcurrent)) &&
                    Interlocked.CompareExchange(ref m_maxConcurrent, concurrent, max) != max)
                {
                }
                try
                {
                    if (Interlocked.Exchange(ref m_started, 1) == 0)
                    {
                        FirstSendThread = Environment.CurrentManagedThreadId;
                        FirstSendStarted.SetResult(true);
                        if (BlockFirstSend)
                        {
                            await ReleaseFirstSend.Task.ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // let a send complete asynchronously now and then.
                        if (index % 7 == 0)
                        {
                            await Task.Yield();
                        }
                    }

                    if (index == FailIndex)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConnectionClosed, "send failed");
                    }

                    lock (m_sentLock)
                    {
                        m_sent.Add(index);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref m_concurrent);
                }
            }

            private readonly Lock m_sentLock = new();
            private readonly List<int> m_sent = [];
            private int m_concurrent;
            private int m_maxConcurrent;
            private int m_started;
        }
    }
}
