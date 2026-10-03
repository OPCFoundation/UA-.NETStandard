/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional notification staging for a local address space. This delays
    /// observer callbacks, not node mutation; the owner separately controls
    /// visibility and restores its nodes when an authoritative commit is rejected.
    /// </summary>
    public interface ILocalAddressSpaceNotifications
    {
        /// <summary>
        /// Begins one notification batch in the current execution context.
        /// Dispose to discard it, or publish after the authoritative commit.
        /// </summary>
        LocalAddressSpaceNotificationBatch BeginNotificationBatch();
    }

    /// <summary>
    /// Single-use local-address-space notifications staged before publication.
    /// Notification errors happen after the owner's commit and cannot roll it back.
    /// </summary>
    public sealed class LocalAddressSpaceNotificationBatch : IDisposable
    {
        internal LocalAddressSpaceNotificationBatch(Action<LocalAddressSpaceNotificationBatch> end)
        {
            m_end = end;
        }

        /// <summary>
        /// Publishes the staged notifications once. Every notification is attempted
        /// even when an observer fails; failures are returned as an aggregate.
        /// </summary>
        /// <exception cref="InvalidOperationException">The batch was already consumed.</exception>
        /// <exception cref="AggregateException">One or more observers failed after publication.</exception>
        public void Publish()
        {
            if (Interlocked.CompareExchange(ref m_consumed, 1, 0) != 0)
            {
                throw new InvalidOperationException("The notification batch was already published or discarded.");
            }
            m_end(this);
            Action[] notifications;
            lock (m_gate)
            {
                notifications = [.. m_notifications];
                m_notifications.Clear();
            }
            var failures = new List<Exception>();
            foreach (Action notification in notifications)
            {
                try
                {
                    notification();
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
                    or IOException or HttpRequestException or ServiceResultException or OperationCanceledException)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Local address-space notification delivery failed.", failures);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref m_consumed, 2, 0) == 0)
            {
                m_end(this);
                lock (m_gate)
                {
                    m_notifications.Clear();
                }
            }
        }

        internal void Add(Action notification)
        {
            lock (m_gate)
            {
                if (Volatile.Read(ref m_consumed) != 0)
                {
                    throw new InvalidOperationException("Notification staging has already ended.");
                }
                m_notifications.Add(notification);
            }
        }

        private readonly Action<LocalAddressSpaceNotificationBatch> m_end;
        private readonly List<Action> m_notifications = [];
        private readonly Lock m_gate = new();
        private int m_consumed;
    }
}
