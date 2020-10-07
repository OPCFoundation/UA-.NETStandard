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
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays browse tree.
    /// </summary>
    public partial class BrowseTreeViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public BrowseTreeViewCtrl()
        {
            InitializeComponent();
            BrowseTV.ImageList = new ClientUtils().ImageList;
            m_typeImageMapping = new Dictionary<NodeId, int>();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_rootId;
        private NodeId[] m_referenceTypeIds;
        private NodeId m_selectedNodeId;
        private event EventHandler m_AfterSelect;
        private ViewDescription m_view;
        private Dictionary<NodeId, int> m_typeImageMapping;
        #endregion

        #region Public Interface
        /// <summary>
        /// The view to use.
        /// </summary>
        public AttributesListViewCtrl AttributesControl { get; set; }

        /// <summary>
        /// Initializes the control with a root and a set of hierarchial reference types to follow. 
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="rootId">The root of the hierarchy to browse.</param>
        /// <param name="referenceTypeIds">The reference types to follow.</param>
        public void Initialize(
            Session session,
            NodeId rootId,
            params NodeId[] referenceTypeIds)
        {
            // set default root.
            if (NodeId.IsNull(rootId))
            {
                rootId = Opc.Ua.ObjectIds.ObjectsFolder;
            }

            // set default reference type.
            if (referenceTypeIds == null)
            {
                referenceTypeIds = new NodeId[] { Opc.Ua.ReferenceTypeIds.HierarchicalReferences };
            }

            m_rootId = rootId;
            m_referenceTypeIds = referenceTypeIds;

            // save session.
            ChangeSession(session, true);
        }

        /// <summary>
        /// Selects a node in the control.
        /// </summary>
        public bool SelectNode(NodeId nodeId)
        {
            return SelectNode(BrowseTV.Nodes, nodeId);
        }

        /// <summary>
        /// Changes the session used by the control.
        /// </summary>
        /// <param name="session">The session.</param>
        public void ChangeSession(Session session)
        {
            if (Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            ChangeSession(session, false);
        }

        /// <summary>
        /// The view to use.
        /// </summary>
        public ViewDescription View 
        { 
            get
            {
                return m_view;
            }

            set
            {
                if (AttributesControl != null)
                {
                    AttributesControl.View = value;
                }

                m_view = value;
            }
        }

        /// <summary>
        /// Gets or sets the context menu for the browse tree.
        /// </summary>
        public ContextMenuStrip BrowseMenuStrip
        {
            get { return BrowseTV.ContextMenuStrip; }
            set { BrowseTV.ContextMenuStrip = value; }
        }

        /// <summary>
        /// The reference for the currently selected node.
        /// </summary>
        public ReferenceDescription SelectedNode
        {
            get
            {
                if (BrowseTV.SelectedNode == null)
                {
                    return null;
                }

                return BrowseTV.SelectedNode.Tag as ReferenceDescription;
            }
        }

        /// <summary>
        /// The reference for the parent of the currently selected node.
        /// </summary>
        public ReferenceDescription SelectedParent
        {
            get
            {
                if (BrowseTV.SelectedNode == null || BrowseTV.SelectedNode.Parent == null)
                {
                    return null;
                }

                return BrowseTV.SelectedNode.Parent.Tag as ReferenceDescription;
            }
        }

        /// <summary>
        /// Returns the child node at the specified index.
        /// </summary>
        public ReferenceDescription GetChildOfSelectedNode(int index)
        {
            if (BrowseTV.SelectedNode == null)
            {
                return null;
            }

            if (BrowseTV.SelectedNode.Nodes.Count == 1 && BrowseTV.SelectedNode.Nodes[0].Text == String.Empty)
            {
                BrowseTV.SelectedNode.Expand();
            }

            if (index < 0 || index >= BrowseTV.SelectedNode.Nodes.Count)
            {
                return null;
            }

            return BrowseTV.SelectedNode.Nodes[index].Tag as ReferenceDescription;
        }

        /// <summary>
        /// The reference for the parent of the currently selected node.
        /// </summary>
        public void RefreshSelection()
        {
            if (BrowseTV.SelectedNode == null || BrowseTV.SelectedNode.Parent == null)
            {
                return;
            }

            BrowseTV.SelectedNode.Collapse();
            BrowseTV.SelectedNode.Nodes.Clear();
            BrowseTV.SelectedNode.Nodes.Add(new TreeNode());
            BrowseTV.SelectedNode.Expand();
        }

        /// <summary>
        /// Raised after a node is selected in the control.
        /// </summary>
        public event EventHandler AfterSelect { add { m_AfterSelect += value; } remove { m_AfterSelect -= value; } }
        #endregion

        #region Private Methods
        /// <summary>
        /// Recursively finds and selects a node in the control.
        /// </summary>
        private bool SelectNode(TreeNodeCollection nodes, NodeId nodeId)
        {
            foreach (TreeNode node in nodes)
            {
                ReferenceDescription reference = node.Tag as ReferenceDescription;

                if (reference != null)
                {
                    if (reference.NodeId == nodeId)
                    {
                        BrowseTV.SelectedNode = node;
                        node.EnsureVisible();
                        node.Checked = true;
                        return true;
                    }
                }

                SelectNode(node.Nodes, nodeId);
            }

            return false;
        }

        /// <summary>
        /// Changes the session used by the control.
        /// </summary>
        private void ChangeSession(Session session, bool refresh)
        {
            m_session = session;

            if (AttributesControl != null)
            {
                AttributesControl.ChangeSession(session);
            }

            BrowseTV.Nodes.Clear();

            if (m_session != null)
            {
                INode node = m_session.NodeCache.Find(m_rootId);

                if (node != null)
                {
                    TreeNode root = new TreeNode(node.ToString());
                    root.ImageIndex = ClientUtils.GetImageIndex(m_session, node.NodeClass, node.TypeDefinitionId, false);
                    root.SelectedImageIndex = ClientUtils.GetImageIndex(m_session, node.NodeClass, node.TypeDefinitionId, true);

                    ReferenceDescription reference = new ReferenceDescription();
                    reference.NodeId = node.NodeId;
                    reference.NodeClass = node.NodeClass;
                    reference.BrowseName = node.BrowseName;
                    reference.DisplayName = node.DisplayName;
                    reference.TypeDefinition = node.TypeDefinitionId;
                    root.Tag = reference;

                    root.Nodes.Add(new TreeNode());
                    BrowseTV.Nodes.Add(root);
                    root.Expand();
                    BrowseTV.SelectedNode = root;
                }
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the DoubleClick event of the BrowseTV control.
        /// </summary>
        private void BrowseTV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (BrowseTV.SelectedNode == null)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the AfterSelect event of the BrowseTV control.
        /// </summary>
        private void BrowseTV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                m_selectedNodeId = null;

                if (BrowseTV.SelectedNode == null)
                {
                    if (m_AfterSelect != null) m_AfterSelect(this, new EventArgs());
                    return;
                }

                // get node to browse.
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;
                NodeId nodeId = m_rootId;

                if (reference != null)
                {
                    nodeId = (NodeId)reference.NodeId;
                }

                m_selectedNodeId = nodeId;

                if (AttributesControl != null)
                {
                    AttributesControl.ReadAttributes(m_selectedNodeId, true);
                }

                // raise event.
                if (m_AfterSelect != null) m_AfterSelect(this, new EventArgs());
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the BeforeExpand event of the BrowseTV control.
        /// </summary>
        private void BrowseTV_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;
                e.Node.Nodes.Clear();

                // build list of references to browse.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

                for (int ii = 0; ii < m_referenceTypeIds.Length; ii++)
                {
                    BrowseDescription nodeToBrowse = new BrowseDescription();

                    nodeToBrowse.NodeId = m_rootId;
                    nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                    nodeToBrowse.ReferenceTypeId = m_referenceTypeIds[ii];
                    nodeToBrowse.IncludeSubtypes = true;
                    nodeToBrowse.NodeClassMask = 0;
                    nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                    if (reference != null)
                    {
                        nodeToBrowse.NodeId = (NodeId)reference.NodeId;
                    }

                    nodesToBrowse.Add(nodeToBrowse);
                }

                // add the childen to the control.
                SortedDictionary<ExpandedNodeId, TreeNode> dictionary = new SortedDictionary<ExpandedNodeId, TreeNode>();

                ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, View, nodesToBrowse, false);

                for (int ii = 0; references != null && ii < references.Count; ii++)
                {
                    reference = references[ii];

                    // ignore out of server references.
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    if (dictionary.ContainsKey(reference.NodeId))
                    {
                        continue;
                    }

                    TreeNode child = new TreeNode(reference.ToString());

                    child.Nodes.Add(new TreeNode());
                    child.Tag = reference;

                    if (!reference.TypeDefinition.IsAbsolute)
                    {
                        try
                        {
                            if (!m_typeImageMapping.ContainsKey((NodeId)reference.TypeDefinition))
                            {
                                List<NodeId> nodeIds = ClientUtils.TranslateBrowsePaths(m_session, (NodeId)reference.TypeDefinition, m_session.NamespaceUris, Opc.Ua.BrowseNames.Icon);

                                if (nodeIds.Count > 0 && nodeIds[0] != null)
                                {
                                    DataValue value = m_session.ReadValue(nodeIds[0]);
                                    byte[] bytes = value.Value as byte[];

                                    if (bytes != null)
                                    {
                                        System.IO.MemoryStream istrm = new System.IO.MemoryStream(bytes);
                                        Image icon = Image.FromStream(istrm);
                                        BrowseTV.ImageList.Images.Add(icon);
                                        m_typeImageMapping[(NodeId)reference.TypeDefinition] = BrowseTV.ImageList.Images.Count - 1;
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            Utils.Trace(exception, "Error loading image.");
                        }
                    }

                    int index = 0;

                    if (!m_typeImageMapping.TryGetValue((NodeId)reference.TypeDefinition, out index))
                    {
                        child.ImageIndex = ClientUtils.GetImageIndex(m_session, reference.NodeClass, reference.TypeDefinition, false);
                        child.SelectedImageIndex = ClientUtils.GetImageIndex(m_session, reference.NodeClass, reference.TypeDefinition, true);
                    }
                    else
                    {
                        child.ImageIndex = index;
                        child.SelectedImageIndex = index;
                    }

                    dictionary[reference.NodeId] = child;
                }

                // add nodes to tree.
                foreach (TreeNode node in dictionary.Values.OrderBy(i => i.Text))
                {
                    e.Node.Nodes.Add(node);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void BrowseTV_MouseDown(object sender, MouseEventArgs e)
        {
            try
            {
                BrowseTV.SelectedNode = BrowseTV.GetNodeAt(e.X, e.Y);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void Browse_RefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                RefreshSelection();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
