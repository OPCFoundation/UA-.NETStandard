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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Validates <see cref="UdpMessageRepeater"/> retransmission semantics
    /// as defined by Part 14 §6.4.1 — UDP-only publishers may repeat each
    /// NetworkMessage <c>MessageRepeatCount</c> times with
    /// <c>MessageRepeatDelay</c> spacing to mitigate IP-layer loss.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("6.4.1")]
    public sealed class UdpMessageRepeaterTests
    {
        [Test]
        public async Task ZeroRepeats_SendsOnce()
        {
            var repeater = new UdpMessageRepeater(0, TimeSpan.FromMilliseconds(10), TimeProvider.System);
            int count = 0;

            await repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                return default;
            }).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(repeater.RepeatCount, Is.Zero);
        }

        [Test]
        public async Task NegativeCount_CoercedToZero_SendsOnce()
        {
            var repeater = new UdpMessageRepeater(-5, TimeSpan.FromMilliseconds(10), TimeProvider.System);
            int count = 0;

            await repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                return default;
            }).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(repeater.RepeatCount, Is.Zero);
        }

        [Test]
        [CancelAfter(kTestTimeoutMilliseconds)]
        public async Task ThreeRepeats_SendsFourTimes_FakeTimerAdvanced()
        {
            TimeSpan repeatDelay = TimeSpan.FromMilliseconds(100);
            using var fake = new TrackingFakeTimeProvider(repeatDelay);
            var repeater = new UdpMessageRepeater(3, repeatDelay, fake);
            int count = 0;

            Task sendTask = repeater.SendWithRepeatsAsync(_ =>
            {
                Interlocked.Increment(ref count);
                return default;
            }).AsTask();

            for (int i = 0; i < 3; i++)
            {
                await WaitForCompletionAsync(
                    fake.WaitForTimerCreatedAsync(),
                    $"Timed out waiting for repeat timer {i + 1}.")
                    .ConfigureAwait(false);
                fake.Advance(repeatDelay);
            }

            await WaitForCompletionAsync(
                sendTask,
                "Timed out waiting for the repeated sends to complete.")
                .ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public async Task ZeroDelay_StillRepeatsRequestedCount()
        {
            var repeater = new UdpMessageRepeater(2, TimeSpan.Zero, TimeProvider.System);
            int count = 0;

            await repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                return default;
            }).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(repeater.RepeatDelay, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public async Task NegativeDelay_CoercedToZero()
        {
            var repeater = new UdpMessageRepeater(
                1,
                TimeSpan.FromMilliseconds(-10),
                TimeProvider.System);

            int count = 0;
            await repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                return default;
            }).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(repeater.RepeatDelay, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void NullDelegate_Throws()
        {
            var repeater = new UdpMessageRepeater(0, TimeSpan.Zero, TimeProvider.System);

            Assert.That(
                async () => await repeater.SendWithRepeatsAsync(null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NullTimeProvider_Throws()
        {
            Assert.That(
                () => new UdpMessageRepeater(0, TimeSpan.Zero, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task CancellationBeforeFirstSend_DoesNotInvokeDelegate()
        {
            var repeater = new UdpMessageRepeater(3, TimeSpan.FromMilliseconds(1), TimeProvider.System);
            int count = 0;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await repeater.SendWithRepeatsAsync(_ =>
                {
                    count++;
                    return default;
                }, cts.Token).ConfigureAwait(false);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(count, Is.Zero);
        }

        [Test]
        [CancelAfter(kTestTimeoutMilliseconds)]
        public async Task CancellationBetweenRepeats_StopsLoop()
        {
            TimeSpan repeatDelay = TimeSpan.FromMilliseconds(50);
            using var fake = new TrackingFakeTimeProvider(repeatDelay);
            var repeater = new UdpMessageRepeater(5, repeatDelay, fake);
            int count = 0;
            using var cts = new CancellationTokenSource();

            Task sendTask = repeater.SendWithRepeatsAsync(_ =>
            {
                int sendNumber = Interlocked.Increment(ref count);
                if (sendNumber == 2)
                {
                    cts.Cancel();
                }
                return default;
            }, cts.Token).AsTask();

            await WaitForCompletionAsync(
                fake.WaitForTimerCreatedAsync(),
                "Timed out waiting for the first repeat timer.")
                .ConfigureAwait(false);
            fake.Advance(repeatDelay);

            try
            {
                await WaitForCompletionAsync(
                    sendTask,
                    "Timed out waiting for cancellation to stop the repeater.")
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public async Task CancellationWithZeroDelay_StopsLoop()
        {
            var repeater = new UdpMessageRepeater(10, TimeSpan.Zero, TimeProvider.System);
            int count = 0;
            using var cts = new CancellationTokenSource();

            try
            {
                await repeater.SendWithRepeatsAsync(_ =>
                {
                    count++;
                    if (count == 3)
                    {
                        cts.Cancel();
                    }
                    return default;
                }, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(count, Is.EqualTo(3));
        }

        private static async Task WaitForCompletionAsync(Task completion, string timeoutMessage)
        {
            Task completed = await Task.WhenAny(
                completion,
                Task.Delay(s_completionTimeout)).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(completion), timeoutMessage);
            await completion.ConfigureAwait(false);
        }

        private sealed class TrackingFakeTimeProvider : FakeTimeProvider, IDisposable
        {
            public TrackingFakeTimeProvider(TimeSpan repeatDelay)
            {
                m_repeatDelay = repeatDelay;
            }

            public Task WaitForTimerCreatedAsync()
            {
                return m_timerCreated.WaitAsync();
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = base.CreateTimer(callback, state, dueTime, period);
                if (dueTime == m_repeatDelay && period == Timeout.InfiniteTimeSpan)
                {
                    m_timerCreated.Release();
                }
                return timer;
            }

            public void Dispose()
            {
                m_timerCreated.Dispose();
            }

            private readonly TimeSpan m_repeatDelay;
            private readonly SemaphoreSlim m_timerCreated = new(0);
        }

        private const int kTestTimeoutMilliseconds = 10000;
        private static readonly TimeSpan s_completionTimeout =
            TimeSpan.FromMilliseconds(kTestTimeoutMilliseconds / 2);
    }
}
