// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
