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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PollingWotSubscription"/> transient-fault
    /// recovery and disposal semantics.
    /// </summary>
    [TestFixture]
    public sealed class PollingWotSubscriptionTests
    {
        private sealed class ThrowingReconnectPolicy : IChannelReconnectPolicy
        {
            public Task Faulted => m_faulted.Task;

            public TimeSpan GetDelay(int attempt)
            {
                m_faulted.TrySetResult();
                throw new InvalidOperationException("retry policy fault");
            }

            private readonly TaskCompletionSource m_faulted = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static WotCompiledForm Form()
        {
            return new WotCompiledForm(
                        new WotBindingIdentity("test", "1.0", "urn:test"),
                        WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                        WoTBindingCapabilityEnum.ObserveProperty, "observeproperty",
                        new WotEndpointDescriptor("test", "h", 1, "test://h"),
                        new WotAddressingDescriptor("t"),
                        new WotOperationDescriptor(WoTBindingCapabilityEnum.ObserveProperty, "observeproperty", "GET"),
                        new WotPayloadDescriptor("application/json", "json"),
                        [], isExecutable: true);
        }

        [Test]
        public async Task ConsecutiveUnhealthyPollsBackOffAndAHealthyPollResets()
        {
            var timestamps = new ConcurrentQueue<DateTime>();
            var backedOff = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;

            // A retry policy with a clearly-longer-than-interval delay makes the backoff
            // observable without making the test slow or timing-fragile.
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(300),
                MaxDelay = TimeSpan.FromMilliseconds(300)
            };

            var subscription = new PollingWotSubscription(
                Form(),
                _ =>
                {
                    timestamps.Enqueue(DateTime.UtcNow);
                    if (Interlocked.Increment(ref calls) >= 3)
                    {
                        backedOff.TrySetResult(true);
                    }
                    // Report unhealthy without throwing: this is how a binding that maps a
                    // failure onto a bad StatusCode reports it.
                    return new ValueTask<bool>(false);
                },
                TimeSpan.FromMilliseconds(10),
                retryPolicy: policy);

            await using (subscription)
            {
                Assert.That(
                    await Task.WhenAny(backedOff.Task, Task.Delay(10000)).ConfigureAwait(false),
                    Is.SameAs(backedOff.Task),
                    "The poll loop must keep running while the source is unhealthy.");

                Assert.That(subscription.ConsecutiveFailures, Is.GreaterThan(0),
                    "An unhealthy poll must be counted so the retry policy can back off.");

                DateTime[] taken = [.. timestamps];
                TimeSpan gap = taken[^1] - taken[^2];
                Assert.That(gap, Is.GreaterThan(TimeSpan.FromMilliseconds(150)),
                    "Backing off must space polls out well beyond the 10 ms interval, so an " +
                    "offline asset is not hammered once per poll cycle.");
            }
        }

        [Test]
        public async Task AHealthyPollResetsTheFailureCount()
        {
            bool healthy = false;
            var healthyObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var subscription = new PollingWotSubscription(
                Form(),
                _ =>
                {
                    if (healthy)
                    {
                        healthyObserved.TrySetResult(true);
                        return new ValueTask<bool>(true);
                    }
                    return new ValueTask<bool>(false);
                },
                TimeSpan.FromMilliseconds(10),
                retryPolicy: new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.FromMilliseconds(10),
                    MaxDelay = TimeSpan.FromMilliseconds(10)
                });

            await using (subscription)
            {
                await Task.Delay(100).ConfigureAwait(false);
                Assert.That(subscription.ConsecutiveFailures, Is.GreaterThan(0));

                healthy = true;
                Assert.That(
                    await Task.WhenAny(healthyObserved.Task, Task.Delay(10000))
                        .ConfigureAwait(false),
                    Is.SameAs(healthyObserved.Task));
                await Task.Delay(50).ConfigureAwait(false);

                Assert.That(subscription.ConsecutiveFailures, Is.Zero,
                    "Recovery must clear the backoff so polling returns to the normal interval.");
            }
        }

        [Test]
        public async Task APolicyThatGivesUpStopsTheLoop()
        {
            int calls = 0;
            var subscription = new PollingWotSubscription(
                Form(),
                _ =>
                {
                    Interlocked.Increment(ref calls);
                    return new ValueTask<bool>(false);
                },
                TimeSpan.FromMilliseconds(5),
                // MaxAttempts 1 means the policy reports "stop retrying" after the first failure.
                retryPolicy: new ExponentialBackoffChannelReconnectPolicy { MaxAttempts = 1 });

            await using (subscription)
            {
                await Task.Delay(200).ConfigureAwait(false);
                int observed = Volatile.Read(ref calls);
                await Task.Delay(200).ConfigureAwait(false);

                Assert.That(Volatile.Read(ref calls), Is.EqualTo(observed),
                    "Once the retry policy gives up the loop must stop rather than spin.");
            }
        }

        [Test]
        public async Task TransientPollExceptionIsReportedAndPollingContinues()
        {
            int calls = 0;
            var errors = new ConcurrentQueue<Exception>();
            var recovered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var subscription = new PollingWotSubscription(
                Form(),
                _ =>
                {
                    // Fault the first iteration, then succeed so recovery can be
                    // observed. A permanently faulting loop would never reach the
                    // second successful iteration.
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        throw new InvalidOperationException("transient poll fault");
                    }
                    recovered.TrySetResult(true);
                    return default;
                },
                TimeSpan.FromMilliseconds(20),
                onError: errors.Enqueue);

            await using (subscription.ConfigureAwait(false))
            {
                Task done = await Task.WhenAny(recovered.Task, Task.Delay(5000)).ConfigureAwait(false);
                Assert.That(done, Is.SameAs(recovered.Task),
                    "The poll loop must keep polling after a transient fault.");
            }

            Assert.That(errors, Has.Count.GreaterThanOrEqualTo(1),
                "The transient fault must be reported to the error handler.");
            Assert.That(Volatile.Read(ref calls), Is.GreaterThanOrEqualTo(2),
                "Polling must continue after a transient fault.");
        }

        [Test]
        public async Task DisposeAsyncAfterCallbackFaultCompletesCleanly()
        {
            var faulted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var subscription = new PollingWotSubscription(
                Form(),
                _ => throw new InvalidOperationException("always faults"),
                TimeSpan.FromMilliseconds(10),
                onError: _ => faulted.TrySetResult(true));

            // Ensure at least one faulting iteration has been observed.
            Task observed = await Task.WhenAny(faulted.Task, Task.Delay(5000)).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(faulted.Task), "The callback fault must be reported.");

            // DisposeAsync must not rethrow the callback fault and must complete
            // promptly (the cancellation source is disposed in a finally).
            Task dispose = subscription.DisposeAsync().AsTask();
            Task first = await Task.WhenAny(dispose, Task.Delay(5000)).ConfigureAwait(false);
            Assert.That(first, Is.SameAs(dispose), "DisposeAsync must not hang after a callback fault.");
            Assert.DoesNotThrowAsync(async () => await dispose.ConfigureAwait(false),
                "DisposeAsync must never rethrow a transient poll/callback fault.");
        }

        [Test]
        public async Task DisposeAsyncDoesNotSwallowNorSurfaceCancellation()
        {
            using var started = new SemaphoreSlim(0, 1);
            var subscription = new PollingWotSubscription(
                Form(),
                async token =>
                {
                    started.Release();
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return true;
                },
                TimeSpan.FromMilliseconds(10));

            Assert.That(await started.WaitAsync(5000).ConfigureAwait(false), Is.True);

            // Cooperative cancellation on dispose stops the loop cleanly without
            // surfacing an OperationCanceledException from DisposeAsync.
            Assert.DoesNotThrowAsync(async () => await subscription.DisposeAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task DisposeAsyncAfterRetryPolicyFaultCompletesCleanly()
        {
            var policy = new ThrowingReconnectPolicy();
            var subscription = new PollingWotSubscription(
                Form(),
                _ => new ValueTask<bool>(false),
                TimeSpan.FromMilliseconds(10),
                retryPolicy: policy);

            Task faulted = await Task.WhenAny(policy.Faulted, Task.Delay(5000)).ConfigureAwait(false);
            Assert.That(faulted, Is.SameAs(policy.Faulted), "The retry policy fault must be observed first.");

            Assert.DoesNotThrowAsync(async () => await subscription.DisposeAsync().ConfigureAwait(false),
                "DisposeAsync must not rethrow residual non-cancellation loop faults.");
        }
    }
}
