// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Reports generated events using application-owned concrete derivatives of abstract standard events.
    /// </summary>
    internal sealed class ExtrusionEventEmitter
    {
        /// <summary>
        /// Creates an emitter for a standard source/notifier.
        /// </summary>
        internal ExtrusionEventEmitter(ExtrusionRuntime runtime, BaseObjectState notifier)
        {
            m_runtime = runtime;
            m_notifier = notifier;
        }

        /// <summary>
        /// Creates the concrete derivative once per owner without changing a published standard type.
        /// </summary>
        internal async ValueTask<NodeId> EnsureTypeAsync(
            ExpandedNodeId standardType,
            string name,
            CancellationToken cancellationToken)
        {
            var id = new NodeId("Extrusion/EventTypes/" + name, m_runtime.InstanceNamespaceIndex);
            SemaphoreSlim gate = s_gates.GetValue(m_runtime.Manager, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_runtime.Manager.FindPredefinedNode(id) == null)
                {
                    var type = new BaseObjectTypeState
                    {
                        NodeId = id,
                        BrowseName = new QualifiedName(name, m_runtime.InstanceNamespaceIndex),
                        SymbolicName = name,
                        DisplayName = new LocalizedText(name),
                        SuperTypeId = ExpandedNodeId.ToNodeId(standardType, m_runtime.SystemContext.NamespaceUris),
                        IsAbstract = false,
                        IsPartOfTypeHierarchy = true
                    };
                    await m_runtime.RegisterAsync(type, cancellationToken).ConfigureAwait(false);
                }
                return id;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Publishes a complete generated event using the original committed source timestamp.
        /// </summary>
        internal ValueTask ReportAsync(
            BaseEventState notification,
            NodeId concreteType,
            NodeState source,
            DateTimeUtc timestamp,
            string message)
        {
            notification.Initialize(m_runtime.SystemContext, source, EventSeverity.Low, new LocalizedText(message));
            notification.TypeDefinitionId = concreteType;
            notification.EventType!.Value = concreteType;
            notification.Time!.Value = timestamp;
            notification.ReceiveTime!.Value = DateTimeUtc.Now;
            return m_notifier.ReportEventAsync(m_runtime.SystemContext, notification);
        }

        /// <summary>
        /// Emits the Part 3 structural change separately from production counters.
        /// </summary>
        internal ValueTask ModelChangedAsync(BaseInstanceState node, bool added)
        {
            GeneralModelChangeEventState notification =
                m_runtime.SystemContext.CreateInstanceOfGeneralModelChangeEventType();
            notification.Changes!.Value =
            [
                new ModelChangeStructureDataType
                {
                    Affected = node.NodeId,
                    AffectedType = node.TypeDefinitionId,
                    Verb = (byte)(added
                        ? ModelChangeStructureVerbMask.NodeAdded : ModelChangeStructureVerbMask.NodeDeleted)
                }
            ];
            return ReportAsync(notification, Opc.Ua.ObjectTypeIds.GeneralModelChangeEventType,
                m_notifier, DateTimeUtc.Now, added ? "Extrusion instance added." : "Extrusion instance removed.");
        }

        /// <summary>Serializes shared event-type registration without another node manager.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, SemaphoreSlim> s_gates = new();

        /// <summary>The single model and address-space owner.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The standard notifier prescribed by the relevant part.</summary>
        private readonly BaseObjectState m_notifier;
    }
}
