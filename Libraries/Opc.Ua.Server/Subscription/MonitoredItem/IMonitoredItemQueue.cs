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
    /// Used to create <see cref="IDataChangeMonitoredItemQueue"/> / <see cref="IEventMonitoredItemQueue"/> and dispose unmanaged resources on server shutdown
    /// </summary>
    public interface IMonitoredItemQueueFactory : IDisposable
    {
        /// <summary>
        /// Creates an empty queue for data values.
        /// </summary>
        IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool isDurable);

        /// <summary>
        /// Creates an empty queue for events.
        /// </summary>
        IEventMonitoredItemQueue CreateEventQueue(bool isDurable);

        /// <summary>
        /// If true durable queues can be created by the factory, if false only regular queues with small queue sizes are returned
        /// </summary>
        bool SupportsDurableQueues { get; }
    }

    /// <summary>
    /// Provides a durable queue for data changes and events that can handle a queue size of several thousand elements.
    /// T defines if the queue is for events or for DataValues
    /// </summary>
    public interface IDataChangeMonitoredItemQueue : IDisposable
    {
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
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="queueErrors">Specifies wether errors should be queued.</param>
        void SetQueueSize(uint queueSize, bool queueErrors);

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

    /// <summary>
    /// Provides a durable queue for data changes and events that can handle a queue size of several thousand elements.
    /// T defines if the queue is for events or for DataValues
    /// </summary>
    public interface IEventMonitoredItemQueue : IDisposable
    {
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
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">if true remove oldest entries from the queue when the queue size decreases, else remove newest</param>
        void SetQueueSize(uint queueSize, bool discardOldest);

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        /// <param name="instance">the event to chack for duplicates</param>
        /// <returns>true if event already in queue</returns>
        bool IsEventContainedInQueue(IFilterTarget instance);


        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        void Enqueue(EventFieldList value);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out EventFieldList value);
    }
}
