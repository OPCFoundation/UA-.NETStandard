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
using System.Collections.Generic;
using Opc.Ua.Server;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;

namespace Opc.Ua.SampleServer
{
    /// <summary>
    /// Displays diagnostics information for a server running within the process.
    /// </summary>
    public partial class ServerDiagnosticsCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerDiagnosticsCtrl"/> class.
        /// </summary>
        public ServerDiagnosticsCtrl()
        {
            InitializeComponent();
        }
        #endregion

#region Private Fields
        private StandardServer m_server;
        private ApplicationConfiguration m_configuration;
        private DispatcherTimer m_dispatcherTimer;
        #endregion

        #region Public Interface
        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        /// <param name="server">The server displayed in the form.</param>
        /// <param name="configuration">The configuration used to initialize the server.</param>
        public void Initialize(StandardServer server, ApplicationConfiguration configuration)
        {
            m_server = server;
            m_configuration = configuration;

            m_dispatcherTimer = new DispatcherTimer();
            m_dispatcherTimer.Tick += UpdateTimerCTRL_Tick;
            m_dispatcherTimer.Interval = new TimeSpan(0, 0, 5); // tick every 5 seconds
            m_dispatcherTimer.Start();

            // add the urls to the drop down.
            UrlCB.Items.Clear();

            foreach (EndpointDescription endpoint in m_server.GetEndpoints())
            {
                if (!UrlCB.Items.Contains(endpoint.EndpointUrl))
                {
                    UrlCB.Items.Add(endpoint.EndpointUrl);
                }
            }

            if (UrlCB.Items.Count > 0)
            {
                UrlCB.SelectedIndex = 0;
            }
        }
#endregion

#region Private Methods
        /// <summary>
        /// Updates the sessions displayed in the form.
        /// </summary>
        private void UpdateSessions()
        {

            IList<Session> sessions = m_server.CurrentInstance.SessionManager.GetSessions();

            if (sessions.Count != SessionsLV.Items.Count)
            {
                SessionsLV.Items.Clear();
            }

            for (int ii = 0; ii < sessions.Count; ii++)
            {
                Session session = sessions[ii];
                lock (session.DiagnosticsLock)
                {
                    string itemContent = Utils.Format("{0}:{1}:{2}:{3:HH:mm:ss}",
                        session.SessionDiagnostics.SessionName,
                        (session.Identity != null) ? session.Identity.DisplayName : String.Empty,
                        session.Id,
                        session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());

                    ListViewItem item;
                    if (SessionsLV.Items[ii] == null)
                    {
                        item = new ListViewItem();
                        item.Content = itemContent;
                        SessionsLV.Items.Add(item);
                    }
                    else
                    {
                        item = SessionsLV.Items[ii] as ListViewItem;
                        item.Content = itemContent;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the subscriptions displayed in the form.
        /// </summary>
        private void UpdateSubscriptions()
        {
            SubscriptionsLV.Items.Clear();

            IList<Subscription> subscriptions = m_server.CurrentInstance.SubscriptionManager.GetSubscriptions();

            for (int ii = 0; ii < subscriptions.Count; ii++)
            {
                Subscription subscription = subscriptions[ii];
                string itemContent;

                lock (subscription.DiagnosticsLock)
                {
                    itemContent = Utils.Format("{0}:{1}:{2}:{3}",
                         subscription.Id.ToString(),
                        (int)subscription.PublishingInterval,
                        subscription.MonitoredItemCount,
                        subscription.Diagnostics.NextSequenceNumber);
                }

                ListViewItem item;
                if (SubscriptionsLV.Items[ii] == null)
                {
                    item = new ListViewItem();
                    item.Content = itemContent;
                    SubscriptionsLV.Items.Add(item);
                }
                else
                {
                    item = SubscriptionsLV.Items[ii] as ListViewItem;
                    item.Content = itemContent;
                }
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// A callback used to periodically refresh the form contents.
        /// </summary>
        private void UpdateTimerCTRL_Tick(object sender, object e)
        {
            try
            {
                ServerStateLB.Text = m_server.CurrentInstance.CurrentState.ToString();
                ServerTimeLB.Text = String.Format("{0:HH:mm:ss}", DateTime.Now);
                UpdateSessions();
                UpdateSubscriptions();
            }
            catch (Exception)
            {
            }
        }
#endregion

    }
}
