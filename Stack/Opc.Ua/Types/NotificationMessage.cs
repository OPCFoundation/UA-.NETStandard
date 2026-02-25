/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public StringCollection StringTable { get; set; }

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
            PublishTime == DateTime.MinValue &&
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
                if (!extension.TryGetEncodeable(out DataChangeNotification notification))
                {
                    continue;
                }

                if (reverse)
                {
                    for (int ii = notification.MonitoredItems.Count - 1; ii >= 0; ii--)
                    {
                        MonitoredItemNotification datachange = notification.MonitoredItems[ii];

                        if (datachange != null)
                        {
                            datachange.Message = this;
                            datachanges.Add(datachange);
                        }
                    }
                }
                else
                {
                    for (int ii = 0; ii < notification.MonitoredItems.Count; ii++)
                    {
                        MonitoredItemNotification datachange = notification.MonitoredItems[ii];

                        if (datachange != null)
                        {
                            datachange.Message = this;
                            datachanges.Add(datachange);
                        }
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
                if (!extension.TryGetEncodeable(out EventNotificationList notification))
                {
                    continue;
                }

                if (reverse)
                {
                    for (int ii = notification.Events.Count - 1; ii >= 0; ii--)
                    {
                        EventFieldList eventFields = notification.Events[ii];

                        if (eventFields != null)
                        {
                            eventFields.Message = this;
                            events.Add(eventFields);
                        }
                    }
                }
                else
                {
                    for (int ii = 0; ii < notification.Events.Count; ii++)
                    {
                        EventFieldList eventFields = notification.Events[ii];

                        if (eventFields != null)
                        {
                            eventFields.Message = this;
                            events.Add(eventFields);
                        }
                    }
                }
            }

            return events;
        }
    }
}
