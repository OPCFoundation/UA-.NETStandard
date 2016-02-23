/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
