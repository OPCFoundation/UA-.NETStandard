/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ServiceModel;
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
