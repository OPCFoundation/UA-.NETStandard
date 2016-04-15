/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace Opc.Ua.Sample.Controls
{
    public class SubscriptionMock
    {
        internal void Initialize(Subscription subscription)
        {
            throw new NotImplementedException();
        }

        internal void Initialize(Subscription subscription, object p)
        {
            throw new NotImplementedException();
        }

        internal void NotificationReceived(NotificationEventArgs e)
        {
            throw new NotImplementedException();
        }

        internal void SubscriptionChanged(SubscriptionStateChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        internal void PublishStatusChanged()
        {
            throw new NotImplementedException();
        }
    }
    public partial class SubscriptionDlg : Page
    {
        TextBox WindowMonitoredItemsMI = new TextBox();
        TextBox WindowEventsMI = new TextBox();
        TextBox WindowDataChangesMI = new TextBox();
        SubscriptionMock MonitoredItemsCTRL = new SubscriptionMock();
        SubscriptionMock EventsCTRL = new SubscriptionMock();
        SubscriptionMock DataChangesCTRL = new SubscriptionMock();
        #region Constructors
        public SubscriptionDlg()
        {
            InitializeComponent();

            m_SessionNotification = new NotificationEventHandler(Session_Notification);
            m_SubscriptionStateChanged = new SubscriptionStateChangedEventHandler(Subscription_StateChanged);
            m_PublishStatusChanged = new EventHandler(Subscription_PublishStatusChanged);
        }
        #endregion

        #region Private Fields
        private Subscription m_subscription;
        private NotificationEventHandler m_SessionNotification;
        private SubscriptionStateChangedEventHandler m_SubscriptionStateChanged;
        private EventHandler m_PublishStatusChanged;
        #endregion

        #region Public Interface
        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public async Task<Subscription> New(Session session)
        {
            if (session == null) throw new ArgumentNullException("session");

            Subscription subscription = new Subscription(session.DefaultSubscription);
            SubscriptionEditDlg subscriptionEditDlg = new SubscriptionEditDlg();
            if (!await subscriptionEditDlg.ShowDialog(subscription))
            {
                return null;
            }

            session.AddSubscription(subscription);
            subscription.Create();

            Show(subscription);

            return subscription;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");
            
            // remove previous subscription.
            if (m_subscription != null)
            {
                m_subscription.StateChanged -= m_SubscriptionStateChanged;
                m_subscription.PublishStatusChanged -= m_PublishStatusChanged;
                m_subscription.Session.Notification -= m_SessionNotification;
            }
            
            // start receiving notifications from the new subscription.
            m_subscription = subscription;
  
            if (subscription != null)
            {
                m_subscription.StateChanged += m_SubscriptionStateChanged;
                m_subscription.PublishStatusChanged += m_PublishStatusChanged;
                m_subscription.Session.Notification += m_SessionNotification;
            }                    

            MonitoredItemsCTRL.Initialize(subscription);
            EventsCTRL.Initialize(subscription, null);
            DataChangesCTRL.Initialize(subscription, null);

            WindowMI_Click(WindowMonitoredItemsMI, null);

            UpdateStatus();
        }

        private void WindowMI_Click(object windowMI, object p)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the controls displaying the status of the subscription.
        /// </summary>
        private void UpdateStatus()
        {
            NotificationMessage message = null;

            if (m_subscription != null)
            {
                message = m_subscription.LastNotification;
            }

            PublishingEnabledTB.Text = String.Empty;

            if (m_subscription != null)
            {
                PublishingEnabledTB.Text = (m_subscription.CurrentPublishingEnabled)?"Enabled":"Disabled";
            }

            LastUpdateTimeTB.Text = String.Empty;

            if (message != null)
            {
                LastUpdateTimeTB.Text = String.Format("{0:HH:mm:ss}", message.PublishTime.ToLocalTime());
            }

            LastMessageIdTB.Text = String.Empty;

            if (message != null)
            {
                LastMessageIdTB.Text = String.Format("{0}", message.SequenceNumber);
            }

            // determine what window to show.
            bool hasEvents = false;
            bool hasDatachanges = false;

            foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
            {
                if (monitoredItem.Filter is EventFilter)
                {
                    hasEvents = true;
                }
                
                if (monitoredItem.NodeClass == NodeClass.Variable)
                {
                    hasDatachanges = true;
                }
            }

            // enable appropriate windows.
            WindowEventsMI.IsEnabled = hasEvents;
            WindowDataChangesMI.IsEnabled = hasDatachanges;

            // show the datachange window if there are no event items.
            if (hasDatachanges && !hasEvents)
            {
                WindowMI_Click(WindowDataChangesMI, null);
            }

            // show events window if there are no datachange items.
            if (hasEvents && !hasDatachanges)
            {
                WindowMI_Click(WindowEventsMI, null);
            }

        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Processes a Publish repsonse from the server.
        /// </summary>
        void Session_Notification(Session session, NotificationEventArgs e)
        {
            try
            {
                // ignore notifications for other subscriptions.
                if (!Object.ReferenceEquals(m_subscription,  e.Subscription))
                {
                    return;
                }
                                
                // notify controls of the change.
                EventsCTRL.NotificationReceived(e);
                DataChangesCTRL.NotificationReceived(e);

                // update subscription status.
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        /// <summary>
        /// Handles a change to the state of the subscription.
        /// </summary>
        void Subscription_StateChanged(Subscription subscription, SubscriptionStateChangedEventArgs e)
        {
            try
            {
                // ignore notifications for other subscriptions.
                if (!Object.ReferenceEquals(m_subscription,  subscription))
                {
                    return;
                }

                // notify controls of the change.
                EventsCTRL.SubscriptionChanged(e);
                DataChangesCTRL.SubscriptionChanged(e);
                MonitoredItemsCTRL.SubscriptionChanged(e);

                // update subscription status.
                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        
        /// <summary>
        /// Handles a change to the publish status for the subscription.
        /// </summary>
        void Subscription_PublishStatusChanged(object subscription, EventArgs e)
        {
            try
            {
                // ignore notifications for other subscriptions.
                if (!Object.ReferenceEquals(m_subscription,  subscription))
                {
                    return;
                }

                // notify controls of the change.
                DataChangesCTRL.PublishStatusChanged();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
  
#endregion
    }
}
