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
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Reflection;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a hierarchy of nodes.
    /// </summary>
    public partial class BrowseTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BrowseTreeCtrl"/> class.
        /// </summary>
        public BrowseTreeCtrl()
        {
            InitializeComponent();

            foreach (BrowseDirection value in Enum.GetValues(typeof(BrowseDirection)))
            {
                BrowseDirectionCTRL.Items.Add(value);
            }
        }

        private NodeId m_rootId;
        private NodeId m_viewId;
        private Session m_session;
        private NodeId m_referenceTypeId;
        private BrowseDirection m_browseDirection;
        private BrowseListCtrl m_referencesCTRL;
        private AttributeListCtrl m_attributesCTRL;
        private event EventHandler<NodesSelectedEventArgs> m_nodesSelected;

        /// <summary>
        /// The control that displays the non-hierarchial references for the selected node.
        /// </summary>
        public BrowseListCtrl ReferencesCTRL
        {
            get { return m_referencesCTRL;  }
            set { m_referencesCTRL = value; }
        }

        /// <summary>
        /// The control that displays the attributes/properties for the selected node.
        /// </summary>
        public AttributeListCtrl AttributesCTRL
        {
            get { return m_attributesCTRL;  }
            set { m_attributesCTRL = value; }
        }

        /// <summary>
        /// Raised when the select menu item is clicked.
        /// </summary>
        public event EventHandler<NodesSelectedEventArgs> NodesSelected
        {
            add { m_nodesSelected += value;  }
            remove { m_nodesSelected -= value;  }
        }

        #region NodesSelectedEventArgs Class
        /// <summary>
        /// Specifies the nodes that where selected in the control.
        /// </summary>
        public class NodesSelectedEventArgs : EventArgs
        {
            /// <summary>
            /// Constructs a new object.
            /// </summary>
            public NodesSelectedEventArgs(IList<ReferenceDescription> nodes)
            {
                m_nodes = nodes;
            }

            /// <summary>
            /// The nodes that where selected.
            /// </summary>
            public IList<ReferenceDescription> Nodes
            {
                get { return m_nodes; }
            }

            private IList<ReferenceDescription> m_nodes;
        }
        #endregion
        
        /// <summary>
        /// Displays the a root in the control.
        /// </summary>
        public void Initialize(
            Session session, 
            NodeId rootId, 
            NodeId viewId,
            NodeId referenceTypeId, 
            BrowseDirection browseDirection)
        {
            m_session = session;
            m_rootId = rootId;
            m_viewId = viewId;
            m_referenceTypeId = referenceTypeId;
            m_browseDirection = browseDirection;

            NodesTV.Nodes.Clear();

            if (m_session == null)
            {
                return;
            }

            if (NodeId.IsNull(m_rootId))
            {
                m_rootId = Objects.RootFolder;
            }

            if (NodeId.IsNull(m_referenceTypeId))
            {
                m_referenceTypeId = ReferenceTypeIds.HierarchicalReferences;
            }

            ReferenceTypeCTRL.Initialize(m_session, ReferenceTypeIds.HierarchicalReferences);
            ReferenceTypeCTRL.SelectedTypeId = m_referenceTypeId;

            ILocalNode root = m_session.NodeCache.Find(m_rootId) as ILocalNode;

            if (root == null)
            {
                return;
            }
            
            ReferenceDescription reference = new ReferenceDescription();

            reference.ReferenceTypeId = referenceTypeId;
            reference.IsForward = true;
            reference.NodeId = root.NodeId;
            reference.NodeClass = root.NodeClass;
            reference.BrowseName = root.BrowseName;
            reference.DisplayName = root.DisplayName;
            reference.TypeDefinition = root.TypeDefinitionId;

            TreeNode rootNode = new TreeNode(reference.ToString());

            rootNode.ImageKey = rootNode.SelectedImageKey = GuiUtils.GetTargetIcon(session, reference);
            rootNode.Tag = reference;
            rootNode.Nodes.Add(new TreeNode());
            
            NodesTV.Nodes.Add(rootNode);
        }

        /// <summary>
        /// Browses the children of the node and updates the tree.
        /// </summary>
        private bool BrowseChildren(TreeNode parent)
        {
            ReferenceDescription reference = parent.Tag as ReferenceDescription;

            if (reference == null)
            {
                return false;
            }

            parent.Nodes.Clear();

            if (reference.NodeId.IsAbsolute)
            {
                return false;
            }

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = (NodeId)reference.NodeId;
            nodeToBrowse.BrowseDirection = m_browseDirection;
            nodeToBrowse.ReferenceTypeId = m_referenceTypeId;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = 0;
            nodeToBrowse.ResultMask = (uint)(int)BrowseResultMask.All;
            
            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
            nodesToBrowse.Add(nodeToBrowse);

            ViewDescription view = null;

            if (NodeId.IsNull(m_viewId))
            {
                view = new ViewDescription();
                view.ViewId = m_viewId;
                view.Timestamp = DateTime.MinValue;
                view.ViewVersion = 0;
            }
        
            BrowseResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Browse(
                null,
                view,
                0,
                nodesToBrowse,
                out results,
                out diagnosticInfos);

            if (results.Count != 1 || StatusCode.IsBad(results[0].StatusCode))
            {
                return false;
            }

            UpdateNode(parent, results[0].References);

            while (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                ByteStringCollection continuationPoints = new ByteStringCollection();
                continuationPoints.Add(results[0].ContinuationPoint);

                m_session.BrowseNext(
                    null,
                    parent == null,
                    continuationPoints,
                    out results,
                    out diagnosticInfos);

                if (results.Count != 1 || StatusCode.IsBad(results[0].StatusCode))
                {
                    return false;
                }
            
                UpdateNode(parent, results[0].References);
            }

            return true;
        }

        /// <summary>
        /// Adds the browse results to the node (if not null). 
        /// </summary>
        private void UpdateNode(TreeNode parent, ReferenceDescriptionCollection references)
        { 
            try
            {
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];

                    TreeNode childNode = new TreeNode(reference.ToString());

                    childNode.ImageKey = childNode.SelectedImageKey = GuiUtils.GetTargetIcon(m_session, reference);
                    childNode.Tag = reference;
                                        
                    childNode.Nodes.Add(new TreeNode());
                    
                    parent.Nodes.Add(childNode);
                }
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        
        #region Overridden Members
        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override void SelectNode()
        {
            base.SelectNode();
            
            ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

            if (reference == null)
            {
                return;
            }

            // update attributes control.
            if (AttributesCTRL != null)
            {
                AttributesCTRL.Initialize(m_session, reference.NodeId);
            }

            // update references control.
            if (ReferencesCTRL != null)
            {
                ReferencesCTRL.Initialize(m_session, reference.NodeId);
            }
        }

        /// <see cref="BaseTreeCtrl.BeforeExpand" />
        protected override bool BeforeExpand(TreeNode clickedNode)
        {
            try
            {
                // check if a placeholder child is present.
                if (clickedNode.Nodes.Count == 1 && clickedNode.Nodes[0].Text == String.Empty)
                {
                    // browse.
                    return !BrowseChildren(clickedNode);
                }

                // do not cancel expand.
                return false;
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
                return false;
            }
        }

        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(TreeNode clickedNode)
        {
            if (NodesTV.SelectedNode == null)
            {
                return;
            }

            SelectMI.Enabled = true;

            if (NodesTV.SelectedNode.Nodes.Count > 0 && NodesTV.SelectedNode.Nodes[0].Text != String.Empty)
            {
                SelectChildrenMI.Enabled = true;
            }
        }
        #endregion

        #region Event Handlers
        private void RootBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                Initialize(m_session, (NodeId)reference.NodeId, m_viewId, m_referenceTypeId, m_browseDirection);
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseDirectionCTRL_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                m_browseDirection = (BrowseDirection)BrowseDirectionCTRL.SelectedItem;

                if (NodesTV.SelectedNode != null)
                {
                    NodesTV.SelectedNode.Collapse();
                    NodesTV.SelectedNode.Nodes.Clear();
                    NodesTV.SelectedNode.Nodes.Add(new TreeNode());
                    NodesTV.SelectedNode.Expand();
                    return;
                }
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_nodesSelected != null)
                {
                    if (NodesTV.SelectedNode == null)
                    {
                        return;
                    }

                    ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                    if (reference != null)
                    {
                        ReferenceDescriptionCollection collection = new ReferenceDescriptionCollection();
                        collection.Add(reference);
                        m_nodesSelected(this, new NodesSelectedEventArgs(collection));
                    }
                }        
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectChildrenMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_nodesSelected != null)
                {
                    if (NodesTV.SelectedNode == null || NodesTV.SelectedNode.Nodes.Count == 0)
                    {
                        return;
                    }

                    ReferenceDescriptionCollection collection = new ReferenceDescriptionCollection();

                    foreach (TreeNode child in NodesTV.SelectedNode.Nodes)
                    {
                        ReferenceDescription reference = child.Tag as ReferenceDescription;

                        if (reference != null)
                        {
                            collection.Add(reference);
                        }
                    }

                    if (collection.Count > 0)
                    {                    
                        m_nodesSelected(this, new NodesSelectedEventArgs(collection));
                    }
                }   
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ReferenceTypeCTRL_ReferenceSelectionChanged(object sender, ReferenceTypeCtrl.ReferenceSelectedEventArgs e)
        {
            try
            {
                m_referenceTypeId = e.ReferenceTypeId;

                if (NodesTV.SelectedNode != null)
                {
                    NodesTV.SelectedNode.Collapse();
                    NodesTV.SelectedNode.Nodes.Clear();
                    NodesTV.SelectedNode.Nodes.Add(new TreeNode());
                    NodesTV.SelectedNode.Expand();
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
