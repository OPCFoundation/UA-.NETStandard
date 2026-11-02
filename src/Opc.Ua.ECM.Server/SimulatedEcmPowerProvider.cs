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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Deterministic reference power provider. It changes only in-memory state;
    /// it never changes host power, sends wake packets or disconnects the server.
    /// </summary>
    public sealed class SimulatedEcmPowerProvider : IEcmStandbyProvider, IEcmWolProvider
    {
        /// <summary>
        /// Creates a simulated device with explicit modes and WOL metadata.
        /// </summary>
        public SimulatedEcmPowerProvider(
            ArrayOf<EcmEnergySavingMode> modes,
            ByteString macAddress,
            double wolEntryTime = 1000,
            double wolRecoveryTime = 2000,
            double wolMinimumStay = 1000,
            TimeProvider? timeProvider = null)
        {
            if (modes.Count == 0)
            {
                throw new ArgumentException("At least one energy-saving mode is required.", nameof(modes));
            }
            if (macAddress.Length != 6)
            {
                throw new ArgumentException("WOL requires exactly six MAC address bytes.", nameof(macAddress));
            }
            ValidateDuration(wolEntryTime, nameof(wolEntryTime));
            ValidateDuration(wolRecoveryTime, nameof(wolRecoveryTime));
            ValidateDuration(wolMinimumStay, nameof(wolMinimumStay));
            EcmControlResult.ValidateDuration(wolEntryTime + wolMinimumStay + wolRecoveryTime, nameof(wolEntryTime));
            var ids = new HashSet<byte>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < modes.Count; ii++)
            {
                EcmEnergySavingMode mode = modes[ii] ??
                    throw new ArgumentException("Modes must not contain null.", nameof(modes));
                EcmControlResult.ValidateDuration(
                    mode.TimeToPause + mode.TimeMinLengthOfStay + mode.RegularTimeToOperate, nameof(modes));
                if (!ids.Add(mode.Id) || !names.Add(mode.Name))
                {
                    throw new ArgumentException("Mode IDs and names must be unique.", nameof(modes));
                }
            }
            m_modes = modes.Span.ToArray().ToArrayOf();
            m_macAddress = ByteString.From(macAddress.Span);
            m_wolEntryTime = wolEntryTime;
            RegularTimeToOperate = wolRecoveryTime;
            m_wolMinimumStay = wolMinimumStay;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ByteString MacAddress => ByteString.From(m_macAddress.Span);

        /// <inheritdoc/>
        public double RegularTimeToOperate { get; }

        /// <inheritdoc/>
        public double TimeMinPause => m_wolEntryTime + m_wolMinimumStay + RegularTimeToOperate;

        /// <inheritdoc/>
        public uint ModePowerConsumption => 0;

        /// <inheritdoc/>
        public ValueTask<EcmPowerSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                EcmEnergySavingMode? mode = FindMode(m_destination);
                double remaining = Remaining(now);
                double recovery = RecoveryTime(now);
                float power = FindMode(m_source)?.ModePowerConsumption ?? 0;
                float energy = 0;
                if (remaining > 0)
                {
                    energy = m_status == EcmStandbyStatus.MovingToReadyToOperate ?
                        FindMode(m_source)?.EnergyConsumptionToOperate ?? 0 : mode?.EnergyConsumptionToPause ?? 0;
                }
                var snapshot = new EcmPowerSnapshot(
                    m_status,
                    m_source,
                    m_destination,
                    m_pauseTime,
                    remaining,
                    recovery,
                    m_source == Ready ? 0 : FindMode(m_source)?.RegularTimeToOperate ??
                        (m_source == Wol ? RegularTimeToOperate : 0),
                    power,
                    energy,
                    StatusCodes.Good,
                    now.UtcDateTime,
                    m_modes);
                return new ValueTask<EcmPowerSnapshot>(snapshot);
            }
        }

        /// <inheritdoc/>
        public ValueTask<EcmControlResult> StartPauseAsync(
            double pauseTime,
            CancellationToken cancellationToken = default)
        {
            ValidateDuration(pauseTime, nameof(pauseTime));
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                if (pauseTime == 0)
                {
                    return new ValueTask<EcmControlResult>(EndPauseCore(now));
                }
                EcmEnergySavingMode? selected = null;
                for (int ii = 0; ii < m_modes.Count; ii++)
                {
                    EcmEnergySavingMode mode = m_modes[ii];
                    double stay = pauseTime - mode.TimeToPause - mode.RegularTimeToOperate;
                    if (pauseTime >= mode.TimeMinPause && stay >= mode.TimeMinLengthOfStay &&
                        stay <= mode.TimeMaxLengthOfStay &&
                        (selected == null || mode.ModePowerConsumption < selected.ModePowerConsumption))
                    {
                        selected = mode;
                    }
                }
                if (selected == null)
                {
                    return new ValueTask<EcmControlResult>(Reject(0x50));
                }
                EcmControlResult result = SwitchCore(selected, now);
                if (StatusCode.IsGood(result.Status))
                {
                    m_pauseTime = pauseTime;
                }
                else
                {
                    return new ValueTask<EcmControlResult>(new EcmControlResult(result.Status, result.ReturnCode));
                }
                return new ValueTask<EcmControlResult>(result);
            }
        }

        /// <inheritdoc/>
        public ValueTask<EcmControlResult> EndPauseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                return new ValueTask<EcmControlResult>(EndPauseCore(now));
            }
        }

        /// <inheritdoc/>
        public ValueTask<EcmControlResult> SwitchToEnergySavingModeAsync(
            byte modeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                EcmEnergySavingMode? mode = FindMode(modeId);
                return new ValueTask<EcmControlResult>(mode == null
                    ? Reject(0x52, m_source)
                    : SwitchCore(mode, now));
            }
        }

        /// <inheritdoc/>
        public ValueTask<EcmControlResult> SwitchOffWolAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                if (m_status is EcmStandbyStatus.SleepWol or EcmStandbyStatus.MovingToSleepWol)
                {
                    return new ValueTask<EcmControlResult>(WolResult(Remaining(now)));
                }
                if (m_status is not (EcmStandbyStatus.ReadyToOperate or EcmStandbyStatus.EnergySavingMode))
                {
                    return new ValueTask<EcmControlResult>(Reject(0x54));
                }
                BeginTransition(EcmStandbyStatus.MovingToSleepWol, Wol, m_wolEntryTime, now);
                return new ValueTask<EcmControlResult>(WolResult(m_wolEntryTime));
            }
        }

        /// <summary>
        /// Simulates receipt of a wake request without sending any network traffic.
        /// </summary>
        public ValueTask<EcmControlResult> SimulateWakeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_stateGate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                UpdateArrival(now);
                if (m_status == EcmStandbyStatus.WakeUpWol)
                {
                    return new ValueTask<EcmControlResult>(
                        new EcmControlResult(StatusCodes.Good, 0, currentTimeToOperate: Remaining(now)));
                }
                if (m_status != EcmStandbyStatus.SleepWol)
                {
                    return new ValueTask<EcmControlResult>(Reject(0x54));
                }
                double recovery = RecoveryTime(now);
                BeginTransition(EcmStandbyStatus.WakeUpWol, Ready, recovery, now);
                return new ValueTask<EcmControlResult>(
                    new EcmControlResult(StatusCodes.Good, 0, currentTimeToOperate: recovery));
            }
        }

        /// <summary>
        /// Starts a requested energy-saving transition, or reports an incompatible state.
        /// </summary>
        private EcmControlResult SwitchCore(EcmEnergySavingMode mode, DateTimeOffset now)
        {
            if (m_destination == mode.Id &&
                m_status is EcmStandbyStatus.MovingToEnergySavingMode or EcmStandbyStatus.EnergySavingMode)
            {
                return ModeResult(mode, Remaining(now));
            }
            if (m_status is not (EcmStandbyStatus.ReadyToOperate or EcmStandbyStatus.EnergySavingMode))
            {
                return Reject(0x54, m_source);
            }
            BeginTransition(EcmStandbyStatus.MovingToEnergySavingMode, mode.Id, mode.TimeToPause, now);
            return ModeResult(mode, mode.TimeToPause);
        }

        /// <summary>
        /// Requests operation without bypassing entry or minimum-stay time.
        /// </summary>
        private EcmControlResult EndPauseCore(DateTimeOffset now)
        {
            if (m_status == EcmStandbyStatus.ReadyToOperate)
            {
                m_pauseTime = 0;
                return new EcmControlResult(StatusCodes.Good, 0);
            }
            if (m_status == EcmStandbyStatus.MovingToReadyToOperate)
            {
                return new EcmControlResult(StatusCodes.Good, 0, currentTimeToOperate: Remaining(now));
            }
            if (m_status is not (EcmStandbyStatus.EnergySavingMode or EcmStandbyStatus.MovingToEnergySavingMode))
            {
                return Reject(0x54);
            }
            double recovery = RecoveryTime(now);
            byte source = m_destination;
            BeginTransition(EcmStandbyStatus.MovingToReadyToOperate, Ready, recovery, now);
            m_source = source;
            m_pauseTime = 0;
            return new EcmControlResult(StatusCodes.Good, 0, currentTimeToOperate: recovery);
        }

        /// <summary>
        /// Starts a transition; arrival is published only on a subsequent provider read.
        /// </summary>
        private void BeginTransition(EcmStandbyStatus status, byte destination, double duration, DateTimeOffset now)
        {
            m_status = status;
            m_destination = destination;
            m_transitionStarted = now;
            m_transitionDuration = duration;
        }

        /// <summary>
        /// Advances the simulated physical state according to the supplied time provider.
        /// </summary>
        private void UpdateArrival(DateTimeOffset now)
        {
            if (Remaining(now) > 0)
            {
                return;
            }
            switch (m_status)
            {
                case EcmStandbyStatus.MovingToEnergySavingMode:
                    m_status = EcmStandbyStatus.EnergySavingMode;
                    break;
                case EcmStandbyStatus.MovingToSleepWol:
                    m_status = EcmStandbyStatus.SleepWol;
                    break;
                case EcmStandbyStatus.MovingToReadyToOperate:
                case EcmStandbyStatus.WakeUpWol:
                    m_status = EcmStandbyStatus.ReadyToOperate;
                    break;
                default:
                    return;
            }
            m_source = m_destination;
        }

        /// <summary>
        /// Gets the recovery estimate, including unfinished entry and required minimum stay.
        /// </summary>
        private double RecoveryTime(DateTimeOffset now)
        {
            if (m_status is EcmStandbyStatus.MovingToReadyToOperate or EcmStandbyStatus.WakeUpWol)
            {
                return Remaining(now);
            }
            EcmEnergySavingMode? mode = FindMode(m_destination);
            double minimumStay = m_destination == Wol ? m_wolMinimumStay : mode?.TimeMinLengthOfStay ?? 0;
            double recovery = m_destination == Wol ? RegularTimeToOperate : mode?.RegularTimeToOperate ?? 0;
            return Math.Max(0, m_transitionDuration - (now - m_transitionStarted).TotalMilliseconds + minimumStay) +
                recovery;
        }

        /// <summary>
        /// Gets the remaining transition time.
        /// </summary>
        private double Remaining(DateTimeOffset now)
        {
            return Math.Max(0, m_transitionDuration - (now - m_transitionStarted).TotalMilliseconds);
        }

        /// <summary>
        /// Finds a configured nonreserved mode.
        /// </summary>
        private EcmEnergySavingMode? FindMode(byte id)
        {
            for (int ii = 0; ii < m_modes.Count; ii++)
            {
                if (m_modes[ii].Id == id)
                {
                    return m_modes[ii];
                }
            }
            return null;
        }

        /// <summary>
        /// Produces an accepted mode result.
        /// </summary>
        private static EcmControlResult ModeResult(EcmEnergySavingMode mode, double remaining)
        {
            return new EcmControlResult(StatusCodes.Good, 0, mode.Id, remaining,
                mode.RegularTimeToOperate, mode.TimeMinLengthOfStay);
        }

        /// <summary>
        /// Produces an accepted WOL result.
        /// </summary>
        private EcmControlResult WolResult(double remaining)
        {
            return new EcmControlResult(StatusCodes.Good, 0, Wol, remaining,
                RegularTimeToOperate, m_wolMinimumStay);
        }

        /// <summary>
        /// Produces a device rejection without converting it into a bad Call status.
        /// </summary>
        private static EcmControlResult Reject(byte returnCode, byte effectiveMode = 0)
        {
            return new EcmControlResult(StatusCodes.Uncertain, returnCode, effectiveMode);
        }

        /// <summary>
        /// Checks a finite nonnegative simulator duration.
        /// </summary>
        private static void ValidateDuration(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 ||
                value > TimeSpan.MaxValue.TotalMilliseconds)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }

        /// <summary>The reserved ready-to-operate mode.</summary>
        private const byte Ready = 0xFF;

        /// <summary>The reserved WOL mode.</summary>
        private const byte Wol = 0xFE;

        /// <summary>Serializes simulated device state.</summary>
        private readonly System.Threading.Lock m_stateGate = new();

        /// <summary>The immutable configured modes.</summary>
        private readonly ArrayOf<EcmEnergySavingMode> m_modes;

        /// <summary>The copied MAC address.</summary>
        private readonly ByteString m_macAddress;

        /// <summary>The simulated WOL entry time.</summary>
        private readonly double m_wolEntryTime;

        /// <summary>The minimum simulated WOL stay.</summary>
        private readonly double m_wolMinimumStay;

        /// <summary>The injectable device clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The current physical state.</summary>
        private EcmStandbyStatus m_status = EcmStandbyStatus.ReadyToOperate;

        /// <summary>The source mode.</summary>
        private byte m_source = Ready;

        /// <summary>The destination mode.</summary>
        private byte m_destination = Ready;

        /// <summary>The accepted pause duration.</summary>
        private double m_pauseTime;

        /// <summary>The current transition's acquisition-time origin.</summary>
        private DateTimeOffset m_transitionStarted = DateTimeOffset.MinValue;

        /// <summary>The accepted transition duration, independent of the clock's remaining calendar range.</summary>
        private double m_transitionDuration;
    }
}
