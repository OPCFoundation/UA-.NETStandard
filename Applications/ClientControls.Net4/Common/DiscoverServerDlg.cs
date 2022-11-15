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
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to specify a host name and discovers the servers.
    /// </summary>
    public partial class DiscoverServerDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public DiscoverServerDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion
        
        #region Private Fields
        private ApplicationConfiguration m_configuration;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Shows the dialog.
        /// </summary>
        /// <param name="configuration">The client applicatio configuration.</param>
        /// <returns>The selected endpoint url</returns>
        public string ShowDialog(ApplicationConfiguration configuration)
        {
            return ShowDialog(configuration, null);
        }

        /// <summary>
        /// Shows the dialog.
        /// </summary>
        /// <param name="configuration">The client applicatio configuration.</param>
        /// <param name="hostName">The default host name.</param>
        /// <returns>The selected endpoint url</returns>
        public string ShowDialog(ApplicationConfiguration configuration, string hostName)
        {
            m_configuration = configuration;

            if (String.IsNullOrEmpty(hostName))
            {
                ValueTB.Text = System.Net.Dns.GetHostName();
            }
            else
            {
                ValueTB.Text = hostName;
            }

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return ServersLB.SelectedItem as string;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the endpoints for the host.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        private string[] GetEndpoints(string hostName)
        {
            List<string> urls = new List<string>();

            try
            {
                Cursor = Cursors.WaitCursor;

                // set a short timeout because this is happening in the drop down event.
                EndpointConfiguration configuration = EndpointConfiguration.Create(m_configuration);
                configuration.OperationTimeout = 20000;

                // Connect to the local discovery server and find the available servers.
                using (DiscoveryClient client = DiscoveryClient.Create(new Uri(Utils.Format("opc.tcp://{0}:4840", hostName)), configuration))
                {
                    ApplicationDescriptionCollection servers = client.FindServers(null);

                    // populate the drop down list with the discovery URLs for the available servers.
                    for (int ii = 0; ii < servers.Count; ii++)
                    {
                        // don't show discovery servers.
                        if (servers[ii].ApplicationType == ApplicationType.DiscoveryServer)
                        {
                            continue;
                        }

                        for (int jj = 0; jj < servers[ii].DiscoveryUrls.Count; jj++)
                        {
                            string discoveryUrl = servers[ii].DiscoveryUrls[jj];

                            // Many servers will use the '/discovery' suffix for the discovery endpoint.
                            // The URL without this prefix should be the base URL for the server. 
                            if (discoveryUrl.EndsWith("/discovery"))
                            {
                                discoveryUrl = discoveryUrl.Substring(0, discoveryUrl.Length - "/discovery".Length);
                            }

                            // remove duplicates.
                            if (!urls.Contains(discoveryUrl))
                            {
                                urls.Add(discoveryUrl);
                            }
                        }
                    }
                }

                return urls.ToArray();
            }
            catch (Exception)
            {
                return urls.ToArray();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
        #endregion
                
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (Utils.ParseUri(ServersLB.SelectedItem as string) != null)
                {
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ServersLB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                OkBTN.Enabled = Utils.ParseUri(ServersLB.SelectedItem as string) != null;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void FindBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                ServersLB.Items.Clear();
                ServersLB.Items.Add("No endpoints found.");

                if (String.IsNullOrEmpty(ValueTB.Text))
                {
                    return;
                }

                ServersLB.Items.Clear();

                foreach (string url in GetEndpoints(ValueTB.Text))
                {
                    ServersLB.Items.Add(url);
                }

                if (ServersLB.Items.Count == 0)
                {
                    ServersLB.Items.Add("No endpoints found.");
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void ValueTB_TextChanged(object sender, EventArgs e)
        {
            ServersLB.Items.Clear();
        }
        #endregion
    }
}
