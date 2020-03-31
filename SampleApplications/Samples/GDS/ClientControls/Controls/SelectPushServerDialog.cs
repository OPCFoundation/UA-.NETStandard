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

using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class SelectPushServerDialog : Form
    {
        public SelectPushServerDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        private ServerPushConfigurationClient m_pushServer;

        public string ShowDialog(IWin32Window owner, ServerPushConfigurationClient pushServer, IList<string> serverUrls)
        {
            m_pushServer = pushServer;

            ServersListBox.Items.Clear();

            foreach (var serverUrl in serverUrls)
            {
                ServersListBox.Items.Add(serverUrl);
            }

            ServerUrlTextBox.Text = pushServer.EndpointUrl;
            UserNameCredentialsRB.Checked = true;
            OkButton.Enabled = Uri.IsWellFormedUriString(ServerUrlTextBox.Text.Trim(), UriKind.Absolute);

            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            return ServerUrlTextBox.Text.Trim();
        }

        private void ServersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ServerUrlTextBox.Text = ServersListBox.SelectedItem as string;
        }

        private void ServerUrlTextBox_TextChanged(object sender, EventArgs e)
        {
            OkButton.Enabled = Uri.IsWellFormedUriString(ServerUrlTextBox.Text.Trim(), UriKind.Absolute);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            try
            {
                string url = ServerUrlTextBox.Text.Trim();

                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    throw new ArgumentException("The URL is not valid: " + url, "ServerUrl");
                }

                try
                {
                    Cursor = Cursors.WaitCursor;

                    var endpoint = CoreClientUtils.SelectEndpoint(url, false, 5000);

                    if (UserNameCredentialsRB.Checked)
                    {
                        if (endpoint.FindUserTokenPolicy(UserTokenType.UserName, (string)null) == null)
                        {
                            throw new ArgumentException("Server does not support username/password user identity tokens.");
                        }

                        var identity = new Opc.Ua.Client.Controls.UserNamePasswordDlg().ShowDialog(m_pushServer.AdminCredentials, "Provide PushServer Administrator Credentials");

                        if (identity != null)
                        {
                            m_pushServer.AdminCredentials = identity;
                        }
                    }

                    m_pushServer.Connect(url).Wait();
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                DialogResult = System.Windows.Forms.DialogResult.OK;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void SelectPushServerDialog_Load(object sender, EventArgs e)
        {

        }
    }
}
