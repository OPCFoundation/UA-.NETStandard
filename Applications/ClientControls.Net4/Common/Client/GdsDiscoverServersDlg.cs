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
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Searches for the servers in a GDS.
    /// </summary>
    public partial class GdsDiscoverServersDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Constructs the form.
        /// </summary>
        public GdsDiscoverServersDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            ServersLV.SmallImageList = new ClientUtils().ImageList;

            List<object> items = new  List<object>();

            foreach (object value in Enum.GetValues(typeof(Match)))
            {
                items.Add(value);
            }

            ApplicationNameCB.Items.AddRange(items.ToArray());
            MachineNameCB.Items.AddRange(items.ToArray());
            ApplicationUriCB.Items.AddRange(items.ToArray());
            ProductUriCB.Items.AddRange(items.ToArray());

            ApplicationNameCB.SelectedIndex = 0;
            MachineNameCB.SelectedIndex = 0;
            ApplicationUriCB.SelectedIndex = 0;
            ProductUriCB.SelectedIndex = 0;
        }
        #endregion

        #region Match Enumeration
        private enum Match
        {
            StartsWith,
            IsExactly,
            EndsWith,
            Contains
        }
        #endregion

        #region Private Fields
        private ApplicationDescription m_application;
        #endregion

        #region Private Constants
        /// <summary>
        /// The identifier for the Directory Object.
        /// </summary>
        private const uint GdsId_Directory = 584;

        /// <summary>
        /// The identifier for the Directory_Applications Object.
        /// </summary>
        private const uint GdsId_Directory_Applications = 586;

        /// <summary>
        /// The identifier for the RootDirectoryEntryType_QueryServers Method.
        /// </summary>
        private const uint GdsId_RootDirectoryEntryType_QueryServers = 550;

        /// <summary>
        /// The identifier for the ApplicationElementType ObjectType.
        /// </summary>
        public const uint GdsId_ApplicationElementType = 572;
        #endregion

        #region Public Interface
        /// <summary>
        /// Shows the dialog.
        /// </summary>
        public async Task<ApplicationDescription> ShowDialog(ApplicationConfiguration configuration, bool showSearchPanel)
        {
            List<string> urls = new List<string>();

            foreach (EndpointDescription endpoint in configuration.ClientConfiguration.DiscoveryServers)
            {
                urls.Add(endpoint.EndpointUrl);
            }

            if (urls.Count == 0)
            {
                // TODO find servers with LDS
                urls.Add("opc.tcp://localhost:58800/GlobalDiscoveryServer");
            }

            ServerCTRL.Configuration = configuration;
            ServerCTRL.SetAvailableUrls(urls);

            try
            {
                await ServerCTRL.Connect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }

            OkBTN.Visible = true;
            CancelBTN.Visible = true;

            BrowseCK.Checked = showSearchPanel;
            BrowseCK.Checked = !showSearchPanel;

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_application;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds the results to the control.
        /// </summary>
        private void UpdateResults(ApplicationDescription[] descriptions)
        {
            ServersLV.Items.Clear();

            if (descriptions == null)
            {
                return;
            }

            for (int ii = 0; ii < descriptions.Length; ii++)
            {
                ApplicationDescription description = descriptions[ii];

                if (description == null)
                {
                    continue;
                }

                ListViewItem item = new ListViewItem();
                item.Text = Utils.Format("{0}", description.ApplicationName);
                item.ImageIndex = ClientUtils.GetImageIndex(ServerCTRL.Session, NodeClass.Object, null, false);
                item.SubItems.Add(new ListViewItem.ListViewSubItem());
                item.SubItems.Add(new ListViewItem.ListViewSubItem());
                item.SubItems.Add(new ListViewItem.ListViewSubItem());
                item.Tag = description;
                ServersLV.Items.Add(item);

                item.SubItems[1].Text = description.ApplicationType.ToString();

                if (description.DiscoveryUrls == null)
                {
                    continue;
                }
                
                // collect the domains and protocols.
                List<string> domains = new List<string>();
                List<string> protocols = new List<string>();

                foreach (string discoveryUrl in description.DiscoveryUrls)
                {
                    Uri url = Utils.ParseUri(discoveryUrl);

                    if (url != null)
                    {
                        if (!domains.Contains(url.DnsSafeHost))
                        {
                            domains.Add(url.DnsSafeHost);
                        }

                        if (!protocols.Contains(url.Scheme))
                        {
                            protocols.Add(url.Scheme);
                        }
                    }
                }

                // format the domains.
                StringBuilder buffer = new StringBuilder();

                foreach (string domain in domains)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(domain);
                }

                item.SubItems[2].Text = buffer.ToString();

                // format the protocols.
                buffer = new StringBuilder();

                foreach (string protocol in protocols)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(protocol);
                }

                item.SubItems[3].Text = buffer.ToString();
            }

            // adjust column widths.
            for (int ii = 0; ii < ServersLV.Columns.Count; ii++)
            {
                ServersLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Adds wildcards to the filter.
        /// </summary>
        private string ProcessFilter(ComboBox selection, TextBox filter)
        {
            if (String.IsNullOrEmpty(filter.Text))
            {
                return String.Empty;
            }
                    
            string text = filter.Text;
            Match match = (Match)selection.SelectedItem;

            if (match == Match.Contains || match == Match.StartsWith)
            {
                if (!text.EndsWith("%"))
                {
                    text = text + "%";
                }
            }

            if (match == Match.Contains || match == Match.EndsWith)
            {
                if (!text.StartsWith("%"))
                {
                    text = "%" + text;
                }
            }

            return text;
        }

        /// <summary>
        /// Searches the server for servers.
        /// </summary>
        private void Search()
        {
            Session session = ServerCTRL.Session;

            if (session == null)
            {
                return;
            }

            NodeId elementId = null;            
            ReferenceDescription reference = SystemElementBTN.SelectedReference;

            if (reference != null && !reference.NodeId.IsAbsolute)
            {
                elementId = (NodeId)reference.NodeId;
            }

            ushort namespaceIndex = (ushort)session.NamespaceUris.GetIndex(Namespaces.OpcUaGds);

            IList<object> outputArguments = session.Call(
                new NodeId(GdsId_Directory, namespaceIndex),
                new NodeId(GdsId_RootDirectoryEntryType_QueryServers, namespaceIndex),
                elementId,
                ProcessFilter(ApplicationNameCB, ApplicationNameTB),
                ProcessFilter(MachineNameCB, MachineNameTB),
                ProcessFilter(ApplicationUriCB, ApplicationUriTB),
                ProcessFilter(ProductUriCB, ProductUriTB));

            if (outputArguments != null && outputArguments.Count == 1)
            {
                ExtensionObject[] extensions = outputArguments[0] as ExtensionObject[];
                ApplicationDescription[] descriptions = (ApplicationDescription[])ExtensionObject.ToArray(extensions, typeof(ApplicationDescription));
                UpdateResults(descriptions);
            }
        }

        /// <summary>
        /// Reads the application description from the GDS.
        /// </summary>
        private ApplicationDescription Read(NodeId nodeId)
        {
            NamespaceTable wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.OpcUaGds);

            string[] browsePaths = new string[] 
            {
                "1:ApplicationName",
                "1:ApplicationType",
                "1:ApplicationUri",
                "1:ProductUri",
                "1:GatewayServerUri",
                "1:DiscoveryUrls"
            };

            List<NodeId> propertyIds = ClientUtils.TranslateBrowsePaths(
                ServerCTRL.Session,
                nodeId,
                wellKnownNamespaceUris,
                browsePaths);

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (NodeId propertyId in propertyIds)
            {
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = propertyId;
                nodeToRead.AttributeId = Attributes.Value;
                nodesToRead.Add(nodeToRead);
            }

            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ServerCTRL.Session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            ApplicationDescription application = new ApplicationDescription();

            application.ApplicationName = results[0].GetValue<LocalizedText>(null);
            application.ApplicationType = (ApplicationType)results[1].GetValue<int>((int)ApplicationType.Server);
            application.ApplicationUri = results[2].GetValue<string>(null);
            application.ProductUri = results[3].GetValue<string>(null);
            application.GatewayServerUri = results[4].GetValue<string>(null);

            string[] discoveryUrls = results[5].GetValue<string[]>(null);

            if (discoveryUrls != null)
            {
                application.DiscoveryUrls = new StringCollection(discoveryUrls);
            }

            return application;
        }
        #endregion

        #region Event Handlers
        private void SearchBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Search();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void CloseBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ServerCTRL_ConnectComplete(object sender, EventArgs e)
        {
            try
            {
                Session session = ServerCTRL.Session;

                if (session != null)
                {
                    ushort namespaceIndex = (ushort)session.NamespaceUris.GetIndex(Opc.Ua.Namespaces.OpcUaGds);
                    NodeId rootId = new NodeId(GdsId_Directory_Applications, namespaceIndex);
                    NodeId[] referenceTypeIds = new NodeId[] { Opc.Ua.ReferenceTypeIds.Organizes, Opc.Ua.ReferenceTypeIds.HasChild };

                    BrowseCTRL.Initialize(session, rootId, referenceTypeIds);
                    SystemElementBTN.Session = session;
                    SystemElementBTN.RootId = rootId;
                    SystemElementBTN.ReferenceTypeIds = referenceTypeIds;
                }
                else
                {
                    BrowseCTRL.ChangeSession(session);
                    SystemElementBTN.Session = session;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ServerCTRL_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                Session session = ServerCTRL.Session;
                BrowseCTRL.ChangeSession(session);
                SystemElementBTN.Session = session;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void BrowseCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                SearchPN.Visible = !BrowseCK.Checked;
                SearchBTN.Enabled = !BrowseCK.Checked;
                BrowseCTRL.Visible = BrowseCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ServersLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (ServerCTRL.Session == null)
                {
                    return;
                }

                foreach (ListViewItem item in ServersLV.SelectedItems)
                {
                    m_application = item.Tag as ApplicationDescription;
                    OkBTN.Enabled = m_application.ApplicationType == ApplicationType.Server || m_application.ApplicationType == ApplicationType.ClientAndServer;
                    return;
                }

                m_application = null;
                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void BrowseCTRL_AfterSelect(object sender, EventArgs e)
        {
            try
            {         
                if (ServerCTRL.Session == null)
                {
                    return;
                }

                ushort namespaceIndex = (ushort)ServerCTRL.Session.NamespaceUris.GetIndex(Opc.Ua.Namespaces.OpcUaGds);
                NodeId typeId = new NodeId(GdsId_ApplicationElementType, namespaceIndex);

                ReferenceDescription reference = BrowseCTRL.SelectedNode;

                if (reference != null)
                {
                    if (reference.TypeDefinition == typeId)
                    {
                        m_application = Read((NodeId)reference.NodeId);
                        OkBTN.Enabled = m_application.ApplicationType == ApplicationType.Server || m_application.ApplicationType == ApplicationType.ClientAndServer;
                        return;
                    }
                }

                m_application = null;
                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion

    }
}
