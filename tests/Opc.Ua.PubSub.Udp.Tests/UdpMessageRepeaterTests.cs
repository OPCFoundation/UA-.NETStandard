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
        public async Task ThreeRepeats_SendsFourTimes_FakeTimerAdvanced()
        {
            const int repeatCount = 3;
            var repeatDelay = TimeSpan.FromMilliseconds(100);
            var fake = new ObservableFakeTimeProvider();
            var repeater = new UdpMessageRepeater(repeatCount, repeatDelay, fake);
            int count = 0;

            ValueTask sendTask = repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                return default;
            });

            for (int timerNumber = 1; timerNumber <= repeatCount; timerNumber++)
            {
                await fake.WaitForTimerCreatedAsync(timerNumber)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                fake.Advance(repeatDelay);
            }

            await sendTask.AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(repeatCount + 1));
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
        public async Task CancellationBetweenRepeats_StopsLoop()
        {
            var fake = new FakeTimeProvider();
            var repeater = new UdpMessageRepeater(5, TimeSpan.FromMilliseconds(50), fake);
            int count = 0;
            using var cts = new CancellationTokenSource();

            ValueTask sendTask = repeater.SendWithRepeatsAsync(_ =>
            {
                count++;
                if (count == 2)
                {
                    cts.Cancel();
                }
                return default;
            }, cts.Token);

            for (int i = 0; i < 6 && !sendTask.IsCompleted; i++)
            {
                fake.Advance(TimeSpan.FromMilliseconds(50));
                await Task.Yield();
            }

            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(count, Is.LessThan(6));
            Assert.That(count, Is.GreaterThanOrEqualTo(2));
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

        private sealed class ObservableFakeTimeProvider : FakeTimeProvider
        {
            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = base.CreateTimer(callback, state, dueTime, period);
                int timerNumber = Interlocked.Increment(ref m_timerCount);
                if (timerNumber == 1)
                {
                    m_firstTimerCreated.TrySetResult(true);
                }
                else if (timerNumber == 2)
                {
                    m_secondTimerCreated.TrySetResult(true);
                }
                else if (timerNumber == 3)
                {
                    m_thirdTimerCreated.TrySetResult(true);
                }
                return timer;
            }

            public Task<bool> WaitForTimerCreatedAsync(int timerNumber)
            {
                return timerNumber switch
                {
                    1 => m_firstTimerCreated.Task,
                    2 => m_secondTimerCreated.Task,
                    3 => m_thirdTimerCreated.Task,
                    _ => throw new ArgumentOutOfRangeException(nameof(timerNumber))
                };
            }

            private readonly TaskCompletionSource<bool> m_firstTimerCreated = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_secondTimerCreated = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_thirdTimerCreated = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_timerCount;
        }
    }
}
