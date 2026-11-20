// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides an optionally durable queue for events used by <see cref="EventQueueHandler"/> and created by <see cref="IMonitoredItemQueueFactory"/>.
    /// If long running operations are performed by the queue the operation should be performed in a separate thread to avoid blocking the main thread.
    /// Min Enqueue performance: no long running operations, fast enqueue until max queue size is reached
    /// Min Dequeue performance: MaxNotificationsPerPublish * 3 with no delay, in a cycle of 3 * MinPublishingInterval in the least favorable condition (single MI, continous publishing (MinPublishingInterval --, MaxNotificationsPerPublish ++), very large queue)
    /// Set Queue size is allowed to be slow
    /// </summary>
    public interface IEventMonitoredItemQueue : IDisposable
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
        /// Sets the queue size. If the queue contained entries before strip the existing
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">if true remove oldest entries from the queue when the queue size decreases, else remove newest</param>
        void SetQueueSize(uint queueSize, bool discardOldest);

        /// <summary>
        /// Checks the last (newest) 1k queue entries if the event is already in there
        /// used to detect duplicate instances of the same event being reported via multiple paths.
        /// </summary>
        /// <param name="instance">the event to chack for duplicates</param>
        /// <returns>true if event already in queue</returns>
        bool IsEventContainedInQueue(IFilterTarget instance);

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        void Enqueue(QueuedEvent value);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out QueuedEvent value);
    }
}
