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
using System.Text;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class DiscoveryControl : UserControl
    {
        public DiscoveryControl()
        {
            InitializeComponent();
            DiscoveryTreeView.ImageList = new ImageListControl().ImageList;
            ServersGridView.AutoGenerateColumns = false;
            EndpointsGridView.AutoGenerateColumns = false;

            m_dataset = new DataSet();

            m_dataset.Tables.Add("Servers");
            m_dataset.Tables[0].Columns.Add("ServerName", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerCapabilities", typeof(string));
            m_dataset.Tables[0].Columns.Add("EndpointUrl", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerOnNetwork", typeof(ServerOnNetwork));
            m_dataset.Tables[0].Columns.Add("ApplicationDescription", typeof(ApplicationDescription));

            ServersGridView.DataSource = m_dataset.Tables[0].DefaultView;

            m_dataset.Tables.Add("Endpoints");
            m_dataset.Tables[1].Columns.Add("EndpointUrl", typeof(string));
            m_dataset.Tables[1].Columns.Add("SecurityMode", typeof(string));
            m_dataset.Tables[1].Columns.Add("SecurityProfile", typeof(string));
            m_dataset.Tables[1].Columns.Add("Endpoint", typeof(EndpointDescription));

            EndpointsGridView.DataSource = m_dataset.Tables[1].DefaultView;
        }

        private LocalDiscoveryServerClient m_lds;
        private GlobalDiscoveryServerClient m_gds;
        private ConfiguredEndpointCollection m_endpoints;
        private QueryServersFilter m_filters;
        private DataSet m_dataset;

        private DataTable ServersTable { get { return m_dataset.Tables[0]; } }
        private DataTable EndpointsTable { get { return m_dataset.Tables[1]; } }

        private enum RootFolders
        {
            LocalMachine,
            LocalNetwork,
            GlobalDiscovery,
            CustomDiscovery,
            Add
        }

        private class ExpandNodeData
        {
            public TreeNode Parent;
            public LocalDiscoveryServerClient Lds;
        }

        [DefaultValue(300)]
        [SettingsBindable(true)]
        public int SplitterDistance
        {
            get { return MainPanel.SplitterDistance; }
            set { MainPanel.SplitterDistance = value; }
        }

        public ContextMenuStrip TreeViewContextMenu
        {
            get { return DiscoveryTreeView.ContextMenuStrip; }
        }

        public EndpointDescription SelectedEndpoint
        {
            get
            {
                TreeNode node = DiscoveryTreeView.SelectedNode;

                if (node != null)
                {
                    if (RootFolders.LocalMachine.Equals(node.Tag) || RootFolders.LocalNetwork.Equals(node.Tag) || RootFolders.GlobalDiscovery.Equals(node.Tag))
                    {
                        if (ServersGridView.SelectedRows.Count > 0)
                        {
                            DataRowView view = (DataRowView)ServersGridView.SelectedRows[0].DataBoundItem;
                            DataRow row = (DataRow)view.Row;

                            if (!row.IsNull(3))
                            {
                                ServerOnNetwork server = (ServerOnNetwork)row[3];

                                if (server != null)
                                {
                                    var endpoint = new EndpointDescription(server.DiscoveryUrl)
                                    {
                                        Server = GetApplicationDescription(server)
                                    };
                                    return endpoint;
                                }
                            }

                            if (!row.IsNull(4))
                            {
                                ApplicationDescription application = (ApplicationDescription)row[4];

                                if (application != null)
                                {
                                    var endpoint = new EndpointDescription(SelectDiscoveryUrl(application))
                                    {
                                        Server = application
                                    };
                                    return endpoint;
                                }
                            }
                        }

                        return null;
                    }

                    if (node.Tag is ConfiguredEndpoint)
                    {
                        var endpoint = GetSelectedEndpoint();

                        if (endpoint == null)
                        {
                            ConfiguredEndpoint ce = (ConfiguredEndpoint)node.Tag;

                            if (ce.Description.EndpointUrl == ce.Description.Server.ApplicationUri)
                            {
                                ce.Description.Server = GetApplicationDescription(new ServerOnNetwork() { DiscoveryUrl = ce.Description.EndpointUrl });
                            }

                            endpoint = ce.Description;
                        }

                        return endpoint;
                    }

                    if (node.Tag is ApplicationDescription)
                    {
                        var endpoint = GetSelectedEndpoint();

                        if (endpoint == null)
                        {
                            ApplicationDescription application = (ApplicationDescription)node.Tag;
                            endpoint = new EndpointDescription(SelectDiscoveryUrl(application))
                            {
                                Server = application
                            };
                        }

                        return endpoint;
                    }

                    if (node.Tag is ServerOnNetwork)
                    {
                        var endpoint = GetSelectedEndpoint();

                        if (endpoint == null)
                        {
                            ServerOnNetwork server = (ServerOnNetwork)node.Tag;
                            endpoint = new EndpointDescription(server.DiscoveryUrl)
                            {
                                Server = GetApplicationDescription(server)
                            };
                        }

                        return endpoint;
                    }
                }

                return null;
            }
        }

        private ApplicationDescription GetApplicationDescription(ServerOnNetwork server)
        {
            ApplicationDescription fallback = null;

            try
            {
                foreach (var application in m_lds.FindServers(server.DiscoveryUrl, null))
                {
                    if (fallback == null)
                    {
                        fallback = application;
                    }

                    if (application.DiscoveryUrls != null)
                    {
                        if (application.DiscoveryUrls.Contains(server.DiscoveryUrl))
                        {
                            return application;
                        }
                    }
                }
            }
            catch (Exception)
            {
                fallback = new ApplicationDescription()
                {
                    ApplicationType = ApplicationType.Server,
                    ApplicationName = server.ServerName,
                    ApplicationUri = server.DiscoveryUrl
                };

                fallback.DiscoveryUrls.Add(server.DiscoveryUrl);
            }

            return fallback;
        }

        private EndpointDescription GetSelectedEndpoint()
        {
            if (EndpointsGridView.SelectedRows.Count > 0)
            {
                DataRowView view = (DataRowView)EndpointsGridView.SelectedRows[0].DataBoundItem;
                DataRow row = (DataRow)view.Row;
                return (EndpointDescription)row[3];
            }

            return null;
        }

        public void Initialize(
            ConfiguredEndpointCollection endpoints, 
            LocalDiscoveryServerClient lds, 
            GlobalDiscoveryServerClient gds, 
            QueryServersFilter filters)
        {
            m_lds = lds;
            m_gds = gds;
            m_filters = filters;
            
            DiscoveryTreeView.Nodes.Clear();

            TreeNode node = new TreeNode("Local Machine");
            node.SelectedImageIndex = node.ImageIndex = ImageIndex.Computer;
            node.Tag = RootFolders.LocalMachine;
            node.Nodes.Add(new TreeNode());
            DiscoveryTreeView.Nodes.Add(node);

            node = new TreeNode("Local Network");
            node.SelectedImageIndex = node.ImageIndex = ImageIndex.LocalNetwork;
            node.Tag = RootFolders.LocalNetwork;
            node.Nodes.Add(new TreeNode());
            DiscoveryTreeView.Nodes.Add(node);

            node = new TreeNode("Global Discovery");
            node.SelectedImageIndex = node.ImageIndex = ImageIndex.GlobalNetwork;
            node.Tag = RootFolders.GlobalDiscovery;
            node.Nodes.Add(new TreeNode());
            DiscoveryTreeView.Nodes.Add(node);

            if (endpoints != null)
            {
                m_endpoints = endpoints;

                node = new TreeNode("Custom Discovery");
                node.SelectedImageIndex = node.ImageIndex = ImageIndex.ClosedFolder;
                node.Tag = RootFolders.CustomDiscovery;

                TreeNode child = new TreeNode("<double click to add server>");
                child.SelectedImageIndex = child.ImageIndex = ImageIndex.Add;
                child.Tag = RootFolders.Add;
                node.Nodes.Add(child);

                DiscoveryTreeView.Nodes.Add(node);

                foreach (ConfiguredEndpoint ce in m_endpoints.Endpoints)
                {
                    child = new TreeNode(Utils.Format("{0}", ce.ToString()));
                    child.SelectedImageIndex = child.ImageIndex = (ce.Description.SecurityMode == MessageSecurityMode.None && ce.EndpointUrl.Scheme != Utils.UriSchemeHttps) ? ImageIndex.InSecure : ImageIndex.Secure;
                    child.Tag = ce;
                    node.Nodes.Add(child);
                }
            }
        }

        private string SelectDiscoveryUrl(ApplicationDescription server)
        {
            if (server == null || server.DiscoveryUrls == null)
            {
                return null;
            }

            string url = null;

            // always use opc.tcp by default.
            foreach (string discoveryUrl in server.DiscoveryUrls)
            {
                if (discoveryUrl.StartsWith("opc.tcp://"))
                {
                    url = discoveryUrl;
                    break;
                }
            }

            // try HTTPS if no opc.tcp.
            if (url == null)
            {
                foreach (string discoveryUrl in server.DiscoveryUrls)
                {
                    if (discoveryUrl.StartsWith("https://"))
                    {
                        url = discoveryUrl;
                        break;
                    }
                }
            }

            // use the first URL if nothing else.
            if (url == null)
            {
                url = server.DiscoveryUrls[0];
            }

            return url;
        }

        private void DiscoveryTreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count != 1 || !String.IsNullOrEmpty(e.Node.Nodes[0].Text))
            {
                return;
            }

            e.Node.Nodes.Clear();

            if (RootFolders.LocalMachine.Equals(e.Node.Tag))
            {
                m_lds.BeginFindServers(
                    OnFindServersComplete,
                    new ExpandNodeData() { Parent = e.Node, Lds = m_lds });

                return;
            }

            if (RootFolders.LocalNetwork.Equals(e.Node.Tag))
            {
                m_lds.BeginFindServersOnNetwork(
                    0,
                    100,
                    OnFindServersOnNetworkComplete,
                    new ExpandNodeData() { Parent = e.Node, Lds = m_lds });

                return;
            }

            if (RootFolders.GlobalDiscovery.Equals(e.Node.Tag))
            {
                var servers = new ViewServersOnNetworkDialog(m_gds).ShowDialog(this, ref m_filters);

                if (servers != null)
                {
                    foreach (var server in servers)
                    {
                        TreeNode node = new TreeNode(String.Format("{0}", server.ServerName));
                        node.SelectedImageIndex = node.ImageIndex = ImageIndex.Server;
                        node.Tag = server;
                        node.Nodes.Add(new TreeNode());
                        e.Node.Nodes.Add(node);
                    }
                }

                return;
            }

            if (RootFolders.CustomDiscovery.Equals(e.Node.Tag))
            {
                return;
            }

            if (e.Node.Tag is Uri)
            {
                m_lds.BeginFindServers(
                    e.Node.Tag.ToString(),
                    null,
                    null,
                    null,
                    null,
                    OnFindServersComplete,
                    new ExpandNodeData() { Parent = e.Node, Lds = m_lds });

                return;
            }
        }

        private void OnFindServersComplete(IAsyncResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new AsyncCallback(OnFindServersComplete), result);
                return;
            }

            try
            {
                ExpandNodeData data = (ExpandNodeData)result.AsyncState;

                List<ApplicationDescription> servers = data.Lds.EndFindServers(result);

                foreach (ApplicationDescription server in servers)
                {
                    if (server.ApplicationType == ApplicationType.DiscoveryServer)
                    {
                        continue;
                    }

                    TreeNode node = new TreeNode(String.Format("{0}", server.ApplicationName));
                    node.SelectedImageIndex = node.ImageIndex = (server.ApplicationType == ApplicationType.DiscoveryServer) ? ImageIndex.LocalNetwork : ImageIndex.Server;
                    node.Tag = server;
                    node.Nodes.Add(new TreeNode());
                    data.Parent.Nodes.Add(node);
                }

                if (DiscoveryTreeView.SelectedNode == data.Parent)
                {
                    ShowApplicationDescriptions(data.Parent.Nodes);
                }
                else
                {
                    data.Parent.Expand();
                }
            }
            catch (Exception e)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, e);
            }
        }

        private void ShowApplicationDescriptions(TreeNodeCollection nodes)
        {
            ServersTable.Rows.Clear();

            foreach (TreeNode node in nodes)
            {
                ApplicationDescription server = node.Tag as ApplicationDescription;

                if (server == null)
                {
                    continue;
                }

                DataRow row = ServersTable.NewRow();

                row[0] = (LocalizedText.IsNullOrEmpty(server.ApplicationName)) ? "" : server.ApplicationName.Text;
                row[1] = server.ApplicationType.ToString();

                StringBuilder buffer = new StringBuilder();

                if (server.DiscoveryUrls != null)
                {
                    foreach (var url in server.DiscoveryUrls)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append(", ");
                        }

                        buffer.Append(url);
                    }
                }

                row[2] = buffer.ToString();
                row[3] = null;
                row[4] = server;

                ServersTable.Rows.Add(row);
            }

            m_dataset.AcceptChanges();

            foreach (DataGridViewRow row in ServersGridView.Rows)
            {
                row.Selected = false;
            }
        }

        private void OnFindServersOnNetworkComplete(IAsyncResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new AsyncCallback(OnFindServersOnNetworkComplete), result);
                return;
            }

            try
            {
                ExpandNodeData data = (ExpandNodeData)result.AsyncState;

                DateTime lastCounterResetTime;
                List<ServerOnNetwork> servers = data.Lds.EndFindServersOnNetwork(result, out lastCounterResetTime);

                foreach (ServerOnNetwork server in servers)
                {
                    if (server.ServerCapabilities.Contains("LDS"))
                    {
                        continue;
                    }

                    TreeNode node = new TreeNode(String.Format("{0}", server.ServerName));
                    node.SelectedImageIndex = node.ImageIndex = ImageIndex.Server;
                    node.Tag = server;
                    node.Nodes.Add(new TreeNode());
                    data.Parent.Nodes.Add(node);
                }

                if (DiscoveryTreeView.SelectedNode == data.Parent)
                {
                    ShowServerOnNetworks(data.Parent.Nodes);
                }
                else
                {
                    data.Parent.Expand();
                }
            }
            catch (Exception e)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, e);
            }
        }

        private void ShowServerOnNetworks(TreeNodeCollection nodes)
        {
            ServersTable.Rows.Clear();

            foreach (TreeNode node in nodes)
            {
                ServerOnNetwork server = node.Tag as ServerOnNetwork;

                if (server == null)
                {
                    continue;
                }

                DataRow row = ServersTable.NewRow();

                row[0] = server.ServerName;

                StringBuilder buffer = new StringBuilder();

                if (server.ServerCapabilities != null)
                {
                    foreach (var capability in server.ServerCapabilities)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append(", ");
                        }

                        buffer.Append(capability);
                    }
                }

                row[1] = buffer.ToString();
                row[2] = server.DiscoveryUrl;
                row[3] = server;
                row[4] = null;

                ServersTable.Rows.Add(row);
            }

            m_dataset.AcceptChanges();

            foreach (DataGridViewRow row in ServersGridView.Rows)
            {
                row.Selected = false;
            }
        }

        private class GetEndpointsData
        {
            public TreeNode Parent;
            public LocalDiscoveryServerClient Lds;
        }

        private void OnGetEndpointsComplete(IAsyncResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new AsyncCallback(OnGetEndpointsComplete), result);
                return;
            }

            GetEndpointsData data = (GetEndpointsData)result.AsyncState;

            try
            {
                List<EndpointDescription> endpoints = data.Lds.EndGetEndpoints(result);

                if (DiscoveryTreeView.SelectedNode == data.Parent)
                {
                    ShowEndpointDescriptions(endpoints);
                }
            }
            catch (Exception e)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, e); 
            }
        }

        private void ShowEndpointDescriptions(List<EndpointDescription> endpoints)
        {
            EndpointsTable.Rows.Clear();

            bool headerSet = false;

            foreach (EndpointDescription endpoint in endpoints)
            {
                if (!headerSet)
                {
                    ApplicationNameTextBox.Text = (LocalizedText.IsNullOrEmpty(endpoint.Server.ApplicationName)) ? "---" : endpoint.Server.ApplicationName.Text;
                    ApplicationTypeTextBox.Text = endpoint.Server.ApplicationType.ToString();
                    ApplicationUriTextBox.Text = endpoint.Server.ApplicationUri;
                    ProductUriTextBox.Text = endpoint.Server.ProductUri;

                    headerSet = true;
                }

                DataRow row = EndpointsTable.NewRow();

                row[0] = endpoint.EndpointUrl;
                row[1] = endpoint.SecurityMode.ToString();
                row[2] = SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri);
                row[3] = endpoint;

                EndpointsTable.Rows.Add(row);
            }

            m_dataset.AcceptChanges();

            foreach (DataGridViewRow row in EndpointsGridView.Rows)
            {
                row.Selected = false;
            }
        }

        private void DiscoveryTreeView_MouseDown(object sender, MouseEventArgs e)
        {
            TreeNode node = DiscoveryTreeView.GetNodeAt(e.Location);

            if (node != null)
            {
                DiscoveryTreeView.SelectedNode = node;
            }
        }

        private void RefreshMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = DiscoveryTreeView.SelectedNode;

            if (node == null)
            {
                return;
            }

            if (RootFolders.CustomDiscovery.Equals(node.Tag))
            {
                return;
            }

            node.Collapse();
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode());
        }

        private void RefreshWithParametersMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = DiscoveryTreeView.SelectedNode;

            if (node == null)
            {
                return;
            }

            node.Collapse();
            node.Nodes.Clear();

            if (RootFolders.LocalMachine.Equals(node.Tag))
            {
                /*
                FindServersRequest result = new EditComplexValueDlg().ShowDialog(
                    null,
                    null,
                    new FindServersRequest(),
                    false,
                    "Specify the Request Parameters") as FindServersRequest;

                if (result == null)
                {
                    return;
                }

                m_lds.BeginFindServers(
                    null,
                    null,
                    result.EndpointUrl,
                    result.LocaleIds,
                    result.ServerUris,
                    OnFindServersComplete,
                    new ExpandNodeData() { Parent = node, Lds = m_lds });
                 * */

                return;
            }
        }

        private void DiscoveryTreeView_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (DiscoveryTreeView.SelectedNode == null || !RootFolders.Add.Equals(DiscoveryTreeView.SelectedNode.Tag))
                {
                    return;
                }

                string url = new EndpointUrlDialog().ShowDialog(null);

                if (url != null)
                {
                    ConfiguredEndpoint ce = m_endpoints.Add(new EndpointDescription(url));
                    ce.Description.Server.ApplicationUri = null;
                    ce.Description.Server.ApplicationName = null;

                    TreeNode node = new TreeNode(Utils.Format("{0}", ce.ToString()));
                    node.SelectedImageIndex = node.ImageIndex = ImageIndex.InSecure;
                    node.Tag = ce;

                    DiscoveryTreeView.SelectedNode.Parent.Nodes.Add(node);
                    DiscoveryTreeView.SelectedNode = node;

                    try
                    {
                        m_endpoints.Save();
                    }
                    catch
                    {
                        // ignore.
                    }
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void DeleteMenuItem_Click(object sender, EventArgs e)
        {
            if (DiscoveryTreeView.SelectedNode != null)
            {
                ConfiguredEndpoint ce = DiscoveryTreeView.SelectedNode.Tag as ConfiguredEndpoint;

                if (ce != null)
                {
                    DiscoveryTreeView.SelectedNode.Remove();

                    try
                    {
                        m_endpoints.Remove(ce);
                        m_endpoints.Save();
                    }
                    catch
                    {
                        // ignore.
                    }
                }
            }
        }

        private void PopupMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            TreeNode node = DiscoveryTreeView.SelectedNode;

            if (node != null)
            {
                RefreshMenuItem.Enabled = !(node.Tag is ConfiguredEndpoint);
                RefreshWithParametersMenuItem.Visible = ((node.Tag is ConfiguredEndpoint) || (node.Tag is ApplicationDescription) || (node.Tag is Uri) || (RootFolders.LocalMachine.Equals(node.Tag)));
                DeleteMenuItem.Visible = node.Parent != null && RootFolders.CustomDiscovery.Equals(node.Parent.Tag);
                AddEndpointMenuItem.Visible = RootFolders.Add.Equals(DiscoveryTreeView.SelectedNode.Tag);
                AddEndpointSeparatorMenuItem.Visible = AddEndpointMenuItem.Visible;
            }
            else
            {
                RefreshMenuItem.Enabled = false;
                RefreshWithParametersMenuItem.Visible = false;
                DeleteMenuItem.Visible = false;
                AddEndpointMenuItem.Visible = false;
                AddEndpointSeparatorMenuItem.Visible = false;
            }
        }

        private void ShowPanel(bool list)
        {
            ServersGridView.Visible = list;
            EndpointsGridView.Visible = !list;
            ApplicationDescriptionPanel.Visible = !list;
            FilterPanel.Visible = list;

            if (list)
            {
                string filter = FilterTextBox.Text.Trim();

                if (!String.IsNullOrEmpty(filter))
                {
                    ServersTable.DefaultView.RowFilter = String.Format("ServerName LIKE '{0}%' OR ServerCapabilities LIKE '{0}%' OR EndpointUrl LIKE '{0}%'", filter);
                }
                else
                {
                    ServersTable.DefaultView.RowFilter = "";
                }
            }
        }

        private void DiscoveryTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (RootFolders.LocalMachine.Equals(e.Node.Tag))
                {
                    ServersTable.Rows.Clear();
                    ShowPanel(true);

                    if (e.Node.Nodes.Count == 1 && String.IsNullOrEmpty(e.Node.Nodes[0].Text))
                    {
                        e.Node.Nodes.Clear();

                        m_lds.BeginFindServers(
                            OnFindServersComplete,
                            new ExpandNodeData() { Parent = e.Node, Lds = m_lds });
                    }
                    else
                    {
                        ShowApplicationDescriptions(e.Node.Nodes);
                    }

                    return;
                }

                if (RootFolders.LocalNetwork.Equals(e.Node.Tag))
                {
                    ServersTable.Rows.Clear();
                    ShowPanel(true);

                    if (e.Node.Nodes.Count == 1 && String.IsNullOrEmpty(e.Node.Nodes[0].Text))
                    {
                        e.Node.Nodes.Clear();

                        m_lds.BeginFindServersOnNetwork(
                            0,
                            1000,
                            OnFindServersOnNetworkComplete,
                            new ExpandNodeData() { Parent = e.Node, Lds = m_lds });
                    }
                    else
                    {
                        ShowServerOnNetworks(e.Node.Nodes);
                    }

                    return;
                }

                if (RootFolders.GlobalDiscovery.Equals(e.Node.Tag))
                {
                    ServersTable.Rows.Clear();
                    ShowPanel(true);

                    if (e.Node.Nodes.Count == 1 && String.IsNullOrEmpty(e.Node.Nodes[0].Text))
                    {
                        e.Node.Nodes.Clear();

                        var servers = new ViewServersOnNetworkDialog(m_gds).ShowDialog(this, ref m_filters);

                        if (servers != null)
                        {
                            foreach (var server in servers)
                            {
                                TreeNode node = new TreeNode(String.Format("{0}", server.ServerName));
                                node.SelectedImageIndex = node.ImageIndex = ImageIndex.Server;
                                node.Tag = server;
                                node.Nodes.Add(new TreeNode());
                                e.Node.Nodes.Add(node);
                            }
                        }
                    }
                       
                    ShowServerOnNetworks(e.Node.Nodes);

                    return;
                }

                if (RootFolders.CustomDiscovery.Equals(e.Node.Tag))
                {
                    ServersTable.Rows.Clear();
                    ShowPanel(true);
                    return;
                }

                if (e.Node.Tag is ApplicationDescription)
                {
                    EndpointsTable.Rows.Clear();
                    ShowPanel(false);

                    ApplicationDescription application = (ApplicationDescription)e.Node.Tag;

                    ApplicationNameTextBox.Text = (LocalizedText.IsNullOrEmpty(application.ApplicationName))?"---":application.ApplicationName.Text;
                    ApplicationTypeTextBox.Text = application.ApplicationType.ToString();
                    ApplicationUriTextBox.Text = application.ApplicationUri;
                    ProductUriTextBox.Text = application.ProductUri;
                    
                    string discoveryUrl = SelectDiscoveryUrl(application);
                    
                    if (discoveryUrl != null)
                    {
                        m_lds.BeginGetEndpoints(
                            discoveryUrl,
                            null,
                            OnGetEndpointsComplete,
                            new GetEndpointsData() { Parent = e.Node, Lds = m_lds });
                    }
                }
                
                if (e.Node.Tag is ServerOnNetwork)
                {
                    EndpointsTable.Rows.Clear();
                    ShowPanel(false);

                    ServerOnNetwork server = (ServerOnNetwork)e.Node.Tag;

                    ApplicationNameTextBox.Text = server.ServerName;
                    ApplicationTypeTextBox.Text = "---";
                    ApplicationUriTextBox.Text = "---";
                    ProductUriTextBox.Text = "---";

                    try
                    {
                        Cursor = Cursors.WaitCursor;

                        m_lds.BeginGetEndpoints(
                            server.DiscoveryUrl,
                            null,
                            OnGetEndpointsComplete,
                            new GetEndpointsData() { Parent = e.Node, Lds = m_lds });
                    }
                    finally
                    {
                        Cursor = Cursors.Default;
                    }
                }

                if (e.Node.Tag is ConfiguredEndpoint)
                {
                    EndpointsTable.Rows.Clear();
                    ShowPanel(false);

                    ConfiguredEndpoint server = (ConfiguredEndpoint)e.Node.Tag;

                    ApplicationNameTextBox.Text = "---";
                    ApplicationTypeTextBox.Text = "---";
                    ApplicationUriTextBox.Text = "---";
                    ProductUriTextBox.Text = "---";

                    m_lds.BeginGetEndpoints(
                        server.EndpointUrl.ToString(),
                        null,
                        OnGetEndpointsComplete,
                        new GetEndpointsData() { Parent = e.Node, Lds = m_lds });
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void FilterTextBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                string filter = FilterTextBox.Text.Trim();

                if (!String.IsNullOrEmpty(filter))
                {
                    ServersTable.DefaultView.RowFilter = String.Format("ServerName LIKE '%{0}%' OR ServerCapabilities LIKE '%{0}%' OR EndpointUrl LIKE '%{0}%'", filter);
                }
                else
                {
                    ServersTable.DefaultView.RowFilter = "";
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void EndpointsGridView_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {
                foreach (DataGridViewRow row in ServersGridView.Rows)
                {
                    row.Selected = false;
                }
            }
        }

        private void EndpointsGridView_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (EndpointsGridView.SelectedRows.Count > 0)
                {
                    DataRowView view = (DataRowView)EndpointsGridView.SelectedRows[0].DataBoundItem;
                    DataRow row = (DataRow)view.Row;
                    new EndpointUrlDialog().ShowDialog((string)row[0]);
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ServersGridView_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (ServersGridView.SelectedRows.Count > 0)
                {
                    DataRowView view = (DataRowView)ServersGridView.SelectedRows[0].DataBoundItem;
                    DataRow row = (DataRow)view.Row;
                    new EndpointUrlDialog().ShowDialog((string)row[2]);
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }
    }
}
