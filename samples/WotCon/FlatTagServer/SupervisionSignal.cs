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
using System.Threading.Tasks;
using Opc.Ua;

namespace FlatTagServer
{
    /// <summary>
    /// One supervision signal of a pump, exposed both as the boolean tag the
    /// aggregate Thing Description reads and as the OPC UA alarm condition an
    /// operator subscribes to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are deliberately the same signal seen twice. The boolean is what
    /// a flat-tag client polls; the condition is what a Part 9 client subscribes
    /// to, acknowledges and confirms. Driving both from one setter is what makes
    /// the sample able to demonstrate that an aggregating server can turn the
    /// former into the latter.
    /// </para>
    /// <para>
    /// The condition supports acknowledgement and confirmation because the
    /// aggregating server propagates a client's acknowledgement back to this
    /// server; without <c>ConfirmedState</c> there would be nothing for the
    /// second half of that round trip to reach.
    /// </para>
    /// </remarks>
    public sealed class SupervisionSignal
    {
        /// <summary>
        /// Creates a supervision signal and its alarm condition.
        /// </summary>
        /// <param name="context">The system context used to create the nodes.</param>
        /// <param name="telemetry">The telemetry context of the owning server.</param>
        /// <param name="parent">The supervision Object that owns the signal.</param>
        /// <param name="namespaceIndex">The namespace the nodes belong to.</param>
        /// <param name="tagPath">The node identifier of the boolean tag.</param>
        /// <param name="name">The browse name of the boolean tag.</param>
        /// <param name="conditionName">The browse name of the alarm condition.</param>
        /// <param name="severity">The severity reported while the alarm is active.</param>
        /// <param name="initiallyActive">The initial state of the signal.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="parent"/> is <c>null</c>.
        /// </exception>
        public SupervisionSignal(
            ISystemContext context,
            ITelemetryContext telemetry,
            BaseObjectState parent,
            ushort namespaceIndex,
            string tagPath,
            string name,
            string conditionName,
            ushort severity,
            bool initiallyActive)
        {
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            m_severity = severity;
            m_active = initiallyActive;

            Tag = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(tagPath, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                DataType = DataTypeIds.Boolean,
                ValueRank = ValueRanks.Scalar,
                // Writable so a client can trip the supervision signal, which is
                // how the sample and its tests drive the alarm without waiting on
                // a simulated process.
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Historizing = false,
                Value = Variant.From(initiallyActive),
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            parent.AddChild(Tag);

            Condition = new AlarmConditionState(telemetry, parent);
            // The optional children have to exist before Create runs: Create is
            // what assigns their NodeIds and wires the Part 9 method handlers, so
            // attaching them afterwards leaves them unidentified and the whole
            // address space fails to import.
            Condition.ConfirmedState = new TwoStateVariableState(Condition);
            Condition.Confirm = new AddCommentMethodState(Condition);
            Condition.Create(
                context,
                new NodeId(tagPath + ".Alarm", namespaceIndex),
                new QualifiedName(conditionName, namespaceIndex),
                new LocalizedText("en", conditionName),
                assignNodeIds: true);
            parent.AddChild(Condition);

            Condition.SourceNode!.Value = parent.NodeId;
            Condition.SourceName!.Value = parent.BrowseName.Name ?? string.Empty;
            Condition.ConditionName!.Value = conditionName;
            Condition.AutoReportStateChanges = true;

            Condition.SetEnableState(context, enabled: true);
            Condition.SetSeverity(context, EventSeverity.Medium);
            ApplyState(context, initiallyActive, report: false);

            // Writing the tag is what trips the signal, so the write has to drive
            // the condition rather than only store a value; otherwise the two
            // views of one signal would disagree.
            Tag.OnSimpleWriteValueAsync = (writeContext, _, value, _) =>
            {
                if (!value.TryGetValue(out bool requested))
                {
                    return new ValueTask<AttributeWriteResult>(
                        new AttributeWriteResult(StatusCodes.BadTypeMismatch));
                }
                if (requested != m_active)
                {
                    SetActive(writeContext, requested);
                }
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good));
            };
        }

        /// <summary>
        /// Gets the boolean tag the aggregate Thing Description reads.
        /// </summary>
        public BaseDataVariableState Tag { get; }

        /// <summary>
        /// Gets the alarm condition raised while the signal is active.
        /// </summary>
        public AlarmConditionState Condition { get; }

        /// <summary>
        /// Gets whether the signal is currently active.
        /// </summary>
        public bool IsActive => m_active;

        /// <summary>
        /// Drives both the tag and the alarm condition from one signal value and
        /// reports the resulting event.
        /// </summary>
        /// <remarks>
        /// Going active clears acknowledgement and confirmation so the condition
        /// requires operator attention, and sets <c>Retain</c> so a
        /// <c>ConditionRefresh</c> replays it. Going inactive leaves the
        /// condition retained until it has been both acknowledged and confirmed,
        /// which is what OPC 10000-9 requires of an alarm the operator has not
        /// finished with.
        /// </remarks>
        /// <param name="context">The system context.</param>
        /// <param name="active">The new state of the signal.</param>
        public void SetActive(ISystemContext context, bool active)
        {
            m_active = active;

            Tag.Value = Variant.From(active);
            Tag.Timestamp = DateTime.UtcNow;
            Tag.ClearChangeMasks(context, includeChildren: false);

            ApplyState(context, active, report: true);
        }

        /// <summary>
        /// Applies one signal value to the condition, optionally reporting the
        /// event.
        /// </summary>
        /// <remarks>
        /// The initial state and every later transition go through here so a
        /// signal that starts active is indistinguishable from one that is
        /// tripped a moment later. Reporting is suppressed for the initial state
        /// because the address space is still being built and there is nobody to
        /// notify yet.
        /// </remarks>
        private void ApplyState(ISystemContext context, bool active, bool report)
        {
            Condition.SetActiveState(context, active);
            if (active)
            {
                Condition.SetSeverity(context, (EventSeverity)m_severity);
                Condition.SetAcknowledgedState(context, acknowledged: false);
                Condition.SetConfirmedState(context, confirmed: false);
                Condition.Message!.Value = new LocalizedText(
                    "en",
                    Condition.ConditionName!.Value + " is active.");
                Condition.Retain!.Value = true;
            }
            else
            {
                Condition.SetAcknowledgedState(context, acknowledged: true);
                Condition.SetConfirmedState(context, confirmed: true);
                Condition.Message!.Value = new LocalizedText(
                    "en",
                    Condition.ConditionName!.Value + " returned to normal.");
                Condition.Retain!.Value = false;
            }

            if (report)
            {
                Condition.ReportEvent(context, Condition);
            }
        }

        private readonly ushort m_severity;
        private bool m_active;
    }
}
