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
    /// Optionally supports durable queues and can be used to perform shared background operations on the queues
    /// </summary>
    public interface IMonitoredItemQueueFactory : IDisposable
    {
        /// <summary>
        /// Creates an empty queue for data values.
        /// </summary>
        IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool isDurable, uint monitoredItemId);

        /// <summary>
        /// Creates an empty queue for events.
        /// </summary>
        IEventMonitoredItemQueue CreateEventQueue(bool isDurable, uint monitoredItemId);

        /// <summary>
        /// If true durable queues can be created by the factory, if false only regular queues with small queue sizes are returned
        /// </summary>
        bool SupportsDurableQueues { get; }
    }
}
