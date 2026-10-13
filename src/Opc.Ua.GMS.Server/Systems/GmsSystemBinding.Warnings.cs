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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Resolves the persistent native condition for advanced standard condition workflows.
        /// </summary>
        internal bool TryGetSensorWarning(string key, [NotNullWhen(true)] out SensorWarningAlarmState? state)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (!m_disposed && m_warnings.TryGetValue(key, out WarningBinding? binding))
            {
                state = binding.State;
                return true;
            }
            state = null;
            return false;
        }

        /// <summary>
        /// Initializes the actual native factory through the core condition lifecycle before the root is registered.
        /// </summary>
        private void ConfigureWarnings(IMachineBuilder<GMSState> builder)
        {
            foreach (GmsSensorWarning value in m_input!.SensorWarnings)
            {
                var name = new QualifiedName(value.Key, m_context.InstanceNamespaceIndex);
                SensorWarningAlarmState alarm = m_context.Context.CreateInstanceOfSensorWarningAlarmType(
                    builder.State, name);
                alarm.Create(m_context.Context, alarm.NodeId, name, new LocalizedText(value.Key), false);
                m_context.Context.AssignInstanceChildNodeIds(alarm);
                alarm.ReferenceTypeId = ReferenceTypeIds.HasCondition;
                builder.State.AddChild(alarm);
                builder.State.AddReferenceIfMissing(ReferenceTypeIds.HasEventSource, false, alarm.NodeId);
                alarm.AddReferenceIfMissing(ReferenceTypeIds.HasEventSource, true, builder.State.NodeId);
                builder.State.EventNotifier |= EventNotifiers.SubscribeToEvents;
                alarm.AutoReportStateChanges = true;
                alarm.ConditionName!.Value = value.Key;
                alarm.SetEnableState(m_context.Context, true);
                alarm.SetActiveState(m_context.Context, false);
                alarm.SetAcknowledgedState(m_context.Context, true);
                alarm.ConditionClassId!.Value = Opc.Ua.ObjectTypeIds.MaintenanceConditionClassType;
                alarm.ConditionClassName!.Value = new LocalizedText("MaintenanceConditionClass");
                alarm.BranchId!.Value = NodeId.Null;
                alarm.ClientUserId!.Value = string.Empty;
                alarm.Comment!.Value = LocalizedText.Null;
                SelectLimit(alarm, Opc.Ua.BrowseNames.LowLimit, value.LowLimit);
                SelectLimit(alarm, Opc.Ua.BrowseNames.HighLimit, value.HighLimit);
                var binding = new WarningBinding(alarm);
                ConfigureWarningPermissions(binding);
                m_warnings.Add(value.Key, binding);
            }
        }

        /// <summary>
        /// Selects a real inherited optional limit without inventing a threshold.
        /// </summary>
        private void SelectLimit(SensorWarningAlarmState alarm, string name, in DataValue value)
        {
            if (!value.IsNull)
            {
                if (name == Opc.Ua.BrowseNames.LowLimit)
                {
                    alarm.AddLowLimit(m_context.Context);
                }
                else
                {
                    alarm.AddHighLimit(m_context.Context);
                }
                BaseVariableState limit = Child<BaseVariableState>(alarm, new QualifiedName(name));
                limit.AccessLevel = AccessLevels.CurrentRead;
                limit.UserAccessLevel = AccessLevels.CurrentRead;
            }
        }

        /// <summary>
        /// Validates all condition identities and values before any inherited or native observation can change.
        /// </summary>
        private void ValidateWarnings(GmsSystemSnapshot snapshot)
        {
            RequireSelection(GmsFeatures.SensorWarnings, !snapshot.SensorWarnings.IsNull);
            if (snapshot.SensorWarnings.Count > m_options.MaximumSensorWarnings)
            {
                throw Invalid("The persistent sensor-warning inventory exceeds its configured bound.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (GmsSensorWarning value in snapshot.SensorWarnings)
            {
                if (value == null || !keys.Add(value.Key) || value.Severity is < 1 or > 1000 ||
                    (value.AllowedOperations & ~MachineToolConditionOperations.All) != 0)
                {
                    throw Invalid(
                        "Sensor warnings require unique keys, standard permissions and severity 1 through 1000.");
                }
                Text(value.Key, true);
                Text(value.SensorKey, true);
                Text(value.ErrorCode, true);
                Text(value.Message.Text, true);
                if (!StatusCode.IsBad(value.Sample.StatusCode) && value.Sample.SourceTimestamp == default)
                {
                    throw Invalid("Usable sensor warnings require an acquisition timestamp.");
                }
                Raw(value.LowLimit, BuiltInType.Double);
                Raw(value.HighLimit, BuiltInType.Double);
                if (value.LowLimit.WrappedValue.TryGetValue(out double low) &&
                    value.HighLimit.WrappedValue.TryGetValue(out double high) && low > high)
                {
                    throw Invalid("A sensor warning's lower limit cannot exceed its upper limit.");
                }
            }
            if (Observation is { } previous)
            {
                if (snapshot.SensorWarnings.Count != previous.SensorWarnings.Count)
                {
                    throw Invalid("Persistent warning identities cannot be discarded instead of clearing their state.");
                }
                for (int i = 0; i < previous.SensorWarnings.Count; i++)
                {
                    GmsSensorWarning old = previous.SensorWarnings[i];
                    GmsSensorWarning next = snapshot.SensorWarnings[i];
                    if (old.Key != next.Key || old.SensorKey != next.SensorKey ||
                        old.InputNodeId != next.InputNodeId || old.LowLimit.IsNull != next.LowLimit.IsNull ||
                        old.HighLimit.IsNull != next.HighLimit.IsNull)
                    {
                        throw Invalid("A persistent warning retains its source, input and configured limit shape.");
                    }
                }
            }
        }

        /// <summary>
        /// Prepares references and event identities before the containing transaction commits.
        /// Standard condition state helpers and event reporting run only after the coherent value commit.
        /// </summary>
        private List<PendingWarning> PrepareWarnings(
            GmsSystemSnapshot snapshot, Dictionary<string, BaseToolState> sensors, List<Action> writes)
        {
            var pending = new List<PendingWarning>();
            foreach (GmsSensorWarning value in snapshot.SensorWarnings)
            {
                if (!sensors.TryGetValue(value.SensorKey, out BaseToolState? source) ||
                    source is not SensorState sensor)
                {
                    throw Invalid("A sensor-warning source must be an actual single sensor of this GMS.");
                }
                NodeId input = value.InputNodeId;
                if (input.IsNull)
                {
                    input = sensor.ToolLife!.FindChild(m_context.Context, GmsName(BrowseNames.Qualified))!.NodeId;
                }
                else if (!HasVariable(sensor, input))
                {
                    throw Invalid("The sensor-warning input must be a variable under its actual local sensor.");
                }
                WarningBinding binding = m_warnings[value.Key];
                if (binding.State.LowLimit != null)
                {
                    writes.Add(() => Set(binding.State.LowLimit, value.LowLimit));
                }
                if (binding.State.HighLimit != null)
                {
                    writes.Add(() => Set(binding.State.HighLimit, value.HighLimit));
                }
                if (binding.Observation != value)
                {
                    pending.Add(new PendingWarning(binding, value, sensor, input, Uuid.NewUuid().ToByteString()));
                }
            }
            return pending;
        }

        /// <summary>
        /// Performs bounded descendant validation rather than trusting a namespace index as instance ownership.
        /// </summary>
        private bool HasVariable(NodeState source, NodeId nodeId)
        {
            var pending = new Stack<NodeState>();
            pending.Push(source);
            int count = 0;
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (++count > 4096)
                {
                    throw Invalid("The native sensor subtree exceeds its bounded input-reference search.");
                }
                if (node.NodeId == nodeId && node is BaseVariableState)
                {
                    return true;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
            return false;
        }

        /// <summary>
        /// Applies accepted condition observations through the standard lifecycle and retains exact events for retry.
        /// </summary>
        private async ValueTask PublishWarningsAsync(CancellationToken cancellationToken)
        {
            GmsSystemBinding? previous = s_callback.Value;
            s_callback.Value = this;
            try
            {
                while (m_pendingWarningIndex < m_pendingWarnings.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PendingWarning pending = m_pendingWarnings[m_pendingWarningIndex];
                    SensorWarningAlarmState state = pending.Binding.State;
                    if (pending.Event == null)
                    {
                        UpdateWarning(pending);
                        var captured = new InstanceStateSnapshot();
                        captured.Initialize(m_context.Context, state);
                        pending.Event = captured;
                        pending.Binding.Observation = pending.Value;
                    }
                    if (state.EnabledState!.Id!.Value)
                    {
                        await state.ReportEventAsync(m_context.Context, pending.Event, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    m_pendingWarningIndex++;
                }
                m_pendingWarnings.Clear();
                m_pendingWarningIndex = 0;
            }
            finally
            {
                s_callback.Value = previous;
            }
        }

        /// <summary>
        /// Updates native warning values using core acknowledgement, active-state and retain semantics.
        /// </summary>
        private void UpdateWarning(PendingWarning pending)
        {
            SensorWarningAlarmState state = pending.Binding.State;
            GmsSensorWarning value = pending.Value;
            state.SourceNode!.Value = pending.Sensor.NodeId;
            state.SourceName!.Value = value.SensorKey;
            state.InputNode!.Value = pending.Input;
            state.ErrorCode!.Value = value.ErrorCode;
            state.Message!.Value = value.Message;
            state.SetSeverity(m_context.Context, (EventSeverity)value.Severity);
            state.EventId!.Value = pending.EventId;
            state.EventType!.Value = state.TypeDefinitionId;
            state.Time!.Value = value.Sample.SourceTimestamp;
            state.ReceiveTime!.Value = (m_context.GetService<TimeProvider>() ?? TimeProvider.System)
                .GetUtcNow().UtcDateTime;
            state.Quality!.Value = value.Sample.StatusCode;
            state.Quality.SourceTimestamp!.Value = value.Sample.SourceTimestamp;
            state.Quality.StatusCode = value.Sample.StatusCode;
            state.Quality.Timestamp = value.Sample.SourceTimestamp;
            pending.Sensor.AddReferenceIfMissing(ReferenceTypeIds.HasCondition, false, state.NodeId);
            state.AddReferenceIfMissing(ReferenceTypeIds.HasCondition, true, pending.Sensor.NodeId);
            if (value.IsActive && !state.ActiveState!.Id!.Value)
            {
                state.SetAcknowledgedState(m_context.Context, false);
            }
            if (state.ActiveState!.Id!.Value != value.IsActive)
            {
                state.SetActiveState(m_context.Context, value.IsActive);
                state.ActiveState.TransitionTime?.Value = value.Sample.SourceTimestamp;
            }
            state.ActiveState.StatusCode = value.Sample.StatusCode;
            state.ActiveState.Timestamp = value.Sample.SourceTimestamp;
            state.ActiveState.Id!.StatusCode = value.Sample.StatusCode;
            state.ActiveState.Id.Timestamp = value.Sample.SourceTimestamp;
            state.ErrorCode.StatusCode = value.Sample.StatusCode;
            state.ErrorCode.Timestamp = value.Sample.SourceTimestamp;
        }

        /// <summary>
        /// Applies provider permissions through standard condition callbacks; server authorization still applies.
        /// </summary>
        private static void ConfigureWarningPermissions(WarningBinding binding)
        {
            SensorWarningAlarmState state = binding.State;
            ConditionEnableEventHandler? enable = state.OnEnableDisable;
            state.OnEnableDisable = (context, condition, enabled) =>
                Allowed(binding,
                    enabled ? MachineToolConditionOperations.Enable : MachineToolConditionOperations.Disable)
                    ? enable?.Invoke(context, condition, enabled) ?? ServiceResult.Good :
                    StatusCodes.BadUserAccessDenied;
            ConditionAddCommentEventHandler? comment = state.OnAddComment;
            state.OnAddComment = (context, condition, eventId, text) =>
                Allowed(binding, MachineToolConditionOperations.Comment)
                    ? comment?.Invoke(context, condition, eventId, text) ?? ServiceResult.Good :
                    StatusCodes.BadUserAccessDenied;
            ConditionAddCommentEventHandler? acknowledge = state.OnAcknowledge;
            state.OnAcknowledge = (context, condition, eventId, text) =>
                Allowed(binding, MachineToolConditionOperations.Acknowledge)
                    ? acknowledge?.Invoke(context, condition, eventId, text) ?? ServiceResult.Good :
                    StatusCodes.BadUserAccessDenied;
        }

        /// <summary>
        /// Tests the last accepted provider policy without affecting physical equipment.
        /// </summary>
        private static bool Allowed(WarningBinding binding, MachineToolConditionOperations operation) =>
            binding.Observation != null && (binding.Observation.AllowedOperations & operation) != 0;

        /// <summary>Persistent actual native conditions, owned by the root's shared fluent registration.</summary>
        private readonly Dictionary<string, WarningBinding> m_warnings = new(StringComparer.Ordinal);

        /// <summary>Accepted native event identities awaiting successful standard event publication.</summary>
        private List<PendingWarning> m_pendingWarnings = [];

        /// <summary>The next event to retry, without generating another identity or reacquiring equipment.</summary>
        private int m_pendingWarningIndex;

        /// <summary>
        /// Keeps protocol state in the actual generated/core condition implementation.
        /// </summary>
        private sealed class WarningBinding(SensorWarningAlarmState state)
        {
            /// <summary>
            /// Gets the actual native condition.
            /// </summary>
            internal SensorWarningAlarmState State { get; } = state;

            /// <summary>
            /// Gets or sets the last accepted equipment warning.
            /// </summary>
            internal GmsSensorWarning? Observation { get; set; }
        }

        /// <summary>
        /// Captures only a provider observation and its stable event identity, not another condition state machine.
        /// </summary>
        private sealed class PendingWarning(
            WarningBinding binding, GmsSensorWarning value, SensorState sensor, NodeId input, ByteString eventId)
        {
            /// <summary>
            /// Gets the actual condition binding.
            /// </summary>
            internal WarningBinding Binding { get; } = binding;

            /// <summary>
            /// Gets the accepted warning observation.
            /// </summary>
            internal GmsSensorWarning Value { get; } = value;

            /// <summary>
            /// Gets the validated actual sensor source.
            /// </summary>
            internal SensorState Sensor { get; } = sensor;

            /// <summary>
            /// Gets the validated actual input variable.
            /// </summary>
            internal NodeId Input { get; } = input;

            /// <summary>
            /// Gets the identity retained across failed publication retries.
            /// </summary>
            internal ByteString EventId { get; } = eventId;

            /// <summary>
            /// Gets or sets the immutable captured condition event.
            /// </summary>
            internal InstanceStateSnapshot? Event { get; set; }
        }
    }
}
