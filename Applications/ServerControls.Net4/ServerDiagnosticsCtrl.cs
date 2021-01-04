/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Server;

namespace Opc.Ua.Server.Controls
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
            UpdateTimerCTRL.Enabled = true;
            
            // add the urls to the drop down.
            UrlCB.Items.Clear();

            foreach (EndpointDescription endpoint in m_server.GetEndpoints())
            {
                if (UrlCB.FindStringExact(endpoint.EndpointUrl) == -1)
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
            SessionsLV.Items.Clear();

            IList<Session> sessions = m_server.CurrentInstance.SessionManager.GetSessions();

            for (int ii = 0; ii < sessions.Count; ii++)
            {
                Session session = sessions[ii];

                lock (session.DiagnosticsLock)
                {
                    ListViewItem item = new ListViewItem(session.SessionDiagnostics.SessionName);

                    if (session.Identity != null)
                    {
                        item.SubItems.Add(session.Identity.DisplayName);
                    }
                    else
                    {
                        item.SubItems.Add(String.Empty);
                    }

                    item.SubItems.Add(String.Format("{0}", session.Id));
                    item.SubItems.Add(String.Format("{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime()));

                    SessionsLV.Items.Add(item);
                }
            }

            // adjust 
            for (int ii = 0; ii < SessionsLV.Columns.Count; ii++)
            {
                SessionsLV.Columns[ii].Width = -2;
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

                ListViewItem item = new ListViewItem(subscription.Id.ToString());

                item.SubItems.Add(String.Format("{0}", (int)subscription.PublishingInterval));
                item.SubItems.Add(String.Format("{0}", subscription.MonitoredItemCount));

                lock (subscription.DiagnosticsLock)
                {
                    item.SubItems.Add(String.Format("{0}", subscription.Diagnostics.NextSequenceNumber));
                }

                SubscriptionsLV.Items.Add(item);
            }

            for (int ii = 0; ii < SubscriptionsLV.Columns.Count; ii++)
            {
                SubscriptionsLV.Columns[ii].Width = -2;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// A callback used to periodically refresh the form contents.
        /// </summary>
        private void UpdateTimerCTRL_Tick(object sender, EventArgs e)
        {
            try
            {
                ServerStateLB.Text = m_server.CurrentInstance.CurrentState.ToString();
                ServerTimeLB.Text = String.Format("{0:HH:mm:ss}", DateTime.Now);
                UpdateSessions();
                sessionsLB.Text = Convert.ToString( SessionsLV.Items.Count );
                UpdateSubscriptions();
                subscriptionsLB.Text = Convert.ToString( SubscriptionsLV.Items.Count );
                int itemTotal = 0;
                for ( int i = 0; i < SubscriptionsLV.Items.Count; i++ )
                {
                    itemTotal += Convert.ToInt32( SubscriptionsLV.Items[i].SubItems[2].Text );
                }
                itemsLB.Text = Convert.ToString( itemTotal );
            }
            catch (Exception exception)
            {
                ServerUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
