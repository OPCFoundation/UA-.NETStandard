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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.GranulateDrying.Server
{
    public sealed partial class GranulateDryingDevice
    {
        /// <summary>
        /// Prepares bounded GDD error and maintenance conditions before a process commit becomes visible.
        /// Core condition states retain responsibility for acknowledgement, refresh, and branch semantics.
        /// </summary>
        private async ValueTask PrepareConditionsAsync(CommitState next, CancellationToken cancellationToken)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (GranulateDryingError error in next.Errors)
            {
                active.Add(error.Id);
            }
            foreach (KeyValuePair<string, HelpOffNormalAlarmState> entry in m_errors.ToArray())
            {
                HelpOffNormalAlarmState alarm = entry.Value;
                if (!active.Contains(entry.Key) && !alarm.ActiveState!.Id!.Value &&
                    alarm.AckedState!.Id!.Value && alarm.GetBranchCount() == 0)
                {
                    if (m_registered)
                    {
                        await m_runtime.Manager.DeleteNodeAsync(
                            m_runtime.Manager.SystemContext, alarm.NodeId, cancellationToken).ConfigureAwait(false);
                    }
                    if (alarm.Parent is BaseObjectState source)
                    {
                        source.RemoveNotifier(Context, alarm, true);
                        source.RemoveChild(alarm);
                    }
                    m_methods.RemoveAll(method => IsDescendantOf(method, alarm));
                    m_errors.Remove(entry.Key);
                }
            }
            int newErrors = 0;
            int reactivations = 0;
            foreach (GranulateDryingError error in next.Errors)
            {
                if (!m_errors.TryGetValue(error.Id, out HelpOffNormalAlarmState? alarm))
                {
                    newErrors++;
                }
                else if (!alarm.ActiveState!.Id!.Value && !alarm.AckedState!.Id!.Value)
                {
                    reactivations++;
                }
            }
            int branches = m_errors.Values.Sum(alarm => alarm.GetBranchCount()) +
                m_maintenanceErrors.Values.Sum(alarm => alarm.GetBranchCount());
            if (m_errors.Count + newErrors > Options.MaximumErrors ||
                branches + reactivations > Options.MaximumErrors)
            {
                throw Error(StatusCodes.BadTooManyOperations,
                    "Acknowledge retained conditions before exceeding the configured condition/branch limit.");
            }
            for (int index = 0; index < next.Errors.Count; index++)
            {
                GranulateDryingError error = next.Errors[index];
                if (m_errors.ContainsKey(error.Id))
                {
                    continue;
                }
                BaseObjectState source = Target(error.Sources[0]);
                HelpOffNormalAlarmState alarm = Context.CreateInstanceOfHelpOffNormalAlarmType(source,
                    new QualifiedName("DryingError_" + Uuid.NewUuid(), m_runtime.InstanceNamespaceIndex));
                InitializeCondition(alarm, source, error.Id);
                m_errors.Add(error.Id, alarm);
                if (m_registered)
                {
                    await m_runtime.Manager.AddPredefinedNodeAsync(alarm, cancellationToken).ConfigureAwait(false);
                }
            }
            for (int ii = 1; ii <= Options.Maintenance.Count; ii++)
            {
                if (m_maintenanceErrors.ContainsKey(ii))
                {
                    continue;
                }
                GDDMaintenanceState source = Maintenance(ii);
                MaintenanceAlarmState alarm = Context.CreateInstanceOfMaintenanceAlarmType(source,
                    new QualifiedName("MaintenanceCondition", m_runtime.InstanceNamespaceIndex));
                alarm.TypeDefinitionId = await m_runtime.MaintenanceAlarmTypeAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (source.RemainingInterval != null)
                {
                    alarm.AddRemainingInterval(Context);
                }
                alarm.Id!.Value = source.Id!.Value;
                InitializeCondition(alarm, source, source.Id.Value);
                m_maintenanceErrors.Add(ii, alarm);
                if (m_registered)
                {
                    await m_runtime.Manager.AddPredefinedNodeAsync(alarm, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Initializes source identity and reuses namespace-zero condition lifecycle methods.
        /// </summary>
        private void InitializeCondition(HelpOffNormalAlarmState alarm, BaseObjectState source, string id)
        {
            alarm.CreateAsPredefinedNode(Context);
            alarm.AutoReportStateChanges = false;
            alarm.SourceNode!.Value = source.NodeId;
            alarm.SourceName!.Value = source.BrowseName.Name ?? string.Empty;
            alarm.ConditionName!.Value = id;
            alarm.EventType!.Value = alarm.TypeDefinitionId;
            alarm.BranchId!.Value = NodeId.Null;
            alarm.InputNode!.Value = State.Operation!.ActiveErrors!.NodeId;
            alarm.ClientUserId!.Value = string.Empty;
            alarm.Quality!.Value = StatusCodes.Good;
            alarm.Comment!.Value = LocalizedText.Null;
            alarm.SetEnableState(Context, true);
            alarm.SetActiveState(Context, false);
            alarm.SetAcknowledgedState(Context, true);
            alarm.Retain!.Value = false;
            source.AddChild(alarm);
            source.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, false, alarm.NodeId);
            alarm.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, true, source.NodeId);
            source.AddNotifier(Context, Opc.Ua.ReferenceTypeIds.HasEventSource, false, alarm);
            alarm.AddNotifier(Context, Opc.Ua.ReferenceTypeIds.HasEventSource, true, source);
            if (!ReferenceEquals(source, State))
            {
                source.EventNotifier |= EventNotifiers.SubscribeToEvents;
                State.AddNotifier(Context, Opc.Ua.ReferenceTypeIds.HasEventSource, false, source);
                source.AddNotifier(Context, Opc.Ua.ReferenceTypeIds.HasEventSource, true, State);
            }
            BindConditionMethod(alarm, alarm.Acknowledge);
            BindConditionMethod(alarm, alarm.AddComment);
            BindConditionSwitch(alarm, alarm.Enable);
            BindConditionSwitch(alarm, alarm.Disable);
        }

        /// <summary>
        /// Publishes changes in the mandatory error list and GDD-specific maintenance status.
        /// </summary>
        private async ValueTask PublishConditionsAsync(CommitState previous, CommitState next, DateTimeUtc timestamp)
        {
            var active = new Dictionary<string, GranulateDryingError>(StringComparer.Ordinal);
            foreach (GranulateDryingError error in next.Errors)
            {
                active.Add(error.Id, error);
            }
            foreach (KeyValuePair<string, HelpOffNormalAlarmState> entry in m_errors)
            {
                HelpOffNormalAlarmState alarm = entry.Value;
                bool present = active.TryGetValue(entry.Key, out GranulateDryingError? error);
                LocalizedText message = present ? error!.Message : new LocalizedText("Error cleared");
                LocalizedText help = present ? error!.HelpText : LocalizedText.Null;
                ushort severity = present ? error!.Severity : Math.Max((ushort)1, alarm.Severity!.Value);
                await UpdateConditionAsync(alarm, present, severity, message, help, timestamp, false)
                    .ConfigureAwait(false);
            }
            foreach (KeyValuePair<int, MaintenanceAlarmState> entry in m_maintenanceErrors)
            {
                GranulateDryingTarget target = GranulateDryingTarget.Maintenance(entry.Key);
                MaintenanceAlarmState alarm = entry.Value;
                DataValue status = next.Values[Maintenance(entry.Key).Status!.NodeId];
                bool known = StatusCode.IsGood(status.StatusCode) &&
                    TryEnumeration(status.WrappedValue, out int number);
                int currentStatus = known && TryEnumeration(status.WrappedValue, out number) ? number : 0;
                bool changed = (int)alarm.Status!.Value != currentStatus
                    || alarm.Status.StatusCode != status.StatusCode;
                alarm.Status.Value = (MaintenanceStatusEnumeration)currentStatus;
                alarm.Status.StatusCode = status.StatusCode;
                alarm.Status.Timestamp = status.SourceTimestamp;
                if (alarm.RemainingInterval != null &&
                    m_values.TryGetValue((target, "RemainingInterval"), out ValueBinding? interval))
                {
                    DataValue value = next.Values[interval.Node.NodeId];
                    changed |= !alarm.RemainingInterval.WrappedValue.Equals(value.WrappedValue) ||
                        alarm.RemainingInterval.StatusCode != value.StatusCode;
                    alarm.RemainingInterval.WrappedValue = value.WrappedValue;
                    alarm.RemainingInterval.StatusCode = value.StatusCode;
                    alarm.RemainingInterval.Timestamp = value.SourceTimestamp;
                    EUInformation units = Maintenance(entry.Key).RemainingInterval!.EngineeringUnits?.Value ??
                        throw Error(StatusCodes.BadConfigurationError,
                            "Maintenance intervals require engineering units.");
                    alarm.RemainingInterval.AddEngineeringUnits(Context);
                    alarm.RemainingInterval.EngineeringUnits!.Value = units;
                }
                LocalizedText help = TryValue(next.Values, target, "AdditionalInformation", out DataValue guidance) &&
                    guidance.WrappedValue.TryGetValue(out string text) ? new LocalizedText(text) : LocalizedText.Null;
                ushort severity = currentStatus == 2 ? Options.Maintenance[entry.Key - 1].DueSeverity
                    : Options.Maintenance[entry.Key - 1].WarningSeverity;
                await UpdateConditionAsync(alarm, known && currentStatus != 0, severity,
                    Maintenance(entry.Key).DisplayName, help, timestamp, changed).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates a generated condition from committed data, retaining unacknowledged return-to-normal states.
        /// </summary>
        private async ValueTask UpdateConditionAsync(
            HelpOffNormalAlarmState alarm,
            bool active,
            ushort severity,
            LocalizedText message,
            LocalizedText help,
            DateTimeUtc timestamp,
            bool changed)
        {
            bool wasActive = alarm.ActiveState!.Id!.Value;
            changed |= wasActive != active || alarm.Severity!.Value != severity ||
                !alarm.Message!.Value.Equals(message) || !alarm.HelpText!.Value.Equals(help);
            if (!changed)
            {
                return;
            }
            if (active && !wasActive)
            {
                if (!alarm.AckedState!.Id!.Value && !alarm.EventId!.Value.IsNull)
                {
                    ConditionState? branch = alarm.CreateBranch(Context,
                        new NodeId(Uuid.NewUuid(), m_runtime.InstanceNamespaceIndex));
                    if (branch != null)
                    {
                        await ReportConditionAsync(branch, timestamp).ConfigureAwait(false);
                    }
                }
                alarm.SetAcknowledgedState(Context, false);
            }
            alarm.SetActiveState(Context, active);
            alarm.SetSeverity(Context, (EventSeverity)severity);
            alarm.Message!.Value = message;
            alarm.HelpText!.Value = help;
            alarm.Retain!.Value = alarm.EnabledState!.Id!.Value &&
                (active || !alarm.AckedState!.Id!.Value || alarm.GetBranchCount() != 0);
            alarm.ActiveState.Timestamp = timestamp;
            alarm.AckedState!.Timestamp = timestamp;
            alarm.Severity!.Timestamp = timestamp;
            if (alarm.EnabledState.Id.Value)
            {
                await ReportConditionAsync(alarm, timestamp).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delivers a snapshot asynchronously so existing asynchronous event sinks are never blocked.
        /// </summary>
        private async ValueTask ReportConditionAsync(ConditionState alarm, DateTimeUtc timestamp)
        {
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            alarm.Time!.Value = timestamp;
            alarm.ReceiveTime!.Value = DateTimeUtc.Now;
            alarm.ClearChangeMasks(Context, true);
            if (m_registered)
            {
                var snapshot = new InstanceStateSnapshot();
                snapshot.Initialize(Context, alarm);
                await alarm.ReportEventAsync(Context, snapshot).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds asynchronous authorization around the stack's typed acknowledgement/comment handlers.
        /// </summary>
        private void BindConditionMethod(ConditionState alarm, AddCommentMethodState? method)
        {
            if (method?.OnCall is not { } invoke)
            {
                return;
            }
            method.OnCallAsync = async (context, state, objectId, eventId, comment, ct) =>
                new AddCommentMethodStateResult
                {
                    ServiceResult = await ExecuteConditionAsync(context, alarm, method.BrowseName.Name ?? string.Empty,
                        () => invoke(context, state, objectId, eventId, comment), ct).ConfigureAwait(false)
                };
            method.OnReadUserExecutable = ReadUserExecutable;
            m_methods.Add(method);
        }

        /// <summary>
        /// Adds asynchronous authorization around the stack's condition enable/disable handlers.
        /// </summary>
        private void BindConditionSwitch(ConditionState alarm, MethodState? method)
        {
            if (method?.OnCallMethod is not { } invoke)
            {
                return;
            }
            method.OnCallMethod2Async = (context, state, _, arguments, outputs, ct) =>
                ExecuteConditionAsync(context, alarm, method.BrowseName.Name ?? string.Empty,
                    () => invoke(context, state, arguments, outputs), ct);
            method.OnReadUserExecutable = ReadUserExecutable;
            m_methods.Add(method);
        }

        /// <summary>
        /// Authorizes administrative condition state separately from physical error reset.
        /// </summary>
        private async ValueTask<ServiceResult> ExecuteConditionAsync(
            ISystemContext context,
            ConditionState alarm,
            string member,
            Func<ServiceResult> invoke,
            CancellationToken cancellationToken)
        {
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                var command = new GranulateDryingCommand(
                    GranulateDryingOperation.CommonOperation, GranulateDryingTarget.Device)
                {
                    SessionId = SessionId(context),
                    Member = "Conditions/" + alarm.ConditionName!.Value + "/" + member
                };
                WatchControlSession(command.SessionId);
                await AuthorizeAsync(context, command, pending.Token).ConfigureAwait(false);
                ServiceResult result = invoke();
                if (ServiceResult.IsGood(result))
                {
                    ClaimControl(context, command.SessionId);
                    await ReportConditionAsync(alarm, DateTimeUtc.Now).ConfigureAwait(false);
                }
                return result;
            }
            catch (ServiceResultException exception)
            {
                return exception.Result;
            }
            finally
            {
                EndControlSession();
                m_gate.Release();
            }
        }

        /// <summary>
        /// Identifies method resources belonging to a retired condition graph.
        /// </summary>
        private static bool IsDescendantOf(BaseInstanceState node, NodeState ancestor)
        {
            for (BaseInstanceState? current = node; current != null; current = current.Parent as BaseInstanceState)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Bounded active or retained physical error conditions.
        /// </summary>
        private readonly Dictionary<string, HelpOffNormalAlarmState> m_errors = new(StringComparer.Ordinal);

        /// <summary>
        /// One GDD-specific maintenance condition per configured category.
        /// </summary>
        private readonly Dictionary<int, MaintenanceAlarmState> m_maintenanceErrors = [];
    }
}
