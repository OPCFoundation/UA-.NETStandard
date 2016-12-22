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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Configuration;

namespace Opc.Ua.Com.Client
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
            this.Icon = ConfigUtils.GetAppIcon();
        }
        
        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerForm(ApplicationInstance application)
        {
            InitializeComponent();
            this.Icon = ConfigUtils.GetAppIcon();

            m_application = application;

            if (application.Server is StandardServer)
            {
                this.ServerDiagnosticsCTRL.Initialize((StandardServer)application.Server, application.ApplicationConfiguration);
            }

            TrayIcon.Text = this.Text = application.ApplicationName;
            TrayIcon.Icon = GetAppIcon();
        }
        #endregion

        #region Private Fields
        private ApplicationInstance m_application;
        #endregion

        #region Private Methods
        private static class NativeMethods 
        { 
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] 
            static extern internal IntPtr LoadIcon(IntPtr hInstance, string lpIconName); 

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] 
            static extern internal IntPtr LoadLibrary(string lpFileName); 
        }

        /// <summary>
        /// Gets the application icon.
        /// </summary>
        static Icon GetAppIcon() 
        { 
            string fileName = Assembly.GetEntryAssembly().Location;
            IntPtr hLibrary = NativeMethods.LoadLibrary(fileName);

            if (hLibrary != IntPtr.Zero) 
            { 
                IntPtr hIcon = NativeMethods.LoadIcon(hLibrary, "#32512");

                if (hIcon != IntPtr.Zero) 
                { 
                    return Icon.FromHandle(hIcon); 
                }
            }

            return null;
        } 
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
                m_application.Stop();
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
                StandardServer server = m_application.Server as StandardServer;

                if (server != null)
                {
                    TrayIcon.Text = String.Format(
                        "{0} [{1} {2:HH:mm:ss}]", 
                        m_application.ApplicationName,
                        server.CurrentInstance.CurrentState,
                        DateTime.Now);
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error getting server status.");
            }
        }
        #endregion
    }
}
