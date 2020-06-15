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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A tool bar used to connect to a server.
    /// </summary>
    public partial class ConnectServerCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public ConnectServerCtrl()
        {
            InitializeComponent();
            m_CertificateValidation = new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private int m_reconnectPeriod = 10;
        private SessionReconnectHandler m_reconnectHandler;
        private CertificateValidationEventHandler m_CertificateValidation;
        private EventHandler m_ReconnectComplete;
        private EventHandler m_ReconnectStarting;
        private EventHandler m_KeepAliveComplete;
        private EventHandler m_ConnectComplete;
        private StatusStrip m_StatusStrip;
        private ToolStripItem m_ServerStatusLB;
        private ToolStripItem m_StatusUpateTimeLB;
        #endregion

        #region Public Members
        /// <summary>
        /// A strip used to display session status information.
        /// </summary>
        public StatusStrip StatusStrip
        {
            get { return m_StatusStrip; }
            
            set 
            { 
                if (!Object.ReferenceEquals(m_StatusStrip, value))
                {
                    m_StatusStrip = value;

                    if (value != null)
                    {
                        m_ServerStatusLB = new ToolStripStatusLabel();
                        m_StatusUpateTimeLB = new ToolStripStatusLabel();
                        m_StatusStrip.Items.Add(m_ServerStatusLB);
                        m_StatusStrip.Items.Add(m_StatusUpateTimeLB);
                    }
                }
            }
        }

        /// <summary>
        /// A control that contains the last time a keep alive was returned from the server.
        /// </summary>
        public ToolStripItem ServerStatusControl { get { return m_ServerStatusLB; } set { m_ServerStatusLB = value; } }

        /// <summary>
        /// A control that contains the last time a keep alive was returned from the server.
        /// </summary>
        public ToolStripItem StatusUpateTimeControl { get { return m_StatusUpateTimeLB; } set { m_StatusUpateTimeLB = value; } }
            
        /// <summary>
        /// The name of the session to create.
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating that the domain checks should be ignored when connecting.
        /// </summary>
        public bool DisableDomainCheck { get; set; }

        /// <summary>
        /// The URL displayed in the control.
        /// </summary>
        public string ServerUrl
        {
            get 
            {
                if (UrlCB.SelectedIndex >= 0)
                {
                    return (string)UrlCB.SelectedItem;
                }

                return UrlCB.Text; 
            }

            set
            {
                UrlCB.SelectedIndex = -1;
                UrlCB.Text = value;
            }
        }

        /// <summary>
        /// Whether to use security when connecting.
        /// </summary>
        public bool UseSecurity
        {
            get { return UseSecurityCK.Checked; }
            set { UseSecurityCK.Checked = value; }
        }

        /// <summary>
        /// The locales to use when creating the session.
        /// </summary>
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// The user identity to use when creating the session.
        /// </summary>
        public IUserIdentity UserIdentity { get; set; }

        /// <summary>
        /// The client application configuration.
        /// </summary>
        public ApplicationConfiguration Configuration
        {
            get { return m_configuration; }
            
            set 
            {
                if (!Object.ReferenceEquals(m_configuration, value))
                {
                    if (m_configuration != null)
                    {
                        m_configuration.CertificateValidator.CertificateValidation -= m_CertificateValidation;
                    }

                    m_configuration = value;

                    if (m_configuration != null)
                    {
                        m_configuration.CertificateValidator.CertificateValidation += m_CertificateValidation;
                    }
                }
            }
        }
        
        /// <summary>
        /// The currently active session. 
        /// </summary>
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// The number of seconds between reconnect attempts (0 means reconnect is disabled).
        /// </summary>
        [DefaultValue(10)]
        public int ReconnectPeriod
        {
            get { return m_reconnectPeriod; }
            set { m_reconnectPeriod = value; }
        }

        /// <summary>
        /// Raised when a good keep alive from the server arrives.
        /// </summary>
        public event EventHandler KeepAliveComplete
        {
            add { m_KeepAliveComplete += value; }
            remove { m_KeepAliveComplete -= value; }
        }
        
        /// <summary>
        /// Raised when a reconnect operation starts.
        /// </summary>
        public event EventHandler ReconnectStarting
        {
            add { m_ReconnectStarting += value; }
            remove { m_ReconnectStarting -= value; }
        }

        /// <summary>
        /// Raised when a reconnect operation completes.
        /// </summary>
        public event EventHandler ReconnectComplete
        {
            add { m_ReconnectComplete += value; }
            remove { m_ReconnectComplete -= value; }
        }

        /// <summary>
        /// Raised after successfully connecting to or disconnecing from a server.
        /// </summary>
        public event EventHandler ConnectComplete
        {
            add { m_ConnectComplete += value; }
            remove { m_ConnectComplete -= value; }
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <returns>The new session object.</returns>
        public Session Connect()
        {
            // disconnect from existing session.
            Disconnect();

            // determine the URL that was selected.
            string serverUrl = UrlCB.Text;

            if (UrlCB.SelectedIndex >= 0)
            {
                serverUrl = (string)UrlCB.SelectedItem;
            }

            // select the best endpoint.
            EndpointDescription endpointDescription = ClientUtils.SelectEndpoint(serverUrl, UseSecurityCK.Checked);

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            m_session = Session.Create(
                m_configuration,
                endpoint,
                false,
                !DisableDomainCheck,
                (String.IsNullOrEmpty(SessionName))?m_configuration.ApplicationName:SessionName,
                60000,
                UserIdentity,
                PreferredLocales);

            // set up keep alive callback.
            m_session.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);

            // raise an event.
            DoConnectComplete(null);

            // return the new session.
            return m_session;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <param name="serverUrl">The URL of a server endpoint.</param>
        /// <param name="useSecurity">Whether to use security.</param>
        /// <returns>The new session object.</returns>
        public Session Connect(string serverUrl, bool useSecurity)
        {
            UrlCB.Text = serverUrl;
            UseSecurityCK.Checked = useSecurity;
            return Connect();
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public void Disconnect()
        {
            UpdateStatus(false, DateTime.UtcNow, "Disconnected");

            // stop any reconnect operation.
            if (m_reconnectHandler != null)
            {
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
            }

            // disconnect any existing session.
            if (m_session != null)
            {
                m_session.Close(10000);
                m_session = null;
            }

            // raise an event.
            DoConnectComplete(null);
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        public void Discover(string hostName)
        {
            string endpointUrl = new DiscoverServerDlg().ShowDialog(m_configuration, hostName);

            if (endpointUrl != null)
            {
                ServerUrl = endpointUrl;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Raises the connect complete event on the main GUI thread.
        /// </summary>
        private void DoConnectComplete(object state)
        {
            if (m_ConnectComplete != null)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new System.Threading.WaitCallback(DoConnectComplete), state);
                    return;
                }

                m_ConnectComplete(this, null);
            }
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        private EndpointDescription SelectEndpoint()
        {
            try
            {
                Cursor = Cursors.WaitCursor;

                // determine the URL that was selected.
                string discoveryUrl = UrlCB.Text;

                if (UrlCB.SelectedIndex >= 0)
                {
                    discoveryUrl = (string)UrlCB.SelectedItem;
                }

                // return the selected endpoint.
                return ClientUtils.SelectEndpoint(discoveryUrl, UseSecurityCK.Checked);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the status control.
        /// </summary>
        /// <param name="error">Whether the status represents an error.</param>
        /// <param name="time">The time associated with the status.</param>
        /// <param name="status">The status message.</param>
        /// <param name="args">Arguments used to format the status message.</param>
        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            if (m_ServerStatusLB != null)
            {
                m_ServerStatusLB.Text = String.Format(status, args);
                m_ServerStatusLB.ForeColor = (error) ? Color.Red : Color.Empty;
            }

            if (m_StatusUpateTimeLB != null)
            {
                m_StatusUpateTimeLB.Text = time.ToLocalTime().ToString("hh:mm:ss");
                m_StatusUpateTimeLB.ForeColor = (error) ? Color.Red : Color.Empty;
            }
        }

        /// <summary>
        /// Handles a keep alive event from a session.
        /// </summary>
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new KeepAliveEventHandler(Session_KeepAlive), session, e);
                return;
            }

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(session, m_session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    if (m_reconnectPeriod <= 0)
                    {
                        UpdateStatus(true, e.CurrentTime, "Communication Error ({0})", e.Status);
                        return;
                    }

                    UpdateStatus(true, e.CurrentTime, "Reconnecting in {0}s", m_reconnectPeriod);

                    if (m_reconnectHandler == null)
                    {
                        if (m_ReconnectStarting != null)
                        {
                            m_ReconnectStarting(this, e);
                        }

                        m_reconnectHandler = new SessionReconnectHandler();
                        m_reconnectHandler.BeginReconnect(m_session, m_reconnectPeriod * 1000, Server_ReconnectComplete);
                    }

                    return;
                }

                // update status.
                UpdateStatus(false, e.CurrentTime, "Connected [{0}]", session.Endpoint.EndpointUrl);
                                
                // raise any additional notifications.
                if (m_KeepAliveComplete != null)
                {
                    m_KeepAliveComplete(this, e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a click on the connect button.
        /// </summary>
        private void Server_ConnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                Connect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a reconnect event complete from the reconnect handler.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler(Server_ReconnectComplete), sender, e);
                return;
            }

            try
            {
                // ignore callbacks from discarded objects.
                if (!Object.ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }

                m_session = m_reconnectHandler.Session;
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;

                // raise any additional notifications.
                if (m_ReconnectComplete != null)
                {
                    m_ReconnectComplete(this, e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new CertificateValidationEventHandler(CertificateValidator_CertificateValidation), sender, e);
                return;
            }

            try
            {
                e.Accept = m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates;

                if (!m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    DialogResult result = MessageBox.Show(
                        e.Certificate.Subject,
                        "Untrusted Certificate",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    e.Accept = (result == DialogResult.Yes);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles a drop down event for the URL control.
        /// </summary>
        private void UrlCB_DropDown(object sender, EventArgs e)
        {
            UrlCB.Items.Clear();

            // create a table of application descriptions that match the drop down entries.
            List<ApplicationDescription> lookupTable = new List<ApplicationDescription>();
            UrlCB.Tag = lookupTable;

            try
            {
                Cursor = Cursors.WaitCursor;

                try
                {
                    IList<string> serverUrls = ClientUtils.DiscoverServers(m_configuration);

                    // populate the drop down list with the discovery URLs for the available servers.
                    for (int ii = 0; ii < serverUrls.Count; ii++)
                    {
                        // remove duplicates.
                        int index = UrlCB.FindStringExact(serverUrls[ii]);

                        if (index < 0)
                        {
                            UrlCB.Items.Add(serverUrls[ii]);
                        }
                    }
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            catch (Exception)
            {
                // ignore errors.
            }
        }
        #endregion
    }
}
