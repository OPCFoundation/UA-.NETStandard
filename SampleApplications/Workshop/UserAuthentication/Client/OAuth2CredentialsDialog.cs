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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Client.Controls;

namespace Quickstarts.UserAuthenticationClient
{
    public partial class OAuth2CredentialsDialog : Form
    {
        private OAuth2Credential m_credential;
        private OAuth2AccessToken m_token;
        private AuthorizationClient m_client;

        public OAuth2CredentialsDialog()
        {
            InitializeComponent();
        }

        public OAuth2AccessToken ShowDialog(OAuth2Credential credential)
        {
            if (credential == null)
            {
                throw new ArgumentNullException("settings");
            }

            m_credential = credential;
            m_client = new AuthorizationClient();

            var url = new UriBuilder(m_credential.AuthorityUrl);
            url.Path += m_credential.AuthorizationEndpoint;
            url.Query = String.Format("response_type=code&client_id={0}&redirect_uri={1}", Uri.EscapeUriString(m_credential.ClientId), Uri.EscapeUriString(m_credential.RedirectUrl));

            Browser.Navigate(url.ToString());

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_token;
        }

        private void ShowError(Exception e)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => ShowError(e)));
                return;
            }

            ClientUtils.HandleException(this.Text, e);
        }

        private async void Browser_Navigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            try
            {
                int index = e.Url.PathAndQuery.IndexOf("code=");

                if (index >= 0)
                {
                    var token = e.Url.PathAndQuery.Substring(index + "code=".Length);

                    index = token.IndexOf("&");

                    if (index >= 0)
                    {
                        token = token.Substring(0, index);
                    }

                    var resourceId = (m_credential.ServerResourceId != null) ? m_credential.ServerResourceId : null;

                    Browser.Visible = false;
                    m_token = await m_client.RequestTokenWithAuthenticationCodeAsync(m_credential, resourceId, token);
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }

        private void Browser_Navigating(object sender, WebBrowserNavigatingEventArgs e)
        {
        }
    }
}
