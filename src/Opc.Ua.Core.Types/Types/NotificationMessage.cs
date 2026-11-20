/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// A message returned in a Publish response.
    /// </summary>
    public partial class NotificationMessage
    {
        /// <summary>
        /// The string table that was received with the message.
        /// </summary>
        public ArrayOf<string> StringTable { get; set; }

        /// <summary>
        /// Gets a value indicating whether there are more
        /// NotificationMessages for this publish interval.
        /// </summary>
        public bool MoreNotifications { get; set; }

        /// <summary>
        /// Gets a value indicating whether this NotificationMessage is empty.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsEmpty => SequenceNumber == 0 &&
            PublishTime == DateTimeUtc.MinValue &&
            NotificationData.Count == 0;

        /// <summary>
        /// Returns the data changes contained in the notification message.
        /// </summary>
        public IList<MonitoredItemNotification> GetDataChanges(bool reverse)
        {
            var datachanges = new List<MonitoredItemNotification>();

            for (int jj = 0; jj < m_notificationData.Count; jj++)
            {
                ExtensionObject extension = m_notificationData[jj];
                if (!extension.TryGetValue(out DataChangeNotification? notification) ||
                    notification == null)
                {
                    continue;
                }

                if (reverse)
                {
                    for (int ii = notification.MonitoredItems.Count - 1; ii >= 0; ii--)
                    {
                        datachanges.Add(notification.MonitoredItems[ii]);
                    }
                }
                else
                {
                    for (int ii = 0; ii < notification.MonitoredItems.Count; ii++)
                    {
                        datachanges.Add(notification.MonitoredItems[ii]);
                    }
                }
            }

            return datachanges;
        }

        /// <summary>
        /// Returns the events contained in the notification message.
        /// </summary>
        public IList<EventFieldList> GetEvents(bool reverse)
        {
            var events = new List<EventFieldList>();

            foreach (ExtensionObject extension in m_notificationData)
            {
                if (!extension.TryGetValue(out EventNotificationList? notification) ||
                    notification == null)
                {
                    continue;
                }

                if (reverse)
                {
                    for (int ii = notification.Events.Count - 1; ii >= 0; ii--)
                    {
                        events.Add(notification.Events[ii]);
                    }
                }
                else
                {
                    for (int ii = 0; ii < notification.Events.Count; ii++)
                    {
                        events.Add(notification.Events[ii]);
                    }
                }
            }

            return events;
        }
    }
}
