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
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;
using Opc.Ua;
using Opc.Ua.Configuration;
using System.IO;

namespace Opc.Ua.Server.Controls
{
    /// <summary>
    /// The primary form displayed by the application.
    /// </summary>
    public partial class ServerForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ServerForm()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerForm(StandardServer server, ApplicationConfiguration configuration)
        {
            InitializeComponent();

            m_server = server;
            m_configuration = configuration;
            this.ServerDiagnosticsCTRL.Initialize(m_server, m_configuration);

            TrayIcon.Text = this.Text = m_configuration.ApplicationName;
            this.Icon = TrayIcon.Icon = ServerUtils.GetAppIcon();
        }


        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerForm(ApplicationInstance application)
        {
            InitializeComponent();

            m_application = application;
            m_server = application.Server as StandardServer;
            m_configuration = application.ApplicationConfiguration;
            this.ServerDiagnosticsCTRL.Initialize(m_server, m_configuration);

            TrayIcon.Text = this.Text = m_configuration.ApplicationName;
            this.Icon = TrayIcon.Icon = ServerUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private ApplicationInstance m_application;
        private StandardServer m_server;
        private ApplicationConfiguration m_configuration;
        #endregion

        #region Event Handlers
        private void ServerForm_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == WindowState)
            {
                Hide();
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void Server_ExitMI_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ServerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                m_server.Stop();
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error stopping server.");
            }
        }

        private void TrayIcon_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                TrayIcon.Text = String.Format(
                    "{0} [{1} {2:HH:mm:ss}]",
                    m_configuration.ApplicationName,
                    m_server.CurrentInstance.CurrentState,
                    DateTime.Now);
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error getting server status.");
            }
        }
        #endregion

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Quit the application", "OPC UA", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        private void ContentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start( Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + "WebHelp" + Path.DirectorySeparatorChar + "index.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }

        }
    }
}
