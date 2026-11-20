/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// The set of all service request types (used for collecting diagnostics and checking permissions).
    /// </summary>
    public enum RequestType
    {
        /// <summary>
        /// The request type is not known.
        /// </summary>
        Unknown,

        /// <summary>
        /// Find servers request type.
        /// </summary>
        FindServers,

        /// <summary>
        /// Get endpoints request type.
        /// </summary>
        GetEndpoints,

        /// <summary>
        /// Create Session request type.
        /// </summary>
        CreateSession,

        /// <summary>
        /// Activate Session request type.
        /// </summary>
        ActivateSession,

        /// <summary>
        /// CloseSession request type.
        /// </summary>
        CloseSession,

        /// <summary>
        /// Cancel request type.
        /// </summary>
        Cancel,

        /// <summary>
        /// Read request type.
        /// </summary>
        Read,

        /// <summary>
        /// HistoryRead request type.
        /// </summary>
        HistoryRead,

        /// <summary>
        /// Write request type.
        /// </summary>
        Write,

        /// <summary>
        /// HistoryUpdate request type.
        /// </summary>
        HistoryUpdate,

        /// <summary>
        /// Call request type.
        /// </summary>
        Call,

        /// <summary>
        /// Create Monitored Items request type.
        /// </summary>
        CreateMonitoredItems,

        /// <summary>
        /// Modify Monitored Items request type.
        /// </summary>
        ModifyMonitoredItems,

        /// <summary>
        /// SetMonitoringMode request type.
        /// </summary>
        SetMonitoringMode,

        /// <summary>
        /// SetTriggering request type.
        /// </summary>
        SetTriggering,

        /// <summary>
        /// Delete Monitored Items request type.
        /// </summary>
        DeleteMonitoredItems,

        /// <summary>
        /// Create Subscription request type.
        /// </summary>
        CreateSubscription,

        /// <summary>
        /// Modify Subscription request type.
        /// </summary>
        ModifySubscription,

        /// <summary>
        /// Set Publishing Mode request type.
        /// </summary>
        SetPublishingMode,

        /// <summary>
        /// Publish request type.
        /// </summary>
        Publish,

        /// <summary>
        /// Republish request type.
        /// </summary>
        Republish,

        /// <summary>
        /// Transfer Subscriptions request type.
        /// </summary>
        TransferSubscriptions,

        /// <summary>
        /// Delete Subscriptions request type.
        /// </summary>
        DeleteSubscriptions,

        /// <summary>
        /// Add Nodes request type.
        /// </summary>
        AddNodes,

        /// <summary>
        /// Add References request type.
        /// </summary>
        AddReferences,

        /// <summary>
        /// Delete Nodes request type.
        /// </summary>
        DeleteNodes,

        /// <summary>
        /// Delete References request type.
        /// </summary>
        DeleteReferences,

        /// <summary>
        /// Browse request type.
        /// </summary>
        Browse,

        /// <summary>
        /// BrowseNext request type.
        /// </summary>
        BrowseNext,

        /// <summary>
        /// Translate BrowsePaths To NodeIds request type.
        /// </summary>
        TranslateBrowsePathsToNodeIds,

        /// <summary>
        /// QueryFirst request type.
        /// </summary>
        QueryFirst,

        /// <summary>
        /// QueryNext request type.
        /// </summary>
        QueryNext,

        /// <summary>
        /// Register Nodes request type.
        /// </summary>
        RegisterNodes,

        /// <summary>
        /// Unregister Nodes request type.
        /// </summary>
        UnregisterNodes
    }
}
