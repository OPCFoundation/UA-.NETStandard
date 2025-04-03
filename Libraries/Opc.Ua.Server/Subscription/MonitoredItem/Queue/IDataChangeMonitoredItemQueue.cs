/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
        void Enqueue(DataValue value, ServiceResult error);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="error">The error associated with the value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out DataValue value, out ServiceResult error);

        /// <summary>
        /// returns the the oldest value in the queue without dequeueing. Null if queue is empty
        /// </summary>
        DataValue PeekOldestValue();

        /// <summary>
        /// Replace the last (newest) value in the queue with the provided Value. Used when values are provided faster than the sampling interval
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void OverwriteLastValue(DataValue value, ServiceResult error);

        /// <summary>
        /// Returns the last (newest) value in the queue without dequeuing
        /// </summary>
        /// <returns>the last value, null if queue is empty</returns>
        DataValue PeekLastValue();
    }
}
