/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Used to create <see cref="IDurableMonitoredItemQueue"/> and dispose unmanaged resources on server shutdown
    /// </summary>
    public interface IDurableMonitoredItemQueueFactory : IDisposable
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        IDurableMonitoredItemQueue Create(uint monitoredItemId, Action discardedValueHandler = null);
    }

    /// <summary>
    /// Provides a durable queue for data changes and events that can handle a queue size of several thousand elements.
    /// Internally two queues are used, one for events and one for DataValues
    /// </summary>
    public interface IDurableMonitoredItemQueue : IDisposable
    {
        /// <summary>
        /// The event for the discarded value handler.
        /// </summary>
        event Action DiscardedValueHandler;

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
        int ItemsInValueQueue { get; }

        /// <summary>
        /// Gets number of elements actually contained in event queue.
        /// </summary>
        int ItemsInEventQueue { get; }

        /// <summary>
        /// Sets the sampling interval used when queuing values.
        /// </summary>
        /// <param name="samplingInterval">The new sampling interval.</param>
        void SetSamplingInterval(double samplingInterval);

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        /// <param name="durable">Specifies if the queue is durable and values will be persisted to storage.</param>
        void SetQueueSize(uint queueSize, bool discardOldest, DiagnosticsMasks diagnosticsMasks, bool durable);

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void QueueValue(DataValue value, ServiceResult error);

        /// <summary>
        /// Adds an Event to the queue.
        /// </summary>
        /// <param name="fields">The event to queue.</param>
        /// <param name="error">The error to queue</param>
        void QueueEvent(EventFieldList fields, ServiceResult error);

        /// <summary>
        /// Publishes the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="error">The error associated with the value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool PublishValue(out DataValue value, out ServiceResult error);

        /// <summary>
        /// Publishes the oldest event in the queue.
        /// </summary>
        /// <param name="fields">The event</param>
        /// <param name="error">The error associated with the event</param>
        /// <returns></returns>
        bool PublishEvent(out EventFieldList fields, out ServiceResult error);
    }
}
