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
using System.Reflection;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of nodes.
    /// </summary>
    public partial class NodeListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeListCtrl"/> class.
        /// </summary>
        public NodeListCtrl()
        {
            InitializeComponent();
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private BrowseListCtrl m_referencesCTRL;
        private AttributeListCtrl m_attributesCTRL;
       
		// The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Name",  HorizontalAlignment.Left, null },  
			new object[] { "ID",    HorizontalAlignment.Left,   null }
		};
		#endregion

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
        /// Initializes the control with a set of items.
        /// </summary>
        public void Initialize(Session session, IList<NodeId> nodeIds)
        {
            ItemsLV.Items.Clear();
            m_session = session;

            if (m_session == null || nodeIds == null || nodeIds.Count == 0)
            {
                AdjustColumns();
                return;
            }

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                ILocalNode node = m_session.NodeCache.Find(nodeIds[ii]) as ILocalNode;

                if (node == null)
                {
                    continue;
                }

                AddItem(node);
            }

            AdjustColumns();
        }
        
        /// <summary>
        /// Adds a node to the control.
        /// </summary>
        public void Add(NodeId nodeId)
        {
            ILocalNode node = m_session.NodeCache.Find(nodeId) as ILocalNode;

            if (node != null)
            {
                AddItem(node);
                AdjustColumns();
            }
        }

        /// <summary>
        /// Returns the list of nodes in the control.
        /// </summary>
        public IList<ILocalNode> GetNodeList()
        {
            List<ILocalNode> items = new List<ILocalNode>(ItemsLV.Items.Count);
            
            for (int ii  = 0; ii < ItemsLV.Items.Count; ii++)
            {
                items.Add(ItemsLV.Items[ii].Tag as ILocalNode);
            }

            return items;
        }

        #region Overridden Methods
        /// <see cref="BaseListCtrl.SelectItems" />
        protected override void SelectItems()
        {
            base.SelectItems();

            ILocalNode node = GetSelectedTag(0) as ILocalNode;

            if (node == null)
            {
                return;
            }

            // update attributes control.
            if (AttributesCTRL != null)
            {
                AttributesCTRL.Initialize(m_session, node.NodeId);
            }

            // update references control.
            if (ReferencesCTRL != null)
            {
                ReferencesCTRL.Initialize(m_session, node.NodeId);
            }
        }

        /// <see cref="Opc.Ua.Client.Controls.BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
		}

        /// <see cref="Opc.Ua.Client.Controls.BaseListCtrl.UpdateItem(ListViewItem,object)" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ILocalNode node = item as ILocalNode;

			if (node == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

			listItem.SubItems[0].Text = Utils.Format("{0}", node.DisplayName);
			listItem.SubItems[1].Text = Utils.Format("{0}", node.NodeId);
            
            listItem.ImageKey = GuiUtils.GetTargetIcon(m_session, node.NodeClass, node.TypeDefinitionId);
			listItem.Tag = item;
        }

        /// <summary>
        /// Handles a drop event.
        /// </summary>
        protected override void ItemsLV_DragDrop(object sender, DragEventArgs e)
        {            
            try
            {
                ReferenceDescription reference = e.Data.GetData(typeof(ReferenceDescription)) as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                ILocalNode node = m_session.NodeCache.Find(reference.NodeId) as ILocalNode;

                if (node == null)
                {
                    return;
                }

                AddItem(node);

                AdjustColumns();                    
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                List<ListViewItem> items = new List<ListViewItem>(ItemsLV.SelectedItems.Count);
                
                for (int ii  = 0; ii < ItemsLV.SelectedItems.Count; ii++)
                {
                    items.Add(ItemsLV.SelectedItems[ii]);
                }

                for (int ii  = 0; ii < items.Count; ii++)
                {
                    items[ii].Remove();
                }

                AdjustColumns();
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
