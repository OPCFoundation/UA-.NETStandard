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
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    #region DataChangeQueueHandler
    /// <summary>
    /// Mangages a data value queue for a data change monitoredItem
    /// </summary>
    public interface IDataChangeQueueHandler : IDisposable
    {
        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        void SetQueueSize(uint queueSize, bool discardOldest, DiagnosticsMasks diagnosticsMasks);

        /// <summary>
        /// Set the sampling interval of the queue
        /// </summary>
        /// <param name="samplingInterval">the sampling interval</param>
        void SetSamplingInterval(double samplingInterval);
        /// <summary>
        /// Number of DataValues in the queue
        /// </summary>
        int ItemsInQueue { get; }
        /// <summary>
        /// Queues a value
        /// </summary>
        /// <param name="value">the dataValue</param>
        /// <param name="error">the error</param>
        void QueueValue(DataValue value, ServiceResult error);

        /// <summary>
        /// Deques the last item
        /// </summary>
        /// <param name="value"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        bool PublishSingleValue(out DataValue value, out ServiceResult error);
    }
    #endregion

    #region EventQueueHandler
    /// <summary>
    /// Mangages an event queue for usage by a MonitoredItem
    /// </summary>
    public interface IEventQueueHandler : IDisposable
    {


        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        void SetQueueSize(uint queueSize, bool discardOldest);
        /// <summary>
        /// The number of Items in the queue
        /// </summary>
        int ItemsInQueue { get; }

        /// <summary>
        /// True if the queue is overflowing
        /// </summary>
        bool Overflow { get; }

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        bool IsEventContainedInQueue(IFilterTarget instance);

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        /// <returns></returns>
        bool SetQueueOverflowIfFull();

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        void QueueEvent(EventFieldList fields);

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context"></param>
        /// <param name="notifications"></param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        void Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish);

       
    }
    #endregion
}
