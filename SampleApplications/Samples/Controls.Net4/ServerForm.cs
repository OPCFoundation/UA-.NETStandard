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

using Opc.Ua.Server;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class ServerForm : Form
    {
        #region Constructors
        public ServerForm(StandardServer server, ApplicationConfiguration configuration)
        {
            InitializeComponent();
            this.Icon = this.TrayIcon.Icon = ClientUtils.GetAppIcon();
                        
            GuiUtils.DisplayUaTcpImplementation(this, configuration);

            m_server = server;

            if (!configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                m_server.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
        }
        #endregion
        
        #region Private Fields
        private bool m_exit;
        private StandardServer m_server;
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Shows the diagnostics window and starts the update timer.
        /// </summary>
        private void ShowStatus()
        {            
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            Timer.Enabled = true;
        }
        
        /// <summary>
        /// Hides the diagnostics window and starts the update timer.
        /// </summary>
        private void HideStatus()
        {            
            this.WindowState = FormWindowState.Minimized;
            Timer.Enabled = false;
        }
        
		/// <summary>
		/// Displays an unhandled exception.
		/// </summary>
		public static void HandleException(string caption, MethodBase method, Exception e)
		{
            if (String.IsNullOrEmpty(caption))
            {
                caption = method.Name;
            }

			MessageBox.Show(e.Message, caption);
		}
        #endregion
        
        #region Event Handlers
        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            try
            {
                Opc.Ua.Client.Controls.GuiUtils.HandleCertificateValidationError(this, validator, e);
            }
            catch (Exception exception)
            {
				Opc.Ua.Client.Controls.GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ExitMI_Click(object sender, EventArgs e)
        {
            m_exit = true;
            Close();
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                ShowStatus();          
            }
            catch (Exception exception)
            {
				HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (e.CloseReason == CloseReason.UserClosing && !m_exit)
                {
                    e.Cancel = true;
                    HideStatus();
                }       
            }
            catch (Exception exception)
            {
				HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ShowMI_Click(object sender, EventArgs e)
        {
            try
            {
                ShowStatus();          
            }
            catch (Exception exception)
            {
				HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                ServerStatusDataType status = m_server.GetStatus();

                StartTimeTB.Text   = Utils.Format("{0:HH:mm:ss.ff}", status.StartTime.ToLocalTime());
                CurrentTimeTB.Text = Utils.Format("{0:HH:mm:ss.ff}", status.CurrentTime.ToLocalTime());
                ServerStateTB.Text = Utils.Format("{0}", status.State);
            }
            catch (Exception exception)
            {
				HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
