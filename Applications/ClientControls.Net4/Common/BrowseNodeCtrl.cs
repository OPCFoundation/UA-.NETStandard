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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays browse tree.
    /// </summary>
    public partial class BrowseNodeCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public BrowseNodeCtrl()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_rootId;
        private NodeId[] m_referenceTypeIds;
        private NodeId m_selectedNodeId;
        private event EventHandler m_AfterSelect;
        #endregion

        #region Public Interface
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
            ChangeSession(session);
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

            m_session = session;
            BrowseTV.Nodes.Clear();
            AttributesLV.Items.Clear();

            if (m_session != null)
            {
                TreeNode root = new TreeNode(session.NodeCache.GetDisplayText(m_rootId));
                root.Nodes.Add(new TreeNode());
                BrowseTV.Nodes.Add(root);
                root.Expand();
                BrowseTV.SelectedNode = root;
            }
        }

        /// <summary>
        /// The view to use.
        /// </summary>
        public ViewDescription View { get; set; }

        /// <summary>
        /// Gets or sets the default position of the splitter
        /// </summary>
        public int SplitterDistance
        {
            get { return MainPN.SplitterDistance; }
            set { MainPN.SplitterDistance = value; }
        }

        /// <summary>
        /// Gets or sets a flag that indicates whether the attributes should be displayed.
        /// </summary>
        public bool AttributesListCollapsed 
        {
            get { return MainPN.Panel2Collapsed;  }
            set { MainPN.Panel2Collapsed = value; }  
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
        /// Reads the attributes for the node.
        /// </summary>
        private void ReadAttributes(NodeId nodeId)
        {
            // build list of attributes to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = attributeId;
                nodesToRead.Add(nodeToRead);
            }

            // read the attributes.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                // check for error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    if (results[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        continue;
                    }
                }

                // add the metadata for the attribute.
                uint attributeId = nodesToRead[ii].AttributeId;
                ListViewItem item = new ListViewItem(Attributes.GetBrowseName(attributeId));
                item.SubItems.Add(Attributes.GetBuiltInType(attributeId).ToString());

                if (Attributes.GetValueRank(attributeId) >= 0)
                {
                    item.SubItems[0].Text += "[]";
                }

                // add the value.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    item.SubItems.Add(results[ii].StatusCode.ToString());
                }
                else
                {
                    item.SubItems.Add(ClientUtils.GetAttributeDisplayText(m_session, attributeId, results[ii].WrappedValue));
                }

                item.Tag = results[ii];

                // display in list.
                AttributesLV.Items.Add(item);
            }

            // set the column widths.
            for (int ii = 0; ii < AttributesLV.Columns.Count; ii++)
            {
                AttributesLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Reads the properties for the node.
        /// </summary>
        private void ReadProperties(NodeId nodeId)
        {
            // build list of references to browse.
            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)NodeClass.Variable;
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            nodesToBrowse.Add(nodeToBrowse);

            // find properties.
            ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, View, nodesToBrowse, false);

            // build list of properties to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; references != null && ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                // ignore out of server references.
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = (NodeId)reference.NodeId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.Handle = reference;
                nodesToRead.Add(nodeToRead);
            }

            if (nodesToRead.Count == 0)
            {
                return;
            }
            
            // read the properties.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                ReferenceDescription reference = (ReferenceDescription)nodesToRead[ii].Handle;

                TypeInfo typeInfo = TypeInfo.Construct(results[ii].Value);

                // add the metadata for the attribute.
                ListViewItem item = new ListViewItem(reference.ToString());
                item.SubItems.Add(typeInfo.BuiltInType.ToString());

                if (typeInfo.ValueRank >= 0)
                {
                    item.SubItems[1].Text += "[]";
                }

                // add the value.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    item.SubItems.Add(results[ii].StatusCode.ToString());
                }
                else
                {
                    item.SubItems.Add(results[ii].WrappedValue.ToString());
                }

                item.Tag = results[ii];

                // display in list.
                AttributesLV.Items.Add(item);
            }

            // set the column widths.
            for (int ii = 0; ii < AttributesLV.Columns.Count; ii++)
            {
                AttributesLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Handles the AfterSelect event of the BrowseTV control.
        /// </summary>
        private void BrowseTV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                AttributesLV.Items.Clear();
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

                if (!MainPN.Panel2Collapsed)
                {
                    ReadAttributes(m_selectedNodeId);
                    ReadProperties(m_selectedNodeId);
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
                ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, View, nodesToBrowse, false);

                for (int ii = 0; references != null && ii < references.Count; ii++)
                {
                    reference = references[ii];

                    // ignore out of server references.
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    TreeNode child = new TreeNode(reference.ToString());
                    child.Nodes.Add(new TreeNode());
                    child.Tag = reference;

                    e.Node.Nodes.Add(child);
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

        private void AttributesLV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (AttributesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                DataValue value = AttributesLV.SelectedItems[0].Tag as DataValue;

                if (value == null)
                {
                    return;
                }

                Array array = value.Value as Array;

                if (array != null)
                {
                    new EditArrayDlg().ShowDialog(array, BuiltInType.Null, true, "View Atttribute Value");
                }
                else
                {
                    new EditDataValueDlg().ShowDialog(value.WrappedValue, "View Atttribute Value");
                }
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
