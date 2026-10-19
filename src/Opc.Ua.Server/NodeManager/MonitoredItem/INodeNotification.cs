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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Base interface for node notifications sent to the channel.
    /// </summary>
    internal interface INodeNotification
    {
        /// <summary>
        /// Gets the system context captured for processing the notification.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Gets the source emission for <c>INodeNotification</c>.
        /// </summary>
        MasterNodeManager.NotificationDispatchLease? SourceEmission { get; }
    }

    /// <summary>
    /// Represents a snapshot of the changing attributes of a node.
    /// </summary>
    internal class DataChangeSnapshot : INodeNotification
    {
        /// <inheritdoc cref="INodeNotification.Context"/>
        public ISystemContext Context { get; set; } = null!;

        /// <summary>
        /// Gets or sets the source emission for <c>DataChangeSnapshot</c>.
        /// </summary>
        public MasterNodeManager.NotificationDispatchLease? SourceEmission { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the node whose changes were captured.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Gets or sets the node-state changes represented by this snapshot.
        /// </summary>
        public NodeStateChangeMasks Changes { get; set; }

        /// <summary>
        /// Pre-read raw <see cref="DataValue"/>s keyed by attribute id, captured without
        /// any index range or data encoding applied. Each consumer (monitored item) applies
        /// its own <see cref="IDataChangeMonitoredItem2.IndexRange"/> and
        /// <see cref="IDataChangeMonitoredItem2.DataEncoding"/> at queue time.
        /// </summary>
        public Dictionary<uint, DataValue> AttributeSnapshots { get; set; } = [];

        /// <summary>
        /// Value snapshots read at enqueue time in the context of each Value monitored item's
        /// subscriber, keyed by monitored item id. Only captured when the reporter was denied
        /// reading a value whose user access level depends on the user, so a subscriber that may
        /// read it receives the value of this change rather than a later one.
        /// Session and identity attribution prevent a later attachment or transfer from inheriting
        /// a value captured for a different owner.
        /// </summary>
        public Dictionary<uint, (DataValue Value, ISession? Session, IUserIdentity? Identity)>?
            SubscriberValueSnapshots { get; set; }
    }

    /// <summary>
    /// Marks a change that reached a cut-off source so the items it would have reached are
    /// retired in channel order, after the snapshots captured before the cutoff.
    /// </summary>
    internal sealed class CutOffRetirementNotification : INodeNotification
    {
        /// <inheritdoc cref="INodeNotification.Context"/>
        public ISystemContext Context { get; set; } = null!;

        /// <summary>
        /// Gets the source emission, which a cut-off retirement never holds.
        /// </summary>
        public MasterNodeManager.NotificationDispatchLease? SourceEmission => null;

        /// <summary>
        /// Gets or sets the change masks of the cut-off change.
        /// </summary>
        public NodeStateChangeMasks Changes { get; set; }
    }

    /// <summary>
    /// Represents a snapshot of a node event.
    /// </summary>
    internal class EventSnapshot : INodeNotification
    {
        /// <inheritdoc cref="INodeNotification.Context"/>
        public ISystemContext Context { get; set; } = null!;

        /// <summary>
        /// Gets or sets the source emission for <c>EventSnapshot</c>.
        /// </summary>
        public MasterNodeManager.NotificationDispatchLease? SourceEmission { get; set; }

        /// <summary>
        /// Gets or sets the captured event fields used when evaluating event filters.
        /// </summary>
        public IFilterTarget EventTargetSnapshot { get; set; } = null!;
    }
}
