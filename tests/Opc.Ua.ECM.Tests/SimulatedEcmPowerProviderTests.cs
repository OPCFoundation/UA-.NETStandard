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
using Opc.Ua.ECM.Server;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Deterministic reference-provider state and wire-result tests.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SimulatedEcmPowerProviderTests
    {
        /// <summary>
        /// Proves acceptance does not imply arrival and IDs remain consistent.
        /// </summary>
        [Test]
        public async Task StartPausePublishesMovingBeforeArrival()
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            EcmControlResult result = await provider.StartPauseAsync(10000).ConfigureAwait(false);
            EcmPowerSnapshot moving = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(result.ReturnCode, Is.Zero);
            Assert.That(result.ModeId, Is.EqualTo(2));
            Assert.That(result.CurrentTimeToDestination, Is.EqualTo(2000));
            Assert.That(result.RegularTimeToOperate, Is.EqualTo(3000));
            Assert.That(result.TimeMinLengthOfStay, Is.EqualTo(2000));
            Assert.That(moving.Status, Is.EqualTo(EcmStandbyStatus.MovingToEnergySavingMode));
            Assert.That(moving.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(moving.DestinationModeId, Is.EqualTo(2));
            Assert.That(moving.RegularTimeToOperate, Is.Zero);
            Assert.That(moving.CurrentTimeToOperate, Is.EqualTo(7000));
            clock.Advance(1999);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.MovingToEnergySavingMode));
            clock.Advance(1);
            EcmPowerSnapshot arrived = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(arrived.Status, Is.EqualTo(EcmStandbyStatus.EnergySavingMode));
            Assert.That(arrived.SourceModeId, Is.EqualTo(2));
            Assert.That(arrived.DestinationModeId, Is.EqualTo(2));
            Assert.That(arrived.CurrentTimeToDestination, Is.Zero);
            Assert.That(arrived.EnergyConsumptionToDestination, Is.Zero);
            Assert.That(arrived.RegularTimeToOperate, Is.EqualTo(3000));
        }

        /// <summary>
        /// Proves repeated requests do not restart an accepted transition.
        /// </summary>
        [Test]
        public async Task RepeatedPauseRetainsArrivalTime()
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.StartPauseAsync(10000).ConfigureAwait(false);
            clock.Advance(500);
            EcmControlResult repeated = await provider.StartPauseAsync(11000).ConfigureAwait(false);
            Assert.That(repeated.CurrentTimeToDestination, Is.EqualTo(1500));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).PauseTime, Is.EqualTo(11000));
        }

        /// <summary>
        /// Proves unsuitable pauses return a device rejection without changing state.
        /// </summary>
        [Test]
        public async Task NoSuitableModePreservesReadyState()
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            EcmControlResult rejected = await provider.StartPauseAsync(1).ConfigureAwait(false);
            EcmPowerSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(rejected.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(rejected.ReturnCode, Is.EqualTo(0x50));
            Assert.That(rejected.ModeId, Is.Zero);
            Assert.That(snapshot.Status, Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
            Assert.That(snapshot.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(snapshot.DestinationModeId, Is.EqualTo(0xFF));
            Assert.That(snapshot.PauseTime, Is.Zero);
        }

        /// <summary>
        /// Proves all reserved mode IDs are rejected as ordinary configured modes.
        /// </summary>
        [TestCase(0)]
        [TestCase(0xF0)]
        [TestCase(0xFE)]
        [TestCase(0xFF)]
        public async Task ReservedModeRequestHasSeparateCallAndDeviceStatus(byte id)
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            EcmControlResult result = await provider.SwitchToEnergySavingModeAsync(id).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(result.ReturnCode, Is.EqualTo(0x52));
            Assert.That(result.ModeId, Is.EqualTo(0xFF));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
        }

        /// <summary>
        /// Proves a conflicting request cannot replace an in-flight transition.
        /// </summary>
        [Test]
        public async Task ModeChangeInTransitIsRejected()
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            await provider.SwitchToEnergySavingModeAsync(1).ConfigureAwait(false);
            EcmControlResult result = await provider.SwitchToEnergySavingModeAsync(2).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(result.ReturnCode, Is.EqualTo(0x54));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).DestinationModeId, Is.EqualTo(1));
        }

        /// <summary>
        /// Proves rejected StartPause outputs differ from the effective current mode returned by Switch.
        /// </summary>
        [Test]
        public async Task RejectedStartPauseClearsModeAndTimingsWhileSwitchRetainsCurrentMode()
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            await provider.SwitchToEnergySavingModeAsync(1).ConfigureAwait(false);

            EcmControlResult pause = await provider.StartPauseAsync(10000).ConfigureAwait(false);
            EcmControlResult switchMode = await provider.SwitchToEnergySavingModeAsync(2).ConfigureAwait(false);
            EcmPowerSnapshot state = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(pause.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(pause.ReturnCode, Is.EqualTo(0x54));
            Assert.That(pause.ModeId, Is.Zero);
            Assert.That(pause.CurrentTimeToDestination, Is.Zero);
            Assert.That(pause.RegularTimeToOperate, Is.Zero);
            Assert.That(pause.TimeMinLengthOfStay, Is.Zero);
            Assert.That(switchMode.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(switchMode.ReturnCode, Is.EqualTo(0x54));
            Assert.That(switchMode.ModeId, Is.EqualTo(0xFF));
            Assert.That(switchMode.CurrentTimeToDestination, Is.Zero);
            Assert.That(switchMode.RegularTimeToOperate, Is.Zero);
            Assert.That(switchMode.TimeMinLengthOfStay, Is.Zero);
            Assert.That(state.Status, Is.EqualTo(EcmStandbyStatus.MovingToEnergySavingMode));
            Assert.That(state.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(state.DestinationModeId, Is.EqualTo(1));
            Assert.That(state.PauseTime, Is.Zero);
        }

        /// <summary>
        /// Proves EndPause during entry includes unfinished entry, minimum stay and recovery.
        /// </summary>
        [Test]
        public async Task EndPauseInTransitDoesNotSkipRequiredStay()
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.SwitchToEnergySavingModeAsync(2).ConfigureAwait(false);
            clock.Advance(500);
            EcmControlResult result = await provider.EndPauseAsync().ConfigureAwait(false);
            Assert.That(result.CurrentTimeToOperate, Is.EqualTo(6500));
            EcmPowerSnapshot moving = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(moving.Status, Is.EqualTo(EcmStandbyStatus.MovingToReadyToOperate));
            Assert.That(moving.DestinationModeId, Is.EqualTo(0xFF));
            clock.Advance(6499);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.MovingToReadyToOperate));
            clock.Advance(1);
            EcmPowerSnapshot ready = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(ready.Status, Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
            Assert.That(ready.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(ready.DestinationModeId, Is.EqualTo(0xFF));
            Assert.That(ready.RegularTimeToOperate, Is.Zero);
            Assert.That(ready.PauseTime, Is.Zero);
        }

        /// <summary>
        /// Proves zero PauseTime follows the EndPause path.
        /// </summary>
        [Test]
        public async Task ZeroPauseReturnsToOperation()
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.StartPauseAsync(10000).ConfigureAwait(false);
            clock.Advance(4000);
            EcmControlResult result = await provider.StartPauseAsync(0).ConfigureAwait(false);
            Assert.That(result.CurrentTimeToOperate, Is.EqualTo(3000));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.MovingToReadyToOperate));
        }

        /// <summary>
        /// Proves WOL sleep/recovery changes only provider state.
        /// </summary>
        [Test]
        public async Task SimulatedWolRequiresExplicitWake()
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            EcmControlResult sleep = await provider.SwitchOffWolAsync().ConfigureAwait(false);
            Assert.That(sleep.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(sleep.ReturnCode, Is.Zero);
            Assert.That(sleep.ModeId, Is.EqualTo(0xFE));
            Assert.That(sleep.CurrentTimeToDestination, Is.EqualTo(1000));
            Assert.That(sleep.RegularTimeToOperate, Is.EqualTo(2000));
            Assert.That(sleep.TimeMinLengthOfStay, Is.EqualTo(1000));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.MovingToSleepWol));
            clock.Advance(1000);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.SleepWol));
            clock.Advance(100000);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.SleepWol));
            Assert.That((await provider.EndPauseAsync().ConfigureAwait(false)).ReturnCode, Is.EqualTo(0x54));
            EcmControlResult wake = await provider.SimulateWakeAsync().ConfigureAwait(false);
            Assert.That(wake.CurrentTimeToOperate, Is.EqualTo(2000));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.WakeUpWol));
            clock.Advance(2000);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
        }

        /// <summary>
        /// Proves the six-byte MAC is copied both on input and output.
        /// </summary>
        [Test]
        public void WolMacDoesNotAliasCallerStorage()
        {
            byte[] bytes = [2, 0, 0, 0, 0, 1];
            var provider = new SimulatedEcmPowerProvider(Modes(), ByteString.From(bytes));
            bytes[0] = 9;
            Assert.That(provider.MacAddress.Length, Is.EqualTo(6));
            Assert.That(provider.MacAddress[0], Is.EqualTo(2));
        }

        /// <summary>
        /// Proves cancellation cannot mutate device state.
        /// </summary>
        [Test]
        public async Task CancelledCommandDoesNotStartTransition()
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.StartPauseAsync(10000, cancelled.Token).ConfigureAwait(false));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
        }

        /// <summary>
        /// Proves invalid pause values are rejected before state changes.
        /// </summary>
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public async Task InvalidPauseIsRejected(double pauseTime)
        {
            SimulatedEcmPowerProvider provider = Create(new DeviceClock());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await provider.StartPauseAsync(pauseTime).ConfigureAwait(false));
        }

        /// <summary>
        /// The pause classifier includes the minimum pause and both stay endpoints, but excludes adjacent values.
        /// </summary>
        [TestCase(6999, false)]
        [TestCase(7000, true)]
        [TestCase(7001, true)]
        [TestCase(64999, true)]
        [TestCase(65000, true)]
        [TestCase(65001, false)]
        public async Task PauseEligibilityRespectsBothInclusiveDurationBoundaries(double pause, bool accepted)
        {
            var clock = new DeviceClock();
            var provider = new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(2, "DeepPause", 7000, 2000, 2000, 60000, 3000, 1, 0.2f, 0.3f)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()), timeProvider: clock);

            EcmControlResult result = await provider.StartPauseAsync(pause);
            EcmPowerSnapshot state = await provider.ReadAsync();

            Assert.That(result.Status, Is.EqualTo(new StatusCode(accepted ? StatusCodes.Good : StatusCodes.Uncertain)));
            Assert.That(result.ReturnCode, Is.EqualTo(accepted ? 0 : 0x50));
            Assert.That(state.Status, Is.EqualTo(accepted
                ? EcmStandbyStatus.MovingToEnergySavingMode : EcmStandbyStatus.ReadyToOperate));
            Assert.That(state.PauseTime, Is.EqualTo(accepted ? pause : 0));
        }

        /// <summary>
        /// Recovery estimates decrease through minimum stay and stop at the regular recovery duration.
        /// </summary>
        [TestCase(1999, 5001)]
        [TestCase(2000, 5000)]
        [TestCase(2001, 4999)]
        [TestCase(3999, 3001)]
        [TestCase(4000, 3000)]
        [TestCase(4001, 3000)]
        public async Task RecoveryEstimateIncludesOnlyRemainingMinimumStay(int elapsed, double expected)
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.SwitchToEnergySavingModeAsync(2);
            clock.Advance(elapsed);

            EcmPowerSnapshot state = await provider.ReadAsync();
            EcmControlResult result = await provider.EndPauseAsync();

            Assert.That(state.CurrentTimeToOperate, Is.EqualTo(expected));
            Assert.That(result.CurrentTimeToOperate, Is.EqualTo(expected));
            Assert.That(result.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
        }

        /// <summary>
        /// The simulator keeps configured mode identities independent of caller-owned array storage.
        /// </summary>
        [Test]
        public async Task ConfiguredModesDoNotAliasCallerStorage()
        {
            EcmEnergySavingMode[] modes = Modes().Span.ToArray();
            var provider = new SimulatedEcmPowerProvider(
                modes.ToArrayOf(), ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                timeProvider: new DeviceClock());
            modes[1] = new EcmEnergySavingMode(3, "Replacement", 7000, 2000, 2000, 60000, 3000, 0, 0, 0);

            EcmControlResult result = await provider.StartPauseAsync(10000);
            EcmPowerSnapshot state = await provider.ReadAsync();

            Assert.That(result.ModeId, Is.EqualTo(2));
            Assert.That(state.Modes[1].Name, Is.EqualTo("DeepPause"));
            Assert.That(state.Modes[1].ModePowerConsumption, Is.EqualTo(1));
        }

        /// <summary>
        /// Empty, null and duplicate configured identities cannot become provider state.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void InvalidModeCollectionsAreRejected(int partition)
        {
            EcmEnergySavingMode first = Modes()[0];
            ArrayOf<EcmEnergySavingMode> modes = partition switch
            {
                0 => [],
                1 => [null!],
                2 => [first, first],
                _ => [first, new EcmEnergySavingMode(2, first.Name, 7000, 2000, 2000, 60000, 3000, 1, 0, 0)]
            };

            Assert.Throws<ArgumentException>(() => new SimulatedEcmPowerProvider(
                modes, ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan())));
        }

        /// <summary>
        /// Repeated WOL requests retain the first arrival rather than restarting entry.
        /// </summary>
        [TestCase(0, 1000)]
        [TestCase(999, 1)]
        [TestCase(1000, 0)]
        [TestCase(1001, 0)]
        public async Task RepeatedWolRetainsOriginalArrival(int elapsed, double remaining)
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.SwitchOffWolAsync();
            clock.Advance(elapsed);

            EcmControlResult repeated = await provider.SwitchOffWolAsync();
            EcmPowerSnapshot state = await provider.ReadAsync();

            Assert.That(repeated.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(repeated.ReturnCode, Is.Zero);
            Assert.That(repeated.ModeId, Is.EqualTo(0xFE));
            Assert.That(repeated.CurrentTimeToDestination, Is.EqualTo(remaining));
            Assert.That(state.CurrentTimeToDestination, Is.EqualTo(remaining));
            Assert.That(state.Status, Is.EqualTo(elapsed < 1000 ?
                EcmStandbyStatus.MovingToSleepWol : EcmStandbyStatus.SleepWol));
        }

        /// <summary>
        /// Wake recovery includes only the remaining minimum sleep stay and never restarts on repetition.
        /// </summary>
        [TestCase(1000, 3000)]
        [TestCase(1001, 2999)]
        [TestCase(1999, 2001)]
        [TestCase(2000, 2000)]
        [TestCase(2001, 2000)]
        public async Task WolWakeRetainsRecoveryAndMinimumStay(int elapsed, double recovery)
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.SwitchOffWolAsync();
            clock.Advance(elapsed);

            EcmControlResult wake = await provider.SimulateWakeAsync();
            clock.Advance(1);
            EcmControlResult repeated = await provider.SimulateWakeAsync();
            EcmPowerSnapshot moving = await provider.ReadAsync();

            Assert.That(wake.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(wake.ReturnCode, Is.Zero);
            Assert.That(wake.CurrentTimeToOperate, Is.EqualTo(recovery));
            Assert.That(repeated.CurrentTimeToOperate, Is.EqualTo(recovery - 1));
            Assert.That(moving.Status, Is.EqualTo(EcmStandbyStatus.WakeUpWol));
            Assert.That(moving.SourceModeId, Is.EqualTo(0xFE));
            Assert.That(moving.DestinationModeId, Is.EqualTo(0xFF));
            clock.Advance(recovery - 2);
            Assert.That((await provider.ReadAsync()).Status, Is.EqualTo(EcmStandbyStatus.WakeUpWol));
            clock.Advance(1);
            EcmPowerSnapshot arrived = await provider.ReadAsync();
            Assert.That(arrived.Status, Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
            Assert.That(arrived.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(arrived.DestinationModeId, Is.EqualTo(0xFF));
            Assert.That(arrived.CurrentTimeToDestination, Is.Zero);
            Assert.That(arrived.CurrentTimeToOperate, Is.Zero);
        }

        /// <summary>
        /// Waking before sleep arrival is rejected without replacing the accepted sleep transition.
        /// </summary>
        [TestCase(0)]
        [TestCase(999)]
        public async Task PrematureWakePreservesSleepTransition(int elapsed)
        {
            var clock = new DeviceClock();
            SimulatedEcmPowerProvider provider = Create(clock);
            await provider.SwitchOffWolAsync();
            clock.Advance(elapsed);

            EcmControlResult rejected = await provider.SimulateWakeAsync();
            EcmPowerSnapshot state = await provider.ReadAsync();

            Assert.That(rejected.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(rejected.ReturnCode, Is.EqualTo(0x54));
            Assert.That(rejected.ModeId, Is.Zero);
            Assert.That(rejected.CurrentTimeToOperate, Is.Zero);
            Assert.That(state.Status, Is.EqualTo(EcmStandbyStatus.MovingToSleepWol));
            Assert.That(state.DestinationModeId, Is.EqualTo(0xFE));
            Assert.That(state.CurrentTimeToDestination, Is.EqualTo(1000 - elapsed));
        }

        /// <summary>
        /// Zero-duration modes complete on acquisition without inventing transition time or energy.
        /// </summary>
        [Test]
        public async Task ZeroDurationTransitionsCompleteOnAcquisition()
        {
            var provider = new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "Immediate", 0, 0, 0, 0, 0, 0, 0, 0)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                wolEntryTime: 0, wolRecoveryTime: 0, wolMinimumStay: 0, timeProvider: new DeviceClock());

            EcmControlResult pause = await provider.SwitchToEnergySavingModeAsync(1);
            EcmPowerSnapshot paused = await provider.ReadAsync();
            Assert.That(pause.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(paused.Status, Is.EqualTo(EcmStandbyStatus.EnergySavingMode));
            Assert.That(paused.SourceModeId, Is.EqualTo(1));
            Assert.That(paused.CurrentTimeToDestination, Is.Zero);
            Assert.That(paused.EnergyConsumptionToDestination, Is.Zero);

            EcmControlResult end = await provider.EndPauseAsync();
            Assert.That(end.CurrentTimeToOperate, Is.Zero);
            Assert.That((await provider.ReadAsync()).Status, Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
            EcmControlResult sleep = await provider.SwitchOffWolAsync();
            Assert.That(sleep.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(sleep.ModeId, Is.EqualTo(0xFE));
            Assert.That((await provider.ReadAsync()).Status, Is.EqualTo(EcmStandbyStatus.SleepWol));
            EcmControlResult wake = await provider.SimulateWakeAsync();
            Assert.That(wake.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(wake.CurrentTimeToOperate, Is.Zero);
            Assert.That((await provider.ReadAsync()).Status, Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
        }

        /// <summary>
        /// Finite transitions remain representable even when their arrival exceeds the clock's calendar range.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TransitionEstimateDoesNotOverflowCalendarRange(bool wol, bool maximumDuration)
        {
            double duration = maximumDuration ? TimeSpan.MaxValue.TotalMilliseconds : 2000;
            var clock = new DeviceClock(DateTimeOffset.MaxValue.AddMilliseconds(-500));
            var provider = new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "LongPause", 0, duration, 1000, 1000, 2000, 1, 0, 0)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                wolEntryTime: duration, timeProvider: clock);

            EcmControlResult accepted = wol ?
                await provider.SwitchOffWolAsync() : await provider.SwitchToEnergySavingModeAsync(1);
            clock.Advance(499);
            EcmPowerSnapshot state = await provider.ReadAsync();

            Assert.That(accepted.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(accepted.CurrentTimeToDestination, Is.EqualTo(duration));
            Assert.That(state.CurrentTimeToDestination, Is.EqualTo(duration - 499).Within(1));
            Assert.That(state.CurrentTimeToOperate, Is.EqualTo(duration - 499 + 3000).Within(1));
            Assert.That(state.Status, Is.EqualTo(wol ?
                EcmStandbyStatus.MovingToSleepWol : EcmStandbyStatus.MovingToEnergySavingMode));
            Assert.That(state.SourceModeId, Is.EqualTo(0xFF));
            Assert.That(state.DestinationModeId, Is.EqualTo(wol ? 0xFE : 1));
            if (!wol)
            {
                EcmControlResult reverse = await provider.EndPauseAsync();
                EcmPowerSnapshot recovering = await provider.ReadAsync();
                Assert.That(reverse.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
                Assert.That(reverse.CurrentTimeToOperate, Is.EqualTo(duration - 499 + 3000).Within(1));
                Assert.That(recovering.Status, Is.EqualTo(EcmStandbyStatus.MovingToReadyToOperate));
                Assert.That(recovering.CurrentTimeToDestination,
                    Is.EqualTo(reverse.CurrentTimeToOperate).Within(1));
            }
        }

        /// <summary>
        /// Finite individual durations cannot publish an infinite aggregate recovery estimate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OverflowingModeRecoveryIsRejectedBeforeDeviceStateExists(bool minimumStay)
        {
            var mode = new EcmEnergySavingMode(1, "Overflow", 0, double.MaxValue,
                minimumStay ? double.MaxValue : 0, double.MaxValue,
                minimumStay ? 0 : double.MaxValue, 0, 0, 0);

            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimulatedEcmPowerProvider(
                    [mode], ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan())))!;

            Assert.That(error.ParamName, Is.EqualTo("modes"));
        }

        /// <summary>
        /// Creates one simulated reference device.
        /// </summary>
        private static SimulatedEcmPowerProvider Create(DeviceClock clock)
        {
            return new SimulatedEcmPowerProvider(
                Modes(), ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()), timeProvider: clock);
        }

        /// <summary>
        /// Creates modes with distinct duration and power characteristics.
        /// </summary>
        private static ArrayOf<EcmEnergySavingMode> Modes()
        {
            return
            [
                new EcmEnergySavingMode(1, "ShortPause", 3000, 1000, 1000, 60000, 1000, 2, 0.1f, 0.1f),
                new EcmEnergySavingMode(2, "DeepPause", 7000, 2000, 2000, 60000, 3000, 1, 0.2f, 0.3f)
            ];
        }

        /// <summary>
        /// Injectable device time without wall-clock sleeps.
        /// </summary>
        private sealed class DeviceClock : TimeProvider
        {
            /// <summary>
            /// Creates a deterministic clock at a supplied acquisition time or the ordinary test epoch.
            /// </summary>
            public DeviceClock(DateTimeOffset? initial = null)
            {
                m_now = initial ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return m_now;
            }

            /// <summary>
            /// Advances device time in milliseconds.
            /// </summary>
            public void Advance(double milliseconds)
            {
                m_now = m_now.AddMilliseconds(milliseconds);
            }

            /// <summary>The controlled device time.</summary>
            private DateTimeOffset m_now;
        }
    }
}
