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
using NUnit.Framework;
using PadimSample;

namespace Opc.Ua.PADIM.Tests
{
    /// <summary>
    /// Exercises the sample's virtual device clock and operation lifecycle without a server or wall-clock delays.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PadimSampleTests
    {
        /// <summary>
        /// An uncertain enable result still restores acquisition and propagates the original failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SampleSimulationDisablesAfterEnableFailure(bool cancelled)
        {
            bool simulating = false;
            bool read = false;
            int writes = 0;
            CancellationToken disableToken = new CancellationToken(canceled: true);
            CancellationToken callerToken = new CancellationToken(canceled: cancelled);
            Exception failure = cancelled
                ? new OperationCanceledException(callerToken)
                : new InvalidOperationException("The enable notification failed after commit.");
            ValueTask WriteStateAsync(bool enabled, CancellationToken token)
            {
                writes++;
                simulating = enabled;
                if (enabled)
                {
                    throw failure;
                }
                disableToken = token;
                return default;
            }
            ValueTask ReadAsync(CancellationToken token)
            {
                read = true;
                return default;
            }

            if (cancelled)
            {
                OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await DemonstrationSimulation.RunAsync(WriteStateAsync, ReadAsync, callerToken));
                Assert.That(error, Is.SameAs(failure));
            }
            else
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await DemonstrationSimulation.RunAsync(WriteStateAsync, ReadAsync, callerToken));
                Assert.That(error, Is.SameAs(failure));
            }
            Assert.Multiple(() =>
            {
                Assert.That(simulating, Is.False);
                Assert.That(read, Is.False);
                Assert.That(writes, Is.EqualTo(2));
                Assert.That(disableToken, Is.EqualTo(CancellationToken.None));
            });
        }

        /// <summary>
        /// Successful enable restores acquisition whether reading succeeds or fails.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SampleSimulationDisablesAfterRead(bool failRead)
        {
            bool simulating = false;
            bool read = false;
            int writes = 0;
            CancellationToken disableToken = new CancellationToken(canceled: true);
            ValueTask WriteStateAsync(bool enabled, CancellationToken token)
            {
                writes++;
                simulating = enabled;
                if (!enabled)
                {
                    disableToken = token;
                }
                return default;
            }
            ValueTask ReadAsync(CancellationToken token)
            {
                read = simulating;
                if (failRead)
                {
                    throw new InvalidOperationException("The simulated read failed.");
                }
                return default;
            }
            if (failRead)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await DemonstrationSimulation.RunAsync(WriteStateAsync, ReadAsync, CancellationToken.None));
            }
            else
            {
                await DemonstrationSimulation.RunAsync(WriteStateAsync, ReadAsync, CancellationToken.None);
            }
            Assert.Multiple(() =>
            {
                Assert.That(read, Is.True);
                Assert.That(simulating, Is.False);
                Assert.That(writes, Is.EqualTo(2));
                Assert.That(disableToken, Is.EqualTo(CancellationToken.None));
            });
        }

        /// <summary>
        /// Separates method acceptance from completion and rejects a repeat without postponing completion.
        /// </summary>
        [Test]
        public async Task SamplePositionerCompletesAfterTwoRefreshesWithoutRestartingOnRepeatedStart()
        {
            var provider = new DemonstrationDevice(true);
            Assert.That(await provider.WriteAsync("position", Variant.From(12f)), Is.EqualTo(StatusCodes.Good));
            DataValue originalChange = await provider.ReadAsync("dateOfLastChange");

            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.Good));
            DataValue accepted = await provider.ReadAsync("position");
            Assert.That(accepted.WrappedValue, Is.EqualTo(Variant.From(12f)));
            await provider.RefreshAsync(CancellationToken.None);
            DataValue pending = await provider.ReadAsync("position");
            Assert.That(pending.WrappedValue, Is.EqualTo(Variant.From(12f)));
            Assert.That((await provider.ReadAsync("dateOfLastChange")).WrappedValue,
                Is.EqualTo(originalChange.WrappedValue));
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.BadInvalidState));

            await provider.RefreshAsync(CancellationToken.None);
            DataValue completed = await provider.ReadAsync("position");
            Assert.That(completed.WrappedValue, Is.EqualTo(Variant.From(50f)));
            Assert.That(completed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(completed.SourceTimestamp,
                Is.EqualTo(new DateTimeUtc(new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc))));
            Assert.That((await provider.ReadAsync("dateOfLastChange")).WrappedValue,
                Is.EqualTo(Variant.From(new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc))));
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Aborting cancels the pending completion rather than changing the acquired position.
        /// </summary>
        [Test]
        public async Task SamplePositionerAbortPreservesAcquisitionAndAllowsAnotherStart()
        {
            var provider = new DemonstrationDevice(true);
            Assert.That(await provider.WriteAsync("position", Variant.From(12f)), Is.EqualTo(StatusCodes.Good));
            DataValue originalChange = await provider.ReadAsync("dateOfLastChange");
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.Good));
            await provider.RefreshAsync(CancellationToken.None);
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Abort),
                Is.EqualTo(StatusCodes.Good));
            await provider.RefreshAsync(CancellationToken.None);
            await provider.RefreshAsync(CancellationToken.None);

            Assert.That((await provider.ReadAsync("position")).WrappedValue, Is.EqualTo(Variant.From(12f)));
            Assert.That((await provider.ReadAsync("dateOfLastChange")).WrappedValue,
                Is.EqualTo(originalChange.WrappedValue));
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Abort),
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A cancelled refresh cannot advance either the deterministic clock or a pending operation.
        /// </summary>
        [Test]
        public async Task CancelledSampleRefreshDoesNotAdvancePendingCompletion()
        {
            var provider = new DemonstrationDevice(true);
            Assert.That(await provider.WriteAsync("position", Variant.From(12f)), Is.EqualTo(StatusCodes.Good));
            Assert.That(await provider.AutoAdjustPositionerAsync("position", ExecutionModeEnum.Start),
                Is.EqualTo(StatusCodes.Good));
            DataValue before = await provider.ReadAsync("position");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.RefreshAsync(cancellation.Token));

            DataValue after = await provider.ReadAsync("position");
            Assert.That(after.WrappedValue, Is.EqualTo(before.WrappedValue));
            Assert.That(after.SourceTimestamp, Is.EqualTo(before.SourceTimestamp));
            await provider.RefreshAsync(CancellationToken.None);
            Assert.That((await provider.ReadAsync("position")).WrappedValue, Is.EqualTo(Variant.From(12f)));
            await provider.RefreshAsync(CancellationToken.None);
            Assert.That((await provider.ReadAsync("position")).WrappedValue, Is.EqualTo(Variant.From(50f)));
        }

        /// <summary>
        /// The virtual pH adjustment publishes its neutral measurement and calibration time together.
        /// </summary>
        [Test]
        public async Task SamplePhAdjustmentPublishesNeutralValueAndCalibrationTimestamp()
        {
            var provider = new DemonstrationDevice(true);
            Assert.That(await provider.WriteAsync("ph", Variant.From(6f)), Is.EqualTo(StatusCodes.Good));
            await provider.RefreshAsync(CancellationToken.None);
            Assert.That(await provider.ZeroPointAdjustmentAsync("unknown"),
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That((await provider.ReadAsync("ph")).WrappedValue, Is.EqualTo(Variant.From(6f)));
            Assert.That((await provider.ReadAsync("ph/calibratedAt")).WrappedValue,
                Is.EqualTo(Variant.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));

            Assert.That(await provider.ZeroPointAdjustmentAsync("ph"), Is.EqualTo(StatusCodes.Good));

            DataValue measurement = await provider.ReadAsync("ph");
            DataValue calibratedAt = await provider.ReadAsync("ph/calibratedAt");
            Assert.That(measurement.WrappedValue, Is.EqualTo(Variant.From(7f)));
            Assert.That(calibratedAt.WrappedValue,
                Is.EqualTo(Variant.From(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc))));
            Assert.That(calibratedAt.SourceTimestamp, Is.EqualTo(measurement.SourceTimestamp));
        }
    }
}
