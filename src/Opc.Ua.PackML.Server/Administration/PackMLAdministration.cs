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
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Projects OPC 30050 section 6.3.4 administration data and accounts for runtime
    /// with a monotonic clock. The application owns authorization, equipment counters,
    /// alarm integration, refresh scheduling and durable snapshot storage.
    /// </summary>
    public sealed class PackMLAdministration
    {
        /// <summary>
        /// Creates administration accounting for a materialized PackML Admin object.
        /// The highest concrete mode value determines array sizes, not the number of modes.
        /// </summary>
        public PackMLAdministration(
            ISystemContext context,
            PackMLAdminObjectState node,
            TimeProvider timeProvider,
            int maxMode)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (maxMode < 1 || maxMode > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMode));
            }
            node.AddParameter(context);
            node.AddAlarm(context);
            node.AddAlarmExtent(context);
            node.AddAlarmHistory(context);
            node.AddAlarmHistoryExtent(context);
            node.AddWarning(context);
            node.AddWarningExtent(context);
            node.AddStopReason(context);
            node.AddStopReasonExtent(context);
            node.AddModeCurrentTime(context);
            node.AddModeCumulativeTime(context);
            node.AddStateCurrentTime(context);
            node.AddStateCumulativeTime(context);
            node.AddProdConsumedCount(context);
            node.AddProdProcessedCount(context);
            node.AddProdDefectiveCount(context);
            node.AddAccTimeSinceReset(context);
            node.AddMachDesignSpeed(context);
            m_modeCurrent = new long[maxMode + 1];
            m_modeCumulative = new long[maxMode + 1];
            m_stateCurrent = new long[maxMode + 1, k_stateCount];
            m_stateCumulative = new long[maxMode + 1, k_stateCount];
            m_lastTimestamp = m_timeProvider.GetTimestamp();
            PublishTimers();
        }

        /// <summary>
        /// Records the actual mode and innermost active state. Duplicate notifications
        /// do not restart an interval. State zero deactivates machine accounting.
        /// </summary>
        public void RecordState(int mode, uint state)
        {
            if (mode < 0 || mode >= m_modeCurrent.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (state >= k_stateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            lock (m_gate)
            {
                Accumulate();
                if (!m_active || mode != m_mode)
                {
                    if (state != 0)
                    {
                        m_modeCurrent[mode] = 0;
                    }
                }
                if ((!m_active || mode != m_mode || state != m_state) && state != 0)
                {
                    m_stateCurrent[mode, state] = 0;
                }
                m_mode = mode;
                m_state = state;
                m_active = state != 0;
                PublishTimers();
            }
        }

        /// <summary>
        /// Refreshes runtime values without changing the active state.
        /// Fractional seconds survive refreshes and state transitions.
        /// </summary>
        public void Refresh()
        {
            lock (m_gate)
            {
                Accumulate();
                PublishTimers();
            }
        }

        /// <summary>
        /// Resets administrative timers and published production Count fields only.
        /// Never invokes a state-machine Reset or resets AccCount. The application
        /// must first authorize and reset equipment-owned Count fields through its provider.
        /// </summary>
        public void ResetCounters()
        {
            lock (m_gate)
            {
                Array.Clear(m_modeCurrent, 0, m_modeCurrent.Length);
                Array.Clear(m_modeCumulative, 0, m_modeCumulative.Length);
                Array.Clear(m_stateCurrent, 0, m_stateCurrent.Length);
                Array.Clear(m_stateCumulative, 0, m_stateCumulative.Length);
                m_sinceReset = 0;
                m_lastTimestamp = m_timeProvider.GetTimestamp();
                ResetCounts(m_node.ProdConsumedCount);
                ResetCounts(m_node.ProdProcessedCount);
                ResetCounts(m_node.ProdDefectiveCount);
                PublishTimers();
            }
        }

        /// <summary>
        /// Captures timer values for application-owned persistence.
        /// Fractional seconds are truncated only at this persistence boundary.
        /// </summary>
        public PackMLAdministrationSnapshot Capture()
        {
            lock (m_gate)
            {
                Accumulate();
                PublishTimers();
                return new PackMLAdministrationSnapshot
                {
                    ModeCurrentTime = Seconds(m_modeCurrent),
                    ModeCumulativeTime = Seconds(m_modeCumulative),
                    StateCurrentTime = Seconds(m_stateCurrent),
                    StateCumulativeTime = Seconds(m_stateCumulative),
                    AccTimeSinceReset = (int)(m_sinceReset / TimeSpan.TicksPerSecond)
                };
            }
        }

        /// <summary>
        /// Restores counters after validating the complete snapshot. Downtime is not
        /// counted and no former active mode or state is resumed. The application must
        /// call RecordState with the actual initial state after restoration.
        /// </summary>
        public void Restore(PackMLAdministrationSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            Validate(snapshot.ModeCurrentTime, m_modeCurrent.Length);
            Validate(snapshot.ModeCumulativeTime, m_modeCurrent.Length);
            Validate(snapshot.StateCurrentTime);
            Validate(snapshot.StateCumulativeTime);
            ValidateSeconds(snapshot.AccTimeSinceReset);
            lock (m_gate)
            {
                for (int mode = 0; mode < m_modeCurrent.Length; mode++)
                {
                    m_modeCurrent[mode] = (long)snapshot.ModeCurrentTime[mode] * TimeSpan.TicksPerSecond;
                    m_modeCumulative[mode] = (long)snapshot.ModeCumulativeTime[mode] * TimeSpan.TicksPerSecond;
                    for (int state = 0; state < k_stateCount; state++)
                    {
                        int index = (mode * k_stateCount) + state;
                        m_stateCurrent[mode, state] =
                            (long)snapshot.StateCurrentTime.Span[index] * TimeSpan.TicksPerSecond;
                        m_stateCumulative[mode, state] =
                            (long)snapshot.StateCumulativeTime.Span[index] * TimeSpan.TicksPerSecond;
                    }
                }
                m_sinceReset = (long)snapshot.AccTimeSinceReset * TimeSpan.TicksPerSecond;
                m_active = false;
                m_lastTimestamp = m_timeProvider.GetTimestamp();
                PublishTimers();
            }
        }

        /// <summary>
        /// Validates and projects a coherent equipment sample. Extents are configured
        /// maxima, never inferred from current alarm population. Quality and acquisition
        /// time are preserved, including for an absent stop reason.
        /// </summary>
        public void Apply(PackMLAdministrationData data, PackMLAdministrationExtents extents)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (extents is null)
            {
                throw new ArgumentNullException(nameof(extents));
            }
            ValidateElements(data.Parameters, extents.Parameters);
            ValidateElements(data.Alarms, extents.Alarms);
            ValidateElements(data.AlarmHistory, extents.AlarmHistory);
            ValidateElements(data.Warnings, extents.Warnings);
            ValidateExtent(data.StopReason is null ? 0 : 1, extents.StopReasons);
            ValidateElements(data.ConsumedCounts, extents.ConsumedCounts);
            ValidateElements(data.ProcessedCounts, extents.ProcessedCounts);
            ValidateElements(data.DefectiveCounts, extents.DefectiveCounts);
            if (float.IsNaN(data.MachineDesignSpeed) || float.IsInfinity(data.MachineDesignSpeed) ||
                data.MachineDesignSpeed < 0 || data.SourceTimestamp.IsNull)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }
            lock (m_gate)
            {
                Set(m_node.Parameter, Variant.FromStructure(data.Parameters, true),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.Alarm, Variant.FromStructure(data.Alarms, true), data.StatusCode, data.SourceTimestamp);
                Set(m_node.AlarmHistory, Variant.FromStructure(data.AlarmHistory, true),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.Warning, Variant.FromStructure(data.Warnings, true), data.StatusCode, data.SourceTimestamp);
                Set(m_node.StopReason, data.StopReason is { } stopReason
                    ? Variant.FromStructure(stopReason, true)
                    : Variant.From(ExtensionObject.Null),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.ProdConsumedCount, Variant.FromStructure(data.ConsumedCounts, true),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.ProdProcessedCount, Variant.FromStructure(data.ProcessedCounts, true),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.ProdDefectiveCount, Variant.FromStructure(data.DefectiveCounts, true),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.MachDesignSpeed, Variant.From(data.MachineDesignSpeed),
                    data.StatusCode, data.SourceTimestamp);
                Set(m_node.AlarmExtent, Variant.From(extents.Alarms), StatusCodes.Good, data.SourceTimestamp);
                Set(m_node.AlarmHistoryExtent, Variant.From(extents.AlarmHistory),
                    StatusCodes.Good, data.SourceTimestamp);
                Set(m_node.WarningExtent, Variant.From(extents.Warnings), StatusCodes.Good, data.SourceTimestamp);
                Set(m_node.StopReasonExtent, Variant.From(extents.StopReasons), StatusCodes.Good, data.SourceTimestamp);
                m_node.ClearChangeMasks(m_context, true);
            }
        }

        /// <summary>
        /// Rejects negative capacities and over-capacity samples before publication.
        /// </summary>
        private static void ValidateExtent(int count, int capacity)
        {
            if (capacity < 0 || count > capacity)
            {
                throw new ArgumentException("Administration sample exceeds its configured capacity.");
            }
        }

        /// <summary>
        /// Validates complete structure arrays before any administration field is published.
        /// </summary>
        /// <typeparam name="T">The structure element type.</typeparam>
        private static void ValidateElements<T>(ArrayOf<T> values, int capacity) where T : class
        {
            ValidateExtent(values.Count, capacity);
            foreach (T value in values)
            {
                if (value is null)
                {
                    throw new ArgumentException("Administration structure arrays cannot contain null entries.");
                }
            }
        }

        /// <summary>
        /// Validates a single persisted timer.
        /// </summary>
        private static void ValidateSeconds(int value)
        {
            if (value < 0 || value == int.MaxValue)
            {
                throw new ArgumentException("Administration seconds must be between zero and Int32.MaxValue - 1.");
            }
        }

        /// <summary>
        /// Validates a mode-indexed timer array.
        /// </summary>
        private static void Validate(ArrayOf<int> values, int count)
        {
            if (values.IsNull || values.Count != count)
            {
                throw new ArgumentException("Administration mode dimensions do not match the configured modes.");
            }
            foreach (int value in values)
            {
                ValidateSeconds(value);
            }
        }

        /// <summary>
        /// Converts mode ticks to detached whole-second values.
        /// </summary>
        private static ArrayOf<int> Seconds(long[] values)
        {
            var result = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = (int)(values[i] / TimeSpan.TicksPerSecond);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Converts state ticks to a genuine rank-two mode-by-state matrix.
        /// </summary>
        private static MatrixOf<int> Seconds(long[,] values)
        {
            var result = new int[values.GetLength(0), values.GetLength(1)];
            for (int mode = 0; mode < values.GetLength(0); mode++)
            {
                for (int state = 0; state < values.GetLength(1); state++)
                {
                    result[mode, state] = (int)(values[mode, state] / TimeSpan.TicksPerSecond);
                }
            }
            return result;
        }

        /// <summary>
        /// Updates a variable through its base representation without boxing values.
        /// </summary>
        private static void Set(
            BaseVariableState? variable,
            Variant value,
            StatusCode status,
            DateTimeUtc timestamp)
        {
            if (variable is null)
            {
                throw new InvalidOperationException("A PackML administration field is missing.");
            }
            variable.Value = value;
            variable.StatusCode = status;
            variable.Timestamp = timestamp;
        }

        /// <summary>
        /// Validates the exact rank and dimensions and every persisted state timer.
        /// </summary>
        private void Validate(MatrixOf<int> values)
        {
            if (values.IsNull)
            {
                throw new ArgumentException("Administration state matrix is required.");
            }
            int[] dimensions = values.Dimensions;
            if (dimensions.Length != 2 ||
                dimensions[0] != m_modeCurrent.Length || dimensions[1] != k_stateCount)
            {
                throw new ArgumentException("Administration state matrix must be indexed by mode then state.");
            }
            foreach (int value in values.Span)
            {
                ValidateSeconds(value);
            }
        }

        /// <summary>
        /// Accumulates monotonic ticks; all second counters roll to zero at Int32.MaxValue.
        /// </summary>
        private void Accumulate()
        {
            long now = m_timeProvider.GetTimestamp();
            long elapsed = m_timeProvider.GetElapsedTime(m_lastTimestamp, now).Ticks;
            m_lastTimestamp = now;
            if (elapsed < 0)
            {
                throw new InvalidOperationException("The administration clock moved backwards.");
            }
            elapsed %= k_rolloverTicks;
            m_sinceReset = (m_sinceReset + elapsed) % k_rolloverTicks;
            if (m_active)
            {
                m_modeCurrent[m_mode] = (m_modeCurrent[m_mode] + elapsed) % k_rolloverTicks;
                m_modeCumulative[m_mode] = (m_modeCumulative[m_mode] + elapsed) % k_rolloverTicks;
                m_stateCurrent[m_mode, m_state] = (m_stateCurrent[m_mode, m_state] + elapsed) % k_rolloverTicks;
                m_stateCumulative[m_mode, m_state] = (m_stateCumulative[m_mode, m_state] + elapsed) % k_rolloverTicks;
            }
        }

        /// <summary>
        /// Resets a detached copy of published production counts, preserving accumulated counts.
        /// </summary>
        private void ResetCounts(BaseVariableState? variable)
        {
            if (variable is null)
            {
                throw new InvalidOperationException("A PackML production-count field is missing.");
            }
            ArrayOf<PackMLCountDataType> values = variable.Value.GetStructureArray<PackMLCountDataType>();
            ArrayOf<PackMLCountDataType> copies = values.ConvertAll(value =>
            {
                var copy = (PackMLCountDataType)value.Clone();
                copy.Count = 0;
                return copy;
            });
            Set(variable, Variant.FromStructure(copies), variable.StatusCode, m_timeProvider.GetUtcNow().UtcDateTime);
        }

        /// <summary>
        /// Publishes counters using whole seconds while retaining fractional ticks internally.
        /// </summary>
        private void PublishTimers()
        {
            DateTimeUtc now = m_timeProvider.GetUtcNow().UtcDateTime;
            Set(m_node.ModeCurrentTime, Variant.From(Seconds(m_modeCurrent)), StatusCodes.Good, now);
            Set(m_node.ModeCumulativeTime, Variant.From(Seconds(m_modeCumulative)), StatusCodes.Good, now);
            Set(m_node.StateCurrentTime, Variant.From(Seconds(m_stateCurrent)), StatusCodes.Good, now);
            Set(m_node.StateCumulativeTime, Variant.From(Seconds(m_stateCumulative)), StatusCodes.Good, now);
            Set(m_node.AccTimeSinceReset, Variant.From((int)(m_sinceReset / TimeSpan.TicksPerSecond)),
                StatusCodes.Good, now);
            m_node.ClearChangeMasks(m_context, true);
        }

        /// <summary>The standard state values zero through nineteen, including inactive zero.</summary>
        private const int k_stateCount = 20;

        /// <summary>The specified whole-second rollover, represented in fractional-second ticks.</summary>
        private const long k_rolloverTicks = (long)int.MaxValue * TimeSpan.TicksPerSecond;

        /// <summary>The address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The generated administration object.</summary>
        private readonly PackMLAdminObjectState m_node;

        /// <summary>The injected monotonic and wall clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The current mode interval ticks.</summary>
        private readonly long[] m_modeCurrent;

        /// <summary>The cumulative mode ticks.</summary>
        private readonly long[] m_modeCumulative;

        /// <summary>The current state interval ticks.</summary>
        private readonly long[,] m_stateCurrent;

        /// <summary>The cumulative state ticks.</summary>
        private readonly long[,] m_stateCumulative;

        /// <summary>Serializes administration mutations without exposing synchronization.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The last monotonic timestamp.</summary>
        private long m_lastTimestamp;

        /// <summary>Elapsed ticks since the last administrative reset.</summary>
        private long m_sinceReset;

        /// <summary>The active concrete mode value.</summary>
        private int m_mode;

        /// <summary>The active standard state number.</summary>
        private uint m_state;

        /// <summary>Whether an actual machine state has been recorded since initialization.</summary>
        private bool m_active;
    }
}
