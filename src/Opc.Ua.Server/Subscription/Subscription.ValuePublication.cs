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
using System.Runtime.CompilerServices;
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
        /// Associates the precise pooled notification instance, never the potentially duplicated client handle.
        /// </summary>
        internal void TrackValuePublication(MonitoredValuePublication publication)
        {
            m_valuePublicationNotifications.TryAdd(publication.Notification!, publication);
        }

        /// <summary>
        /// Removes all references retained for a completed, failed, timed out, or cancelled waiter.
        /// </summary>
        internal void UnregisterValuePublication(MonitoredValuePublication publication)
        {
            m_valuePublications.TryRemove(publication, out _);
            if (publication.Notification != null)
            {
                ((ICollection<KeyValuePair<MonitoredItemNotification, MonitoredValuePublication>>)
                    m_valuePublicationNotifications).Remove(
                        new KeyValuePair<MonitoredItemNotification, MonitoredValuePublication>(
                            publication.Notification, publication));
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
            if (m_valuePublicationNotifications.IsEmpty)
            {
                return;
            }
            foreach (ExtensionObject extension in message.NotificationData)
            {
                if (!extension.TryGetValue(out DataChangeNotification? notification))
                {
                    continue;
                }
                foreach (MonitoredItemNotification item in notification.MonitoredItems)
                {
                    if (m_valuePublicationNotifications.TryRemove(item, out MonitoredValuePublication? publication))
                    {
                        publication.Complete(published: true);
                    }
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

        /// <summary>Exact notification identities retained only while their bounded observations are pending.</summary>
        private readonly ConcurrentDictionary<MonitoredItemNotification, MonitoredValuePublication>
            m_valuePublicationNotifications = new(new NotificationIdentityComparer());

        private sealed class NotificationIdentityComparer : IEqualityComparer<MonitoredItemNotification>
        {
            public bool Equals(MonitoredItemNotification? x, MonitoredItemNotification? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(MonitoredItemNotification obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
