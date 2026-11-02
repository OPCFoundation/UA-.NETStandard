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
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Scales.Server.Runtime
{
    public sealed partial class RecipeController
    {
        /// <summary>
        /// Gets whether automatic condition waits are enabled.
        /// </summary>
        public bool ConditionWaitsEnabled
        {
            get
            {
                lock (m_lock)
                {
                    return m_conditionWaitsEnabled && !m_disposed;
                }
            }
        }

        /// <summary>
        /// Gets current waits requiring explicit application or operator intervention.
        /// </summary>
        public ArrayOf<RecipeElementState> ConditionWaitsRequiringIntervention
        {
            get
            {
                lock (m_lock)
                {
                    return m_conditionWaits.Where(p => p.Value.Intervention).Select(p => p.Key).ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Enables local, registered-source condition evaluation. Sources must supply good-quality,
        /// timestamped observations at least once within the maximum age.
        /// </summary>
        /// <param name="maximumSourceAge">The maximum acceptable observation age and edge observation gap.</param>
        /// <param name="timeProvider">The clock and timer provider; defaults to the system provider.</param>
        public void EnableConditionWaits(TimeSpan maximumSourceAge, TimeProvider? timeProvider = null)
        {
            if (maximumSourceAge <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSourceAge));
            }
            lock (m_lock)
            {
                CheckConditionConfiguration();
                m_conditionTimer?.Dispose();
                m_maximumSourceAge = maximumSourceAge;
                m_conditionClock = timeProvider ?? TimeProvider.System;
                m_conditionWaitsEnabled = true;
                m_conditionTimer = m_conditionClock.CreateTimer(
                    _ => OnConditionTimer(),
                    null,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Registers an allowed local source and publishes it in SupportedThresholdValues.
        /// ThresholdValueId uses the invariant decimal representation of thresholdId.
        /// No source is resolved or read through a remote connection.
        /// </summary>
        /// <param name="thresholdId">The source's OPC 40200 threshold identifier.</param>
        /// <param name="nodeId">The optional local address-space node identifier, or NodeId.Null.</param>
        /// <param name="name">The source's published name.</param>
        /// <param name="dataType">Boolean or a built-in numeric scalar type.</param>
        /// <param name="engineeringUnits">The numeric source's units, or null for a dimensionless source.</param>
        public void RegisterConditionSource(
            uint thresholdId,
            NodeId nodeId,
            LocalizedText name,
            BuiltInType dataType,
            EUInformation? engineeringUnits = null)
        {
            if (dataType != BuiltInType.Boolean && !IsNumeric(dataType))
            {
                throw new ArgumentOutOfRangeException(nameof(dataType));
            }
            lock (m_lock)
            {
                CheckConditionConfiguration();
                if (m_conditionSources.ContainsKey(thresholdId) ||
                    (!nodeId.IsNull && m_conditionSources.Values.Any(s => s.NodeId == nodeId)))
                {
                    throw new ArgumentException(
                        "The condition source identifier is already registered.",
                        nameof(thresholdId));
                }
                m_conditionSources.Add(thresholdId, new ConditionSource(
                    thresholdId, nodeId, name, dataType, engineeringUnits));
                Scale.AddSupportedThresholdValues(m_owner.Services.Context);
                Scale.SupportedThresholdValues!.Value = m_conditionSources.Values.Select(s => new RecipeThresholdType
                {
                    ThresholdId = s.Id,
                    ThresholdNodeId = s.NodeId,
                    ThresholdName = s.Name
                }).ToArrayOf();
                ScaleValues.Touch(m_owner.Services.Context, Scale.SupportedThresholdValues);
            }
        }

        /// <summary>
        /// Supplies a local source observation, retaining quality and source timestamp.
        /// Bad, uncertain, overflowed, missing, stale, future or out-of-order observations cannot satisfy a wait.
        /// A gap resets edge history; its first good value establishes a baseline, not an edge.
        /// </summary>
        /// <param name="thresholdId">The registered source identifier.</param>
        /// <param name="value">The observed scalar value, quality and source timestamp.</param>
        /// <param name="hasGap">Whether the producer lost any intervening observations.</param>
        public void ObserveConditionSource(uint thresholdId, in DataValue value, bool hasGap = false)
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                if (!m_conditionSources.TryGetValue(thresholdId, out ConditionSource? source))
                {
                    throw new ArgumentException("The condition source is not registered.", nameof(thresholdId));
                }
                DateTime timestamp = (DateTime)value.SourceTimestamp;
                DateTime now = m_conditionClock.GetUtcNow().UtcDateTime;
                bool ordered = !source.HasObservation || timestamp > source.Timestamp;
                bool gap = hasGap || !ordered || (source.HasObservation &&
                    timestamp - source.Timestamp > m_maximumSourceAge);
                source.Value = value.WrappedValue;
                source.Good = ordered && StatusCode.IsGood(value.StatusCode) && !value.StatusCode.Overflow &&
                    timestamp != DateTime.MinValue && timestamp <= now &&
                    now - timestamp <= m_maximumSourceAge &&
                    value.WrappedValue.TypeInfo.ValueRank == ValueRanks.Scalar &&
                    value.WrappedValue.TypeInfo.BuiltInType == source.DataType;
                source.HasObservation = true;
                if (ordered && timestamp != DateTime.MinValue && timestamp <= now)
                {
                    source.Timestamp = timestamp;
                }
                source.Sequence++;
                if (gap || !source.Good || State != RecipeRunState.Running)
                {
                    foreach (ConditionWait wait in m_conditionWaits.Values
                        .Where(w => ReferenceEquals(w.Source, source)))
                    {
                        wait.HasPrevious = false;
                    }
                }
            }
            EvaluateConditionWaits();
        }

        /// <summary>
        /// Evaluates active waits and timeout deadlines using the configured clock.
        /// The timer also calls this method; applications may call it after advancing a test clock.
        /// Timeout is latched as an intervention and never completes an element.
        /// </summary>
        public void EvaluateConditionWaits()
        {
            lock (m_lock)
            {
                EvaluateConditionWaitsCore();
            }
            RaisePending();
        }

        /// <summary>
        /// Runs a timer-driven evaluation. The timer thread has no caller to receive a failure,
        /// so a failing evaluation or recipe event subscriber is logged as an error instead of
        /// terminating the process. Every queued notification has still been delivered.
        /// </summary>
        private void OnConditionTimer()
        {
            try
            {
                EvaluateConditionWaits();
            }
            catch (Exception ex)
            {
                m_owner.Services.Logger.ConditionEvaluationFailed(ex, Scale.BrowseName.Name);
            }
        }

        /// <summary>
        /// Evaluates waits under the lock until no element started during the pass is left unevaluated.
        /// A chain of already satisfied conditions completes in one loop instead of one stack frame per element.
        /// </summary>
        private void EvaluateConditionWaitsCore()
        {
            do
            {
                m_conditionEvaluationRequested = false;
                if (m_disposed || !m_conditionWaitsEnabled)
                {
                    return;
                }
                DateTime now = m_conditionClock.GetUtcNow().UtcDateTime;
                foreach (KeyValuePair<RecipeElementState, ConditionWait> pair in m_conditionWaits.ToList())
                {
                    ConditionWait wait = pair.Value;
                    if (wait.Intervention || !m_current.Contains(pair.Key))
                    {
                        continue;
                    }
                    if (wait.Timeout.HasValue &&
                        m_conditionClock.GetElapsedTime(wait.Started) >= wait.Timeout.Value)
                    {
                        RequireConditionIntervention(pair.Key, wait, "Condition wait timed out.");
                        continue;
                    }
                    if (State != RecipeRunState.Running || wait.Source == null)
                    {
                        continue;
                    }
                    ConditionSource source = wait.Source;
                    if (!source.Good || now - source.Timestamp > m_maximumSourceAge)
                    {
                        wait.HasPrevious = false;
                        continue;
                    }
                    if (wait.Sequence == source.Sequence)
                    {
                        continue;
                    }
                    wait.Sequence = source.Sequence;
                    bool satisfied = false;
                    if (wait.IsEdge)
                    {
                        if (source.Value.TryGetValue(out bool current))
                        {
                            satisfied = wait.HasPrevious && current != wait.Previous &&
                                (wait.Mode == 0 ? current : !current);
                            wait.Previous = current;
                            wait.HasPrevious = true;
                        }
                    }
                    else
                    {
                        satisfied = CompareNumeric(source.Value, wait.Target, wait.Mode);
                    }
                    if (satisfied)
                    {
                        CompleteCore(pair.Key, "condition satisfied");
                    }
                }
            }
            while (m_conditionEvaluationRequested);
            ScheduleConditionTimer();
        }

        /// <summary>
        /// Stops the controller: discards all recipe execution and observation state and
        /// the queued notifications, and stops the condition timer. No condition is evaluated
        /// and no queued notification is admitted after this returns. A drain that was already
        /// delivering a batch on another thread may still start the notification it admitted
        /// before the stop was observed, and a running subscriber callback is not waited for;
        /// await <see cref="DisposeAsync"/> to wait until the controller is quiescent.
        /// </summary>
        public void Dispose()
        {
            _ = Stop();
        }

        /// <summary>
        /// Stops the controller like <see cref="Dispose"/>, then completes once the condition
        /// timer callback and every subscriber callback in flight at the stop have returned.
        /// Every caller, including one that follows an earlier <see cref="Dispose"/>, awaits the
        /// same completion. A subscriber may call this from its own callback; the returned task
        /// then completes after that callback has returned, so it must be awaited, not blocked on.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(Stop());
        }

        /// <summary>
        /// Marks the controller disposed, once, and returns the shared task that completes when
        /// the timer is disposed and every running notification drain has ended.
        /// </summary>
        private Task Stop()
        {
            ITimer? timer;
            TaskCompletionSource<bool> quiesced;
            lock (m_lock)
            {
                if (m_quiesced != null)
                {
                    return m_quiesced.Task;
                }
                quiesced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_quiesced = quiesced;
                m_disposed = true;
                Finish();
                m_conditionSources.Clear();
                m_pending.Clear();
                timer = m_conditionTimer;
                m_conditionTimer = null;
            }
            _ = QuiesceAsync(timer, quiesced);
            return quiesced.Task;
        }

        /// <summary>
        /// Disposes the condition timer, waiting for a running timer callback, then waits for
        /// the notification drains that were running when the controller stopped.
        /// </summary>
        private async Task QuiesceAsync(ITimer? timer, TaskCompletionSource<bool> quiesced)
        {
            try
            {
                if (timer != null)
                {
                    await timer.DisposeAsync().ConfigureAwait(false);
                }
                Task? drained = null;
                lock (m_lock)
                {
                    if (m_activeDrains != 0)
                    {
                        m_drained ??= new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        drained = m_drained.Task;
                    }
                }
                if (drained != null)
                {
                    await drained.ConfigureAwait(false);
                }
                quiesced.TrySetResult(true);
            }
            catch (Exception ex)
            {
                // A synchronous Dispose discards the task, so the failure is logged here and
                // still propagated to every DisposeAsync caller.
                m_owner.Services.Logger.ShutdownFailed(ex, Scale.BrowseName.Name);
                quiesced.TrySetException(ex);
                _ = quiesced.Task.Exception;
            }
        }

        /// <summary>
        /// Requires registration and evaluator configuration to happen between runs.
        /// </summary>
        private void CheckConditionConfiguration()
        {
            if (m_disposed || State != RecipeRunState.Idle)
            {
                throw new InvalidOperationException("Condition sources may only be configured on an idle controller.");
            }
        }

        /// <summary>
        /// Captures the element's configuration and allowed source exactly once on activation.
        /// </summary>
        private void BeginConditionWait(RecipeElementState element)
        {
            if (!m_conditionWaitsEnabled || element is not ConditionSleepState condition)
            {
                return;
            }
            var wait = new ConditionWait { Started = m_conditionClock.GetTimestamp() };
            m_conditionWaits.Add(element, wait);
            string id = condition.ThresholdValueId?.Value ?? string.Empty;
            NodeId nodeId = condition.ThresholdValueNodeId?.Value ?? NodeId.Null;
            ConditionSource? source = null;
            if (!string.IsNullOrEmpty(id) &&
                uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out uint number) &&
                number.ToString(CultureInfo.InvariantCulture) == id)
            {
                m_conditionSources.TryGetValue(number, out source);
            }
            else if (string.IsNullOrEmpty(id) && !nodeId.IsNull)
            {
                source = m_conditionSources.Values.FirstOrDefault(s => s.NodeId == nodeId);
            }
            bool advertised = source != null &&
                Scale.SupportedThresholdValues != null &&
                Scale.SupportedThresholdValues.Value.Contains(s => s.ThresholdId == source.Id &&
                    (nodeId.IsNull || s.ThresholdNodeId == nodeId));
            if (source == null || !advertised || (!nodeId.IsNull && nodeId != source.NodeId))
            {
                RequireConditionIntervention(element, wait, "Condition source is not registered and advertised.");
                return;
            }
            wait.Source = source;
            wait.Sequence = source.Sequence - 1;
            double timeout = condition.Timeout?.Value ?? 0;
            if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout < 0 ||
                timeout >= TimeSpan.MaxValue.TotalMilliseconds)
            {
                RequireConditionIntervention(element, wait, "Condition timeout is invalid.");
                return;
            }
            wait.Timeout = timeout > 0 ? TimeSpan.FromMilliseconds(timeout) : null;
            if (element is AnalogConditionSleepState analog)
            {
                wait.Target = analog.TargetThresholdValue?.WrappedValue ?? Variant.Null;
                wait.Mode = (int)(analog.ConditionMode?.Value ?? (EqualityAndRelationalOperator)(-1));
                BaseVariableState? units = analog.TargetThresholdValue?.FindChild(
                    m_owner.Services.Context, new QualifiedName(Ua.BrowseNames.EngineeringUnits))
                    as BaseVariableState;
                EUInformation? targetUnits = units != null &&
                    units.WrappedValue.TryGetStructure<EUInformation>(out EUInformation? engineeringUnits)
                    ? engineeringUnits
                    : null;
                bool unitsMatch = source.UnitId == targetUnits?.UnitId &&
                    source.UnitNamespace == targetUnits?.NamespaceUri;
                if (wait.Mode is < 0 or > 5 || !NumericCompatible(source.DataType, wait.Target.TypeInfo.BuiltInType) ||
                    !IsFiniteNumeric(wait.Target) || !unitsMatch)
                {
                    RequireConditionIntervention(
                        element,
                        wait,
                        "Condition comparison type, units or operator is invalid.");
                    return;
                }
            }
            else if (element is EdgeTriggeredSleepState edge)
            {
                wait.Target = edge.TargetThresholdValue?.WrappedValue ?? Variant.Null;
                wait.IsEdge = true;
                wait.Mode = (int)(edge.ConditionMode?.Value ?? (EdgeOperator)(-1));
                if (source.DataType != BuiltInType.Boolean || wait.Mode is < 0 or > 1 ||
                    !wait.Target.TryGetValue(out bool target) || target != (wait.Mode == 0))
                {
                    RequireConditionIntervention(element, wait, "Condition edge target or operator is invalid.");
                    return;
                }
                if (source.Good &&
                    m_conditionClock.GetUtcNow().UtcDateTime - source.Timestamp <= m_maximumSourceAge &&
                    source.Value.TryGetValue(out bool previous))
                {
                    wait.Previous = previous;
                    wait.HasPrevious = true;
                }
                wait.Sequence = source.Sequence;
            }
            else
            {
                RequireConditionIntervention(element, wait, "Unsupported condition element.");
                return;
            }
            ScheduleConditionTimer();
            m_conditionEvaluationRequested = true;
        }

        /// <summary>
        /// Latches a blocked wait and queues its notification through the existing scale event infrastructure.
        /// </summary>
        private void RequireConditionIntervention(RecipeElementState element, ConditionWait wait, string reason)
        {
            wait.Intervention = true;
            Report($"Element '{element.BrowseName.Name}' requires operator intervention: {reason}");
            var message = new LocalizedText($"Recipe condition '{element.BrowseName.Name}': {reason}");
            m_pending.Add(() => m_owner.Notifications.RaiseEvent(ScaleNotificationId.GeneralProcessFault, message));
        }

        /// <summary>
        /// Invalidates Boolean history on pause so a transition across a pause is never treated as an edge.
        /// </summary>
        private void ResetConditionEdges()
        {
            foreach (ConditionWait wait in m_conditionWaits.Values)
            {
                wait.HasPrevious = false;
                wait.Sequence = wait.Source?.Sequence ?? 0;
            }
        }

        /// <summary>
        /// Polls only while there are active, non-intervention waits.
        /// </summary>
        private void ScheduleConditionTimer()
        {
            double milliseconds = Math.Min(100, m_maximumSourceAge.TotalMilliseconds);
            foreach (ConditionWait wait in m_conditionWaits.Values)
            {
                if (!wait.Intervention && wait.Timeout.HasValue)
                {
                    milliseconds = Math.Min(
                        milliseconds,
                        (wait.Timeout.Value - m_conditionClock.GetElapsedTime(wait.Started)).TotalMilliseconds);
                }
            }
            TimeSpan interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
            bool active = m_conditionWaits.Values.Any(w => !w.Intervention);
            m_conditionTimer?.Change(
                active ? interval : Timeout.InfiniteTimeSpan,
                active ? interval : Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Determines whether a scalar built-in type is numeric.
        /// </summary>
        private static bool IsNumeric(BuiltInType type)
        {
            return type is >= BuiltInType.SByte and <= BuiltInType.Double;
        }

        /// <summary>
        /// Allows comparisons of all built-in numeric scalar types.
        /// </summary>
        private static bool NumericCompatible(BuiltInType source, BuiltInType target)
        {
            return IsNumeric(source) && IsNumeric(target);
        }

        /// <summary>
        /// Rejects arrays and non-finite targets and observations.
        /// </summary>
        private static bool IsFiniteNumeric(Variant value)
        {
            if (value.TypeInfo.ValueRank != ValueRanks.Scalar || !IsNumeric(value.TypeInfo.BuiltInType))
            {
                return false;
            }
            if (value.TryGetValue(out double number))
            {
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }
            if (value.TryGetValue(out float single))
            {
                return !float.IsNaN(single) && !float.IsInfinity(single);
            }
            return true;
        }

        /// <summary>
        /// Compares finite scalar numbers, preserving all 64-bit integer bits.
        /// </summary>
        private static bool CompareNumeric(Variant value, Variant target, int mode)
        {
            if (!IsFiniteNumeric(value) || !IsFiniteNumeric(target))
            {
                return false;
            }
            (BigInteger left, int leftExponent) = ExactNumber(value);
            (BigInteger right, int rightExponent) = ExactNumber(target);
            int exponent = Math.Min(leftExponent, rightExponent);
            int comparison = (left << (leftExponent - exponent)).CompareTo(right << (rightExponent - exponent));
            return mode switch
            {
                0 => comparison == 0,
                1 => comparison != 0,
                2 => comparison <= 0,
                3 => comparison >= 0,
                4 => comparison < 0,
                5 => comparison > 0,
                _ => false
            };
        }

        /// <summary>
        /// Converts integral scalars exactly to decimal.
        /// </summary>
        private static decimal IntegerValue(Variant value)
        {
            if (value.TryGetValue(out sbyte i8))
            {
                return i8;
            }
            if (value.TryGetValue(out byte u8))
            {
                return u8;
            }
            if (value.TryGetValue(out short i16))
            {
                return i16;
            }
            if (value.TryGetValue(out ushort u16))
            {
                return u16;
            }
            if (value.TryGetValue(out int i32))
            {
                return i32;
            }
            if (value.TryGetValue(out uint u32))
            {
                return u32;
            }
            if (value.TryGetValue(out long i64))
            {
                return i64;
            }
            value.TryGetValue(out ulong u64);
            return u64;
        }

        /// <summary>
        /// Widens single-precision scalars exactly to double.
        /// </summary>
        private static double FloatingValue(Variant value)
        {
            return value.TryGetValue(out float single)
                ? single
                : value.TryGetValue(out double number) ? number : double.NaN;
        }

        /// <summary>
        /// Represents finite numbers as exact binary rationals, avoiding rounded integer-to-double conversions.
        /// </summary>
        private static (BigInteger Mantissa, int Exponent) ExactNumber(Variant value)
        {
            if (value.TypeInfo.BuiltInType <= BuiltInType.UInt64)
            {
                return (new BigInteger(IntegerValue(value)), 0);
            }
            long bits = BitConverter.DoubleToInt64Bits(FloatingValue(value));
            int exponent = (int)((bits >> 52) & 0x7ff);
            long mantissa = bits & 0x000fffffffffffff;
            if (exponent != 0)
            {
                mantissa |= 0x0010000000000000;
            }
            return (new BigInteger(bits < 0 ? -mantissa : mantissa), exponent == 0 ? -1074 : exponent - 1075);
        }

        /// <summary>
        /// An explicitly registered local observation source.
        /// </summary>
        private sealed class ConditionSource
        {
            /// <summary>
            /// Captures immutable source metadata.
            /// </summary>
            public ConditionSource(
                uint id, NodeId nodeId, LocalizedText name, BuiltInType dataType, EUInformation? engineeringUnits)
            {
                Id = id;
                NodeId = nodeId;
                Name = name;
                DataType = dataType;
                UnitId = engineeringUnits?.UnitId;
                UnitNamespace = engineeringUnits?.NamespaceUri;
            }

            /// <summary>
            /// Gets the advertised threshold identifier.
            /// </summary>
            public uint Id { get; }

            /// <summary>
            /// Gets the optional local node identifier.
            /// </summary>
            public NodeId NodeId { get; }

            /// <summary>
            /// Gets the advertised source name.
            /// </summary>
            public LocalizedText Name { get; }

            /// <summary>
            /// Gets the registered scalar type.
            /// </summary>
            public BuiltInType DataType { get; }

            /// <summary>
            /// Gets the unit identifier snapshot.
            /// </summary>
            public int? UnitId { get; }

            /// <summary>
            /// Gets the unit namespace snapshot.
            /// </summary>
            public string? UnitNamespace { get; }

            /// <summary>
            /// Gets or sets the latest observed value.
            /// </summary>
            public Variant Value { get; set; }

            /// <summary>
            /// Gets or sets the timestamp high-water mark.
            /// </summary>
            public DateTime Timestamp { get; set; }

            /// <summary>
            /// Gets or sets whether an observation has been supplied.
            /// </summary>
            public bool HasObservation { get; set; }

            /// <summary>
            /// Gets or sets whether the latest observation was valid at receipt.
            /// </summary>
            public bool Good { get; set; }

            /// <summary>
            /// Gets or sets the local observation sequence.
            /// </summary>
            public long Sequence { get; set; }
        }

        /// <summary>
        /// The activation snapshot and edge history for one current element.
        /// </summary>
        private sealed class ConditionWait
        {
            /// <summary>
            /// Gets or sets the registered source selected at activation.
            /// </summary>
            public ConditionSource? Source { get; set; }

            /// <summary>
            /// Gets or sets the target snapshot.
            /// </summary>
            public Variant Target { get; set; }

            /// <summary>
            /// Gets or sets the operator snapshot.
            /// </summary>
            public int Mode { get; set; }

            /// <summary>
            /// Gets or sets whether the wait observes Boolean edges.
            /// </summary>
            public bool IsEdge { get; set; }

            /// <summary>
            /// Gets or sets whether a contiguous Boolean baseline exists.
            /// </summary>
            public bool HasPrevious { get; set; }

            /// <summary>
            /// Gets or sets the previous Boolean value.
            /// </summary>
            public bool Previous { get; set; }

            /// <summary>
            /// Gets or sets the latest evaluated observation sequence.
            /// </summary>
            public long Sequence { get; set; }

            /// <summary>
            /// Gets or sets the monotonic activation timestamp.
            /// </summary>
            public long Started { get; set; }

            /// <summary>
            /// Gets or sets the optional activation timeout.
            /// </summary>
            public TimeSpan? Timeout { get; set; }

            /// <summary>
            /// Gets or sets whether automatic completion is latched off.
            /// </summary>
            public bool Intervention { get; set; }
        }

        /// <summary>The registered local sources.</summary>
        private readonly Dictionary<uint, ConditionSource> m_conditionSources = [];

        /// <summary>The active activation snapshots.</summary>
        private readonly Dictionary<RecipeElementState, ConditionWait> m_conditionWaits = [];

        /// <summary>The observation freshness limit.</summary>
        private TimeSpan m_maximumSourceAge = TimeSpan.FromSeconds(5);

        /// <summary>The injected clock.</summary>
        private TimeProvider m_conditionClock = TimeProvider.System;

        /// <summary>The active deadline timer.</summary>
        private ITimer? m_conditionTimer;

        /// <summary>Whether automatic evaluation was explicitly enabled.</summary>
        private bool m_conditionWaitsEnabled;

        /// <summary>Whether the controller has shut down.</summary>
        private bool m_disposed;

        /// <summary>Whether a newly started or resumed wait still needs one evaluation pass.</summary>
        private bool m_conditionEvaluationRequested;

        /// <summary>The number of notification drains currently running.</summary>
        private int m_activeDrains;

        /// <summary>Completed when the last drain running at an asynchronous disposal ends.</summary>
        private TaskCompletionSource<bool>? m_drained;

        /// <summary>Completed once the stopped controller is quiescent; set by the first stop.</summary>
        private TaskCompletionSource<bool>? m_quiesced;
    }

    /// <summary>
    /// Source-generated logging for recipe execution.
    /// </summary>
    internal static partial class RecipeControllerLog
    {
        /// <summary>
        /// Reports a failed timer-driven condition evaluation or a failing recipe event subscriber.
        /// </summary>
        [LoggerMessage(
            EventId = ScalesServerEventIds.ScaleRecipes + 0,
            Level = LogLevel.Error,
            Message = "Timer-driven recipe condition evaluation of scale '{Name}' failed.")]
        public static partial void ConditionEvaluationFailed(this ILogger logger, Exception exception, string? name);

        /// <summary>
        /// Reports that stopping the condition timer failed while the recipe controller shut down.
        /// </summary>
        [LoggerMessage(
            EventId = ScalesServerEventIds.ScaleRecipes + 1,
            Level = LogLevel.Error,
            Message = "Recipe controller shutdown of scale '{Name}' failed.")]
        public static partial void ShutdownFailed(this ILogger logger, Exception exception, string? name);
    }
}
