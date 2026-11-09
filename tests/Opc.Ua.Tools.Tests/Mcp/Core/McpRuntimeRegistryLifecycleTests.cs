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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpRuntime
{
    /// <summary>
    /// Verifies time, cancellation, admission draining, and exact ownership without timing-based races.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpRuntimeRegistryLifecycleTests
    {
        /// <summary>
        /// Closing retains both kinds of quota while work drains and while asynchronous disposal is pending.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ClosingOperationsRetainCallerAndHostQuotaUntilDisposalCompletesAsync(bool callerQuota)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = callerQuota ? 2 : 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable owner = context.EnterOwner();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime(disposal.Task);
            var nextRuntime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("closing", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            using CancellationTokenRegistration registration = use.CancellationToken.Register(
                () => cancelled.TrySetResult(true));
            Task closing = context.Registry.CloseAsync(info.Handle).AsTask();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(closing.IsCompleted, Is.False);
                await AssertFullAsync().ConfigureAwait(false);
                await use.DisposeAsync().ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(runtime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(closing.IsCompleted, Is.False);
                await AssertFullAsync().ConfigureAwait(false);
            }
            finally
            {
                await use.DisposeAsync().ConfigureAwait(false);
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
            }
            Assert.That(context.Registry.List().Count, Is.Zero);
            using (context.Callers.EnterTrustedCaller(callerQuota
                ? McpRuntimeTestContext.Owner
                : McpRuntimeTestContext.Owners[1]))
            {
                McpRuntimeInfo next = await context.OpenAsync("replacement", nextRuntime).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(next.Handle));
            }
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(nextRuntime.DisposeCalls, Is.Zero);

            async Task AssertFullAsync()
            {
                using IDisposable contender = context.Callers.EnterTrustedCaller(callerQuota
                    ? McpRuntimeTestContext.Owner
                    : McpRuntimeTestContext.Owners[1]);
                Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("replacement", nextRuntime).AsTask()).ConfigureAwait(false);
                Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
                Assert.That(nextRuntime.DisposeCalls, Is.Zero);
            }
        }

        /// <summary>
        /// Exactly two default borrowers run; repeated use disposal returns only one permit and no resource ownership.
        /// </summary>
        [Test]
        public async Task ConcurrencyQueuesTheThirdUseAndBorrowerDisposalReturnsExactlyOnePermitAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("concurrency", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> first = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> second = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Task<McpRuntimeUse<McpTestRuntime>> thirdPending = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Assert.That(thirdPending.IsCompleted, Is.False);
            Assert.That(first.Runtime, Is.SameAs(runtime));
            Assert.That(second.Runtime, Is.SameAs(runtime));

            await first.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> third =
                await McpRuntimeTestContext.AwaitAsync(thirdPending).ConfigureAwait(false);
            Task<McpRuntimeUse<McpTestRuntime>> fourthPending = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Assert.That(fourthPending.IsCompleted, Is.False,
                "Double disposal must not admit a third simultaneous use.");
            Assert.That(third.Runtime, Is.SameAs(runtime));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            await second.DisposeAsync().ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> fourth =
                await McpRuntimeTestContext.AwaitAsync(fourthPending).ConfigureAwait(false);
            Assert.That(fourth.Runtime, Is.SameAs(runtime));
            await third.DisposeAsync().ConfigureAwait(false);
            await fourth.DisposeAsync().ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Ready));
            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
        }

        /// <summary>
        /// Cancelling a queued borrow leaves the active borrower alone and does not strand a user count or permit.
        /// </summary>
        [Test]
        public async Task QueuedCallerCancellationReleasesAdmissionWithoutDisposingRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
                options.MaxConcurrentOperationsPerRuntime = 1);
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("queued", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> active = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Task<McpRuntimeUse<McpTestRuntime>> queued = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime, cancellation.Token).AsTask();
            Assert.That(queued.IsCompleted, Is.False);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(queued).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(active.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(runtime.DisposeCalls, Is.Zero);

            Task<McpRuntimeUse<McpTestRuntime>> nextPending = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Assert.That(nextPending.IsCompleted, Is.False);
            await active.DisposeAsync().ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> next =
                await McpRuntimeTestContext.AwaitAsync(nextPending).ConfigureAwait(false);
            Assert.That(next.Runtime, Is.SameAs(runtime));
            Assert.That(next.CancellationToken.IsCancellationRequested, Is.False);
            await next.DisposeAsync().ConfigureAwait(false);
            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
        }

        /// <summary>
        /// The default thirty-second deadline starts before queue admission, not when a permit becomes available.
        /// </summary>
        [Test]
        public async Task OperationDeadlineIncludesQueueTimeAndCancelsAtExactBoundaryAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
                options.MaxConcurrentOperationsPerRuntime = 1);
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("deadline", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> active = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            Task<McpRuntimeUse<McpTestRuntime>> queued = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            context.Clock.Advance(TimeSpan.FromSeconds(29) - TimeSpan.FromTicks(1));
            Assert.That(active.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(queued.IsCompleted, Is.False);
            context.Clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(active.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(queued.IsCompleted, Is.False);
            context.Clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
            Assert.That(queued.IsCompleted, Is.False);
            context.Clock.Advance(TimeSpan.FromTicks(1));
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(queued).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(runtime.BusinessCalls, Is.Zero);

            await active.DisposeAsync().ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> next = await McpRuntimeTestContext.AwaitAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(next.Runtime, Is.SameAs(runtime));
            Assert.That(next.CancellationToken.IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// Status polling cannot refresh idle expiry, including the exact tick where the lease expires.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task IdleExpiryHasAnExactBoundaryAndListPollingCannotRenewItAsync(int adjacentTick)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(200);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
            });
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo initial = await context.OpenAsync("idle", runtime).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.That(context.Registry.List().ToArray()!.Single().IdleExpiresAt, Is.EqualTo(initial.IdleExpiresAt));
            Assert.That(context.Registry.List().ToArray()!.Single().CreatedAt, Is.EqualTo(initial.CreatedAt));
            context.Clock.Advance(TimeSpan.FromMilliseconds(100) + TimeSpan.FromTicks(adjacentTick));
            if (adjacentTick < 0)
            {
                Assert.That(context.Registry.List().ToArray()!.Single().IdleExpiresAt,
                    Is.EqualTo(initial.IdleExpiresAt));
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
                Assert.That(use.Runtime, Is.SameAs(runtime));
                Assert.That(context.Registry.List().ToArray()!.Single().IdleExpiresAt,
                    Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(400).AddTicks(-1)));
            }
            else
            {
                Assert.That(context.Registry.List().Count, Is.Zero);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
            }
            Assert.That(runtime.BusinessCalls, Is.Zero);
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Legitimate repeated use moves idle expiry, but neither use nor polling may move the hard deadline.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task HardExpiryCannotBeRenewedByUseOrPollingAsync(int adjacentTick)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(200);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(600);
            });
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo initial = await context.OpenAsync("hard-lease", runtime).ConfigureAwait(false);
            for (int renewal = 0; renewal < 3; renewal++)
            {
                context.Clock.Advance(TimeSpan.FromMilliseconds(150));
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
                Assert.That(use.Runtime, Is.SameAs(runtime));
                Assert.That(context.Registry.List().ToArray()!.Single().ExpiresAt, Is.EqualTo(initial.ExpiresAt));
            }
            McpRuntimeInfo lastPoll = context.Registry.List().ToArray()!.Single();
            Assert.That(lastPoll.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch));
            Assert.That(lastPoll.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(650)));
            Assert.That(lastPoll.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(600)));
            context.Clock.Advance(TimeSpan.FromMilliseconds(150) + TimeSpan.FromTicks(adjacentTick));
            if (adjacentTick < 0)
            {
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
                Assert.That(use.Runtime, Is.SameAs(runtime));
                Assert.That(context.Registry.List().ToArray()!.Single().ExpiresAt, Is.EqualTo(initial.ExpiresAt));
            }
            else
            {
                Assert.That(context.Registry.List().Count, Is.Zero);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
            }
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
        }

        /// <summary>
        /// Activation consumes hard-lease time rather than starting a fresh lease when the factory completes.
        /// </summary>
        [Test]
        public async Task ActivationTimeCountsTowardHardLeaseAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(500);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
            });
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "activation-lease", _ => new ValueTask<McpTestRuntime>(activation.Task)).AsTask();
            context.Clock.Advance(TimeSpan.FromMilliseconds(250));
            activation.SetResult(runtime);
            McpRuntimeInfo ready = await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
            Assert.That(ready.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch));
            Assert.That(ready.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(800)));
            Assert.That(ready.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(750)));
            context.Clock.Advance(TimeSpan.FromMilliseconds(450));
            await using (McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(ready.Handle, McpCapability.WotRuntime).ConfigureAwait(false))
            {
                Assert.That(use.Runtime, Is.SameAs(runtime));
            }
            context.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(ready.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Advancing the injected timer alone closes abandoned runtimes without status calls or protocol work.
        /// </summary>
        [Test]
        public async Task SweeperClosesIdleRuntimeWithoutPollingAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.RuntimeIdleTimeout = TimeSpan.FromSeconds(2);
                options.RuntimeLeaseTimeout = TimeSpan.FromSeconds(6);
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime(disposal.Task);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            McpRuntimeInfo info = await context.OpenAsync("abandoned", runtime).ConfigureAwait(false);
            try
            {
                context.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.That(runtime.DisposeCalls, Is.Zero);
                context.Clock.Advance(TimeSpan.FromSeconds(1));
                await McpRuntimeTestContext.AwaitAsync(runtime.DisposalStarted.Task).ConfigureAwait(false);
                McpRuntimeInfo closing = context.Registry.List().ToArray()!.Single();
                Assert.That(closing.Handle, Is.EqualTo(info.Handle));
                Assert.That(closing.State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(closing.ExpiresAt, Is.EqualTo(info.ExpiresAt));
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(runtime.BusinessCalls, Is.Zero);
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
            }
            finally
            {
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Expiry cancels a pending factory and cleans up even when it returns an owned resource late.
        /// </summary>
        [Test]
        public async Task SweeperExpiresPendingActivationAndDisposesLateRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
                options.RuntimeIdleTimeout = TimeSpan.FromSeconds(1);
                options.RuntimeLeaseTimeout = TimeSpan.FromSeconds(2);
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime();
            CancellationToken activationToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync("expired-opening", token =>
            {
                activationToken = token;
                return new ValueTask<McpTestRuntime>(activation.Task);
            }).AsTask();
            using CancellationTokenRegistration registration = activationToken.Register(
                () => cancelled.TrySetResult(true));
            try
            {
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Opening));
                context.Clock.Advance(TimeSpan.FromSeconds(1));
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(opening.IsCompleted, Is.False);
                activation.SetResult(runtime);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(context.Registry.List().Count, Is.Zero);
                McpRuntimeInfo replacement = await context.OpenAsync("expired-opening", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(context.FactoryCalls, Is.EqualTo(2));
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Close cancels queued and running operations but cannot dispose the runtime until every borrower releases.
        /// </summary>
        [Test]
        public async Task CloseCancelsAndDrainsEveryActiveAndQueuedUseBeforeDisposalAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("drain", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> first = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> second = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            TaskCompletionSource<bool> firstCancelled = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> secondCancelled = McpRuntimeTestContext.Completion<bool>();
            using CancellationTokenRegistration firstRegistration = first.CancellationToken.Register(
                () => firstCancelled.TrySetResult(true));
            using CancellationTokenRegistration secondRegistration = second.CancellationToken.Register(
                () => secondCancelled.TrySetResult(true));
            Task<McpRuntimeUse<McpTestRuntime>> queued = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Task closing = context.Registry.CloseAsync(info.Handle).AsTask();
            try
            {
                Exception? queuedFailure = await McpRuntimeTestContext.CaptureAsync(queued).ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(Task.WhenAll(firstCancelled.Task, secondCancelled.Task))
                    .ConfigureAwait(false);
                Assert.That(queuedFailure, Is.InstanceOf<OperationCanceledException>());
                Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(second.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Exception? closingBorrow = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Assert.That(closingBorrow, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
                await first.DisposeAsync().ConfigureAwait(false);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(closing.IsCompleted, Is.False);
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(false);
                await second.DisposeAsync().ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
            }
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
            Exception? closedBorrow = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(closedBorrow, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// A close begun during factory execution awaits ownership transfer and never publishes the late result.
        /// </summary>
        [Test]
        public async Task CloseWhileFactoryIsInFlightCannotPublishOrLeakItsResultAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime();
            CancellationToken factoryToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync("closing-opening", token =>
            {
                factoryToken = token;
                return new ValueTask<McpTestRuntime>(activation.Task);
            }).AsTask();
            string handle = context.Registry.List().ToArray()!.Single().Handle;
            using CancellationTokenRegistration registration = factoryToken.Register(
                () => cancelled.TrySetResult(true));
            Task closing = context.Registry.CloseAsync(handle).AsTask();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                activation.SetResult(runtime);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
                await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(context.Registry.List().Count, Is.Zero);
                McpRuntimeInfo replacement = await context.OpenAsync("closing-opening", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(replacement.Handle, Is.Not.EqualTo(handle));
                Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(context.FactoryCalls, Is.EqualTo(2));
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Caller cancellation only stops waiting for close; actual ownership cleanup continues and shutdown joins it.
        /// </summary>
        [Test]
        public async Task CancelledCloseWaitDoesNotCancelOwnedCleanupAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime(disposal.Task);
            McpRuntimeInfo info = await context.OpenAsync("cancel-close-wait", runtime).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                    context.Registry.CloseAsync(info.Handle, cancellation.Token).AsTask()).ConfigureAwait(false);
                Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
                await McpRuntimeTestContext.AwaitAsync(runtime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("too-early", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
                Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
            }
            finally
            {
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Failed activation releases its name and quota, preserving the original factory exception.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrNullFactoryReleasesNameAndQuotaAsync(bool returnsNull)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            var factoryFailure = new InvalidOperationException("Synthetic activation failure.");
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.OpenAsync("reusable", _ =>
                returnsNull
                    ? ValueTask.FromResult<McpTestRuntime>(null!)
                    : ValueTask.FromException<McpTestRuntime>(factoryFailure)).AsTask()).ConfigureAwait(false);
            if (returnsNull)
            {
                Assert.That(failure, Is.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("The runtime factory returned no runtime."));
            }
            else
            {
                Assert.That(failure, Is.SameAs(factoryFailure));
            }
            Assert.That(context.Registry.List().Count, Is.Zero);
            var runtime = new McpTestRuntime();
            McpRuntimeInfo recovered = await context.OpenAsync("reusable", runtime).ConfigureAwait(false);
            Assert.That(recovered.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(recovered.Handle));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// Cancelling a cooperative factory releases reservation without taking ownership of an unreturned object.
        /// </summary>
        [Test]
        public async Task CancelledFactoryReleasesPendingQuotaWithoutOwningUnreturnedResourceAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            await using var unreturned = new McpTestRuntime();
            TaskCompletionSource<bool> activation = McpRuntimeTestContext.Completion<bool>();
            Task<McpRuntimeInfo> opening = context.OpenAsync("cancel-factory", async token =>
            {
                await activation.Task.WaitAsync(token).ConfigureAwait(false);
                return unreturned;
            }, ct: cancellation.Token).AsTask();
            try
            {
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Opening));
                await cancellation.CancelAsync().ConfigureAwait(false);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
                Assert.That(context.Registry.List().Count, Is.Zero);
                Assert.That(unreturned.DisposeCalls, Is.Zero);
                McpRuntimeInfo next = await context.OpenAsync("cancel-factory", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(next.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(context.FactoryCalls, Is.EqualTo(2));
            }
            finally
            {
                activation.TrySetResult(true);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Caller cancellation at the ownership-transfer boundary cannot leak the already returned resource.
        /// </summary>
        [Test]
        public async Task CallerCancellationAfterFactoryTransferDisposesReturnedRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var runtime = new McpTestRuntime();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("cancel-transfer", async _ =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return runtime;
                }, ct: cancellation.Token).AsTask()).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
            McpRuntimeInfo next = await context.OpenAsync("cancel-transfer", new McpTestRuntime())
                .ConfigureAwait(false);
            Assert.That(next.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
        }

        /// <summary>
        /// An uncooperative factory cannot publish Ready after the finite activation operation deadline has elapsed.
        /// </summary>
        [Test]
        public async Task ActivationDeadlineRejectsLateFactoryResultAndReleasesQuotaAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            var runtime = new McpTestRuntime();
            CancellationToken factoryToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync("late-result", token =>
            {
                factoryToken = token;
                return new ValueTask<McpTestRuntime>(activation.Task);
            }).AsTask();
            try
            {
                context.Clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
                Assert.That(factoryToken.IsCancellationRequested, Is.False);
                context.Clock.Advance(TimeSpan.FromTicks(1));
                Assert.That(factoryToken.IsCancellationRequested, Is.True);
                activation.SetResult(runtime);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.InstanceOf<OperationCanceledException>(),
                    "OpenAsync must check its linked activation deadline before publishing a late factory result.");
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(context.Registry.List().Count, Is.Zero);
                McpRuntimeInfo recovered = await context.OpenAsync("late-result", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(recovered.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(context.FactoryCalls, Is.EqualTo(2));
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed resource cleanup remains visible and cannot be used to bypass either capacity ceiling.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task FailedCleanupStaysFaultedAndRetainsCallerAndHostQuotaAsync(bool callerQuota)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = callerQuota ? 2 : 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable owner = context.EnterOwner();
            var cleanupFailure = new IOException("Synthetic resource cleanup failure.");
            var runtime = new McpTestRuntime(disposalFailure: cleanupFailure);
            McpRuntimeInfo info = await context.OpenAsync("faulted", runtime).ConfigureAwait(false);
            Exception? closeFailure = await McpRuntimeTestContext.CaptureAsync(
                context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
            McpRuntimeInfo faulted = context.Registry.List().ToArray()!.Single();
            Exception? acquireFailure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Exception? quotaFailure;
            using (context.Callers.EnterTrustedCaller(callerQuota
                ? McpRuntimeTestContext.Owner
                : McpRuntimeTestContext.Owners[1]))
            {
                quotaFailure = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("capacity-bypass", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
            }
            Exception? shutdownFailure = await context.ObserveShutdownAsync().ConfigureAwait(false);

            Assert.That(closeFailure, Is.SameAs(cleanupFailure));
            Assert.That(shutdownFailure, Is.SameAs(cleanupFailure));
            Assert.That(faulted.Handle, Is.EqualTo(info.Handle));
            Assert.That(faulted.Name, Is.EqualTo("faulted"));
            Assert.That(faulted.State, Is.EqualTo(McpRuntimeState.Faulted));
            Assert.That(faulted.Capability, Is.EqualTo(McpCapability.WotRuntime));
            Assert.That(faulted.CreatedAt, Is.EqualTo(info.CreatedAt));
            Assert.That(acquireFailure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(quotaFailure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
        }

        /// <summary>
        /// Cleanup remains observable, but cannot replace the primary caller cancellation of opening.
        /// </summary>
        [Test]
        public async Task CancelledOpenPreservesPrimaryFailureWhenCleanupAlsoFailsAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var cleanupFailure = new IOException("Synthetic secondary cleanup failure.");
            var runtime = new McpTestRuntime(disposalFailure: cleanupFailure);
            Exception? openingFailure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("primary-cancellation", async _ =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return runtime;
                }, ct: cancellation.Token).AsTask()).ConfigureAwait(false);
            McpRuntimeInfo faulted = context.Registry.List().ToArray()!.Single();
            Exception? quotaFailure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("cannot-reclaim", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
            Exception? shutdownFailure = await context.ObserveShutdownAsync().ConfigureAwait(false);

            Assert.That(openingFailure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            Assert.That(shutdownFailure, Is.SameAs(cleanupFailure));
            Assert.That(faulted.Name, Is.EqualTo("primary-cancellation"));
            Assert.That(faulted.State, Is.EqualTo(McpRuntimeState.Faulted));
            Assert.That(quotaFailure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// A committed business result survives caller, deadline, or close cancellation during borrower cleanup.
        /// </summary>
        [TestCase("caller")]
        [TestCase("deadline")]
        [TestCase("close")]
        public async Task CancellationAfterCommittedBusinessResultDoesNotMaskLeaseCleanupAsync(string source)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("committed", runtime).ConfigureAwait(false);
            Task? closing = null;
            CancellationToken operationToken = default;
            int result = await McpRuntimeTestContext.AwaitAsync(CommitAsync()).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(17));
            Assert.That(runtime.CommittedValue, Is.EqualTo(17));
            Assert.That(runtime.BusinessCalls, Is.EqualTo(1));
            Assert.That(operationToken.IsCancellationRequested, Is.True);
            if (closing is null)
            {
                Assert.That(runtime.DisposeCalls, Is.Zero);
                closing = context.Registry.CloseAsync(info.Handle).AsTask();
            }
            await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);

            async Task<int> CommitAsync()
            {
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime, cancellation.Token)
                    .ConfigureAwait(false);
                operationToken = use.CancellationToken;
                int committed = use.Runtime.Commit(7);
                if (source == "caller")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
                else if (source == "deadline")
                {
                    context.Clock.Advance(TimeSpan.FromSeconds(30));
                }
                else
                {
                    TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
                    using CancellationTokenRegistration registration = use.CancellationToken.Register(
                        () => cancelled.TrySetResult(true));
                    closing = context.Registry.CloseAsync(info.Handle).AsTask();
                    await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                }
                return committed;
            }
        }

        /// <summary>
        /// Concurrent and repeated shutdown calls all await the same resource drain, rather than returning early.
        /// </summary>
        [Test]
        public async Task SimultaneousDisposeCallsAwaitOneActualShutdownAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            var firstRuntime = new McpTestRuntime(disposal.Task);
            var secondRuntime = new McpTestRuntime();
            McpRuntimeInfo firstInfo = await context.OpenAsync("first-shutdown", firstRuntime).ConfigureAwait(false);
            await context.OpenAsync("second-shutdown", secondRuntime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(firstInfo.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Task firstShutdown = context.Registry.DisposeAsync().AsTask();
            Task secondShutdown = context.Registry.DisposeAsync().AsTask();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(secondRuntime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(firstShutdown.IsCompleted, Is.False);
                Assert.That(secondShutdown.IsCompleted, Is.False);
                Assert.That(firstRuntime.DisposeCalls, Is.Zero);
                await use.DisposeAsync().ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(firstRuntime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(firstShutdown.IsCompleted, Is.False);
                Assert.That(secondShutdown.IsCompleted, Is.False);
                Assert.That(firstRuntime.DisposeCalls, Is.EqualTo(1));
            }
            finally
            {
                await use.DisposeAsync().ConfigureAwait(false);
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(Task.WhenAll(firstShutdown, secondShutdown))
                    .ConfigureAwait(false);
            }
            await context.Registry.DisposeAsync().ConfigureAwait(false);
            Assert.That(firstRuntime.DisposeCalls, Is.EqualTo(1));
            Assert.That(secondRuntime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
            Assert.That(context.Registry.List, Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Shutdown during activation awaits both late ownership transfer and its actual asynchronous disposal.
        /// </summary>
        [Test]
        public async Task ShutdownWhileFactoryIsInFlightAwaitsAndDisposesLateRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime(disposal.Task);
            CancellationToken factoryToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync("shutdown-opening", token =>
            {
                factoryToken = token;
                return new ValueTask<McpTestRuntime>(activation.Task);
            }).AsTask();
            using CancellationTokenRegistration registration = factoryToken.Register(
                () => cancelled.TrySetResult(true));
            Task shutdown = context.Registry.DisposeAsync().AsTask();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(context.Registry.List, Throws.TypeOf<ObjectDisposedException>());
                activation.SetResult(runtime);
                await McpRuntimeTestContext.AwaitAsync(runtime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            }
            finally
            {
                activation.TrySetResult(runtime);
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(shutdown).ConfigureAwait(false);
            }
            Exception? openingFailure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            Assert.That(openingFailure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
        }

        /// <summary>
        /// Disposing an empty registry requires no caller and permanently seals it without ever starting a timer.
        /// </summary>
        [Test]
        public async Task EmptyShutdownIsIdempotentAndDoesNotRequireCallerOrStartTimersAsync()
        {
            await using var context = new McpRuntimeTestContext();
            await context.Registry.DisposeAsync().ConfigureAwait(false);
            await context.Registry.DisposeAsync().ConfigureAwait(false);
            using IDisposable caller = context.EnterOwner();
            Assert.That(context.Registry.List, Throws.TypeOf<ObjectDisposedException>());
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("after-empty-shutdown", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(context.FactoryCalls, Is.Zero);
        }

        /// <summary>
        /// User cancellation callbacks may fail, but close must still drain users and attempt owned cleanup.
        /// </summary>
        [Test]
        public async Task ThrowingCancellationCallbackStillDrainsAndDisposesRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> callbackEntered = McpRuntimeTestContext.Completion<bool>();
            var callbackFailure = new IOException("Synthetic cancellation callback failure.");
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("throwing-callback", runtime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            using CancellationTokenRegistration registration = use.CancellationToken.Register(() =>
            {
                callbackEntered.TrySetResult(true);
                throw callbackFailure;
            });
            Task closing = context.Registry.CloseAsync(info.Handle).AsTask();
            await McpRuntimeTestContext.AwaitAsync(callbackEntered.Task).ConfigureAwait(false);
            Assert.That(closing.IsCompleted, Is.False);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            await use.DisposeAsync().ConfigureAwait(false);
            Exception? closeFailure = await McpRuntimeTestContext.CaptureAsync(closing).ConfigureAwait(false);
            McpRuntimeInfo faulted = context.Registry.List().ToArray()!.Single();
            Exception? shutdownFailure = await context.ObserveShutdownAsync().ConfigureAwait(false);

            Assert.That(closeFailure, Is.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Runtime disposal completed but cancellation callbacks failed."));
            Assert.That(closeFailure!.InnerException, Is.InstanceOf<AggregateException>());
            Assert.That(((AggregateException)closeFailure.InnerException!).Flatten().InnerExceptions,
                Does.Contain(callbackFailure));
            Assert.That(shutdownFailure, Is.SameAs(closeFailure));
            Assert.That(faulted.State, Is.EqualTo(McpRuntimeState.Faulted));
            Assert.That(faulted.Handle, Is.EqualTo(info.Handle));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
        }

        /// <summary>
        /// One callback's failure cannot make shutdown abandon a different resource's asynchronous cleanup.
        /// </summary>
        [Test]
        public async Task ShutdownDrainsOtherRuntimesWhenOneCancellationCallbackThrowsAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> callbackEntered = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> otherCancelled = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> otherDisposal = McpRuntimeTestContext.Completion<bool>();
            var callbackFailure = new IOException("Synthetic shutdown callback failure.");
            var firstRuntime = new McpTestRuntime();
            var otherRuntime = new McpTestRuntime(otherDisposal.Task);
            McpRuntimeInfo firstInfo = await context.OpenAsync("faulting-shutdown", firstRuntime).ConfigureAwait(false);
            McpRuntimeInfo otherInfo = await context.OpenAsync("other-shutdown", otherRuntime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> first = await context.Registry
                .AcquireAsync<McpTestRuntime>(firstInfo.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> other = await context.Registry
                .AcquireAsync<McpTestRuntime>(otherInfo.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            using CancellationTokenRegistration throwing = first.CancellationToken.Register(() =>
            {
                callbackEntered.TrySetResult(true);
                throw callbackFailure;
            });
            using CancellationTokenRegistration recording = other.CancellationToken.Register(
                () => otherCancelled.TrySetResult(true));
            Task<Exception?> shutdown = context.ObserveShutdownAsync();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(Task.WhenAll(callbackEntered.Task, otherCancelled.Task))
                    .ConfigureAwait(false);
                Assert.That(firstRuntime.DisposeCalls, Is.Zero);
                Assert.That(otherRuntime.DisposeCalls, Is.Zero);
                await first.DisposeAsync().ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
                await McpRuntimeTestContext.AwaitAsync(Task.WhenAll(
                    firstRuntime.DisposalStarted.Task, otherRuntime.DisposalStarted.Task)).ConfigureAwait(false);
                Assert.That(shutdown.IsCompleted, Is.False);
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
                otherDisposal.TrySetResult(true);
            }
            Exception? failure = await McpRuntimeTestContext.AwaitAsync(shutdown).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Runtime disposal completed but cancellation callbacks failed."));
            Assert.That(failure!.InnerException, Is.InstanceOf<AggregateException>());
            Assert.That(((AggregateException)failure.InnerException!).Flatten().InnerExceptions,
                Does.Contain(callbackFailure));
            Assert.That(firstRuntime.DisposeCalls, Is.EqualTo(1));
            Assert.That(otherRuntime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
        }

        /// <summary>
        /// Credential expiry is checked before reservation and activation, including equality with the current time.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task SecretExpiryHasAnExactPreactivationBoundaryAsync(int expiryMilliseconds)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> invalidated = McpRuntimeTestContext.Completion<bool>();
            var runtime = new McpTestRuntime();
            McpSecretDependency dependency = Dependency(
                "boundary", TimeSpan.FromMilliseconds(expiryMilliseconds), invalidated.Task);
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "expiry-boundary", [dependency], _ => ValueTask.FromResult(runtime)).AsTask();
            if (expiryMilliseconds > 0)
            {
                McpRuntimeInfo info = await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
                Assert.That(info.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(info.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(30)));
                Assert.That(info.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(5)));
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
                await McpRuntimeTestContext.AwaitAsync(
                    context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            }
            else
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadIdentityTokenRejected));
                Assert.That(context.FactoryCalls, Is.Zero);
                Assert.That(context.Clock.CreatedTimers, Is.Zero);
                Assert.That(runtime.DisposeCalls, Is.Zero);
            }
            Assert.That(invalidated.Task.IsCompleted, Is.False);
            Assert.That(context.Registry.List().Count, Is.Zero);
            McpRuntimeInfo replacement = await context.OpenAsync("expiry-boundary", new McpTestRuntime())
                .ConfigureAwait(false);
            Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.FactoryCalls, Is.EqualTo(expiryMilliseconds > 0 ? 2 : 1));
        }

        /// <summary>
        /// Any completed revocation or generation signal refuses activation, without a credential for session signals.
        /// </summary>
        [TestCase(false, "completed")]
        [TestCase(false, "canceled")]
        [TestCase(false, "faulted")]
        [TestCase(true, "completed")]
        [TestCase(true, "canceled")]
        [TestCase(true, "faulted")]
        public async Task PreSignaledDependenciesRefuseBeforeFactoryAsync(bool sessionSignal, string completion)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            Task signal = CompletedDependencySignal(completion);
            ArrayOf<McpSecretDependency> dependencies = sessionSignal
                ? []
                : [Dependency("pre-signaled", TimeSpan.FromMinutes(1), signal)];
            ArrayOf<Task> generations = sessionSignal ? [signal] : [];
            var runtime = new McpTestRuntime();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.OpenAsync(
                "pre-signaled", dependencies, generations, _ => ValueTask.FromResult(runtime)).AsTask())
                .ConfigureAwait(false);

            StatusCode expected = sessionSignal
                ? StatusCodes.BadSessionIdInvalid
                : StatusCodes.BadIdentityTokenRejected;
            Assert.That(failure, Is.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected));
            Assert.That(dependencies.Count, Is.EqualTo(sessionSignal ? 0 : 1));
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            McpRuntimeInfo replacement = await context.OpenAsync("pre-signaled", new McpTestRuntime())
                .ConfigureAwait(false);
            Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// The two watcher ceilings are independent; their final allowed slots are watched and excess is preflighted.
        /// </summary>
        [TestCase(16, 0)]
        [TestCase(0, 16)]
        [TestCase(16, 16)]
        [TestCase(17, 16)]
        [TestCase(16, 17)]
        [TestCase(18, 16)]
        [TestCase(16, 18)]
        public async Task DependencyWatcherLimitsAreIndependentAndBoundedAsync(int secretCount, int generationCount)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var secretSignals = new TaskCompletionSource<bool>[secretCount];
            var dependencies = new McpSecretDependency[secretCount];
            for (int index = 0; index < secretCount; index++)
            {
                secretSignals[index] = McpRuntimeTestContext.Completion<bool>();
                dependencies[index] = Dependency(
                    $"dependency-{index}", TimeSpan.FromMinutes(1), secretSignals[index].Task);
            }
            var generationSignals = new TaskCompletionSource<bool>[generationCount];
            var generations = new Task[generationCount];
            for (int index = 0; index < generationCount; index++)
            {
                generationSignals[index] = McpRuntimeTestContext.Completion<bool>();
                generations[index] = generationSignals[index].Task;
            }
            var runtime = new McpTestRuntime();
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "watcher-limits", dependencies.ToArrayOf(), generations.ToArrayOf(),
                _ => ValueTask.FromResult(runtime)).AsTask();
            if (secretCount > 16 || generationCount > 16)
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                string parameter = secretCount > 16 ? "dependencies.Count" : "invalidationSignals.Count";
                Assert.That(failure, Is.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property("ParamName").EqualTo(parameter));
                Assert.That(context.FactoryCalls, Is.Zero);
                Assert.That(context.Clock.CreatedTimers, Is.Zero);
                Assert.That(context.Registry.List().Count, Is.Zero);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                McpRuntimeInfo replacement = await context.OpenAsync("watcher-limits", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
                return;
            }

            McpRuntimeInfo info = await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
            Assert.That(info.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(info.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch));
            Assert.That(info.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(30)));
            Assert.That(info.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(5)));
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(secretCount + 1));
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            using CancellationTokenRegistration registration = use.CancellationToken.Register(
                () => cancelled.TrySetResult(true));
            if (generationCount > 0)
            {
                generationSignals[^1].SetResult(true);
            }
            else
            {
                secretSignals[^1].SetResult(true);
            }
            await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(secretSignals.Count(signal => signal.Task.IsCompleted),
                Is.EqualTo(generationCount > 0 ? 0 : 1));
            Assert.That(generationSignals.Count(signal => signal.Task.IsCompleted),
                Is.EqualTo(generationCount > 0 ? 1 : 0));
            await use.DisposeAsync().ConfigureAwait(false);
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Malformed dependency components fail before creating entries, timers, or an owned runtime.
        /// </summary>
        [TestCase("dependency")]
        [TestCase("reference")]
        [TestCase("revocation")]
        [TestCase("generation")]
        public async Task NullDependencyComponentsRefuseBeforeFactoryAsync(string component)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> pending = McpRuntimeTestContext.Completion<bool>();
            McpSecretDependency valid = Dependency("nullable-input", TimeSpan.FromMinutes(1), pending.Task);
            ArrayOf<McpSecretDependency> dependencies = component switch
            {
                "dependency" => [null!],
                "reference" => [new McpSecretDependency(null!, pending.Task)],
                "revocation" => [new McpSecretDependency(valid.Reference, null!)],
                _ => []
            };
            ArrayOf<Task> generations = component == "generation" ? [null!] : [];
            var runtime = new McpTestRuntime();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.OpenAsync(
                "null-component", dependencies, generations, _ => ValueTask.FromResult(runtime)).AsTask())
                .ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ArgumentNullException>());
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(pending.Task.IsCompleted, Is.False);
        }

        /// <summary>
        /// Expiry and revocation independently cancel active and queued uses; session signals require no credentials.
        /// </summary>
        [TestCase("expiry")]
        [TestCase("revocation")]
        [TestCase("session")]
        public async Task DependencyChangesCancelActiveAndQueuedOperationsAsync(string source)
        {
            await using var context = new McpRuntimeTestContext(options =>
                options.MaxConcurrentOperationsPerRuntime = 1);
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> invalidated = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            ArrayOf<McpSecretDependency> dependencies = source == "session"
                ? []
                : [Dependency("operation", s_dependencyLifetime, invalidated.Task)];
            ArrayOf<Task> generations = source == "session" ? [invalidated.Task] : [];
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync(
                "dependency-operation", dependencies, generations, _ => ValueTask.FromResult(runtime))
                .ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            using CancellationTokenRegistration registration = use.CancellationToken.Register(
                () => cancelled.TrySetResult(true));
            Task<McpRuntimeUse<McpTestRuntime>> queued = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Assert.That(queued.IsCompleted, Is.False);
            context.Clock.Advance(source == "expiry"
                ? s_dependencyLifetime - TimeSpan.FromTicks(1)
                : TimeSpan.FromMilliseconds(100));
            Assert.That(use.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            if (source == "expiry")
            {
                context.Clock.Advance(TimeSpan.FromTicks(1));
            }
            else
            {
                invalidated.SetResult(true);
            }

            // Wait for the watcher itself; List or Acquire must not supply a fallback expiry sweep.
            await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
            Exception? queuedFailure = await McpRuntimeTestContext.CaptureAsync(queued).ConfigureAwait(false);
            Assert.That(queuedFailure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(use.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(invalidated.Task.IsCompleted, Is.EqualTo(source != "expiry"));
            Assert.That(dependencies.Count, Is.EqualTo(source == "session" ? 0 : 1));
            McpRuntimeInfo closing = context.Registry.List().ToArray()!.Single();
            Assert.That(closing.Handle, Is.EqualTo(info.Handle));
            Assert.That(closing.State, Is.EqualTo(McpRuntimeState.Closing));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(runtime.BusinessCalls, Is.Zero);
            await use.DisposeAsync().ConfigureAwait(false);
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// A newly requested operation is refused immediately, whether validation or the watcher starts closing first.
        /// </summary>
        [TestCase("expiry")]
        [TestCase("revocation")]
        [TestCase("session")]
        public async Task InvalidatedDependenciesCannotAdmitAnotherOperationAsync(string source)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> invalidated = McpRuntimeTestContext.Completion<bool>();
            ArrayOf<McpSecretDependency> dependencies = source == "session"
                ? []
                : [Dependency("new-operation", s_dependencyLifetime, invalidated.Task)];
            ArrayOf<Task> generations = source == "session" ? [invalidated.Task] : [];
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync(
                "invalidated-operation", dependencies, generations, _ => ValueTask.FromResult(runtime))
                .ConfigureAwait(false);
            TriggerDependency(source, context.Clock, invalidated);
            Task<McpRuntimeUse<McpTestRuntime>> acquiring = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(acquiring).ConfigureAwait(false);
            if (failure is null)
            {
                McpRuntimeUse<McpTestRuntime> unexpected = await acquiring.ConfigureAwait(false);
                await unexpected.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(failure, Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState).Or.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(runtime.BusinessCalls, Is.Zero);
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Dependency watchers are installed before the factory await and prevent a late result from becoming Ready.
        /// </summary>
        [TestCase("expiry")]
        [TestCase("revocation")]
        [TestCase("session")]
        public async Task DependencyChangesDuringActivationCannotPublishOrLeakRuntimeAsync(string source)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> invalidated = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            ArrayOf<McpSecretDependency> dependencies = source == "session"
                ? []
                : [Dependency("activation", s_dependencyLifetime, invalidated.Task)];
            ArrayOf<Task> generations = source == "session" ? [invalidated.Task] : [];
            var runtime = new McpTestRuntime();
            CancellationToken factoryToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "dependency-activation", dependencies, generations, token =>
                {
                    factoryToken = token;
                    return new ValueTask<McpTestRuntime>(activation.Task);
                }).AsTask();
            string handle = context.Registry.List().ToArray()!.Single().Handle;
            using CancellationTokenRegistration registration = factoryToken.Register(
                () => cancelled.TrySetResult(true));
            try
            {
                TriggerDependency(source, context.Clock, invalidated);
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Closing));
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Exception? full = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("too-early", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
                Assert.That(full, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
                activation.SetResult(runtime);
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
                Assert.That(context.Registry.List().Count, Is.Zero);
                Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
                Assert.That(invalidated.Task.IsCompleted, Is.EqualTo(source != "expiry"));
                McpRuntimeInfo replacement = await context.OpenAsync("dependency-activation", new McpTestRuntime())
                    .ConfigureAwait(false);
                Assert.That(replacement.Handle, Is.Not.EqualTo(handle));
                Assert.That(replacement.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(context.FactoryCalls, Is.EqualTo(2));
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Explicit close joins pending watchers and factory cleanup; later old signals cannot close a new generation.
        /// </summary>
        [Test]
        public async Task CloseDuringDependentActivationCleansWatchersBeforeNameReuseAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> revoked = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> generation = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> disposal = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            var runtime = new McpTestRuntime(disposal.Task);
            CancellationToken factoryToken = default;
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "reused-dependency-name", [Dependency("old-generation", s_dependencyLifetime, revoked.Task)],
                [generation.Task], token =>
                {
                    factoryToken = token;
                    return new ValueTask<McpTestRuntime>(activation.Task);
                }).AsTask();
            string handle = context.Registry.List().ToArray()!.Single().Handle;
            using CancellationTokenRegistration registration = factoryToken.Register(
                () => cancelled.TrySetResult(true));
            Task closing = context.Registry.CloseAsync(handle).AsTask();
            try
            {
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                activation.SetResult(runtime);
                await McpRuntimeTestContext.AwaitAsync(runtime.DisposalStarted.Task).ConfigureAwait(false);
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            }
            finally
            {
                activation.TrySetResult(runtime);
                disposal.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
            }
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
            Assert.That(revoked.Task.IsCompleted, Is.False);
            Assert.That(generation.Task.IsCompleted, Is.False);
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);

            var replacement = new McpTestRuntime();
            McpRuntimeInfo reopened = await context.OpenAsync("reused-dependency-name", replacement)
                .ConfigureAwait(false);
            revoked.SetResult(true);
            generation.SetResult(true);
            context.Clock.Advance(s_dependencyLifetime);
            await using (McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(reopened.Handle, McpCapability.WotRuntime).ConfigureAwait(false))
            {
                Assert.That(use.Runtime, Is.SameAs(replacement));
                Assert.That(use.CancellationToken.IsCancellationRequested, Is.False);
            }
            Assert.That(reopened.Handle, Is.Not.EqualTo(handle));
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(reopened.Handle));
            Assert.That(replacement.DisposeCalls, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
        }

        /// <summary>
        /// A business result already observed by the caller is not replaced by dependency cancellation during cleanup.
        /// </summary>
        [TestCase("expiry")]
        [TestCase("revocation")]
        [TestCase("session")]
        public async Task CompletedBusinessResultSurvivesDependencyInvalidationCleanupAsync(string source)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> invalidated = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            ArrayOf<McpSecretDependency> dependencies = source == "session"
                ? []
                : [Dependency("committed", s_dependencyLifetime, invalidated.Task)];
            ArrayOf<Task> generations = source == "session" ? [invalidated.Task] : [];
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync(
                "dependent-commit", dependencies, generations, _ => ValueTask.FromResult(runtime))
                .ConfigureAwait(false);
            CancellationToken operationToken = default;
            int result = await McpRuntimeTestContext.AwaitAsync(CommitAsync()).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(17));
            Assert.That(runtime.CommittedValue, Is.EqualTo(17));
            Assert.That(runtime.BusinessCalls, Is.EqualTo(1));
            Assert.That(operationToken.IsCancellationRequested, Is.True);
            Assert.That(invalidated.Task.IsCompleted, Is.EqualTo(source != "expiry"));
            Assert.That(dependencies.Count, Is.EqualTo(source == "session" ? 0 : 1));
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);

            async Task<int> CommitAsync()
            {
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
                operationToken = use.CancellationToken;
                using CancellationTokenRegistration registration = operationToken.Register(
                    () => cancelled.TrySetResult(true));
                int committed = use.Runtime.Commit(7);
                TriggerDependency(source, context.Clock, invalidated);
                await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                return committed;
            }
        }

        /// <summary>
        /// Factory failure stops both watcher types without cancelling or disposing borrowed invalidation sources.
        /// </summary>
        [Test]
        public async Task FailedFactoryStopsDependencyWatchersAndReleasesQuotaAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> revoked = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> generation = McpRuntimeTestContext.Completion<bool>();
            var failure = new InvalidOperationException("Synthetic dependent activation failure.");
            Exception? actual = await McpRuntimeTestContext.CaptureAsync(context.OpenAsync(
                "failed-dependent-factory", [Dependency("failed", s_dependencyLifetime, revoked.Task)],
                [generation.Task], _ => ValueTask.FromException<McpTestRuntime>(failure)).AsTask())
                .ConfigureAwait(false);
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(revoked.Task.IsCompleted, Is.False);
            Assert.That(generation.Task.IsCompleted, Is.False);
            McpRuntimeInfo next = await context.OpenAsync("failed-dependent-factory", new McpTestRuntime())
                .ConfigureAwait(false);
            Assert.That(next.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
        }

        /// <summary>
        /// A completed factory cannot renew an already expired lease before the first periodic sweep can run.
        /// </summary>
        [TestCase(500, -1, false)]
        [TestCase(500, 0, true)]
        [TestCase(500, 1, true)]
        [TestCase(850, 0, true)]
        public async Task CompletedFactoryCannotPublishAnExpiredLeaseBeforeSweepAsync(
            int elapsedMilliseconds, int adjacentTick, bool expired)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(500);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var runtime = new McpTestRuntime();
            TimeSpan elapsed = TimeSpan.FromMilliseconds(elapsedMilliseconds) + TimeSpan.FromTicks(adjacentTick);
            bool cancelledAtFactoryReturn = true;
            Assert.That(context.Clock.GetUtcNow(), Is.EqualTo(McpRuntimeTestContext.Epoch));
            Task<McpRuntimeInfo> opening = context.OpenAsync("completed-factory-window", token =>
            {
                context.Clock.Advance(elapsed);
                cancelledAtFactoryReturn = token.IsCancellationRequested;
                return ValueTask.FromResult(runtime);
            }, ct: cancellation.Token).AsTask();

            if (expired)
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
                Assert.That(opening.IsFaulted, Is.True);
                Assert.That(context.Registry.List().Count, Is.Zero);
            }
            else
            {
                McpRuntimeInfo ready = await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
                Assert.That(ready.State, Is.EqualTo(McpRuntimeState.Ready));
                Assert.That(ready.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch));
                Assert.That(ready.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(800)));
                Assert.That(ready.IdleExpiresAt,
                    Is.EqualTo(McpRuntimeTestContext.Epoch + elapsed + TimeSpan.FromMilliseconds(500)));
                Assert.That(context.Registry.List().ToArray()!.Single(), Is.EqualTo(ready));
                Assert.That(runtime.DisposeCalls, Is.Zero);
                await McpRuntimeTestContext.AwaitAsync(
                    context.Registry.CloseAsync(ready.Handle).AsTask()).ConfigureAwait(false);
            }

            Assert.That(cancelledAtFactoryReturn, Is.False);
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            Assert.That(context.Clock.GetUtcNow(), Is.EqualTo(McpRuntimeTestContext.Epoch + elapsed));
            Assert.That(context.Clock.GetUtcNow(), Is.LessThan(McpRuntimeTestContext.Epoch.AddSeconds(1)));
            Assert.That(context.Clock.CreatedTimers, Is.EqualTo(2));
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);

            var replacement = new McpTestRuntime();
            McpRuntimeInfo recovered = await context.OpenAsync("completed-factory-window", replacement)
                .ConfigureAwait(false);
            Assert.That(recovered.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(recovered.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch + elapsed));
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(recovered.Handle));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
            Assert.That(replacement.DisposeCalls, Is.Zero);
            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(recovered.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Registry.List().Count, Is.Zero);
        }

        /// <summary>
        /// Creates safe metadata and a borrowed signal only, without allocating or fabricating secret material.
        /// </summary>
        private static McpSecretDependency Dependency(string reference, TimeSpan lifetime, Task invalidated)
        {
            return new McpSecretDependency(
                new McpSecretReference(
                    reference, "runtime-test", "urn:test:runtime", McpRuntimeTestContext.Epoch + lifetime,
                    CredentialId: null),
                invalidated);
        }

        /// <summary>
        /// Represents each terminal invalidation state and observes synthetic fault diagnostics locally.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static Task<bool> CompletedDependencySignal(string completion)
        {
            TaskCompletionSource<bool> signal = McpRuntimeTestContext.Completion<bool>();
            switch (completion)
            {
                case "completed":
                    signal.SetResult(true);
                    break;
                case "canceled":
                    signal.SetCanceled();
                    break;
                case "faulted":
                    signal.SetException(new InvalidOperationException("Synthetic invalidation signal failure."));
                    _ = signal.Task.Exception;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(completion));
            }
            return signal.Task;
        }

        /// <summary>
        /// Triggers one independent lifetime cause before the one-second registry sweeper can participate.
        /// </summary>
        private static void TriggerDependency(
            string source, McpRuntimeTestClock clock, TaskCompletionSource<bool> invalidated)
        {
            if (source == "expiry")
            {
                clock.Advance(s_dependencyLifetime);
            }
            else
            {
                invalidated.SetResult(true);
            }
        }

        private static readonly TimeSpan s_dependencyLifetime = TimeSpan.FromMilliseconds(500);
    }
}
#endif
