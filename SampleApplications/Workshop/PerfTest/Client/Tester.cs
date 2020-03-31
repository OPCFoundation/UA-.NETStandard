/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.PerfTestClient
{
    class Tester
    {
		// gets or sets the update rate.
        public int SamplingRate
        {
            get { return m_samplingRate; }
            set { m_samplingRate = value; }
        }
	
		// gets or sets the item count.
        public int ItemCount
        {
            get { return m_itemCount; }
            set { m_itemCount = value; }
        }

		// returns the number of callbacks that have arrived.
        public int MessageCount
        {
            get { return m_messageCount; }
        }

		// returns the total number of item updates that have arrived.
        public int TotalItemUpdateCount
        {
            get { return m_totalItemUpdateCount; }
        }

		// returns the time of the first callback.
        public DateTime FirstMessageTime
        {
            get { return m_firstMessageTime; }
        }

		// returns the time of the last callback.
        public DateTime LastMessageTime
        {
            get { return m_lastMessageTime; }
        }

        /// <summary>
        /// Gets the last sequence number.
        /// </summary>
        /// <value>The last sequence number.</value>
        public string[] GetMessages()
        {
            lock (m_lock)
            {
                string[] strings = m_logMessages.ToArray();
                m_logMessages.Clear();
                return strings;
            }
        }

        /// <summary>
        /// Gets the statistics.
        /// </summary>
        /// <param name="messageCount">The message count.</param>
        /// <param name="totalItemUpdateCount">The total item update count.</param>
        /// <param name="firstMessageTime">The first message time.</param>
        /// <param name="lastMessageTime">The last message time.</param>
        /// <param name="minItemUpdateCount">The min item update count.</param>
        /// <param name="maxItemUpdateCount">The max item update count.</param>
        public void GetStatistics(
            out int messageCount,
            out int totalItemUpdateCount,
            out DateTime firstMessageTime,
            out DateTime lastMessageTime,
            out int minItemUpdateCount,
            out int maxItemUpdateCount)
        {
            lock (m_lock)
            {
                messageCount = m_messageCount;
                totalItemUpdateCount = m_totalItemUpdateCount;
                firstMessageTime = m_firstMessageTime;
                lastMessageTime = m_lastMessageTime;
                minItemUpdateCount = Int32.MaxValue;
                maxItemUpdateCount = 0;

                if (m_itemUpdateCounts != null)
                {
                    for (int ii = 0; ii < m_itemUpdateCounts.Length; ii++)
                    {
                        if (minItemUpdateCount > m_itemUpdateCounts[ii])
                        {
                            minItemUpdateCount = m_itemUpdateCounts[ii];
                        }

                        if (maxItemUpdateCount < m_itemUpdateCounts[ii])
                        {
                            maxItemUpdateCount = m_itemUpdateCounts[ii];
                        }
                    }
                }

                m_totalItemUpdateCount = 0;
                m_firstMessageTime = m_lastMessageTime;
                m_lastMessageTime = DateTime.MinValue;
                m_itemUpdateCounts = new int[m_itemCount];
            }
        }

        /// <summary>
        /// Starts the specified session.
        /// </summary>
        /// <param name="session">The session.</param>
        public void Start(Session session)
        {
            m_NotificationEventHandler = new NotificationEventHandler(Session_Notification);
            session.Notification += m_NotificationEventHandler;

            Subscription subscription = m_subscription = new Subscription();

            subscription.PublishingInterval = m_samplingRate;
            subscription.KeepAliveCount = 10;
            subscription.LifetimeCount = 100;
            subscription.MaxNotificationsPerPublish = 50000;
            subscription.PublishingEnabled = false;
            subscription.TimestampsToReturn = TimestampsToReturn.Neither;
            subscription.Priority = 1;
            subscription.DisableMonitoredItemCache = true;

            session.AddSubscription(subscription);
            subscription.Create();

            DateTime start = HiResClock.UtcNow;

            for (int ii = 0; ii < m_itemCount; ii++)
            {
                MonitoredItem monitoredItem = new MonitoredItem((uint)ii);

                monitoredItem.StartNodeId = new NodeId((uint)((1<<24) + ii), 2);
                monitoredItem.AttributeId = Attributes.Value;
                monitoredItem.SamplingInterval = -1;
                monitoredItem.Filter = null;
                monitoredItem.QueueSize = 0;
                monitoredItem.DiscardOldest = true;
                monitoredItem.MonitoringMode = MonitoringMode.Reporting;

                subscription.AddItem(monitoredItem);
            }

            subscription.ApplyChanges();
            DateTime end = HiResClock.UtcNow;

            ReportMessage("Time to add {1} items {0}ms.", (end - start).TotalMilliseconds, m_itemCount);

            start = HiResClock.UtcNow;
            subscription.SetPublishingMode(true);
            end = HiResClock.UtcNow;

            ReportMessage("Time to emable publishing {0}ms.", (end - start).TotalMilliseconds);
        }

        /// <summary>
        /// Stops the test.
        /// </summary>
        public void Stop()
        {
            lock (m_lock)
            {
                if (m_subscription != null && m_subscription.Session != null)
                {
                    if (m_NotificationEventHandler != null)
                    {
                        m_subscription.Session.Notification -= m_NotificationEventHandler;
                    }

                    m_subscription.Delete(true);
                }
            }
        }

        void ReportMessage(string message, params object[] args)
        {
	        lock (m_lock)
	        {        		
		        if (m_logMessages == null)
		        {
                    m_logMessages = new List<string>();
		        }

		        if (args != null && args.Length > 0)
		        {
			        m_logMessages.Add(Utils.Format(message, args));
		        }
		        else
		        {
                    m_logMessages.Add(message);
		        }
	        }
        }

        void Session_Notification(Session session, NotificationEventArgs e)
        {
            lock (m_lock)
            {
		        if (m_messageCount == 0)
		        {
			        m_firstMessageTime = DateTime.UtcNow;
			        m_totalItemUpdateCount = 0;
                    m_itemUpdateCounts = new int[m_itemCount];
		        }

		        m_messageCount++;
                m_lastMessageTime = DateTime.UtcNow;

                int count = 0;

                for (int ii = 0; ii < e.NotificationMessage.NotificationData.Count; ii++)
                {
                    DataChangeNotification notification = e.NotificationMessage.NotificationData[ii].Body as DataChangeNotification;

                    if (notification == null)
                    {
                        continue;
                    }

                    for (int jj = 0; jj < notification.MonitoredItems.Count; jj++)
                    {
                        count++;
                        int clientHandle = (int)notification.MonitoredItems[jj].ClientHandle;

                        m_totalItemUpdateCount++;

                        if (clientHandle >= 0 && clientHandle < m_itemUpdateCounts.Length)
                        {
                            m_itemUpdateCounts[clientHandle]++;
                        }
                    }
                }

                // ReportMessage("OnDataChange. Time={0} ({3}), Count={1}/{2}", DateTime.UtcNow.ToString("mm:ss.fff"), count, m_totalItemUpdateCount, (m_lastMessageTime - m_firstMessageTime).TotalMilliseconds);
            }
        }

        private object m_lock = new object();
        private List<string> m_logMessages;
        private int m_samplingRate;
        private int m_itemCount;
        private int m_messageCount;
        private int m_totalItemUpdateCount;
        private DateTime m_firstMessageTime;
        private DateTime m_lastMessageTime;
        private int[] m_itemUpdateCounts;
        private Subscription m_subscription;
        private NotificationEventHandler m_NotificationEventHandler;
    }
}
