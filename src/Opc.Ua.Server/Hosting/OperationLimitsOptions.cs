// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Configurable server operation limits exposed via the unified
    /// dependency-injection surface. Binder-friendly mirror of
    /// <see cref="OperationLimits"/>; copied verbatim into
    /// <see cref="ServerConfiguration.OperationLimits"/> by the hosted
    /// service when <see cref="OpcUaServerOptions.OperationLimits"/> is
    /// non-null.
    /// </summary>
    /// <remarks>
    /// Any value left at zero is treated as "unlimited" by the OPC UA
    /// server stack — match the underlying
    /// <see cref="OperationLimits"/> semantics.
    /// </remarks>
    public sealed class OperationLimitsOptions
    {
        /// <inheritdoc cref="OperationLimits.MaxNodesPerRead"/>
        public uint MaxNodesPerRead { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerHistoryReadData"/>
        public uint MaxNodesPerHistoryReadData { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerHistoryReadEvents"/>
        public uint MaxNodesPerHistoryReadEvents { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerWrite"/>
        public uint MaxNodesPerWrite { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerHistoryUpdateData"/>
        public uint MaxNodesPerHistoryUpdateData { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerHistoryUpdateEvents"/>
        public uint MaxNodesPerHistoryUpdateEvents { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerMethodCall"/>
        public uint MaxNodesPerMethodCall { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerBrowse"/>
        public uint MaxNodesPerBrowse { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerRegisterNodes"/>
        public uint MaxNodesPerRegisterNodes { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds"/>
        public uint MaxNodesPerTranslateBrowsePathsToNodeIds { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxNodesPerNodeManagement"/>
        public uint MaxNodesPerNodeManagement { get; set; }

        /// <inheritdoc cref="OperationLimits.MaxMonitoredItemsPerCall"/>
        public uint MaxMonitoredItemsPerCall { get; set; }

        /// <summary>
        /// Projects this binder-friendly view into the OPC UA
        /// <see cref="OperationLimits"/> structure consumed by
        /// <c>ApplicationConfigurationBuilder.SetOperationLimits</c>.
        /// </summary>
        internal OperationLimits ToOperationLimits()
        {
            return new OperationLimits
            {
                MaxNodesPerRead = MaxNodesPerRead,
                MaxNodesPerHistoryReadData = MaxNodesPerHistoryReadData,
                MaxNodesPerHistoryReadEvents = MaxNodesPerHistoryReadEvents,
                MaxNodesPerWrite = MaxNodesPerWrite,
                MaxNodesPerHistoryUpdateData = MaxNodesPerHistoryUpdateData,
                MaxNodesPerHistoryUpdateEvents = MaxNodesPerHistoryUpdateEvents,
                MaxNodesPerMethodCall = MaxNodesPerMethodCall,
                MaxNodesPerBrowse = MaxNodesPerBrowse,
                MaxNodesPerRegisterNodes = MaxNodesPerRegisterNodes,
                MaxNodesPerTranslateBrowsePathsToNodeIds = MaxNodesPerTranslateBrowsePathsToNodeIds,
                MaxNodesPerNodeManagement = MaxNodesPerNodeManagement,
                MaxMonitoredItemsPerCall = MaxMonitoredItemsPerCall
            };
        }
    }
}
