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
    public partial class FilterOperandListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public FilterOperandListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private IList<ContentFilterElement> m_elements;
        private int m_index;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Index",   HorizontalAlignment.Left, null },
			new object[] { "Operand", HorizontalAlignment.Left, null },
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
        public void Initialize(Session session, IList<ContentFilterElement> elements, int index)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            Clear();
            
            m_session  = session;
            m_elements = elements;
            m_index    = index;

            if (elements == null || index < 0 || index >= elements.Count)
            {
                return;                
            }

            foreach (FilterOperand operand in elements[index].GetOperands())
            {
                AddItem(operand);
            }

            AdjustColumns();
        }

        /// <summary>
        /// Returns the list of operands in the control.
        /// </summary>
        public List<FilterOperand> GetOperands()
        {
            List<FilterOperand> operands = new List<FilterOperand>();
                    
            for (int ii = 0; ii < ItemsLV.Items.Count; ii++)
            {
                FilterOperand operand = ItemsLV.Items[ii].Tag as FilterOperand;

                if (operand != null)
                {
                    operands.Add(operand);
                }
            }

            return operands;
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            NewMI.Enabled    = true;
            EditMI.Enabled   = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item, int index)
        {
			FilterOperand operand = item as FilterOperand;

			if (operand == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
                      
            listItem.SubItems[0].Text = String.Format("[{0}]", index);
            listItem.SubItems[1].Text = operand.ToString(m_session.NodeCache);
                        
            listItem.Tag = operand;
        }
        #endregion
               
        #region Event Handlers
        private void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                FilterOperand operand = new FilterOperandEditDlg().ShowDialog(m_session, m_elements, m_index, null);

                if (operand == null)
                {
                    return;
                }              

                // insert after the current selection.
                int index = ItemsLV.SelectedIndices.Count;

                if (ItemsLV.SelectedIndices.Count > 0)
                {
                    index = ItemsLV.SelectedIndices[0]+1;
                }

                AddItem(operand, "SimpleItem", index);

                // must update index for all items.
                for (int ii = 0; ii < ItemsLV.Items.Count; ii++)
                {
                    UpdateItem(ItemsLV.Items[ii], ItemsLV.Items[ii].Tag, ii);
                }
                
                AdjustColumns();

                m_elements[m_index].FilterOperands.Clear();
                m_elements[m_index].SetOperands(GetOperands());
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {
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
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
