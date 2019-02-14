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
    public partial class FilterOperandEditDlg : Form
    {
        #region Constructors
        public FilterOperandEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            OperandTypeCB.Items.Clear();
            OperandTypeCB.Items.Add(typeof(LiteralOperand).Name);
            OperandTypeCB.Items.Add(typeof(AttributeOperand).Name);
            OperandTypeCB.Items.Add(typeof(ElementOperand).Name);

            foreach (BuiltInType datatype in Enum.GetValues(typeof(BuiltInType)))
            {
                DataTypeCB.Items.Add(datatype);
            }

            AttributeIdCB.Items.AddRange(Attributes.GetBrowseNames());
        }
        #endregion

        #region Private Fields
        private Session m_session;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public FilterOperand ShowDialog(
            Session                     session, 
            IList<ContentFilterElement> elements, 
            int                         index, 
            FilterOperand               operand)
        {
            if (session == null)  throw new ArgumentNullException("session");
            if (elements == null) throw new ArgumentNullException("elements");

            m_session = session;
            
            TypeDefinitionIdCTRL.Browser = new Browser(session);
            TypeDefinitionIdCTRL.RootId  = ObjectTypes.BaseEventType;

            OperandTypeCB.SelectedItem = typeof(LiteralOperand).Name;

            if (operand != null)
            {
                OperandTypeCB.SelectedItem = operand.GetType().Name;
            }
            
            ElementsCB.Items.Clear();

            for (int ii = index+1; ii < elements.Count; ii++)
            {
                ElementsCB.Items.Add(elements[ii].ToString(m_session.NodeCache));
            }
                        
            ElementOperand elementOperand = operand as ElementOperand;

            if (elementOperand != null)
            {
                ElementsCB.SelectedIndex = (int)elementOperand.Index - index -1;
            }
            
            AttributeOperand attributeOperand = operand as AttributeOperand;

            if (attributeOperand != null)
            {
                TypeDefinitionIdCTRL.Identifier = attributeOperand.NodeId;
                BrowsePathTB.Text               = attributeOperand.BrowsePath.Format(session.NodeCache.TypeTree);
                AttributeIdCB.SelectedItem      = Attributes.GetBrowseName(attributeOperand.AttributeId);
                IndexRangeTB.Text               = attributeOperand.IndexRange;
                AliasTB.Text                    = attributeOperand.Alias;
            }
            
            LiteralOperand literalOperand = operand as LiteralOperand;

            if (literalOperand != null)
            {
                NodeId datatypeId = TypeInfo.GetDataTypeId(literalOperand.Value.Value);
                DataTypeCB.SelectedItem = TypeInfo.GetBuiltInType(datatypeId);

                StringBuilder buffer = new StringBuilder();

                Array array = literalOperand.Value.Value as Array;

                if (array != null)
                {
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        if (ii > 0)
                        {
                            buffer.Append("\r\n");
                        }

                        buffer.AppendFormat("{0}", new Variant(array.GetValue(ii)));
                    }
                }
                else
                {
                    buffer.AppendFormat("{0}", literalOperand.Value);
                }

                ValueTB.Text = buffer.ToString();
            }
                        
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return operand;
        }
        #endregion
        
        #region Event Handlers
        private void OperandTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                switch ((string)OperandTypeCB.SelectedItem)
                {
                    case "LiteralOperand":
                    {
                        LiteralPN.Visible   = true;
                        AttributePN.Visible = false;
                        ElementPN.Visible   = false;
                        break;
                    }

                    case "AttributeOperand":
                    {
                        LiteralPN.Visible   = false;
                        AttributePN.Visible = true;
                        ElementPN.Visible   = false;
                        break;
                    }

                    case "ElementOperand":
                    {
                        LiteralPN.Visible   = false;
                        AttributePN.Visible = false;
                        ElementPN.Visible   = true;
                        break;
                    }
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
