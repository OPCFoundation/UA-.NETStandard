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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    public partial class Subscription
    {
        /// <summary>
        /// Registers without acquiring the subscription lock while a monitored-item lock is held.
        /// </summary>
        internal bool RegisterValuePublication(MonitoredValuePublication publication, ISession session)
        {
            if (IsValuePublicationSessionUnavailable(session))
            {
                return false;
            }
            if (!ReferenceEquals(Session, session))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The subscription was transferred.");
            }
            m_valuePublications.TryAdd(publication, 0);

            // Teardown may have taken its snapshot before this registration was visible.
            if (IsValuePublicationSessionUnavailable(session))
            {
                publication.Complete(published: false);
            }
            else if (!ReferenceEquals(Session, session))
            {
                publication.Fail(StatusCodes.BadInvalidState);
            }
            return true;
        }

        /// <summary>
        /// Associates the notification just added to <paramref name="notifications"/> by its
        /// position in the publish pass, never by the potentially duplicated client handle.
        /// Called by the monitored item under the subscription lock while it publishes.
        /// </summary>
        internal bool TrackValuePublication(
            MonitoredValuePublication publication,
            Queue<MonitoredItemNotification> notifications)
        {
            // only the queue of the publish pass ends up in a message of this subscription.
            if (!ReferenceEquals(notifications, m_publishDataChanges))
            {
                return false;
            }
            publication.NotificationOrdinal = m_publishDataChangeOrdinal + notifications.Count - 1;
            m_trackedValuePublications.TryAdd(publication, 0);
            return true;
        }

        /// <summary>
        /// Removes all references retained for a completed, failed, timed out, or cancelled waiter.
        /// </summary>
        internal void UnregisterValuePublication(MonitoredValuePublication publication)
        {
            m_valuePublications.TryRemove(publication, out _);
            m_trackedValuePublications.TryRemove(publication, out _);
        }

        /// <summary>
        /// Takes <paramref name="count"/> data changes out of the publish queue for
        /// <paramref name="message"/> and assigns the observations tracked among them.
        /// Called under the subscription lock.
        /// </summary>
        private void AdvanceDataChangeOrdinal(NotificationMessage? message, int count)
        {
            long first = m_publishDataChangeOrdinal;
            m_publishDataChangeOrdinal += count;
            if (message == null || m_trackedValuePublications.IsEmpty)
            {
                return;
            }
            foreach (MonitoredValuePublication publication in m_trackedValuePublications.Keys)
            {
                if (publication.NotificationOrdinal >= first &&
                    publication.NotificationOrdinal < m_publishDataChangeOrdinal)
                {
                    publication.Message = message;
                }
            }
        }

        private bool IsValuePublicationSessionUnavailable(ISession session)
        {
            return IsDeleted || Volatile.Read(ref m_expired) || Session == null || session.IsClosing;
        }

        /// <summary>
        /// Observes only the message selected for a constructed Publish response, not its prebatched successors.
        /// </summary>
        internal void CompleteValuePublications(NotificationMessage message)
        {
            if (m_trackedValuePublications.IsEmpty)
            {
                return;
            }
            foreach (MonitoredValuePublication publication in m_trackedValuePublications.Keys)
            {
                if (ReferenceEquals(publication.Message, message) &&
                    m_trackedValuePublications.TryRemove(publication, out _))
                {
                    publication.Complete(published: true);
                }
            }
        }

        private void EndValuePublications(StatusCode failure = default)
        {
            foreach (MonitoredValuePublication publication in m_valuePublications.Keys)
            {
                if (StatusCode.IsBad(failure))
                {
                    publication.Fail(failure);
                }
                else
                {
                    publication.Complete(published: false);
                }
            }
        }

        /// <summary>Observations waiting for either publication or subscriber teardown.</summary>
        private readonly ConcurrentDictionary<MonitoredValuePublication, byte> m_valuePublications = new();

        /// <summary>Observations whose final notification is queued for, or already in, a message.</summary>
        private readonly ConcurrentDictionary<MonitoredValuePublication, byte> m_trackedValuePublications = new();

        /// <summary>
        /// Ordinal of the next data change taken out of the publish queue. It only grows, so
        /// the ordinal of a queued notification is unique for the subscription.
        /// </summary>
        private long m_publishDataChangeOrdinal;
    }
}
