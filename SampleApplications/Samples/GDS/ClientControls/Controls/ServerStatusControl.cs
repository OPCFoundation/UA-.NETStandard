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
using System.Drawing;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class ServerStatusControl : UserControl
    {
        public ServerStatusControl()
        {
            InitializeComponent();
        }

        private ServerPushConfigurationClient m_server;

        public void Initialize(ServerPushConfigurationClient server)
        {
            m_server = server;
            ServerBrowseControl.Initialize((server != null) ? server.Session : null, Opc.Ua.ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences);
        }

        public void SetServerStatus(ServerStatusDataType status)
        {
            ProductNameTextBox.Text = "---";
            ProductUriTextBox.Text = "---";
            ManufacturerNameTextBox.Text = "---";
            SoftwareVersionTextBox.Text = "---";
            BuildNumberTextBox.Text = "---";
            BuildDateTextBox.Text = "---";
            StartTimeTextBox.Text = "---";
            CurrentTimeTextBox.Text = "---";
            StateTextBox.Text = "---";
            SecondsUntilShutdownTextBox.Text = "---";
            ShutdownReasonTextBox.Text = "---";

            if (status != null)
            {
                if (status.BuildInfo != null)
                {
                    ProductNameTextBox.Text = status.BuildInfo.ProductName;
                    ProductUriTextBox.Text = status.BuildInfo.ProductUri;
                    ManufacturerNameTextBox.Text = status.BuildInfo.ManufacturerName;
                    SoftwareVersionTextBox.Text = status.BuildInfo.SoftwareVersion;
                    BuildNumberTextBox.Text = status.BuildInfo.BuildNumber;
                    BuildDateTextBox.Text = status.BuildInfo.BuildDate.ToLocalTime().ToString("yyyy-MM-dd");
                }

                StartTimeTextBox.Text = status.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                CurrentTimeTextBox.Text = status.CurrentTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                SecondsUntilShutdownTextBox.Text = (status.SecondsTillShutdown > 0) ? status.SecondsTillShutdown.ToString() : "";
                ShutdownReasonTextBox.Text = (status.SecondsTillShutdown > 0) ? String.Format("{0}", status.ShutdownReason) : "";
                StateTextBox.Text = status.State.ToString();
            }
        }
        
        private void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            if (m_server == null)
            {
                return;
            }

            try
            {
                m_server.ApplyChanges();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    Opc.Ua.Client.Controls.ExceptionDlg.Show(Parent.Text, exception);
                }
            }

            try
            {
                m_server.Disconnect();
            }
            catch (Exception)
            {
                // ignore.
            }
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }
    }
}
