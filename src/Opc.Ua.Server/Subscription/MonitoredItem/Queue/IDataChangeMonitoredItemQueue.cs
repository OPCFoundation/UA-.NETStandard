// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides an optionally durable queue for data changes used by <see cref="DataChangeQueueHandler"/> and created by <see cref="IMonitoredItemQueueFactory"/>.
    /// If long running operations are performed by the queue the operation should be performed in a separate thread to avoid blocking the main thread.
    /// Min Enqueue performance: no long running operations, fast enqueue until max queue size is reached
    /// Min Dequeue performance: MaxNotificationsPerPublish * 3 with no delay, in a cycle of 3 * MinPublishingInterval in the least favorable condition (single MI, continous publishing (MinPublishingInterval --, MaxNotificationsPerPublish ++), very large queue)
    /// Queue reset is allowed to be slow
    /// </summary>
    public interface IDataChangeMonitoredItemQueue : IDisposable
    {
        /// <summary>
        /// The Id of the MonitoredItem associated with the queue
        /// </summary>
        uint MonitoredItemId { get; }

        /// <summary>
        /// True if the queue is in durable mode and persists the queue values / supports a large queue size
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        uint QueueSize { get; }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        int ItemsInQueue { get; }

        /// <summary>
        /// Resets thew queue, sets the new queue size initializes an empty queue (caller handles existing entries).
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="queueErrors">Specifies wether errors should be queued.</param>
        void ResetQueue(uint queueSize, bool queueErrors);

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void Enqueue(in DataValue value, ServiceResult error);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="error">The error associated with the value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out DataValue value, out ServiceResult error);

        /// <summary>
        /// Returns the oldest value in the queue without dequeueing.
        /// </summary>
        /// <param name="value">The oldest value, when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a value was peeked; <c>false</c> when the queue is empty.</returns>
        bool TryPeekOldestValue(out DataValue value);

        /// <summary>
        /// Replace the last (newest) value in the queue with the provided Value. Used when values are provided faster than the sampling interval
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void OverwriteLastValue(in DataValue value, ServiceResult error);

        /// <summary>
        /// Returns the last (newest) value in the queue without dequeuing.
        /// </summary>
        /// <param name="value">The newest value, when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a value was peeked; <c>false</c> when the queue is empty.</returns>
        bool TryPeekLastValue(out DataValue value);
    }
}
