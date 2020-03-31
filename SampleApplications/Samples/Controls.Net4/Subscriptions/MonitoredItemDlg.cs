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
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class MonitoredItemDlg : Form
    {
        #region Constructors
        public MonitoredItemDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_SubscriptionStateChanged = new SubscriptionStateChangedEventHandler(Subscription_StateChanged);
            m_MonitoredItemNotification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
            m_PublishStatusChanged = new EventHandler(Subscription_PublishStatusChanged);
        }
        #endregion

        #region Private Fields
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private SubscriptionStateChangedEventHandler m_SubscriptionStateChanged;
        private MonitoredItemNotificationEventHandler m_MonitoredItemNotification;
        private EventHandler m_PublishStatusChanged;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException("monitoredItem");
            
            Show();
            BringToFront();

            // remove previous subscription.
            if (m_monitoredItem != null)
            {
                monitoredItem.Subscription.StateChanged -= m_SubscriptionStateChanged;
                monitoredItem.Subscription.PublishStatusChanged -= m_PublishStatusChanged;
                monitoredItem.Notification -= m_MonitoredItemNotification;
            }
            
            // start receiving notifications from the new subscription.
            m_monitoredItem = monitoredItem;
            m_subscription  = null;
  
            if (m_monitoredItem != null)
            {
                m_subscription = monitoredItem.Subscription;
                m_monitoredItem.Subscription.StateChanged += m_SubscriptionStateChanged;
                m_monitoredItem.Subscription.PublishStatusChanged += m_PublishStatusChanged;
                m_monitoredItem.Notification += m_MonitoredItemNotification;
            }

            WindowMI_Click(WindowStatusMI, null);
            WindowMI_Click(WindowLatestValueMI, null);

            MonitoredItemsCTRL.Initialize(m_monitoredItem);
            EventsCTRL.Initialize(m_subscription, m_monitoredItem);
            DataChangesCTRL.Initialize(m_subscription, m_monitoredItem);
            LatestValueCTRL.ShowValue(m_monitoredItem, false);
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Updates the controls displaying the status of the subscription.
        /// </summary>
        private void UpdateStatus()
        {
            NotificationMessage lastMessage = null;

            if (m_monitoredItem != null)
            {
                lastMessage = m_monitoredItem.LastMessage;
            }

            MonitoringModeTB.Text = String.Empty;
            MonitoringModeTB.ForeColor = Color.Empty;
            MonitoringModeTB.Font = new Font(MonitoringModeTB.Font, FontStyle.Regular);

            if (m_monitoredItem != null)
            {
                MonitoringModeTB.Text = String.Format("{0}", m_monitoredItem.Status.MonitoringMode);
            }

            if (m_subscription != null && m_subscription.PublishingStopped)
            {
                MonitoringModeTB.Text = String.Format("BadNoCommunication");
                MonitoringModeTB.ForeColor = Color.Red;
                MonitoringModeTB.Font = new Font(MonitoringModeTB.Font, FontStyle.Bold);
            }
            
            LastUpdateTimeTB.Text = String.Empty;
            LastMessageIdTB.Text  = String.Empty;

            if (lastMessage != null)
            {
                LastUpdateTimeTB.Text = String.Format("{0:HH:mm:ss}", lastMessage.PublishTime.ToLocalTime());
                LastMessageIdTB.Text  = String.Format("{0}", lastMessage.SequenceNumber);
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Processes a Publish repsonse from the server.
        /// </summary>
        void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(m_MonitoredItemNotification, monitoredItem, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                // ignore notifications for other monitored items.
                if (!Object.ReferenceEquals(m_monitoredItem, monitoredItem))
                {
                    return;
                }
                                
                // notify controls of the change.
                EventsCTRL.NotificationReceived(e);
                DataChangesCTRL.NotificationReceived(e);
                if (e != null)
                {
                    MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
                    LatestValueCTRL.ShowValue(notification, true);
                }
                // update item status.
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
                if (m_monitoredItem == null || !Object.ReferenceEquals(m_monitoredItem.Subscription, subscription))
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

                // update item status.
                UpdateStatus();
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
                if (sender == WindowStatusMI)
                {
                    WindowStatusMI.Checked      = !WindowStatusMI.Checked;
                    WindowLatestValueMI.Checked = false;
                    MonitoredItemsCTRL.Visible  = true; 
                    SplitterPN.Panel1Collapsed  = !WindowStatusMI.Checked;
                }

                else if (sender == WindowHistoryMI)
                {
                    WindowHistoryMI.Checked        = true;
                    WindowLatestValueMI.Checked    = false;
                    MonitoredItemsCTRL.Visible     = true;
                    EventsCTRL.Visible             = m_monitoredItem.NodeClass != NodeClass.Variable;
                    DataChangesCTRL.Visible        = !EventsCTRL.Visible;
                    LatestValueCTRL.Visible        = false;

                    Text = String.Format("{0} - {1} - {2}", m_subscription.DisplayName, m_monitoredItem.DisplayName, "Recent Values");
                }
                
                else if (sender == WindowLatestValueMI)
                {
                    WindowHistoryMI.Checked        = false;
                    WindowLatestValueMI.Checked    = true;
                    MonitoredItemsCTRL.Visible     = true;
                    EventsCTRL.Visible             = false;
                    DataChangesCTRL.Visible        = false; 
                    LatestValueCTRL.Visible        = true;

                    Text = String.Format("{0} - {1} - {2}", m_subscription.DisplayName, m_monitoredItem.DisplayName, "Latest Value");
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MonitoringModeMI_Click(object sender, EventArgs e)
        {
            try
            {
                MonitoringMode monitoringMode = m_monitoredItem.MonitoringMode;

                if (sender == MonitoringModeReportingMI)
                {
                    monitoringMode = MonitoringMode.Reporting;
                }
                
                else if (sender == MonitoringModeSamplingMI)
                {
                    monitoringMode = MonitoringMode.Sampling;
                }

                else if (sender == MonitoringModeDisabledMI)
                {
                    monitoringMode = MonitoringMode.Disabled;
                }
                
                m_monitoredItem.Subscription.SetMonitoringMode(monitoringMode, new MonitoredItem[] { m_monitoredItem });
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MonitoringModeMI_DropDownOpening(object sender, EventArgs e)
        {            
            try
            {
                MonitoringModeReportingMI.Checked = false;
                MonitoringModeSamplingMI.Checked  = false;
                MonitoringModeDisabledMI.Checked  = false;

                switch (m_monitoredItem.MonitoringMode)
                {
                    case MonitoringMode.Reporting: { MonitoringModeReportingMI.Checked = true; break; }
                    case MonitoringMode.Sampling:  { MonitoringModeSamplingMI.Checked  = true; break; }
                    case MonitoringMode.Disabled:  { MonitoringModeDisabledMI.Checked  = true; break; }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
