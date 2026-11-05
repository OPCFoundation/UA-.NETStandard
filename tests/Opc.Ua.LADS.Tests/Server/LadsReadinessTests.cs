/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Server.Hosting;
using LadsProgramRequest = Opc.Ua.LADS.Client.LadsProgramRequest;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests equipment-readiness confirmation independently of writer ownership or automatic execution recovery.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class LadsReadinessTests
    {
        /// <summary>
        /// Missing or rejected confirmation blocks program, unit and control starts, including after another restart.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InterruptedDeviceRequiresReadinessBeforeEveryStartPathAsync(bool configured)
        {
            InMemoryLadsStore store = await CreateStoreAsync().ConfigureAwait(false);
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            int confirmations = 0;
            Func<ILadsDevice, CancellationToken, ValueTask<bool>>? confirm = configured ? (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                confirmations++;
                return new ValueTask<bool>(false);
            } : null;
            for (int restart = 0; restart < 2; restart++)
            {
                await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                    nameof(InterruptedDeviceRequiresReadinessBeforeEveryStartPathAsync),
                    builder => Configure(builder, store, executor.Object, confirm), kDevice).ConfigureAwait(false);
                ILadsDevice device = await server.DeviceAsync(kDevice).ConfigureAwait(false);
                ILadsFunctionalUnit interrupted = device.GetFunctionalUnit(kInterrupted);
                ILadsFunctionalUnit other = device.GetFunctionalUnit(kOther);
                IAnalogControlFunction control = other.GetFunction<IAnalogControlFunction>("Control");
                Assert.That(device.RequiresReadinessConfirmation, Is.True);
                Assert.That(interrupted.Programs!.InterruptedRunId, Is.EqualTo(kRun));
                await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, async () =>
                    await other.Programs!.StartProgramAsync(kTemplate).ConfigureAwait(false)).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => other.StartAsync())
                    .ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => control.StartAsync())
                    .ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState,
                    () => control.StartWithTargetValueAsync(12)).ConfigureAwait(false);
                Assert.That(device.RequiresReadinessConfirmation, Is.True);
                Assert.That(other.Programs!.ActiveRun, Is.Null);
                Assert.That(other.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(control.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(control.TargetValue, Is.EqualTo(0));
                LadsStoredDeviceData saved = (await store.LoadAsync(kIdentity).ConfigureAwait(false))!;
                Assert.That(saved.FunctionalUnits[0].InterruptedRunId, Is.EqualTo(kRun));
            }
            Assert.That(confirmations, Is.EqualTo(configured ? 8 : 0));
            executor.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Failed confirmation or acknowledgement persistence cannot clear the block or dispatch an executor.
        /// </summary>
        [TestCase("exception")]
        [TestCase("cancellation")]
        [TestCase("store")]
        public async Task ReadinessFailurePreservesBlockAndAllowsRetryAsync(string failure)
        {
            InMemoryLadsStore memory = await CreateStoreAsync().ConfigureAwait(false);
            bool rejectSave = false;
            var store = new Mock<ILadsStore>(MockBehavior.Strict);
            store.Setup(value => value.LoadAsync(kIdentity, It.IsAny<CancellationToken>()))
                .Returns((string identity, CancellationToken ct) => memory.LoadAsync(identity, ct));
            store.Setup(value => value.SaveAsync(
                kIdentity, It.IsAny<LadsStoredDeviceData>(), It.IsAny<CancellationToken>()))
                .Returns((string identity, LadsStoredDeviceData snapshot, CancellationToken ct) =>
                    rejectSave ? new ValueTask(Task.FromException(new IOException("Readiness save rejected."))) :
                        memory.SaveAsync(identity, snapshot, ct));
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            executor.Setup(value => value.ExecuteAsync(It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var cancellation = new CancellationTokenSource();
            bool ready = false;
            int confirmations = 0;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ReadinessFailurePreservesBlockAndAllowsRetryAsync),
                builder => Configure(builder, store.Object, executor.Object, (device, ct) =>
                {
                    confirmations++;
                    Assert.That(device.RequiresReadinessConfirmation, Is.True);
                    if (!ready && failure == "exception")
                    {
                        throw new IOException("Equipment confirmation unavailable.");
                    }
                    if (!ready && failure == "cancellation")
                    {
                        cancellation.Cancel();
                        Assert.That(ct.IsCancellationRequested, Is.True);
                    }
                    return new ValueTask<bool>(true);
                }), kDevice).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync(kDevice).ConfigureAwait(false);
            ILadsProgramManager programs = device.GetFunctionalUnit(kOther).Programs!;
            rejectSave = failure == "store";
            if (failure == "cancellation")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await programs.StartProgramAsync(kTemplate, cancellationToken: cancellation.Token)
                        .ConfigureAwait(false));
            }
            else
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await programs.StartProgramAsync(kTemplate).ConfigureAwait(false));
                Assert.That(error!.Message, Is.EqualTo(failure == "store" ?
                    "Readiness save rejected." : "Equipment confirmation unavailable."));
            }
            Assert.That(device.RequiresReadinessConfirmation, Is.True);
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(device.GetFunctionalUnit(kOther).State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That((await memory.LoadAsync(kIdentity).ConfigureAwait(false))!.FunctionalUnits[0].InterruptedRunId,
                Is.EqualTo(kRun));
            executor.Verify(value => value.ExecuteAsync(
                It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()), Times.Never);
            ready = true;
            rejectSave = false;
            string run = await programs.StartProgramAsync(kTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(run).ConfigureAwait(false);
            Assert.That(device.RequiresReadinessConfirmation, Is.False);
            Assert.That(confirmations, Is.EqualTo(2));
            LadsStoredDeviceData saved = (await memory.LoadAsync(kIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].InterruptedRunId, Is.Null);
            Assert.That(saved.FunctionalUnits[0].LastInterruptedRunId, Is.EqualTo(kRun));
            executor.Verify(value => value.ExecuteAsync(
                It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Concurrent unit starts wait for one confirmation, then checkpoint acknowledgement for the whole device.
        /// </summary>
        [Test]
        public async Task ConcurrentStartsShareDeviceReadinessConfirmationAsync()
        {
            InMemoryLadsStore store = await CreateStoreAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int confirmations = 0;
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ConcurrentStartsShareDeviceReadinessConfirmationAsync),
                builder => Configure(builder, store, executor.Object, async (_, ct) =>
                {
                    Interlocked.Increment(ref confirmations);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return true;
                }), kDevice).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync(kDevice).ConfigureAwait(false);
            ILadsFunctionalUnit first = device.GetFunctionalUnit(kInterrupted);
            ILadsFunctionalUnit second = device.GetFunctionalUnit(kOther);
            Task start = first.StartAsync().AsTask();
            Task? queued = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                queued = second.StartAsync().AsTask();
                Assert.That(start.IsCompleted, Is.False);
                Assert.That(queued.IsCompleted, Is.False);
                Assert.That(first.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(second.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                release.TrySetResult(true);
                await Task.WhenAll(start, queued).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(device.RequiresReadinessConfirmation, Is.False);
                Assert.That(first.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(second.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                LadsStoredDeviceData saved = (await store.LoadAsync(kIdentity).ConfigureAwait(false))!;
                Assert.That(saved.FunctionalUnits[0].InterruptedRunId, Is.Null);
                Assert.That(saved.FunctionalUnits[0].LastInterruptedRunId, Is.EqualTo(kRun));
                executor.VerifyNoOtherCalls();
            }
            finally
            {
                release.TrySetResult(true);
                Task[] pending = queued == null ? [start] : [start, queued];
                await Task.WhenAll(pending)
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The wire start uses the same device readiness gate as a direct start.
        /// </summary>
        [Test]
        public async Task ClientStartRequiresEquipmentReadinessAsync()
        {
            InMemoryLadsStore store = await CreateStoreAsync().ConfigureAwait(false);
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            executor.Setup(value => value.ExecuteAsync(It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()))
                .Returns(async (LadsProgramRun run, CancellationToken ct) =>
                {
                    LadsStoredDeviceData saved = (await store.LoadAsync(kIdentity, ct).ConfigureAwait(false))!;
                    Assert.That(saved.FunctionalUnits[0].InterruptedRunId, Is.Null);
                    Assert.That(saved.FunctionalUnits[0].LastInterruptedRunId, Is.EqualTo(kRun));
                    Assert.That(saved.FunctionalUnits[1].InterruptedRunId, Is.EqualTo(run.RunId));
                });
            bool ready = false;
            int confirmations = 0;
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientStartRequiresEquipmentReadinessAsync),
                builder => Configure(builder, store, executor.Object, (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    confirmations++;
                    return new ValueTask<bool>(ready);
                }), kDevice).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync(kDevice).ConfigureAwait(false);
            NodeId unitId = device.GetFunctionalUnit(kOther).NodeId;
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, async () =>
                await connection.Client.StartProgramAsync(unitId, LadsProgramRequest.For(kTemplate))
                    .ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(device.RequiresReadinessConfirmation, Is.True);
            executor.Verify(value => value.ExecuteAsync(
                It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()), Times.Never);
            ready = true;
            string run = await connection.Client.StartProgramAsync(unitId, LadsProgramRequest.For(kTemplate))
                .ConfigureAwait(false);
            ILadsResult result = await device.GetFunctionalUnit(kOther).Programs!.WaitForResultAsync(run)
                .ConfigureAwait(false);
            Assert.That(result.RunId, Is.EqualTo(run));
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(device.RequiresReadinessConfirmation, Is.False);
            Assert.That(confirmations, Is.EqualTo(2));
            executor.Verify(value => value.ExecuteAsync(
                It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A device with no pending interruption does not need confirmation, including historical-only records.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UninterruptedDeviceDoesNotInvokeReadinessAsync(bool historical)
        {
            InMemoryLadsStore store = await CreateStoreAsync().ConfigureAwait(false);
            LadsStoredDeviceData saved = (await store.LoadAsync(kIdentity).ConfigureAwait(false))!;
            saved.FunctionalUnits[0].InterruptedRunId = null;
            saved.FunctionalUnits[0].LastInterruptedRunId = historical ? kRun : null;
            await store.SaveAsync(kIdentity, saved).ConfigureAwait(false);
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            executor.Setup(value => value.ExecuteAsync(It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(UninterruptedDeviceDoesNotInvokeReadinessAsync),
                builder => Configure(builder, store, executor.Object, (_, _) =>
                    throw new InvalidOperationException("No readiness confirmation should be requested.")),
                kDevice).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync(kDevice).ConfigureAwait(false);
            Assert.That(device.RequiresReadinessConfirmation, Is.False);
            ILadsProgramManager programs = device.GetFunctionalUnit(kOther).Programs!;
            string run = await programs.StartProgramAsync(kTemplate).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(run).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(device.GetFunctionalUnit(kInterrupted).Programs!.InterruptedRunId,
                Is.EqualTo(historical ? kRun : null));
            executor.Verify(value => value.ExecuteAsync(
                It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Device disposal cancels and drains an admitted readiness callback without acknowledging interruption.
        /// </summary>
        [Test]
        public async Task DisposalCancelsAndDrainsReadinessAsync()
        {
            InMemoryLadsStore store = await CreateStoreAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new Mock<ILadsProgramExecutor>(MockBehavior.Strict);
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(DisposalCancelsAndDrainsReadinessAsync),
                builder => Configure(builder, store, executor.Object, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                        return true;
                    }
                    finally
                    {
                        exited.TrySetResult(true);
                    }
                }), kDevice).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync(kDevice).ConfigureAwait(false);
            Task<string> start = device.GetFunctionalUnit(kOther).Programs!.StartProgramAsync(kTemplate).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await device.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(exited.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await start.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
                Assert.That(device.RequiresReadinessConfirmation, Is.True);
                LadsStoredDeviceData saved = (await store.LoadAsync(kIdentity).ConfigureAwait(false))!;
                Assert.That(saved.FunctionalUnits[0].InterruptedRunId, Is.EqualTo(kRun));
                executor.VerifyNoOtherCalls();
            }
            finally
            {
                await device.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Seeds a pending physical run independently of server shutdown behavior.
        /// </summary>
        private static async Task<InMemoryLadsStore> CreateStoreAsync()
        {
            var store = new InMemoryLadsStore();
            await store.SaveAsync(kIdentity, new LadsStoredDeviceData
            {
                Identity = kIdentity,
                FunctionalUnits = [new LadsStoredFunctionalUnitData { Name = kInterrupted, InterruptedRunId = kRun }]
            }).ConfigureAwait(false);
            return store;
        }

        /// <summary>
        /// Composes two units so an interruption in one blocks starts in the other.
        /// </summary>
        private static void Configure(
            IOpcUaServerBuilder builder,
            ILadsStore store,
            ILadsProgramExecutor executor,
            Func<ILadsDevice, CancellationToken, ValueTask<bool>>? confirm)
        {
            builder.AddLads(options =>
            {
                options.Store = store;
                options.ConfirmInterruptedDeviceReadinessAsync = confirm;
            }).ConfigureLads(context => context.AddDevice(kDevice, device => device
                .FunctionalUnit(kInterrupted, unit => unit.Programs(programs => programs
                    .Template(kTemplate, _ => { }).Executor(executor)))
                .FunctionalUnit(kOther, unit => unit.AnalogControl("Control").Programs(programs => programs
                    .Template(kTemplate, _ => { }).Executor(executor)))));
        }

        /// <summary>The device used by the readiness tests.</summary>
        private const string kDevice = "Recovered";

        /// <summary>The configured storage identity.</summary>
        private const string kIdentity = "urn:opcua-netstandard:lads:instances#Recovered";

        /// <summary>The unit that had a physical run interrupted.</summary>
        private const string kInterrupted = "Interrupted";

        /// <summary>The sibling unit whose starts must also wait for device readiness.</summary>
        private const string kOther = "Other";

        /// <summary>The configured program template.</summary>
        private const string kTemplate = "Recipe";

        /// <summary>The prior pending run, retained for historical reporting after acknowledgement.</summary>
        private const string kRun = "interrupted-run";
    }
}
