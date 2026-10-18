/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
