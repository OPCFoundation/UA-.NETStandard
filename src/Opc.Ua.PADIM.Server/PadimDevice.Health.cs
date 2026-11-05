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
using Opc.Ua.Di;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.PADIM.Server
{
    public sealed partial class PadimDevice
    {
        /// <summary>
        /// Binds standard device health and publishes the corresponding DI diagnostic conditions.
        /// </summary>
        /// <remarks>
        /// The provider supplies the standard DeviceHealthEnumeration, not a vendor diagnostic code.
        /// Existing server condition handling owns enable/disable, acknowledgements and refresh.
        /// </remarks>
        public PadimValueBinding WithHealthAlarms(string key, IPadimValueProvider provider)
        {
            if (m_healthAlarms != null)
            {
                throw new InvalidOperationException("Device-health alarms are already configured.");
            }
            PadimValueBinding binding = BindValue(State.DeviceHealth!, key, provider);
            State.AddDeviceHealthAlarms(Context);
            INodeBuilder root = Manager.Device(State).Node;
            ushort ns = (ushort)Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
            m_healthAlarms =
            [
                root.CreateAlarm(new QualifiedName("Failure", ns), static owner => new FailureAlarmState(owner)).Alarm,
                root.CreateAlarm(new QualifiedName("CheckFunction", ns),
                    static owner => new CheckFunctionAlarmState(owner)).Alarm,
                root.CreateAlarm(new QualifiedName("OffSpec", ns), static owner => new OffSpecAlarmState(owner)).Alarm,
                root.CreateAlarm(new QualifiedName("MaintenanceRequired", ns),
                    static owner => new MaintenanceRequiredAlarmState(owner)).Alarm
            ];
            m_healthPublicationPending = new bool[m_healthAlarms.Length];
            for (int ii = 0; ii < m_healthAlarms.Length; ii++)
            {
                AlarmConditionState alarm = m_healthAlarms[ii];
                alarm.AutoReportStateChanges = true;
                alarm.RetainStateProvider = static condition =>
                    condition is AlarmConditionState activeAlarm && activeAlarm.ActiveState!.Id!.Value;
                alarm.SetActiveState(Context, false);
                alarm.SetAcknowledgedState(Context, true);
                alarm.SetSeverity(Context, EventSeverity.Low);
                alarm.Quality!.Value = StatusCodes.BadWaitingForInitialData;
                alarm.Retain!.Value = false;
                State.DeviceHealthAlarms!.AddReference(
                    Opc.Ua.Types.ReferenceTypeIds.Organizes, isInverse: false, alarm.NodeId);
            }
            binding.ValidateSample = static sample =>
            {
                if (sample.WrappedValue.TryGetValue(out int category) && category is < 0 or > 4)
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange);
                }
            };
            binding.OnPublishedAsync = PublishHealthAsync;
            return binding;
        }

        /// <summary>
        /// Updates only the standard category supplied by the device; bad quality does not invent a healthy state.
        /// </summary>
        private async ValueTask PublishHealthAsync(DataValue sample, CancellationToken cancellationToken)
        {
            bool hasCategory = sample.WrappedValue.TryGetValue(out int category);
            var events = new List<(AlarmConditionState Alarm, InstanceStateSnapshot Snapshot)>();
            bool[] pending = m_healthPublicationPending!;
            for (int ii = 0; ii < m_healthAlarms!.Length; ii++)
            {
                AlarmConditionState alarm = m_healthAlarms[ii];
                bool active = hasCategory ? category == ii + 1 : alarm.ActiveState!.Id!.Value;
                bool activeChanged = active != alarm.ActiveState!.Id!.Value;
                bool qualityChanged = alarm.Quality!.Value != sample.StatusCode;
                if (!activeChanged && !qualityChanged && !pending[ii])
                {
                    continue;
                }
                pending[ii] = true;
                alarm.Quality.Value = sample.StatusCode;
                alarm.Quality.SourceTimestamp?.Value = sample.SourceTimestamp;
                alarm.Quality.Timestamp = sample.SourceTimestamp;
                if (active && !alarm.ActiveState.Id.Value)
                {
                    await alarm.SetAcknowledgedStateAsync(Context, false, CancellationToken.None).ConfigureAwait(false);
                }
                if (activeChanged)
                {
                    await alarm.SetActiveStateAsync(Context, active, CancellationToken.None).ConfigureAwait(false);
                    alarm.ActiveState.TransitionTime?.Value = sample.SourceTimestamp;
                    alarm.ActiveState.Timestamp = sample.SourceTimestamp;
                }
                EventSeverity severity = active ? EventSeverity.High : EventSeverity.Low;
                if (alarm.Severity!.Value != (ushort)severity)
                {
                    if (!activeChanged)
                    {
                        alarm.ActiveState.TransitionTime?.Value = sample.SourceTimestamp;
                        alarm.ActiveState.Timestamp = sample.SourceTimestamp;
                    }
                    await alarm.SetSeverityAsync(Context, severity, CancellationToken.None).ConfigureAwait(false);
                }
                if (alarm.EnabledState!.Id!.Value)
                {
                    alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
                    alarm.Time!.Value = sample.SourceTimestamp;
                    alarm.ReceiveTime!.Value = TimeProvider.GetUtcNow().UtcDateTime;
                }
                await alarm.ClearChangeMasksAsync(Context, includeChildren: true, CancellationToken.None)
                    .ConfigureAwait(false);
                if (alarm.EnabledState!.Id!.Value)
                {
                    var snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(Context, alarm);
                    events.Add((alarm, snapshot));
                }
            }
            Array.Clear(pending, 0, pending.Length);
            foreach ((AlarmConditionState alarm, InstanceStateSnapshot snapshot) in events)
            {
                await alarm.ReportEventAsync(Context, snapshot, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The native DI conditions managed by the server's existing alarm infrastructure.</summary>
        private AlarmConditionState[]? m_healthAlarms;

        /// <summary>Alarms whose attributes or notification masks need reconciliation after a sink failure.</summary>
        private bool[]? m_healthPublicationPending;
    }
}
