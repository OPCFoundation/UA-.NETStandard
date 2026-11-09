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
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Server
{
    internal sealed partial class ExtrusionDeviceBinding
    {
        /// <summary>
        /// Creates the alarm required by OPC 40084-2 §7.1/§8.3.12 without changing production state.
        /// </summary>
        internal ValueTask ReportBadProductAsync(NodeState source, DateTimeUtc timestamp)
        {
            return ReportProductQualityAsync(source, false, timestamp);
        }

        /// <summary>
        /// Uses the common generated MessageCondition and core condition lifecycle for quality changes.
        /// </summary>
        internal async ValueTask ReportProductQualityAsync(NodeState source, bool good, DateTimeUtc timestamp)
        {
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(CancellationToken.None);
            await m_qualityGate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                ISystemContext context = m_runtime.SystemContext;
                if (!m_qualityAlarms.TryGetValue(source.NodeId, out MessageConditionState? alarm))
                {
                    if (good)
                    {
                        return;
                    }
                    var name = new QualifiedName("BadProduct_" + m_qualityAlarms.Count,
                        m_runtime.InstanceNamespaceIndex);
                    alarm = context.CreateInstanceOfMessageConditionType(m_node, name);
                    m_materializer.AssignStableIds(alarm,
                        m_node.NodeId.IdentifierAsString + "/Quality/" +
                        Uri.EscapeDataString(source.NodeId.IdentifierAsString));
                    alarm.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                    alarm.CreateAsPredefinedNode(context);
                    alarm.SourceNode!.Value = source.NodeId;
                    alarm.SourceName!.Value = source.BrowseName.Name ?? string.Empty;
                    alarm.ConditionName!.Value = "BadProduct";
                    alarm.Id!.Value = "BadProduct";
                    alarm.Classification!.WrappedValue =
                        Variant.From(ExtrusionMessageClassificationEnumeration.LINE_CONTROL);
                    alarm.IsStandstillMessage!.Value = false;
                    alarm.BranchId!.Value = NodeId.Null;
                    alarm.Quality!.Value = StatusCodes.Good;
                    alarm.Comment!.Value = LocalizedText.Null;
                    alarm.ClientUserId!.Value = string.Empty;
                    alarm.SetEnableState(context, true);
                    alarm.SetActiveState(context, false);
                    alarm.SetAcknowledgedState(context, true);
                    alarm.Retain!.Value = false;
                    m_node.AddChild(alarm);
                    try
                    {
                        await m_runtime.RegisterAsync(alarm, pending.Token).ConfigureAwait(false);
                        source.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, false, alarm.NodeId);
                        alarm.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, true, source.NodeId);
                        NodeState notifier = source is Line.JobState && Jobs != null ? Jobs.Node : m_node;
                        notifier.AddNotifier(context, Opc.Ua.ReferenceTypeIds.HasEventSource, false, alarm);
                        alarm.AddNotifier(context, Opc.Ua.ReferenceTypeIds.HasEventSource, true, notifier);
                        m_qualityAlarms.Add(source.NodeId, alarm);
                    }
                    catch (Exception original)
                    {
                        try
                        {
                            await m_runtime.RemoveAsync(alarm).ConfigureAwait(false);
                        }
                        catch (Exception rollback)
                        {
                            throw new AggregateException("Quality alarm creation and rollback failed.", original,
                                rollback);
                        }
                        throw;
                    }
                }
                alarm.SetActiveState(context, !good);
                if (!good)
                {
                    alarm.SetAcknowledgedState(context, false);
                }
                alarm.SetSeverity(context, good ? EventSeverity.Low : EventSeverity.Medium);
                alarm.Retain!.Value = !good || alarm.AckedState?.Id?.Value != true;
                alarm.Message!.Value = new LocalizedText(good
                    ? "Extrusion production quality recovered."
                    : "Extrusion production quality is not acceptable.");
                alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
                alarm.Time!.Value = timestamp;
                alarm.ReceiveTime!.Value = DateTimeUtc.Now;
                alarm.ClearChangeMasks(context, true);
                var snapshot = new InstanceStateSnapshot();
                snapshot.Initialize(context, alarm);
                await alarm.ReportEventAsync(context, snapshot).ConfigureAwait(false);
            }
            finally
            {
                m_qualityGate.Release();
            }
        }

        /// <summary>One actual core condition per independent line/job quality source.</summary>
        private readonly Dictionary<NodeId, MessageConditionState> m_qualityAlarms = [];

        /// <summary>Serializes asynchronous condition creation and reporting.</summary>
        private readonly SemaphoreSlim m_qualityGate = new(1, 1);
    }
}
