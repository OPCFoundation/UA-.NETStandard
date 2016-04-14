/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
