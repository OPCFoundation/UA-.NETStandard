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
    /// A monitored item in a format to be persited by an <see cref="ISubscriptionStore"/>
    /// </summary>
    public interface IStoredMonitoredItem
    {
        /// <summary>
        /// If the item was restored by a node manager
        /// </summary>
        bool IsRestored { get; set; }

        /// <summary>
        /// Whether the monitored node was deleted while the item was monitoring it. A restored
        /// item keeps reporting the node as missing until a compatible node is added again.
        /// </summary>
        bool IsDeleted { get; set; }

        /// <summary>
        /// Whether the item is detached from the NodeManager that used to own it, which is the
        /// state it is left in when its node manager is retired or its node is deleted.
        /// </summary>
        bool IsDetached { get; set; }

        /// <summary>
        /// Alwasys report Updates
        /// </summary>
        bool AlwaysReportUpdates { get; set; }

        /// <summary>
        /// The attribute to monitor
        /// </summary>
        uint AttributeId { get; set; }

        /// <summary>
        /// Identifier of the client
        /// </summary>
        uint ClientHandle { get; set; }

        /// <summary>
        /// The diagnostics masks
        /// </summary>
        DiagnosticsMasks DiagnosticsMasks { get; set; }

        /// <summary>
        /// If the oldes or newest entry shall be discarded on queue overflw
        /// </summary>
        bool DiscardOldest { get; set; }

        /// <summary>
        /// The encoding to use
        /// </summary>
        QualifiedName Encoding { get; set; }

        /// <summary>
        /// The Id of the monitored Item
        /// </summary>
        uint Id { get; set; }

        /// <summary>
        /// The Index Range
        /// </summary>
        string IndexRange { get; set; }

        /// <summary>
        /// The parsed index range
        /// </summary>
        NumericRange ParsedIndexRange { get; set; }

        /// <summary>
        /// If the monitored item is child of a durable subscription
        /// </summary>
        bool IsDurable { get; set; }

        /// <summary>
        /// The last error to notify
        /// </summary>
        ServiceResult LastError { get; set; }

        /// <summary>
        /// THe last value to notify
        /// </summary>
        DataValue LastValue { get; set; }

        /// <summary>
        /// The Monitoring Mode
        /// </summary>
        MonitoringMode MonitoringMode { get; set; }

        /// <summary>
        /// The NodeId being monitored
        /// </summary>
        NodeId NodeId { get; set; }

        /// <summary>
        /// The monitoring filter to use
        /// </summary>
        MonitoringFilter FilterToUse { get; set; }

        /// <summary>
        /// The original monitoring filter
        /// </summary>
        MonitoringFilter OriginalFilter { get; set; }

        /// <summary>
        /// The queue size
        /// </summary>
        uint QueueSize { get; set; }

        /// <summary>
        /// The Range
        /// </summary>
        double Range { get; set; }

        /// <summary>
        /// The sampling invterval to use
        /// </summary>
        double SamplingInterval { get; set; }

        /// <summary>
        /// The source sampling interval
        /// </summary>
        int SourceSamplingInterval { get; set; }

        /// <summary>
        /// The id of the subscription owning the monitored item
        /// </summary>
        uint SubscriptionId { get; set; }

        /// <summary>
        /// The timestamps to return
        /// </summary>
        TimestampsToReturn TimestampsToReturn { get; set; }

        /// <summary>
        /// The type mask
        /// </summary>
        int TypeMask { get; set; }

        /// <summary>
        /// The conditions that currently pass the item's event filter and are tracked for
        /// filtered retain (OPC UA Part 9, B.1.4).
        /// </summary>
        /// <remarks>
        /// Without this state a durable subscription drops the first transition out of
        /// filter scope after a restart, because the item no longer knows that the client
        /// had ever been told about the condition. The entries are opaque keys built by the
        /// monitored item; a store only has to round-trip them. A null or empty array both
        /// mean nothing is being tracked.
        /// </remarks>
        ArrayOf<string> FilteredRetainConditionIds { get; set; }

        /// <summary>
        /// An optional data-change queue pre-hydrated by an asynchronous
        /// <see cref="ISubscriptionStore"/> during restore. Runtime-only; never persisted.
        /// </summary>
        IDataChangeMonitoredItemQueue? RestoredDataChangeQueue { get; set; }

        /// <summary>
        /// An optional event queue pre-hydrated by an asynchronous
        /// <see cref="ISubscriptionStore"/> during restore. Runtime-only; never persisted.
        /// </summary>
        IEventMonitoredItemQueue? RestoredEventQueue { get; set; }
    }

    /// <summary>
    /// Optional persisted state for a monitored item notification that must
    /// survive queue overflow and sampling coalescing.
    /// </summary>
    public interface IStoredMonitoredItemNotificationState
    {
        /// <summary>
        /// Whether a required notification is awaiting publication.
        /// </summary>
        bool RequiredValuePending { get; set; }

        /// <summary>
        /// The required notification value.
        /// </summary>
        DataValue RequiredValue { get; set; }

        /// <summary>
        /// The error associated with the required notification.
        /// </summary>
        ServiceResult RequiredError { get; set; }
    }
}
