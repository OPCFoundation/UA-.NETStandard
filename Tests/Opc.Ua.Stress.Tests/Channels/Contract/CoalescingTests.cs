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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Layer-1 reconnect coalescing contract tests for managed channel entries.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CoalescingTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        [Description("L1-COA1: concurrent reconnect callers coalesce to one underlying cycle.")]
        public async Task ConcurrentReconnectCoalescesToSingleCycleAsync(CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("coalescing-single-cycle");
            ContractTestEnvironment environment = CreateEnvironment(certificate);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("coalescing-single-cycle");
            FakeParticipant participant = new(endpoint);
            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            ChaosBarrier reconnectBarrier = new(expectedParticipants: 1);
            Task[] reconnectTasks = [];

            try
            {
                environment.Bindings.ConfigureNextOpenToBlockOn(reconnectBarrier);
                reconnectTasks = StartReconnectTasks(
                    environment.Manager,
                    channel,
                    TotalCallerCount,
                    _ => ct,
                    ct);

                await WaitForReconnectBarrierAsync(reconnectBarrier, ct).ConfigureAwait(false);
                await WaitForCoalescingWindowAsync(ct).ConfigureAwait(false);

                reconnectBarrier.Release();
                await Task.WhenAll(reconnectTasks).WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                AssertReconnectCycleCounts(environment, participant, expectedCycles: 1);
            }
            finally
            {
                reconnectBarrier.Release();
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-COA2: cancelling half of coalesced callers does not start another cycle.")]
        public async Task ConcurrentReconnectWithMixedCancellationCoalescesToSingleCycleAsync(
            CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("coalescing-mixed-cancellation");
            ContractTestEnvironment environment = CreateEnvironment(certificate);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("coalescing-mixed-cancellation");
            FakeParticipant participant = new(endpoint);
            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            ChaosBarrier reconnectBarrier = new(expectedParticipants: 1);
            CancellationTokenSource[] cancellationSources = CreateLinkedCancellationSources(
                CancelledCallerCount,
                ct);
            Task[] reconnectTasks = [];

            try
            {
                environment.Bindings.ConfigureNextOpenToBlockOn(reconnectBarrier);
                reconnectTasks = StartReconnectTasks(
                    environment.Manager,
                    channel,
                    TotalCallerCount,
                    index => index < CancelledCallerCount ? cancellationSources[index].Token : ct,
                    ct);

                await WaitForReconnectBarrierAsync(reconnectBarrier, ct).ConfigureAwait(false);
                await WaitForCoalescingWindowAsync(ct).ConfigureAwait(false);

                CancelAll(cancellationSources);
                Exception?[] cancelledResults = await Task.WhenAll(
                        reconnectTasks.Take(CancelledCallerCount).Select(CaptureExceptionAsync))
                    .WaitAsync(DefaultWait, ct)
                    .ConfigureAwait(false);

                reconnectBarrier.Release();
                await Task.WhenAll(reconnectTasks.Skip(CancelledCallerCount))
                    .WaitAsync(DefaultWait, ct)
                    .ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(cancelledResults, Has.All.InstanceOf<OperationCanceledException>());
                    AssertReconnectCycleCounts(environment, participant, expectedCycles: 1);
                });
            }
            finally
            {
                reconnectBarrier.Release();
                DisposeAll(cancellationSources);
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-COA3: callers after a completed cycle start a fresh coalescer cycle.")]
        public async Task ConcurrentReconnectAfterCycleBoundaryStartsNewCycleAsync(CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("coalescing-cycle-boundary");
            ContractTestEnvironment environment = CreateEnvironment(certificate);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("coalescing-cycle-boundary");
            FakeParticipant participant = new(endpoint);
            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            ChaosBarrier firstCycleBarrier = new(expectedParticipants: 1);
            ChaosBarrier secondCycleBarrier = new(expectedParticipants: 1);
            Task[] firstCycleTasks = [];

            try
            {
                environment.Bindings.ConfigureNextOpenToBlockOn(firstCycleBarrier);
                firstCycleTasks = StartReconnectTasks(
                    environment.Manager,
                    channel,
                    BoundaryCallerCount,
                    _ => ct,
                    ct);

                await WaitForReconnectBarrierAsync(firstCycleBarrier, ct).ConfigureAwait(false);
                await WaitForCoalescingWindowAsync(ct).ConfigureAwait(false);

                firstCycleBarrier.Release();
                await Task.WhenAll(firstCycleTasks).WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);
                AssertReconnectCycleCounts(environment, participant, expectedCycles: 1);

                environment.Bindings.ConfigureNextOpenToBlockOn(secondCycleBarrier);
                Task[] secondCycleTasks = StartReconnectTasks(
                    environment.Manager,
                    channel,
                    BoundaryCallerCount,
                    _ => ct,
                    ct);
                await WaitForReconnectBarrierAsync(secondCycleBarrier, ct).ConfigureAwait(false);
                await WaitForCoalescingWindowAsync(ct).ConfigureAwait(false);

                secondCycleBarrier.Release();
                await Task.WhenAll(secondCycleTasks).WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                AssertReconnectCycleCounts(environment, participant, expectedCycles: 2);
            }
            finally
            {
                firstCycleBarrier.Release();
                secondCycleBarrier.Release();
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static Task[] StartReconnectTasks(
            ClientChannelManager manager,
            IManagedTransportChannel channel,
            int callerCount,
            Func<int, CancellationToken> cancellationTokenFactory,
            CancellationToken startToken)
        {
            var startBarrier = new ChaosBarrier(callerCount);
            return [.. Enumerable.Range(0, callerCount)
                .Select(index => Task.Run(async () =>
                {
                    await startBarrier.SignalAndWaitAsync(startToken).ConfigureAwait(false);
                    await manager.ReconnectAsync(channel, cancellationTokenFactory(index))
                        .ConfigureAwait(false);
                }))];
        }

        private static Task WaitForReconnectBarrierAsync(ChaosBarrier barrier, CancellationToken ct)
        {
            return WaitUntilAsync(
                () => barrier.ArrivedCount > 0,
                "The reconnect cycle did not reach the transport barrier before timeout.",
                ct);
        }

        private static async Task WaitForCoalescingWindowAsync(CancellationToken ct)
        {
            await Task.Delay(CoalescingWindow, ct).ConfigureAwait(false);
        }

        private static async Task<Exception?> CaptureExceptionAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static CancellationTokenSource[] CreateLinkedCancellationSources(
            int count,
            CancellationToken ct)
        {
            var cancellationSources = new CancellationTokenSource[count];
            for (int index = 0; index < cancellationSources.Length; index++)
            {
                cancellationSources[index] = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }

            return cancellationSources;
        }

        private static void CancelAll(CancellationTokenSource[] cancellationSources)
        {
            foreach (CancellationTokenSource cancellationSource in cancellationSources)
            {
                cancellationSource.Cancel();
            }
        }

        private static void DisposeAll(CancellationTokenSource[] cancellationSources)
        {
            foreach (CancellationTokenSource cancellationSource in cancellationSources)
            {
                cancellationSource.Dispose();
            }
        }

        private static void AssertReconnectCycleCounts(
            ContractTestEnvironment environment,
            FakeParticipant participant,
            int expectedCycles)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    environment.Bindings.Created.Sum(transport => transport.OpenCount),
                    Is.EqualTo(1));
                Assert.That(
                    environment.Bindings.Created.Sum(transport => transport.ReconnectCount),
                    Is.EqualTo(expectedCycles));
                Assert.That(participant.NotificationCount, Is.EqualTo(expectedCycles));
            });
        }

        private const int TotalCallerCount = 100;
        private const int CancelledCallerCount = 50;
        private const int BoundaryCallerCount = 50;
        private static readonly TimeSpan CoalescingWindow = TimeSpan.FromMilliseconds(200);
    }
}
