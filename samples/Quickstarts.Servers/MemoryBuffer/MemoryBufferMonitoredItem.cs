// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// Provides a basic monitored item implementation which does not support queuing.
    /// </summary>
    public class MemoryBufferMonitoredItem : MonitoredItem
    {
        /// <summary>
        /// Initializes the object with its node type.
        /// </summary>
        public MemoryBufferMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            uint offset,
            uint subscriptionId,
            uint id,
            ReadValueId itemToMonitor,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            MonitoringFilter? originalFilter,
            MonitoringFilter? filterToUse,
            Range? range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double minimumSamplingInterval,
            bool createDurable)
            : base(
                server,
                nodeManager,
                managerHandle,
                subscriptionId,
                id,
                itemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                monitoringMode,
                clientHandle,
                originalFilter,
                filterToUse,
                range,
                samplingInterval,
                queueSize,
                discardOldest,
                minimumSamplingInterval,
                createDurable)
        {
            Offset = offset;
        }

        /// <summary>
        /// Initializes the object from a template
        /// </summary>
        public MemoryBufferMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            uint offset,
            IStoredMonitoredItem storedMonitoredItem)
            : base(server, nodeManager, managerHandle, storedMonitoredItem)
        {
            Offset = offset;
        }

        /// <summary>
        /// Modifies the monitored item parameters,
        /// </summary>
        public ServiceResult Modify(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            double samplingInterval)
        {
            return ModifyAttributes(
                diagnosticsMasks,
                timestampsToReturn,
                clientHandle,
                null,
                null,
                null,
                samplingInterval,
                0,
                false)!;
        }

        /// <summary>
        /// The offset in the memory buffer.
        /// </summary>
        public uint Offset { get; }
    }
}
