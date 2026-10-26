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
 *
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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies striped generation admission and address-space epoch lifetimes.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class GenerationGateTests
    {
        /// <summary>
        /// Independent execution contexts cannot inherit a notification's source generation.
        /// </summary>
        [Test]
        public void MasterDoesNotStoreAmbientSourceGeneration()
        {
            Assert.That(
                typeof(MasterNodeManager).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(field => field.FieldType == typeof(AsyncLocal<NodeManagerGeneration>)),
                Is.Empty);
        }

        /// <summary>
        /// Prepared generations refuse entry, while retirement closes a live gate and drains prior work.
        /// </summary>
        [Test]
        public async Task RetiringGenerationRefusesNewAdmissionsAndWaitsForExistingLeaseAsync()
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Prepared);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? _), Is.False);

            gate.Activate();
            Assert.That(gate.TryEnter(out GenerationGate.Lease? lease), Is.True);
            Assert.That(gate.ActiveCount, Is.EqualTo(1));

            gate.Retire();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(gate.TryEnter(out GenerationGate.Lease? _), Is.False);
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);

            lease!.Dispose();
            await drained.ConfigureAwait(false);

            Assert.That(gate.ActiveCount, Is.Zero);
            gate.DisposeGeneration();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
            Assert.That(() => gate.Activate(), Throws.InvalidOperationException);

            var aborted = new GenerationGate(NodeManagerGenerationState.Prepared);
            aborted.Abort();
            Assert.That(aborted.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(() => aborted.Activate(), Throws.InvalidOperationException);
        }

        /// <summary>
        /// Only an active parent can extend admitted work, and each child independently delays retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActiveParentForkKeepsGenerationAliveAfterParentExitAsync(bool retireBeforeFork)
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            if (retireBeforeFork)
            {
                gate.Retire();
            }
            Assert.That(parent!.TryFork(out int child), Is.True);
            Assert.That(gate.ActiveCount, Is.EqualTo(2));
            parent.Dispose();
            parent.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(parent.TryFork(out int rejected), Is.False);
                Assert.That(rejected, Is.EqualTo(-1));
                Assert.That(gate.ActiveCount, Is.EqualTo(1));
            });
            gate.Retire();
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            gate.Exit(child);
            await drained.ConfigureAwait(false);
            Assert.That(gate.ActiveCount, Is.Zero);
            gate.DisposeGeneration();
            Assert.That(parent.TryFork(out int _), Is.False);
            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Racing parent release either refuses a child or counts it before the generation can drain.
        /// </summary>
        [Test]
        public async Task ForkRacingParentReleaseNeverEscapesGenerationDrainAsync()
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                var gate = new GenerationGate(NodeManagerGenerationState.Live);
                Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
                gate.Retire();
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> fork = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    return parent!.TryFork(out int child) ? child : -1;
                });
                Task release = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    parent!.Dispose();
                });
                start.SetResult(true);
                await Task.WhenAll(fork, release).ConfigureAwait(false);
                int slot = await fork.ConfigureAwait(false);
                bool entered = slot >= 0;
                Assert.That(gate.ActiveCount, Is.EqualTo(entered ? 1 : 0));
                Task drained = gate.WaitForDrainAsync();
                Assert.That(drained.IsCompleted, Is.EqualTo(!entered));
                if (entered)
                {
                    gate.Exit(slot);
                }
                await drained.ConfigureAwait(false);
                Assert.That(gate.ActiveCount, Is.Zero);
                gate.DisposeGeneration();
            }
        }

        /// <summary>
        /// A drain scan cannot miss work transferred from a late parent stripe to an earlier child stripe.
        /// </summary>
        [Test]
        public async Task ForkRacingDrainScanCannotCompleteWhileChildRemainsActiveAsync()
        {
            using var barrier = new Barrier(2);
            using var cancellation = new CancellationTokenSource();
            GenerationGate.Lease? parent = null;
            int child = -1;
            Task fork = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        barrier.SignalAndWait(cancellation.Token);
                        parent!.TryFork(out child);
                        parent.Dispose();
                        barrier.SignalAndWait(cancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            try
            {
                for (int iteration = 0; iteration < 10000; iteration++)
                {
                    var gate = new GenerationGate(NodeManagerGenerationState.Live);
                    var counter = (StripedCounter)typeof(GenerationGate)
                        .GetField("m_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gate)!;
                    var counts = (long[])typeof(StripedCounter)
                        .GetField("m_counts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(counter)!;

                    // Model admission on the last real processor stripe, before closing the gate.
                    int parentSlot = counts.Length - 16;
                    Interlocked.Increment(ref counts[parentSlot]);
                    parent = new GenerationGate.Lease(gate, parentSlot);
                    gate.Retire();
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellation.Token), Is.True);
                    Task drained = gate.WaitForDrainAsync();
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellation.Token), Is.True);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(child, Is.GreaterThanOrEqualTo(0));
                            Assert.That(gate.ActiveCount, Is.EqualTo(1));
                            Assert.That(drained.IsCompleted, Is.False,
                                $"Drain completed during parent-to-child handoff at iteration {iteration}.");
                        });
                    }
                    finally
                    {
                        if (child >= 0)
                        {
                            gate.Exit(child);
                        }
                        parent.Dispose();
                    }
                    await drained.ConfigureAwait(false);
                    gate.DisposeGeneration();
                }
            }
            finally
            {
                cancellation.Cancel();
                await fork.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A refused late entrant cannot invalidate a completed closed-gate drain.
        /// </summary>
        [Test]
        public async Task ConfirmedDrainAllowsDisposalWhileLateEntryIsBeingRefusedAsync()
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            gate.Retire();
            Task drained = gate.WaitForDrainAsync();
            parent!.Dispose();
            await drained.ConfigureAwait(false);

            // Reproduce the counter state of a fork paused between its increment and parent recheck.
            var counter = (StripedCounter)typeof(GenerationGate)
                .GetField("m_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gate)!;
            int pending = counter.Enter();
            try
            {
                Assert.That(gate.DisposeGeneration, Throws.Nothing);
            }
            finally
            {
                counter.Exit(pending);
            }
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Disposal still refuses accepted work, including after a retired gate has been restored.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GenerationDisposalRequiresAcceptedWorkToDrain(bool restore)
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            if (restore)
            {
                gate.Retire();
                gate.Restore();
            }
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            gate.Retire();
            try
            {
                Assert.That(gate.DisposeGeneration, Throws.InvalidOperationException);
                Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            }
            finally
            {
                parent!.Dispose();
            }
            gate.DisposeGeneration();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
        }

        /// <summary>
        /// Closing an address-space epoch rejects new request pins and waits for the admitted pin.
        /// </summary>
        [Test]
        public async Task ClosedAddressSpaceEpochRefusesNewPinsAndWaitsForExistingPinAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int slot), Is.True);
            epoch.Close();

            Assert.That(epoch.TryPin(out int _), Is.False);
            Assert.That(epoch.IsDrained, Is.False);
            Task drained = epoch.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);

            epoch.Exit(slot);
            await drained.ConfigureAwait(false);

            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// Concurrent request pins racing view closure are either rejected or included in the drain.
        /// </summary>
        [Test]
        public async Task RequestAdmissionsRacingViewCloseRemainCountedUntilReleasedAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int initial), Is.True);
            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(9);
            var pins = new ConcurrentBag<int>();
            Task[] requestPins = new Task[8];
            for (int ii = 0; ii < requestPins.Length; ii++)
            {
                requestPins[ii] = Task.Factory.StartNew(
                    () =>
                    {
                        ready.Signal();
                        start.Wait();
                        if (epoch.TryPin(out int pin))
                        {
                            pins.Add(pin);
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            Task close = Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    epoch.Close();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            ready.Wait();
            start.Set();
            await Task.WhenAll(requestPins.Append(close)).ConfigureAwait(false);
            Assert.That(epoch.TryPin(out int _), Is.False);
            Task drained = epoch.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            epoch.Exit(initial);
            Assert.That(drained.IsCompleted, Is.EqualTo(pins.IsEmpty));
            foreach (int pin in pins)
            {
                epoch.Exit(pin);
            }
            await drained.ConfigureAwait(false);

            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// Concurrent admission racing retirement cannot escape the drain count.
        /// </summary>
        [Test]
        public async Task AdmissionsRacingGenerationRetirementRemainCountedUntilReleasedAsync()
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? initial), Is.True);
            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(9);
            var leases = new ConcurrentBag<GenerationGate.Lease>();
            Task[] entrants = new Task[8];
            for (int ii = 0; ii < entrants.Length; ii++)
            {
                entrants[ii] = Task.Factory.StartNew(
                    () =>
                    {
                        ready.Signal();
                        start.Wait();
                        if (gate.TryEnter(out GenerationGate.Lease? lease))
                        {
                            leases.Add(lease!);
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            Task retire = Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    gate.Retire();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            ready.Wait();
            start.Set();
            await Task.WhenAll(entrants.Append(retire)).ConfigureAwait(false);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? late), Is.False);
            late?.Dispose();
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            initial!.Dispose();
            Assert.That(drained.IsCompleted, Is.EqualTo(leases.IsEmpty));
            foreach (GenerationGate.Lease lease in leases)
            {
                lease.Dispose();
            }
            await drained.ConfigureAwait(false);

            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Failed and canceled service callbacks release the operation's view pin in cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OperationContextReleasesViewPinAfterExceptionOrCancellationAsync(bool cancel)
        {
            var table = new NodeManagerRoutingTable();
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            AddressSpaceEpoch epoch = context.RoutingSnapshot!.Epoch;

            async Task ExecuteFailedServiceAsync()
            {
                try
                {
                    await Task.Yield();
                    if (cancel)
                    {
                        using var canceled = new CancellationTokenSource();
                        canceled.Cancel();
                        await Task.Delay(Timeout.Infinite, canceled.Token).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("Controlled service failure.");
                }
                finally
                {
                    context.Dispose();
                }
            }

            Exception? failure = null;
            try
            {
                await ExecuteFailedServiceAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(
                failure,
                Is.TypeOf(cancel ? typeof(TaskCanceledException) : typeof(InvalidOperationException)));
            Assert.That(context.HasRoutingPin, Is.False);
            Assert.That(epoch.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Concurrent admission and context disposal never leave a request pin behind.
        /// </summary>
        [Test]
        public async Task ConcurrentRoutingPinAndContextDisposalReleaseTheViewAsync()
        {
            const int requestCount = 32;
            var table = new NodeManagerRoutingTable();
            var contexts = new OperationContext[requestCount];
            using var start = new ManualResetEventSlim();
            Task[] operations = new Task[requestCount * 2];
            for (int ii = 0; ii < requestCount; ii++)
            {
                var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                contexts[ii] = context;
                operations[ii * 2] = Task.Run(() =>
                {
                    start.Wait();
                    try
                    {
                        context.PinRouting(table);
                    }
                    catch (ObjectDisposedException)
                    {
                        Assert.That(context.HasRoutingPin, Is.False);
                    }
                });
                operations[ii * 2 + 1] = Task.Run(() =>
                {
                    start.Wait();
                    context.Dispose();
                });
            }

            start.Set();
            await Task.WhenAll(operations).ConfigureAwait(false);

            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
            foreach (OperationContext context in contexts)
            {
                Assert.That(context.HasRoutingPin, Is.False);
                Assert.That(context.RoutingSnapshot, Is.Null);
            }
        }

        /// <summary>
        /// A retired view drains independently of requests pinned to its replacement view.
        /// </summary>
        [Test]
        public async Task RetiredViewDrainIgnoresNewerViewRequestPinsAsync()
        {
            IAsyncNodeManager firstManager = new Mock<IAsyncNodeManager>().Object;
            IAsyncNodeManager secondManager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerGeneration first = NodeManagerGenerationRegistry.GetOrCreate(firstManager);
            NodeManagerGeneration second = NodeManagerGenerationRegistry.GetOrCreate(secondManager);
            var firstView = CreateView(firstManager);
            var secondView = CreateView(secondManager);

            Assert.That(firstView.TryPin(out int pin), Is.True);
            Assert.That(secondView.TryPin(out int currentPin), Is.True);
            firstView.Epoch.Close();
            first.Gate.Retire();

            Task firstDrained = first.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
            Assert.That(firstDrained.IsCompleted, Is.False);

            firstView.Epoch.Exit(pin);
            await firstDrained.ConfigureAwait(false);
            Assert.That(secondView.Epoch.ActiveCount, Is.EqualTo(1),
                "A request pinned to the newer view must not delay the retired generation.");

            secondView.Epoch.Close();
            second.Gate.Retire();
            Task secondDrained = second.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
            Assert.That(secondDrained.IsCompleted, Is.False);
            secondView.Epoch.Exit(currentPin);
            await secondDrained.ConfigureAwait(false);
        }

        private static NodeManagerRoutingTable.RoutingSnapshot CreateView(IAsyncNodeManager manager)
        {
            return new NodeManagerRoutingTable.RoutingSnapshot(
                [manager],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []);
        }
    }
}
