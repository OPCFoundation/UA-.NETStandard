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
    public partial class ContentFilterElementListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public ContentFilterElementListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private Browser m_browser;
        private ContentFilter m_filter;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Index",      HorizontalAlignment.Left, null },
			new object[] { "Expression", HorizontalAlignment.Left, null }
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
        public void Initialize(Session session, ContentFilter filter)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            Clear();
            
            m_session = session;
            m_browser = new Browser(session);
            m_filter  = filter;

            if (m_filter == null)
            {
                return;                
            }

            foreach (ContentFilterElement element in filter.Elements)
            {
                AddItem(element);
            }

            AdjustColumns();
        }

        /// <summary>
        /// Returns the filter in the control.
        /// </summary>
        public ContentFilter GetFilter()
        {
            ContentFilter filter = new ContentFilter();
                    
            for (int ii = 0; ii < ItemsLV.Items.Count; ii++)
            {
                ContentFilterElement element = ItemsLV.Items[ii].Tag as ContentFilterElement;

                if (element != null)
                {
                    filter.Elements.Add(element);
                }
            }

            return filter;
        }

        /// <summary>
        /// Returns the list of elements in the control.
        /// </summary>
        public List<ContentFilterElement> GetElements()
        {
            List<ContentFilterElement> elements = new List<ContentFilterElement>();
                    
            for (int ii = 0; ii < ItemsLV.Items.Count; ii++)
            {
                ContentFilterElement element = ItemsLV.Items[ii].Tag as ContentFilterElement;

                if (element != null)
                {
                    elements.Add(element);
                }
            }

            return elements;
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            SetOperatorMI.Enabled      = ItemsLV.SelectedItems.Count == 1;
            SelectNodeMI.Enabled       = true;
            EditValueMI.Enabled        = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled           = ItemsLV.SelectedItems.Count > 0;
            CreateElementMI.Enabled    = ItemsLV.SelectedItems.Count > 0;
            CreateElementAndMI.Enabled = ItemsLV.SelectedItems.Count == 2;
            CreateElementOrMI.Enabled  = ItemsLV.SelectedItems.Count == 2;
            CreateElementNotMI.Enabled = ItemsLV.SelectedItems.Count == 1;
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item, int index)
        {
			ContentFilterElement element = item as ContentFilterElement;

			if (element == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
           
            listItem.SubItems[0].Text = String.Format("[{0}]", index);
            listItem.SubItems[1].Text = String.Format("{0}", element.ToString(m_session.NodeCache));
                        
            listItem.Tag = element;
        }
        #endregion
               
        #region Event Handlers
        /// <summary>
        /// Updates the control with a new filter.
        /// </summary>
        private void Update(ContentFilter filter)
        {              
            BeginUpdate();

            int index = 0;

            foreach (ContentFilterElement element in filter.Elements)
            {
                AddItem(element, "Property", index++);
            }
            
            EndUpdate();

            AdjustColumns();
        }

        private void SetOperatorMI_Click(object sender, EventArgs e)
        {
            try
            {
                ContentFilterElement element = SelectedTag as ContentFilterElement;

                if (element == null)
                {
                    return;
                }

                FilterOperator op = element.FilterOperator;

                if (!new FilterOperatorEditDlg().ShowDialog(ref op))
                {
                    return;
                }          

                element.FilterOperator = op;
                UpdateItem(ItemsLV.SelectedItems[0], element);
                AdjustColumns();
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
                DeleteSelection();
                AdjustColumns();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectNodeMI_Click(object sender, EventArgs e)
        {
            try
            {
                ReferenceDescription reference = new SelectNodeDlg().ShowDialog(m_browser, ObjectTypes.BaseEventType);

                if (reference != null)
                {
                    Node node = m_session.NodeCache.Find(reference.NodeId) as Node;

                    if (node == null)
                    {
                        return;
                    }
                                        
                    ContentFilterElement element = null;

                    // build the relative path.
                    QualifiedNameCollection browsePath = new QualifiedNameCollection();
                    NodeId typeId = m_session.NodeCache.BuildBrowsePath(node, browsePath);

                    switch (node.NodeClass)
                    {
                        case NodeClass.Variable:
                        {
                            IVariable variable = node as IVariable;

                            if (variable == null)
                            {
                                break;
                            }

                            // create attribute operand.
                            SimpleAttributeOperand attribute = new SimpleAttributeOperand(
                                m_session.FilterContext, 
                                typeId,
                                browsePath);

                            // create default value.
                            object value = GuiUtils.GetDefaultValue(variable.DataType, variable.ValueRank);

                            // create attribute filter.
                            element = m_filter.Push(FilterOperator.Equals, attribute, value);
                            break;
                        }

                        case NodeClass.Object:
                        {
                            // create attribute operand.
                            SimpleAttributeOperand attribute = new SimpleAttributeOperand(
                                m_session.FilterContext, 
                                typeId,
                                browsePath);

                            attribute.AttributeId = Attributes.NodeId;

                            // create attribute filter.
                            element = m_filter.Push(FilterOperator.IsNull, attribute);
                            break;
                        }

                        case NodeClass.ObjectType:
                        {
                            element = m_filter.Push(FilterOperator.OfType, node.NodeId);
                            break;
                        }

                        default:
                        {
                            throw new ArgumentException("Selected an invalid node.");
                        }
                    }

                    // add element.
                    if (element != null)
                    {
                        AddItem(element);
                        AdjustColumns();
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CreateElementAndMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ItemsLV.SelectedItems.Count != 2)
                {
                    return;
                }
                
                ContentFilterElement element1 = ItemsLV.SelectedItems[0].Tag as ContentFilterElement;
                ContentFilterElement element2 = ItemsLV.SelectedItems[1].Tag as ContentFilterElement;
                
                ContentFilter filter = GetFilter();
                filter.Push(FilterOperator.And, element1, element2);

                Update(filter);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CreateElementOrMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ItemsLV.SelectedItems.Count != 2)
                {
                    return;
                }
                
                ContentFilterElement element1 = ItemsLV.SelectedItems[0].Tag as ContentFilterElement;
                ContentFilterElement element2 = ItemsLV.SelectedItems[1].Tag as ContentFilterElement;
                
                ContentFilter filter = GetFilter();
                filter.Push(FilterOperator.Or, element1, element2);

                Update(filter);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CreateElementNotMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return;
                }
                
                ContentFilterElement element1 = ItemsLV.SelectedItems[0].Tag as ContentFilterElement;

                ContentFilter filter = GetFilter();
                filter.Push(FilterOperator.Not, element1);

                Update(filter);
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
                ContentFilterElement element = SelectedTag as ContentFilterElement;

                if (element == null)
                {
                    return;
                }

                List<FilterOperand> operands = element.GetOperands();

                if (operands.Count != 2)
                {
                    return;                    
                }

                LiteralOperand literal = operands[1] as LiteralOperand;

                if (literal == null)
                {
                    return;
                }

                // get the current value.
                object currentValue = literal.Value.Value;

                if (currentValue == null)
                {
                    currentValue = String.Empty;
                }

                // edit the value.
                object value = new SimpleValueEditDlg().ShowDialog(currentValue, currentValue.GetType());

                if (value == null)
                {
                    return;
                }

                // update value.
                literal.Value = new Variant(value);
                ContentFilter filter = GetFilter();
                Update(filter);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

    }
}
