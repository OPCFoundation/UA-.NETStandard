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
    public partial class PropertyListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public PropertyListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private bool m_showValues;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Property",    HorizontalAlignment.Left, null },  
			new object[] { "Value",       HorizontalAlignment.Left, ""   }, 
			new object[] { "DataType",    HorizontalAlignment.Left, null },
			new object[] { "Description", HorizontalAlignment.Left, null } 
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// Whether the values should be displayed.
        /// </summary>
        public bool ShowValues
        {
            get { return m_showValues;  }
            set { m_showValues = value; }
        }
        
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
        public void Update(Session session, ReferenceDescription reference)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            Clear();

            if (reference == null)
            {
                return;                
            }
            
            m_session = session;

            AddProperties(reference.NodeId);

            AdjustColumns();
        }
		#endregion
        
        #region NodeField Class
        /// <summary>
        /// A field associated with a node.
        /// </summary>
        private class PropertyItem
        {            
            public ReferenceDescription Reference;
            public VariableNode         Property;
        }
		#endregion

        #region Private Methods
        /// <summary>
        /// Adds the properties to the control.
        /// </summary>
        private void AddProperties(ExpandedNodeId nodeId)
        {
            // get node.
            Node node = m_session.NodeCache.Find(nodeId) as Node;

            if (node == null)
            {
                return;
            }

            // get properties from supertype.
            ExpandedNodeId supertypeId = node.GetSuperType(m_session.TypeTree);

            if (supertypeId != null)
            {
                AddProperties(supertypeId);
            }

            // build list of properties to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            Browser browser = new Browser(m_session);
            
            browser.BrowseDirection   = BrowseDirection.Forward;
            browser.ReferenceTypeId   = ReferenceTypeIds.HasProperty;
            browser.IncludeSubtypes   = true;
            browser.NodeClassMask     = (int)NodeClass.Variable;
            browser.ContinueUntilDone = true;

            ReferenceDescriptionCollection references = browser.Browse(node.NodeId);

            // add propertoes to view.
            foreach (ReferenceDescription reference in references)
            {
                PropertyItem field = new PropertyItem();

                field.Reference = reference;
                field.Property  = m_session.NodeCache.Find(reference.NodeId) as VariableNode;

                AddItem(field, "Property", -1);
            }
        }
		#endregion
        
        #region Overridden Methods
        /// <see cref="BaseListCtrl.GetDataToDrag" />
        protected override object GetDataToDrag()
        {
            ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

            foreach (ListViewItem listItem in ItemsLV.SelectedItems)
            {
                PropertyItem property = listItem.Tag as PropertyItem;

                if (property != null)
                {
                    references.Add(property.Reference);
                }
            }

            return references;
        }

        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{                        
            PropertyItem[] items = GetSelectedItems(typeof(PropertyItem)) as PropertyItem[];

            if (items != null && items.Length > 0)
            {
                SelectMI.Enabled = true;
            }
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			PropertyItem property = item as PropertyItem;

			if (property == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

            listItem.SubItems[0].Text = String.Format("{0}", property.Reference);
            listItem.SubItems[1].Text = "";

            if (m_showValues)
            {
                object value = property.Property.Value;
                Array array = value as Array;

                if (array == null)
                {
                    listItem.SubItems[1].Text = String.Format("{0}", value);
                }
                else
                {
                    listItem.SubItems[1].Text = String.Format("{0}[{1}]", value.GetType().GetElementType().Name, array.Length);
                }
            }

            INode node = m_session.NodeCache.Find(property.Property.DataType);

            if (node != null)
            {
                listItem.SubItems[2].Text = String.Format("{0}", node);
            }
            else
            {
                listItem.SubItems[2].Text = String.Format("{0}", property.Property.DataType);
            }

            if (property.Property.ValueRank >= 0)
            {
                listItem.SubItems[2].Text += "[]";
            }
                
            listItem.SubItems[3].Text = String.Format("{0}", property.Property.Description);

			listItem.Tag = item;
        }
        #endregion

        private void SelectMI_Click(object sender, EventArgs e)
        {
            try
            {
                PickItems();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
