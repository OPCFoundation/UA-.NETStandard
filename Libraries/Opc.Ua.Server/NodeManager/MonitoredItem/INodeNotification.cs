using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Base interface for node notifications sent to the channel.
    /// </summary>
    internal interface INodeNotification
    {
        ISystemContext Context { get; }
    }

    /// <summary>
    /// Represents a snapshot of the changing attributes of a node.
    /// </summary>
    internal class DataChangeSnapshot : INodeNotification
    {
        public ISystemContext Context { get; set; } = null!;
        public NodeId NodeId { get; set; }
        public NodeStateChangeMasks Changes { get; set; }

        /// <summary>
        /// Pre-read raw <see cref="DataValue"/>s keyed by attribute id, captured without
        /// any index range or data encoding applied. Each consumer (monitored item) applies
        /// its own <see cref="IDataChangeMonitoredItem2.IndexRange"/> and
        /// <see cref="IDataChangeMonitoredItem2.DataEncoding"/> at queue time.
        /// </summary>
        public Dictionary<uint, DataValue> AttributeSnapshots { get; set; } = new();
    }

    /// <summary>
    /// Represents a snapshot of a node event.
    /// </summary>
    internal class EventSnapshot : INodeNotification
    {
        public ISystemContext Context { get; set; } = null!;
        public IFilterTarget EventTargetSnapshot { get; set; } = null!;
    }
}
