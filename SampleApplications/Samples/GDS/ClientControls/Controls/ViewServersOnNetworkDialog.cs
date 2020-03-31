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
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Client.Controls;
using System.Linq;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class ViewServersOnNetworkDialog : Form
    {
        public ViewServersOnNetworkDialog(GlobalDiscoveryServerClient gds)
        {
            InitializeComponent();
            Icon = ClientUtils.GetAppIcon();
            ServersDataGridView.AutoGenerateColumns = false;

            m_gds = gds;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Servers");

            m_dataset.Tables[0].Columns.Add("RecordId", typeof(uint));
            m_dataset.Tables[0].Columns.Add("ServerName", typeof(string));
            m_dataset.Tables[0].Columns.Add("DiscoveryUrl", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerCapabilities", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerOnNetwork", typeof(ServerOnNetwork));

            ServersDataGridView.DataSource = m_dataset.Tables[0];
        }

        private DataTable ServersTable { get { return m_dataset.Tables[0]; } }
        private DataSet m_dataset;
        private GlobalDiscoveryServerClient m_gds;

        public List<ServerOnNetwork> ShowDialog(IWin32Window owner, ref QueryServersFilter filters)
        {
            ServersTable.Rows.Clear();

            if (filters == null)
            {
                filters = new QueryServersFilter();
            }

            ApplicationUriTextBox.Text = filters.ApplicationUri;
            ApplicationNameTextBox.Text = filters.ApplicationName;
            ProductUriTextBox.Text = filters.ProductUri;
            
            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            List<ServerOnNetwork> servers = new List<ServerOnNetwork>();

            foreach (DataRow row in ServersTable.Rows)
            {
                servers.Add((ServerOnNetwork)row[4]);
            }

            return servers;
        }

        private void ApplicationRecordDataGridView_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                SearchButton.Enabled = ServersDataGridView.SelectedRows.Count > 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            try
            {
                ServersTable.Rows.Clear();

                if (!m_gds.IsConnected)
                {
                    new SelectGdsDialog().ShowDialog(null, m_gds, m_gds.GetDefaultGdsUrls(null));
                }

                uint maxNoOfRecords = (uint)NumberOfRecordsUpDown.Value;

                var servers = m_gds.QueryServers(
                    0,
                    ApplicationNameTextBox.Text.Trim(),
                    ApplicationUriTextBox.Text.Trim(),
                    ProductUriTextBox.Text.Trim(),
                    ServerCapabilitiesTextBox.Tag as IList<string>);

                bool found = false;

                foreach (var server in servers)
                {
                    found = true;

                    if (maxNoOfRecords == 0)
                    {
                        SearchButton.Visible = false;
                        NextButton.Visible = true;
                        StopButton.Visible = true;
                        var nextServers = servers.Where(x => x.RecordId > server.RecordId);
                        StopButton.Tag = nextServers;
                        break;
                    }

                    maxNoOfRecords--;

                    DataRow row = ServersTable.NewRow();

                    row[0] = server.RecordId;
                    row[1] = server.ServerName;
                    row[2] = server.DiscoveryUrl;

                    StringBuilder buffer = new StringBuilder();

                    if (server.ServerCapabilities != null)
                    {
                        foreach (var id in server.ServerCapabilities)
                        {
                            if (buffer.Length > 0)
                            {
                                buffer.Append(',');
                            }

                            buffer.Append(id);
                        }
                    }

                    row[3] = buffer.ToString();
                    row[4] = server;

                    ServersTable.Rows.Add(row);
                }

                if (!found)
                {
                    SearchButton.Visible = true;
                    SearchButton.Enabled = true;
                    NextButton.Visible = false;
                    StopButton.Visible = false;
                }

                ServersTable.AcceptChanges();

                if (ServersTable.Rows.Count == 0)
                {
                    MessageBox.Show(ParentForm, "No servers available that meet the filter criteria.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);                        
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void NextButton_Click(object sender, EventArgs e)
        {
            try
            {
                IEnumerable<ServerOnNetwork> servers = (IEnumerable<ServerOnNetwork>)StopButton.Tag;
                StopButton.Visible = false;
                StopButton.Tag = null;

                uint maxNoOfRecords = (uint)NumberOfRecordsUpDown.Value;

                bool foundAll = false;

                foreach (var server in servers)
                {
                    if (maxNoOfRecords == 0)
                    {
                        foundAll = true;
                        SearchButton.Visible = false;
                        NextButton.Visible = true;
                        StopButton.Visible = true;
                        var nextServers = servers.Where(x => x.RecordId > server.RecordId);
                        StopButton.Tag = nextServers;
                        break;
                    }

                    maxNoOfRecords--;

                    DataRow row = ServersTable.NewRow();

                    row[0] = server.RecordId;
                    row[1] = server.ServerName;
                    row[2] = server.DiscoveryUrl;

                    StringBuilder buffer = new StringBuilder();

                    if (server.ServerCapabilities != null)
                    {
                        foreach (var id in server.ServerCapabilities)
                        {
                            if (buffer.Length > 0)
                            {
                                buffer.Append(',');
                            }

                            buffer.Append(id);
                        }
                    }

                    row[3] = buffer.ToString();
                    row[4] = server;

                    ServersTable.Rows.Add(row);
                }

                if (!foundAll)
                {
                    SearchButton.Visible = true;
                    SearchButton.Enabled = true;
                    NextButton.Visible = false;
                    StopButton.Visible = false;
                }

                ServersTable.AcceptChanges();
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            try
            {
                SearchButton.Visible = true;
                SearchButton.Enabled = true;
                StopButton.Visible = false;
                StopButton.Tag = null;
                NextButton.Visible = false;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void SearchButton_Reset(object sender, EventArgs e)
        {
            try
            {
                SearchButton.Visible = true;
                SearchButton.Enabled = true;
                StopButton.Visible = false;
                StopButton.Tag = null;
                NextButton.Visible = false;
                ServersTable.Rows.Clear();
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }


        private void ServerCapabilitiesButton_Click(object sender, EventArgs e)
        {
            try
            {
                var capabilities = new ServerCapabilitiesDialog().ShowDialog(this, ServerCapabilitiesTextBox.Tag as IList<string>);

                if (capabilities == null)
                {
                    return;
                }

                StringBuilder buffer = new StringBuilder();

                foreach (var capability in capabilities)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(",");
                    }

                    buffer.Append(capability);
                }

                ServerCapabilitiesTextBox.Text = buffer.ToString();
                ServerCapabilitiesTextBox.Tag = capabilities;
                SearchButton_Reset(sender, e);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ApplicationUriTextBox_TextChanged(object sender, EventArgs e)
        {
            SearchButton_Reset(sender, e);
        }

        private void ApplicationNameTextBox_TextChanged(object sender, EventArgs e)
        {
            SearchButton_Reset(sender, e);
        }

        private void ProductUriTextBox_TextChanged(object sender, EventArgs e)
        {
            SearchButton_Reset(sender, e);
        }
    }

    public class QueryServersFilter
    {
        public string ApplicationUri;
        public string ApplicationName;
        public string ProductUri;
        public string[] ServerCapabilities;
    }
}
