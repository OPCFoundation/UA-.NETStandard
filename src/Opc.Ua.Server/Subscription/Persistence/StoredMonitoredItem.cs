// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public class StoredMonitoredItem : IStoredMonitoredItemInvalidation
    {
        /// <inheritdoc/>
        public bool IsRestored { get; set; }

        /// <inheritdoc/>
        public bool IsDeleted { get; set; }

        /// <inheritdoc/>
        public bool IsDetached { get; set; }

        /// <inheritdoc/>
        public bool IsTerminal { get; set; }

        /// <inheritdoc/>
        public uint SubscriptionId { get; set; }

        /// <inheritdoc/>
        public uint Id { get; set; }

        /// <inheritdoc/>
        public int TypeMask { get; set; }

        /// <inheritdoc/>
        public NodeId NodeId { get; set; }

        /// <inheritdoc/>
        public uint AttributeId { get; set; }

        /// <inheritdoc/>
        public string IndexRange { get; set; } = null!;

        /// <inheritdoc/>
        public QualifiedName Encoding { get; set; }

        /// <inheritdoc/>
        public DiagnosticsMasks DiagnosticsMasks { get; set; }

        /// <inheritdoc/>
        public TimestampsToReturn TimestampsToReturn { get; set; }

        /// <inheritdoc/>
        public uint ClientHandle { get; set; }

        /// <inheritdoc/>
        public MonitoringMode MonitoringMode { get; set; }

        /// <inheritdoc/>
        public MonitoringFilter OriginalFilter { get; set; } = null!;

        /// <inheritdoc/>
        public MonitoringFilter FilterToUse { get; set; } = null!;

        /// <inheritdoc/>
        public double Range { get; set; }

        /// <inheritdoc/>
        public double SamplingInterval { get; set; }

        /// <inheritdoc/>
        public uint QueueSize { get; set; }

        /// <inheritdoc/>
        public bool DiscardOldest { get; set; }

        /// <inheritdoc/>
        public int SourceSamplingInterval { get; set; }

        /// <inheritdoc/>
        public bool AlwaysReportUpdates { get; set; }

        /// <inheritdoc/>
        public bool IsDurable { get; set; }

        /// <inheritdoc/>
        public DataValue LastValue { get; set; }

        /// <inheritdoc/>
        public ServiceResult LastError { get; set; } = null!;

        /// <inheritdoc/>
        public NumericRange ParsedIndexRange { get; set; }

        /// <inheritdoc/>
        public ArrayOf<string> FilteredRetainConditionIds { get; set; }

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue? RestoredDataChangeQueue { get; set; }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue? RestoredEventQueue { get; set; }
    }
}
