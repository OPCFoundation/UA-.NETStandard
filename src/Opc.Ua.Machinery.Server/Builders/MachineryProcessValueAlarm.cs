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
using Opc.Ua.Machinery.ProcessValues;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Couples one of the two exclusive alarms OPC 40001-2 declares on a
    /// process value to the value it watches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated factory creates the <c>LimitAlarm</c> /
    /// <c>DeviationAlarm</c> object and its condition machinery, but nothing
    /// more: no limits, no source, no condition lifecycle. An alarm in that
    /// state is a node, not an alarm — it never activates and never reports.
    /// This type supplies the missing half.
    /// </para>
    /// <para>
    /// The four limit Properties are optional on <c>LimitAlarmType</c> and are
    /// not part of the OPC 40001-2 instance declaration, so they are
    /// materialised here with NodeIds minted from the build context; doing it
    /// before the machine is registered is what gets them indexed with the
    /// rest of the tree.
    /// </para>
    /// </remarks>
    internal sealed class MachineryProcessValueAlarm
    {
        public MachineryProcessValueAlarm(
            MachineryBuildScope scope,
            ProcessValueState processValue,
            BaseVariableState signal,
            ExclusiveLimitAlarmState alarm,
            bool deviation)
        {
            m_scope = scope;
            m_processValue = processValue;
            m_signal = signal;
            m_alarm = alarm;
            m_deviation = deviation;
            Initialize();
        }

        /// <summary>
        /// Gets the alarm being driven.
        /// </summary>
        public ExclusiveLimitAlarmState Alarm => m_alarm;

        /// <summary>
        /// Gets whether the alarm watches the deviation from the setpoint
        /// rather than the value itself.
        /// </summary>
        public bool IsDeviation => m_deviation;

        /// <summary>
        /// Writes the four limits, materialising the Properties the model
        /// leaves optional.
        /// </summary>
        public void SetLimits(double? lowLow, double? low, double? high, double? highHigh)
        {
            m_scope.EnsureMutable();
            if (highHigh.HasValue)
            {
                m_alarm.HighHighLimit = CreateLimit(
                    Opc.Ua.BrowseNames.HighHighLimit,
                    highHigh.Value);
            }
            if (high.HasValue)
            {
                m_alarm.HighLimit = CreateLimit(Opc.Ua.BrowseNames.HighLimit, high.Value);
            }
            if (low.HasValue)
            {
                m_alarm.LowLimit = CreateLimit(Opc.Ua.BrowseNames.LowLimit, low.Value);
            }
            if (lowLow.HasValue)
            {
                m_alarm.LowLowLimit = CreateLimit(
                    Opc.Ua.BrowseNames.LowLowLimit,
                    lowLow.Value);
            }
        }

        /// <summary>
        /// Points the deviation alarm at the setpoint it measures against.
        /// </summary>
        public void SetSetpointNode(BaseVariableState setpoint)
        {
            if (m_alarm is ExclusiveDeviationAlarmState deviationAlarm &&
                deviationAlarm.SetpointNode != null)
            {
                deviationAlarm.SetpointNode.Value = setpoint.NodeId;
            }
        }

        /// <summary>
        /// Re-evaluates the alarm against a new reading and reports an event
        /// when the limit state changed.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="observed">
        /// The value to compare — the process value for a limit alarm, the
        /// deviation from the setpoint for a deviation alarm.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the limit state changed and an event
        /// was reported.
        /// </returns>
        public bool Evaluate(ISystemContext context, double observed)
        {
            LimitAlarmStates state = Classify(observed);
            if (state == m_state)
            {
                return false;
            }
            m_state = state;
            m_alarm.SetLimitState(context, state);

            bool active = state != LimitAlarmStates.Inactive;
            m_alarm.Retain!.Value = active;
            m_alarm.SetSeverity(context, SeverityOf(state));
            m_alarm.Message!.Value = new LocalizedText(
                active
                    ? FormattableString.Invariant(
                        $"{m_processValue.BrowseName.Name} {state} at {observed:G6}.")
                    : FormattableString.Invariant(
                        $"{m_processValue.BrowseName.Name} back within limits at {observed:G6}."));
            ReportEvent(context);
            return true;
        }

        private LimitAlarmStates Classify(double observed)
        {
            if (m_alarm.HighHighLimit != null && observed >= m_alarm.HighHighLimit.Value)
            {
                return LimitAlarmStates.HighHigh;
            }
            if (m_alarm.LowLowLimit != null && observed <= m_alarm.LowLowLimit.Value)
            {
                return LimitAlarmStates.LowLow;
            }
            if (m_alarm.HighLimit != null && observed >= m_alarm.HighLimit.Value)
            {
                return LimitAlarmStates.High;
            }
            if (m_alarm.LowLimit != null && observed <= m_alarm.LowLimit.Value)
            {
                return LimitAlarmStates.Low;
            }
            return LimitAlarmStates.Inactive;
        }

        private static EventSeverity SeverityOf(LimitAlarmStates state)
        {
            return state switch
            {
                LimitAlarmStates.HighHigh or LimitAlarmStates.LowLow => EventSeverity.High,
                LimitAlarmStates.High or LimitAlarmStates.Low => EventSeverity.Medium,
                _ => EventSeverity.Low
            };
        }

        private void ReportEvent(ISystemContext context)
        {
            if (m_alarm.EnabledState?.Id?.Value != true)
            {
                return;
            }
            m_alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            m_alarm.Time!.Value = DateTimeUtc.Now;
            m_alarm.ReceiveTime!.Value = m_alarm.Time.Value;
            m_alarm.ClearChangeMasks(context, true);

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, m_alarm);
            m_alarm.ReportEvent(context, snapshot);
        }

        /// <summary>
        /// Gives the condition the identity OPC 10000-9 expects before it can
        /// be enabled: a source, an input, a name, and the event-source edge
        /// that makes it reachable from the process value a client subscribes
        /// to.
        /// </summary>
        private void Initialize()
        {
            ISystemContext context = m_scope.Context;

            m_alarm.SourceNode!.Value = m_processValue.NodeId;
            m_alarm.SourceName!.Value = m_processValue.BrowseName.Name ?? string.Empty;
            m_alarm.ConditionName!.Value = m_alarm.BrowseName.Name ?? string.Empty;
            m_alarm.InputNode!.Value = m_signal.NodeId;
            m_alarm.BranchId!.Value = NodeId.Null;
            m_alarm.ClientUserId!.Value = string.Empty;
            m_alarm.Quality!.Value = StatusCodes.Good;
            m_alarm.Retain!.Value = false;
            m_alarm.Comment!.Value = new LocalizedText(string.Empty);

            m_alarm.SetEnableState(context, enabled: true);
            m_alarm.SetLimitState(context, LimitAlarmStates.Inactive);
            m_alarm.SetSeverity(context, EventSeverity.Low);

            // OPC 10000-9 reaches a condition from its source through
            // HasEventSource; without it a client browsing the process value
            // cannot discover the alarm it owns.
            if (!m_processValue.ReferenceExists(
                    Opc.Ua.ReferenceTypeIds.HasEventSource,
                    false,
                    m_alarm.NodeId))
            {
                m_processValue.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasEventSource,
                    false,
                    m_alarm.NodeId);
                m_alarm.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasEventSource,
                    true,
                    m_processValue.NodeId);
            }
        }

        private PropertyState<double> CreateLimit(string browseName, double value)
        {
            PropertyState<double>? existing =
                MachineryBuilderUtilities.FindChild<PropertyState<double>>(
                    m_scope.Context,
                    m_alarm,
                    new QualifiedName(browseName));
            if (existing != null)
            {
                existing.Value = value;
                return existing;
            }

            PropertyState<double> property =
                PropertyState<double>.With<VariantBuilder>(m_alarm);
            property.SymbolicName = browseName;
            property.BrowseName = new QualifiedName(browseName);
            property.DisplayName = new LocalizedText(browseName);
            property.TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType;
            property.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            property.DataType = Opc.Ua.DataTypeIds.Double;
            property.ValueRank = ValueRanks.Scalar;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            property.ModellingRuleId = NodeId.Null;
            property.Value = value;
            property.NodeId = m_scope.Context.NodeIdFactory!.New(m_scope.Context, property);
            return property;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly ProcessValueState m_processValue;
        private readonly BaseVariableState m_signal;
        private readonly ExclusiveLimitAlarmState m_alarm;
        private readonly bool m_deviation;
        private LimitAlarmStates m_state = LimitAlarmStates.Inactive;
    }
}
