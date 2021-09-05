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
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the use to select a store from a list stores on a machine.
    /// </summary>
    public partial class CertificateStoreTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the control.
        /// </summary>
        public CertificateStoreTreeCtrl()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private CertificateListCtrl m_certificateListCtrl;

        private enum ContainerInfoType
        {
            Root,
            TopLevelStore,
            Service,
            User,
            Store            
        }

        private class ContainerInfo
        {
            public ContainerInfoType Type;
            public string DisplayName;
            public CertificateStoreIdentifier Store;

            public ContainerInfo(ContainerInfoType type, string displayName)
            {
                Type = type;
                DisplayName = displayName;
            }

            public ContainerInfo(ContainerInfoType type, CertificateStoreIdentifier store)
            {
                Type = type;
                DisplayName = store.ToString();
                Store = store;
            }

            /// <summary>
            /// Returns the display string for the object.
            /// </summary>
            public override string ToString()
            {
                return DisplayName;
            }

            /// <summary>
            /// Returns a store for the container.
            /// </summary>
            public CertificateStoreIdentifier GetCertificateStore()
            {
                if (this.Store != null)
                {
                    return this.Store;
                }

                return null;
            }
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Returns the currently selected store.
        /// </summary>
        public CertificateStoreIdentifier SelectedStore
        {
            get
            {
                ContainerInfo info = SelectedTag as ContainerInfo;

                if (info == null)
                {
                    return null;
                }

                return info.GetCertificateStore();
            }
        }

        /// <summary>
        /// A control that can be used to display the contents of a certificate store.
        /// </summary>
        public CertificateListCtrl CertificateListCtrl
        {
            get { return m_certificateListCtrl; }
            set { m_certificateListCtrl = value; }
        }

        /// <summary>
        /// Provides the configuration to use when displaying the control.
        /// </summary>
        public void Initialize()
        {
            NodesTV.Nodes.Clear();
            TreeNode node = AddNode(null, new ContainerInfo(ContainerInfoType.Root, System.Net.Dns.GetHostName()));
            node.Nodes.Add(new TreeNode());
            node.Expand();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Updates the controls after a node is selected.
        /// </summary>
        protected override void SelectNode()
        {
            base.SelectNode();

            if (m_certificateListCtrl != null)
            {
                m_certificateListCtrl.Initialize(SelectedStore, null).Wait();
            }
        }

        /// <summary>
        /// Fetches the children before expanding a node.
        /// </summary>
        protected override bool BeforeExpand(TreeNode clickedNode)
        {
            if (clickedNode == null)
            {
                return false;
            }

            // check for a dummy placeholder node.
            if (clickedNode.Nodes.Count == 1 && clickedNode.Nodes[0].Tag == null)
            {
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    FetchChildren(clickedNode);
                }
                finally
                {
                    this.Cursor = Cursors.Default;
                }
            }

            return base.BeforeExpand(clickedNode);
        }

        /// <summary>
        /// Enables the menu items.
        /// </summary>
        protected override void EnableMenuItems(TreeNode clickedNode)
        {
            base.EnableMenuItems(clickedNode);

            ContainerInfo info = clickedNode.Tag as ContainerInfo;

            if (info != null)
            {
                CopyMI.Enabled = true;

                if (info.Type == ContainerInfoType.Store)
                {
                    IDataObject clipboardData = Clipboard.GetDataObject();

                    if (clipboardData.GetDataPresent(DataFormats.Text))
                    {
                        PasteMI.Enabled = true;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the properties of a node in the view.
        /// </summary>
        protected override void UpdateNode(TreeNode treeNode, object item, string text, string icon)
        {
            base.UpdateNode(treeNode, item, text, icon);

            ContainerInfo info = item as ContainerInfo;

            if (info != null)
            {
                SetIcon(treeNode, info);
            }
        }

        /// <summary>
        /// Returns the data to drag.
        /// </summary>
        protected override object GetDataToDrag(TreeNode node)
        {
            ContainerInfo info = node.Tag as ContainerInfo;

            if (info == null)
            {
                return null;
            }

            return info.GetCertificateStore();
        }

        /// <summary>
        /// Handles a drag enter event.
        /// </summary>
        protected override void NodesTV_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        /// <summary>
        /// Handles a drag over event.
        /// </summary>
        protected override void NodesTV_DragOver(object sender, DragEventArgs e)
        {
            TreeNode node = NodesTV.GetNodeAt(PointToClient(new Point(e.X, e.Y)));

            if (node == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Copy;
        }

        /// <summary>
        /// Handles a drop event.
        /// </summary>
        protected override void NodesTV_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                TreeNode node = NodesTV.GetNodeAt(PointToClient(new Point(e.X, e.Y)));

                if (node == null)
                {
                    return;
                }

                ContainerInfo info = node.Tag as ContainerInfo;

                if (info == null)
                {
                    return;
                }

                if (info.Type == ContainerInfoType.Store)
                {
                    CertificateStoreIdentifier id = info.GetCertificateStore();

                    if (id == null)
                    {
                        return;
                    }

                    object[] certificates = e.Data.GetData(typeof(object[])) as object[];

                    if (certificates == null)
                    {
                        return;
                    }

                    using (ICertificateStore store = id.OpenStore())
                    {
                        for (int ii = 0; ii < certificates.Length; ii++)
                        {
                            X509Certificate2 certificate = certificates[ii] as X509Certificate2;

                            if (certificate != null)
                            {
                                store.Add(certificate);
                            }
                        }
                    }

                    NodesTV.SelectedNode = node;
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the children of a node.
        /// </summary>
        private void FetchChildren(TreeNode parent)
        {
            // get rid of existing children.
            parent.Nodes.Clear();

            // check for a valid node.
            ContainerInfo info = parent.Tag as ContainerInfo;
            
            if (info == null)
            {
                return;
            }

            // add a dummy child to show the + sign.
            foreach (TreeNode child in parent.Nodes)
            {
                child.Nodes.Add(new TreeNode());
            }
        }
        
        /// <summary>
        /// Sets the icon for the tree node.
        /// </summary>
        private void SetIcon(TreeNode treeNode, ContainerInfo info)
        {
            switch (info.Type)
            {
                case ContainerInfoType.Root:
                {
                    treeNode.ImageKey = GuiUtils.Icons.Desktop;
                    treeNode.SelectedImageKey = GuiUtils.Icons.Desktop;
                    break;
                }      

                case ContainerInfoType.Service:
                {
                    treeNode.ImageKey = GuiUtils.Icons.Service;
                    treeNode.SelectedImageKey = GuiUtils.Icons.Service;
                    break;
                }

                case ContainerInfoType.User:
                {
                    treeNode.ImageKey = GuiUtils.Icons.SingleUser;
                    treeNode.SelectedImageKey = GuiUtils.Icons.SingleUser;
                    break;
                }

                case ContainerInfoType.Store:
                {
                    treeNode.ImageKey = GuiUtils.Icons.CertificateStore;
                    treeNode.SelectedImageKey = GuiUtils.Icons.CertificateStore;
                    break;
                }

                case ContainerInfoType.TopLevelStore:
                {
                    treeNode.ImageKey = GuiUtils.Icons.CertificateStore;
                    treeNode.SelectedImageKey = GuiUtils.Icons.CertificateStore;
                    break;
                }

                default:
                {
                    treeNode.ImageKey = GuiUtils.Icons.Folder;
                    treeNode.SelectedImageKey = GuiUtils.Icons.SelectedFolder;
                    break;
                }
            }
        }
        #endregion

        #region Event Handler
        private void CopyMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeNode node = NodesTV.SelectedNode;

                // check if valid store selected.
                ContainerInfo info = node.Tag as ContainerInfo;

                if (info == null)
                {
                    return;
                }

                if (info.Type != ContainerInfoType.Store || node.Parent == null)
                {
                    return;
                }

                CertificateStoreIdentifier store = info.GetCertificateStore();

                StringBuilder builder = new StringBuilder();
                XmlWriter writer = XmlWriter.Create(builder);

                try
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(CertificateStoreIdentifier));
                    serializer.WriteObject(writer, store);
                }
                finally
                {
                    writer.Close();
                }

                ClipboardHack.SetData(DataFormats.Text, builder.ToString());
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void PasteMI_Click(object sender, EventArgs e)
        {
            try
            {
                string xml = (string)ClipboardHack.GetData(DataFormats.Text);

                if (String.IsNullOrEmpty(xml))
                {
                    return;
                }

                // check if in the favorites list.
                ContainerInfo info = NodesTV.SelectedNode.Tag as ContainerInfo;

                // check if pasting into a store.
                if (info.Type == ContainerInfoType.Store)
                {
                    CertificateIdentifier id = null;

                    using (XmlTextReader reader = new XmlTextReader(new StringReader(xml)))
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(CertificateIdentifier));
                        id = (CertificateIdentifier)serializer.ReadObject(reader, false);
                    }

                    if (id.Certificate != null)
                    {
                        CertificateStoreIdentifier storeId = info.GetCertificateStore();

                        using (ICertificateStore store = storeId.OpenStore())
                        {
                            store.Add(id.Certificate);
                        }
                    }

                    SelectNode();
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
