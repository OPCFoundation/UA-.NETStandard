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
    public partial class WriteValueListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public WriteValueListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Name",       HorizontalAlignment.Left, null },  
			new object[] { "NodeId",     HorizontalAlignment.Left, null }, 
			new object[] { "Attribute",  HorizontalAlignment.Left, "Value" }, 
			new object[] { "IndexRange", HorizontalAlignment.Left, "" }, 
			new object[] { "Value",      HorizontalAlignment.Left, "" }, 
			new object[] { "Status",     HorizontalAlignment.Left, "" }, 
			new object[] { "Timestamp",  HorizontalAlignment.Left, "" }
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
        public void Initialize(Session session, WriteValueCollection values)
        {
            if (session == null)  throw new ArgumentNullException("session");
            
            Clear();
            
            m_session = session;

            if (values != null)
            {
                foreach (WriteValue value in values)
                {
                    AddItem(value);
                }
            }

            AdjustColumns();
        }

        /// <summary>
        /// Adds a value to the control.
        /// </summary>
        public void AddValue(ReferenceDescription reference)
        {
            Node node = m_session.NodeCache.Find(reference.NodeId) as Node;

            if (node == null)
            {
                return;
            }

            WriteValue value = new WriteValue();

            value.NodeId      = node.NodeId;
            value.AttributeId = Attributes.Value;
            value.IndexRange  = null;

            // read the display name for non-variables.
            if ((node.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
            {
                value.AttributeId  = Attributes.DisplayName;
            }

            value.Value = GetDefaultValue(value.NodeId, value.AttributeId);

            AddItem(value);
            AdjustColumns();
        }


        /// <summary>
        /// Returns the items in the control.
        /// </summary>
        public WriteValueCollection GetValues()
        {
            WriteValueCollection values = new WriteValueCollection();

            foreach (ListViewItem item in ItemsLV.Items)
            {
                WriteValue value = item.Tag as WriteValue;

                if (value != null)
                {
                    value.Value.StatusCode = StatusCodes.Good;
                    value.Value.ServerTimestamp = DateTime.MinValue;
                    value.Value.SourceTimestamp = DateTime.MinValue;

                    values.Add(value);
                }
            }

            return values;
        }

        /// <summary>
        /// Returns a default value for the node.
        /// </summary>
        private DataValue GetDefaultValue(NodeId nodeId, uint attributeId)
        {
            // find the node.
            Node node = m_session.NodeCache.Find(nodeId) as Node;

            if (node == null)
            {
                return new DataValue();
            }

            // read the current attribute value.
            DataValue value = new DataValue();

            ServiceResult result = node.Read(null, attributeId, value);

            if (ServiceResult.IsBad(result))
            {
                value.Value = GuiUtils.GetDefaultValue(Attributes.GetDataTypeId(attributeId), ValueRanks.Scalar);
            }
                        
            // update the value attribute.
            if (attributeId == Attributes.Value)
            {
                try
                {
                    return m_session.ReadValue(node.NodeId);
                }
                catch (Exception)
                {
                    VariableNode variable = node as VariableNode;

                    if (variable != null)
                    {
                        value.Value = GuiUtils.GetDefaultValue(variable.DataType, variable.ValueRank);
                    }
                }
            }
          
            return value;
        }
		#endregion
                
        #region Overridden Methods
        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            EditValueMI_Click(this, null);
        }

        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            NewMI.Enabled         = true;
            EditMI.Enabled        = ItemsLV.SelectedItems.Count == 1;
            EditValueMI.Enabled   = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled      = ItemsLV.SelectedItems.Count > 0;
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			WriteValue value = item as WriteValue;

			if (value == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
            
            if (value.Value == null)
            {
                value.Value = new DataValue();
            }
            
            if (value.Value.Value == null)
            {
                value.Value = GetDefaultValue(value.NodeId, value.AttributeId);
            }
            
            Node node = m_session.NodeCache.Find(value.NodeId) as Node;

            if (node != null)
            {
                listItem.SubItems[0].Text = String.Format("{0}", node);
            }
            else
            {
                listItem.SubItems[0].Text = String.Format("{0}", value.NodeId);
            }
            
            listItem.SubItems[1].Text = String.Format("{0}", value.NodeId);
            listItem.SubItems[2].Text = String.Format("{0}", Attributes.GetBrowseName(value.AttributeId));
            listItem.SubItems[3].Text = String.Format("{0}", value.IndexRange);
            listItem.SubItems[4].Text = String.Format("{0}", value.Value.Value);
            listItem.SubItems[5].Text = String.Format("{0}", value.Value.StatusCode);
            listItem.SubItems[6].Text = String.Format("{0}", value.Value.SourceTimestamp);
            
			listItem.Tag = item;
            listItem.ImageKey = "DataType";
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
                    
                AddValue(reference);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
        
        #region Event Handlers
        private void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                WriteValue value = new WriteValue();

                if (new WriteValueEditDlg().ShowDialog(m_session, value))
                {
                    AddItem(value);
                }

                AdjustColumns();
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
                WriteValue[] values = GetSelectedItems(typeof(WriteValue)) as WriteValue[];

                if (values == null || values.Length != 1)
                {
                    return;
                }

                if (new WriteValueEditDlg().ShowDialog(m_session, values[0]))
                {
                    Node node = m_session.NodeCache.Find(values[0].NodeId) as Node;

                    if (node != null)
                    {
                        DataValue value = new DataValue();

                        ServiceResult result = node.Read(null, values[0].AttributeId, value);

                        if (ServiceResult.IsGood(result))
                        {
                            values[0].Value = value;
                        }
                    }

                    UpdateItem(ItemsLV.SelectedItems[0], values[0]);
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EditValueMI_Click(object sender, EventArgs e)
        {
            try
            {
                WriteValue[] values = GetSelectedItems(typeof(WriteValue)) as WriteValue[];

                if (values == null || values.Length != 1)
                {
                    return;
                }

                NodeId datatypeId = Attributes.GetDataTypeId(values[0].AttributeId);
                int valueRank = Attributes.GetValueRank(values[0].AttributeId);

                if (values[0].AttributeId == Attributes.Value)
                {
                    VariableNode node = m_session.NodeCache.Find(values[0].NodeId) as VariableNode;

                    if (node != null)
                    {
                        datatypeId = node.DataType;
                        valueRank = node.ValueRank;
                    }
                }

                bool useIndexRange = false;
                object value = null;

                // check IndexRange for arrays
                if (values[0].AttributeId == Attributes.Value && valueRank > 0)
                {
                    NumericRange indexRange;
                    ServiceResult result = NumericRange.Validate(values[0].IndexRange, out indexRange);

                    if (ServiceResult.IsGood(result) && indexRange != NumericRange.Empty)
                    {
                        useIndexRange = true;
                    }
                }

                if (useIndexRange)
                {
                    value = new ComplexValueEditDlg().ShowDialog(values[0]);

                    WriteValue writeValue = value as WriteValue;

                    if (writeValue != null)
                    {
                        value = writeValue.Value.Value;
                    }
                }
                else
                {
                    value = GuiUtils2.EditValue(m_session, values[0].Value.Value, datatypeId, valueRank);
                }

                if (value != null)
                {
                    values[0].Value.Value = value;
                    values[0].Value.StatusCode = StatusCodes.Good;

                    UpdateItem(ItemsLV.SelectedItems[0], values[0]);

                    AdjustColumns();
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
                List<ListViewItem> itemsToDelete = new List<ListViewItem>();

                foreach (ListViewItem item in ItemsLV.SelectedItems)
                {
                    itemsToDelete.Add(item);
                }

                foreach (ListViewItem item in itemsToDelete)
                {
                    item.Remove();
                }

                AdjustColumns();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
