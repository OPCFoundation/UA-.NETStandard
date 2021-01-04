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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of references for a node.
    /// </summary>
    public partial class BrowseListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BrowseListCtrl"/> class.
        /// </summary>
        public BrowseListCtrl()
        {
            InitializeComponent();
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
       
		// The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Type",   HorizontalAlignment.Left, null },  
			new object[] { "Target", HorizontalAlignment.Left, null }
		};
		#endregion
            
        /// <summary>
        /// Initializes the control with a set of items.
        /// </summary>
        public void Initialize(Session session, ExpandedNodeId nodeId)
        {
            ItemsLV.Items.Clear();
            m_session = session;

            if (m_session == null)
            {
                return;
            }

            ILocalNode node = m_session.NodeCache.Find(nodeId) as ILocalNode;

            if (node == null)
            {
                return;
            }

            IList<IReference> references = null;

            references = node.References.Find(ReferenceTypes.NonHierarchicalReferences, false, true, m_session.TypeTree);

            for (int ii = 0; ii < references.Count; ii++)
            {
                AddItem(references[ii]);
            }
            
            references = node.References.Find(ReferenceTypes.NonHierarchicalReferences, true, true, m_session.TypeTree);

            for (int ii = 0; ii < references.Count; ii++)
            {
                AddItem(references[ii]);
            }

            AdjustColumns();
        }

        #region Overridden Methods
        /// <see cref="Opc.Ua.Client.Controls.BaseListCtrl.UpdateItem(ListViewItem,object)" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            IReference reference = item as IReference;

			if (reference == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
            
            IReferenceType referenceType = m_session.NodeCache.Find(reference.ReferenceTypeId) as IReferenceType;

            if (referenceType != null)
            {
                if (reference.IsInverse)
                {
			        listItem.SubItems[0].Text = Utils.Format("{0}", referenceType.InverseName);
                }
                else
                {
			        listItem.SubItems[0].Text = Utils.Format("{0}", referenceType.DisplayName);
                }
            }
            else
            {
			    listItem.SubItems[0].Text = Utils.Format("{0}", reference.ReferenceTypeId);
            }
            
            INode target = m_session.NodeCache.Find(reference.TargetId) as INode;

            if (target != null)
            {
			    listItem.SubItems[1].Text = Utils.Format("{0}", target.DisplayName);
            }
            else
            {
			    listItem.SubItems[1].Text = Utils.Format("{0}", reference.TargetId);
            }
            
            listItem.ImageKey = GuiUtils.GetTargetIcon(m_session, NodeClass.ReferenceType, null);
			listItem.Tag = reference;
        }
        #endregion
    }
}
