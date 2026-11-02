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
using Opc.Ua.AMB;
using Opc.Ua.LADS.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The handle of a maintenance task: a <c>MaintenanceRequiredAlarmType</c> condition with the AMB
    /// <c>MaintenanceEventStateMachineType</c> (Planned, Executing, Finished).
    /// </summary>
    internal sealed class LadsMaintenanceTask : ILadsMaintenanceTask, IDisposable
    {
        /// <summary>
        /// Creates the handle, initializes the condition and binds the task methods.
        /// </summary>
        public LadsMaintenanceTask(LadsDevice device, MaintenanceTaskState node, LadsMaintenanceTaskOptions options)
        {
            Device = device;
            Node = node;
            m_options = options;
            ISystemContext context = Runtime.Context;
            node.AddStartTask(context).AddStopTask(context).AddResetTask(context).AddLastExecutionDate(context);
            if (options.RecurrencePeriod is TimeSpan recurrence)
            {
                node.AddRecurrencePeriod(context);
                node.RecurrencePeriod!.Value = recurrence.TotalMilliseconds;
            }
            if (options.EstimatedDowntime is TimeSpan downtime)
            {
                node.AddEstimatedDowntime(context);
                node.EstimatedDowntime!.Value = downtime.TotalMilliseconds;
            }
            if (!options.PlannedDate.IsNull)
            {
                node.AddPlannedDate(context);
                node.PlannedDate!.Value = options.PlannedDate;
            }
            node.LastExecutionDate!.Value = DateTimeUtc.MinValue;

            MaintenanceEventStateMachineState stateMachine = node.MaintenanceState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The maintenance state machine is missing.");
            Machine = new LadsStateMachine<LadsMaintenanceStateValue>(
                stateMachine,
                context,
                LadsStateMaps.Maintenance,
                LadsMaintenanceStateValue.Planned);
            Machine.StateChanged += state =>
            {
                Runtime.Logger.MaintenanceStateChanged(Device.Name, Name, state);
                StateChanged?.Invoke(state);
            };

            node.EventType?.Value = node.TypeDefinitionId;
            node.SourceNode?.Value = device.NodeId;
            node.SourceName?.Value = device.Name;
            node.ConditionName?.Value = Name;
            node.ConditionClassId?.Value = global::Opc.Ua.ObjectTypeIds.MaintenanceConditionClassType;
            node.ConditionClassName?.Value = new LocalizedText("MaintenanceConditionClassType");
            node.AutoReportStateChanges = true;
            node.SetEnableState(context, true);
            ApplyCondition(context, active: true, message: default);
            node.ClearChangeMasks(context, true);

            Runtime.Bind(node.StartTask, (_, ct) => StartAsync(ct));
            Runtime.Bind(node.StopTask, (inputs, ct) =>
            {
                var result = MaintenanceTaskResultEnum.Undetermined;
                if (inputs.Count > 0 && inputs[0].TryGetValue(out int code))
                {
                    if (!Enum.IsDefined(typeof(MaintenanceTaskResultEnum), code))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                    result = (MaintenanceTaskResultEnum)code;
                }
                LocalizedText comment = default;
                if (inputs.Count > 1)
                {
                    inputs[1].TryGetValue(out comment);
                }
                return StopAsync(result, comment, ct);
            });
            Runtime.Bind(node.ResetTask, (_, ct) => ResetAsync(ct));
        }

        /// <summary>
        /// Gets the device.
        /// </summary>
        public LadsDevice Device { get; }

        /// <summary>
        /// Gets the condition node.
        /// </summary>
        public MaintenanceTaskState Node { get; }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime => Device.Runtime;

        /// <summary>
        /// Gets the maintenance state machine.
        /// </summary>
        public LadsStateMachine<LadsMaintenanceStateValue> Machine { get; }

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        ILadsDevice ILadsMaintenanceTask.Device => Device;

        /// <inheritdoc/>
        public LadsMaintenanceStateValue State => Machine.Current;

        /// <inheritdoc/>
        public DateTimeUtc LastExecutionDate
        {
            get
            {
                lock (m_lock)
                {
                    return m_lastExecution;
                }
            }
        }

        /// <inheritdoc/>
        public MaintenanceTaskResultEnum LastResult
        {
            get
            {
                lock (m_lock)
                {
                    return m_lastResult;
                }
            }
        }

        /// <inheritdoc/>
        public event Action<LadsMaintenanceStateValue>? StateChanged;

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Machine.Require(LadsMaintenanceStateValue.Planned);
                await AskProviderAsync(LadsMaintenanceCommand.Start, cancellationToken).ConfigureAwait(false);
                Transition(LadsMaintenanceStateValue.Executing);
                ApplyCondition(Runtime.Context, active: true, message: default);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(
            MaintenanceTaskResultEnum result = MaintenanceTaskResultEnum.Success,
            LocalizedText comment = default,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Machine.Require(LadsMaintenanceStateValue.Executing);
                await AskProviderAsync(LadsMaintenanceCommand.Stop, cancellationToken).ConfigureAwait(false);
                DateTimeUtc now = Runtime.TimeProvider.GetUtcNow();
                lock (m_lock)
                {
                    m_lastExecution = now;
                    m_lastResult = result;
                }
                Runtime.Update(Node.LastExecutionDate, Variant.From(now));
                Transition(LadsMaintenanceStateValue.Finished);
                ApplyCondition(Runtime.Context, active: false, comment);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Machine.Require(LadsMaintenanceStateValue.Finished);
                await AskProviderAsync(LadsMaintenanceCommand.Reset, cancellationToken).ConfigureAwait(false);
                if (m_options.RecurrencePeriod is TimeSpan recurrence && Node.PlannedDate != null)
                {
                    DateTime next = LastExecutionDate.ToDateTime() + recurrence;
                    Runtime.Update(Node.PlannedDate, Variant.From((DateTimeUtc)next));
                }
                Transition(LadsMaintenanceStateValue.Planned);
                ApplyCondition(Runtime.Context, active: true, message: default);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Asks the device provider to accept a command.
        /// </summary>
        private async ValueTask AskProviderAsync(LadsMaintenanceCommand command, CancellationToken cancellationToken)
        {
            ServiceResult result = await Device.Provider
                .OnMaintenanceTaskAsync(this, command, cancellationToken)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                Runtime.Logger.CommandRejected(Name, command.ToString(), result.StatusCode);
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Applies a transition and rejects a vetoed one.
        /// </summary>
        private void Transition(LadsMaintenanceStateValue target)
        {
            if (!Machine.TransitionTo(target))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The transition of the maintenance task '{0}' to {1} was vetoed.",
                    Name,
                    target);
            }
        }

        /// <summary>
        /// Updates the condition state and reports it as an event.
        /// </summary>
        private void ApplyCondition(ISystemContext context, bool active, LocalizedText message)
        {
            MaintenanceTaskState condition = Node;
            if (condition.Message != null)
            {
                condition.Message.Value = message.IsNullOrEmpty
                    ? (m_options.Message.IsNullOrEmpty ? new LocalizedText(Name) : m_options.Message)
                    : message;
            }
            if (active)
            {
                if (condition.ActiveState?.Id?.Value != true)
                {
                    condition.SetActiveState(context, true);
                    condition.SetAcknowledgedState(context, false);
                }
                condition.SetSeverity(context, (EventSeverity)ActiveSeverity);
                condition.Retain?.Value = true;
            }
            else
            {
                condition.SetActiveState(context, false);
                condition.SetSeverity(context, (EventSeverity)InactiveSeverity);
                condition.Retain?.Value = condition.AckedState?.Id?.Value != true;
            }
            if (!Device.IsPublished || condition.EnabledState?.Id?.Value != true)
            {
                condition.ClearChangeMasks(context, true);
                return;
            }
            condition.EventId?.Value = Uuid.NewUuid().ToByteString();
            condition.Time?.Value = DateTimeUtc.Now;
            condition.ReceiveTime?.Value = DateTimeUtc.Now;
            condition.ClearChangeMasks(context, true);
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, condition);
            condition.ReportEvent(context, snapshot);
        }

        /// <summary>
        /// Releases the command gate.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private const ushort ActiveSeverity = 500;
        private const ushort InactiveSeverity = 100;
        private readonly LadsMaintenanceTaskOptions m_options;
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private DateTimeUtc m_lastExecution = DateTimeUtc.MinValue;
        private MaintenanceTaskResultEnum m_lastResult = MaintenanceTaskResultEnum.Undetermined;
    }
}
