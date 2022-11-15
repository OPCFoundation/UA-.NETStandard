/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A message return in a Publish response.
    /// </summary>
    public partial class NotificationMessage
    {
        #region Public Interface
        /// <summary>
        /// The string table that was received with the message.
        /// </summary>
        public List<string> StringTable
        {
            get { return m_stringTable;  }
            set { m_stringTable = value; }
        }

        /// <summary>
        /// Gets a value indicating whether this NotificationMessage is empty.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsEmpty
        {
            get
            {
                if (SequenceNumber == 0 &&
                    PublishTime == DateTime.MinValue &&
                    NotificationData.Count == 0)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns the data changes contained in the notification message.
        /// </summary>
        public IList<MonitoredItemNotification> GetDataChanges(bool reverse)
        {
            List<MonitoredItemNotification> datachanges = new List<MonitoredItemNotification>();

            for (int jj = 0; jj < m_notificationData.Count; jj++)
            {
                ExtensionObject extension = m_notificationData[jj];

                if (ExtensionObject.IsNull(extension))
                {
                    continue;
                }

                DataChangeNotification notification = extension.Body as DataChangeNotification;
                                
                if (notification == null)
                {
                    continue;
                }
    
                if (reverse)
                {
                    for (int ii = notification.MonitoredItems.Count-1; ii >= 0; ii--)
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
            List<EventFieldList> events = new List<EventFieldList>();

            foreach (ExtensionObject extension in m_notificationData)
            {
                if (ExtensionObject.IsNull(extension))
                {
                    continue;
                }

                EventNotificationList notification = extension.Body as EventNotificationList;
                                
                if (notification == null)
                {
                    continue;
                }            
    
                if (reverse)
                {
                    for (int ii = notification.Events.Count-1; ii >= 0; ii--)
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
        #endregion

        #region Private Fields
        private List<string> m_stringTable;
        #endregion
    }
}
