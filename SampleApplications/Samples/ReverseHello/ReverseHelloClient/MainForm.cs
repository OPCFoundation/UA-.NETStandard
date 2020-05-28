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
using System.Linq;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace ReverseHelloTestClient
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = Utils.ReplaceLocalhost("opc.tcp://localhost:65200/");

            m_connections = new Dictionary<Uri, ConnectionWaitingEventArgs>();

            m_reverseConnectManager = reverseConnectManager;
            RegisterWaitingConnection();

            this.Text = m_configuration.ApplicationName;
        }

        private async void OnConnectionWaiting(object sender, ConnectionWaitingEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler<ConnectionWaitingEventArgs>(OnConnectionWaiting), sender, e);
                return;
            }

            try
            {
                // only require approval once.
                ConnectionWaitingEventArgs pending = null;

                if (!m_connections.TryGetValue(e.EndpointUrl, out pending))
                {
                    m_connections[e.EndpointUrl] = null;

                    if (MessageBox.Show(
                        $"Server requested a connection. Accept?\nUrl: {e.EndpointUrl}\nUri: {e.ServerUri}", "Reverse connection",
                        MessageBoxButtons.YesNo) != DialogResult.Yes)
                    {
                        e.Accepted = false;
                        return;
                    }
                }
                else
                {
                    // was already rejected
                    if (pending == null)
                    {
                        e.Accepted = false;
                        return;
                    }
                }

                e.Accepted = true;

                // closing the connection should cause the server to immediately re-connect.
                m_connections[e.EndpointUrl] = e;
                // on a known endpoint, ask for user Identity if anonymous is unsupported.
                var endpointDescription = ConnectServerCTRL.GetEndpointDescription(e.EndpointUrl);
                if (endpointDescription != null)
                {
                    IUserIdentity userIdentity = null;
                    foreach (var userIdentityToken in endpointDescription.UserIdentityTokens)
                    {
                        switch (userIdentityToken.TokenType)
                        {
                            case UserTokenType.Anonymous:
                                userIdentity = new UserIdentity();
                                break;
                            case UserTokenType.UserName:
                                var userNameDialog = new UserNamePasswordDlg();
                                userIdentity = userNameDialog.ShowDialog(ConnectServerCTRL.UserIdentity, "Server needs username/password");
                                break;
                            case UserTokenType.Certificate:
                                // not supported yet
                                break;
                        }
                        if (userIdentity != null)
                        {
                            break;
                        }
                    }
                    ConnectServerCTRL.UserIdentity = userIdentity;
                }
                else
                {
                    ConnectServerCTRL.UserIdentity = null;
                }

                m_session = await ConnectServerCTRL.ConnectAsync(e, ConnectServerCTRL.UseSecurity);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                if (m_session == null)
                {
                    RegisterWaitingConnection();
                }
            }
        }

        /// <summary>
        /// Register for new reverse connection.
        /// </summary>
        private void RegisterWaitingConnection()
        {
            if (m_connections.Count != 0)
            {
                var registration = m_connections.Where(r => r.Value != null).FirstOrDefault();
                if (registration.Value != null)
                {
                    m_reverseConnectManager.RegisterWaitingConnection(
                        registration.Value.EndpointUrl, registration.Value.ServerUri,
                        OnConnectionWaiting, ReverseConnectManager.ReverseConnectStrategy.Once);
                    return;
                }
            }

            m_reverseConnectManager.RegisterWaitingConnection(
                new Uri(ConnectServerCTRL.ServerUrl), null,
                OnConnectionWaiting, ReverseConnectManager.ReverseConnectStrategy.AnyOnce);
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private bool m_connectedOnce;
        private ReverseConnectManager m_reverseConnectManager;
        private Dictionary<Uri, ConnectionWaitingEventArgs> m_connections;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private async void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.DisconnectAsync();

                // force the client to prompt again if the server re-connects.
                m_connections.Clear();

                RegisterWaitingConnection();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private void Server_ConnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                // browse the instances in the server.
                BrowseCTRL.Initialize(m_session, ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, ReferenceTypeIds.Aggregates);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                BrowseCTRL.ChangeSession(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;
                BrowseCTRL.ChangeSession(m_session);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await ConnectServerCTRL.DisconnectAsync();
        }
        #endregion
    }
}
