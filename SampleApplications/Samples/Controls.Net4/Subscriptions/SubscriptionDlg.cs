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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class SubscriptionDlg : Form
    {
        #region Constructors
        public SubscriptionDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_SessionNotification = new NotificationEventHandler(Session_Notification);
            m_SubscriptionStateChanged = new SubscriptionStateChangedEventHandler(Subscription_StateChanged);
            m_PublishStatusChanged = new EventHandler(Subscription_PublishStatusChanged);
        }
        #endregion

        #region Private Fields
        private Subscription m_subscription;
        private NotificationEventHandler m_SessionNotification;
        private SubscriptionStateChangedEventHandler m_SubscriptionStateChanged;
        private CreateMonitoredItemsDlg m_createDialog;
        private EventHandler m_PublishStatusChanged;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public Subscription New(Session session)
        {            
            if (session == null) throw new ArgumentNullException("session");

            Subscription subscription = new Subscription(session.DefaultSubscription);

            if (!new SubscriptionEditDlg().ShowDialog(subscription))
            {
                return null;
            }
            
            session.AddSubscription(subscription);    
            subscription.Create();

            Subscription duplicateSubscription = session.Subscriptions.FirstOrDefault(s => s.Id != 0 && s.Id.Equals(subscription.Id) && s != subscription);
            if (duplicateSubscription != null)
            {
                Utils.Trace("Duplicate subscription was created with the id: {0}", duplicateSubscription.Id);

                DialogResult result = MessageBox.Show("Duplicate subscription was created with the id: " + duplicateSubscription.Id + ". Do you want to keep it?", "Warning", MessageBoxButtons.YesNo);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    duplicateSubscription.Delete(false);
                    session.RemoveSubscription(subscription);

                    return null;
                }
            }

            Show(subscription);
            
            return subscription;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");
            
            Show();
            BringToFront();

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

            WindowMI_Click(WindowDataChangesMI, null);

            UpdateStatus();
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
            WindowEventsMI.Enabled = hasEvents;
            WindowDataChangesMI.Enabled = hasDatachanges;

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
            if (InvokeRequired)
            {
                BeginInvoke(m_SessionNotification, session, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

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
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Handles a change to the state of the subscription.
        /// </summary>
        void Subscription_StateChanged(Subscription subscription, SubscriptionStateChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_SubscriptionStateChanged, subscription, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

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
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        
        /// <summary>
        /// Handles a change to the publish status for the subscription.
        /// </summary>
        void Subscription_PublishStatusChanged(object subscription, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_PublishStatusChanged, subscription, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

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
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionMI_DropDownOpening(object sender, EventArgs e)
        {            
            try
            {
                SubscriptionEnablePublishingMI.Checked = m_subscription.CurrentPublishingEnabled;
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void WindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (sender == WindowMonitoredItemsMI)
                {
                    WindowMonitoredItemsMI.Checked = !WindowMonitoredItemsMI.Checked;
                    WindowEventsMI.Checked         = false;
                    MonitoredItemsCTRL.Visible     = true; 
                    SplitterPN.Panel1Collapsed         = !WindowMonitoredItemsMI.Checked;
                }

                else if (sender == WindowDataChangesMI)
                {
                    WindowDataChangesMI.Checked    = true;
                    WindowEventsMI.Checked         = false;
                    MonitoredItemsCTRL.Visible     = true;
                    EventsCTRL.Visible             = false;
                    DataChangesCTRL.Visible        = true; 

                    Text = String.Format("{0} - {1}", m_subscription.DisplayName, "Data Changes");
                }
                
                else if (sender == WindowEventsMI)
                {
                    WindowDataChangesMI.Checked    = false;
                    WindowEventsMI.Checked         = true;
                    MonitoredItemsCTRL.Visible     = true;
                    EventsCTRL.Visible             = true;
                    DataChangesCTRL.Visible        = false; 

                    Text = String.Format("{0} - {1}", m_subscription.DisplayName, "Events");
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionEnablePublishingMI_Click(object sender, EventArgs e)
        {
            try
            {
                m_subscription.SetPublishingMode(SubscriptionEnablePublishingMI.Checked);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (m_createDialog != null)
                {
                    m_createDialog.Close();
                }

                MonitoredItemsCTRL.FormClosing();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (!new SubscriptionEditDlg().ShowDialog(m_subscription))
                {
                    return;
                }
                
                m_subscription.Modify();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionCreateItemMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_createDialog == null)
                {
                    m_createDialog = new CreateMonitoredItemsDlg();
                    m_createDialog.FormClosing += new FormClosingEventHandler(CreateDialog_FormClosing);
                }

                m_createDialog.Show(m_subscription, false);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void CreateDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (m_createDialog == sender)
                {
                    m_createDialog = null;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscriptionCreateItemFromTypeMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_createDialog == null)
                {
                    m_createDialog = new CreateMonitoredItemsDlg();
                    m_createDialog.FormClosing += new FormClosingEventHandler(CreateDialog_FormClosing);
                }

                m_createDialog.Show(m_subscription, true);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ConditionRefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                m_subscription.ConditionRefresh();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
