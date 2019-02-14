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
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class NodeListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public NodeListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private NodeIdCollection m_nodeIds;
        private NodeClass m_nodeClassMask;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Name",   HorizontalAlignment.Left, null },  
			new object[] { "NodeId", HorizontalAlignment.Left, null },  
			new object[] { "Class",  HorizontalAlignment.Left, null }
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Sets the nodes in the control.
        /// </summary>
        public void Initialize(Session session, NodeIdCollection nodeIds, NodeClass nodeClassMask)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            Clear();
            
            m_session       = session;
            m_nodeIds       = nodeIds;
            m_nodeClassMask = (nodeClassMask == 0)?(NodeClass)Byte.MaxValue:nodeClassMask;

            if (nodeIds == null)
            {
                return;                
            }

            foreach (NodeId nodeId in nodeIds)
            {
                INode node = m_session.NodeCache.Find(nodeId);

                if (node != null && (m_nodeClassMask & node.NodeClass) != 0)
                {
                    AddItem(node, "Property", -1);
                }
            }

            AdjustColumns();
        }
        
        /// <summary>
        /// Adds a node to the list.
        /// </summary>
        public void AddNodeId(ReferenceDescription reference)
        {
            if (reference != null)
            {
                AddNodeId(reference.NodeId);
                AdjustColumns();
            }
        }

        /// <summary>
        /// Adds a node to the list.
        /// </summary>
        public void AddNodeId(ExpandedNodeId nodeId)
        {
            Node node = m_session.NodeCache.Find(nodeId) as Node;

            if (node == null)
            {
                return;
            }

            if ((node.NodeClass & m_nodeClassMask) != 0)
            {
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    Node target = listItem.Tag as Node;

                    if (target != null)
                    {
                        if (target.NodeId == node.NodeId)
                        {
                            UpdateItem(listItem, node);
                            return;
                        }
                    }
                }
            
                AddItem(node, "Property", -1);
                return;
            }

            if (node.NodeClass == NodeClass.ObjectType || node.NodeClass == NodeClass.VariableType)
            {
                ExpandedNodeId supertypeId = node.FindTarget(ReferenceTypeIds.HasSubtype, true, 0);

                if (supertypeId != null)
                {
                    AddNodeId(supertypeId);
                }
            }
            
            IList<IReference> properties = node.ReferenceTable.Find(ReferenceTypeIds.HasProperty, false, true, m_session.TypeTree);

            for (int ii = 0; ii < properties.Count; ii++)
            {
                AddNodeId(properties[ii].TargetId);
            }
        }

        /// <summary>
        /// Returns the node ids in the control.
        /// </summary>
        public NodeIdCollection GetNodeIds()
        {
            NodeIdCollection nodeIds = new NodeIdCollection();

            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                Node node = listItem.Tag as Node;

                if (node != null)
                {
                    nodeIds.Add(node.NodeId);
                }
            }

            return nodeIds;
        }
		#endregion
        
        #region Private Methods
		#endregion
        
        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{               
            ViewMI.Enabled   = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			Node node = item as Node;

			if (node == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

            listItem.SubItems[0].Text = String.Format("{0}", node);
		    listItem.SubItems[1].Text = String.Format("{0}", node.NodeId);
		    listItem.SubItems[2].Text = String.Format("{0}", (NodeClass)node.NodeClass);

			listItem.Tag = item;
        }
        #endregion
        
        #region Event Handlers
        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                Node[] nodes = GetSelectedItems(typeof(Node)) as Node[];

                if (nodes == null || nodes.Length == 1)
                {
                    new NodeAttributesDlg().ShowDialog(m_session, nodes[0].NodeId);
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        protected override void ItemsLV_DragDrop(object sender, DragEventArgs e)
        {            
            try
            {
                ReferenceDescription reference = e.Data.GetData(typeof(ReferenceDescription)) as ReferenceDescription;

                if (reference != null)
                {
                    AddNodeId(reference);
                }

                ReferenceDescriptionCollection references = e.Data.GetData(typeof(ReferenceDescriptionCollection)) as ReferenceDescriptionCollection;

                if (references != null)
                {
                    foreach (ReferenceDescription current in references)
                    {
                        AddNodeId(current);
                    }
                }

                ReadValueIdCollection valueIds = e.Data.GetData(typeof(ReadValueIdCollection)) as ReadValueIdCollection;

                if (valueIds != null)
                {
                    foreach (ReadValueId valueId in valueIds)
                    {
                        AddItem(valueId.NodeId);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                List<ListViewItem> items = new List<ListViewItem>();

                foreach (ListViewItem item in ItemsLV.SelectedItems)
                {
                    items.Add(item);
                }

                foreach (ListViewItem item in items)
                {
                    item.Remove();
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
