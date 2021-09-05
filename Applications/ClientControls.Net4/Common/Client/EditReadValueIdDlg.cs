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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditReadValueIdDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditReadValueIdDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            // add the attributes in numerical order.
            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                AttributeCB.Items.Add(Attributes.GetBrowseName(attributeId));
            }
        }
        #endregion

        #region EncodingInfo Class
        /// <summary>
        /// Stores information about a data encoding.
        /// </summary>
        private class EncodingInfo
        {
            public QualifiedName EncodingName;

            public override string ToString()
            {
                if (EncodingName != null)
                {
                    return EncodingName.ToString();
                }

                return "Not Set";
            }
        }
        #endregion
      
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the read request parameters for the set of nodes provided.
        /// </summary>
        public ReadValueId[] ShowDialog(Session session, params ReadValueId[] nodesToRead)
        {
            NodeBTN.Session = session;
            NodeBTN.SelectedReference = null;

            bool editNode = true;
            bool editAttribute = true;
            bool editIndexRange = true;
            bool editDataEncoding = true;

            // populate the controls.
            if (nodesToRead != null && nodesToRead.Length > 0)
            {
                bool nonValueAttribute = false;

                for (int ii = 0; ii < nodesToRead.Length; ii++)
                {
                    if (nodesToRead[ii] == null)
                    {
                        continue;
                    }

                    // only show the node if all have the same node id.
                    if (editNode)
                    {
                        if (NodeBTN.SelectedNode != null && nodesToRead[ii].NodeId != NodeBTN.SelectedNode)
                        {
                            NodeTB.Visible = false;
                            NodeLB.Visible = false;
                            NodeBTN.Visible = false;
                            editNode = false;
                        }
                        else
                        {
                            NodeBTN.SelectedNode = nodesToRead[ii].NodeId;
                        }
                    }

                    // only show the attribute if all have the same attribute id.
                    if (editAttribute)
                    {
                        // check if any non-value attributes are present.
                        if (nodesToRead[ii].AttributeId != Attributes.Value)
                        {
                            nonValueAttribute = true;
                        }

                        int index = (int)nodesToRead[ii].AttributeId - 1;

                        if (AttributeCB.SelectedIndex != -1 && index != AttributeCB.SelectedIndex)
                        {
                            AttributeCB.Visible = false;
                            AttributeLB.Visible = false;
                            editAttribute = false;
                        }
                        else
                        {
                            AttributeCB.SelectedIndex = index;
                        }
                    }
                }

                DataEncodingCB.Items.Clear();
                editIndexRange = !nonValueAttribute;

                IndexRangeLB.Visible = editIndexRange;
                IndexRangeTB.Visible = editIndexRange;

                if (!nonValueAttribute)
                {
                    // use the index range for the first node as template.
                    IndexRangeTB.Text = nodesToRead[0].IndexRange;

                    // fetch the available encodings for the first node in the list from the server.
                    IVariableBase variable = session.NodeCache.Find(nodesToRead[0].NodeId) as IVariableBase;

                    if (variable != null)
                    {
                        if (session.NodeCache.IsTypeOf(variable.DataType, Opc.Ua.DataTypeIds.Structure))
                        {
                            DataEncodingCB.Items.Add(new EncodingInfo());
                            DataEncodingCB.SelectedIndex = 0;

                            foreach (INode encoding in session.NodeCache.Find(variable.DataType, Opc.Ua.ReferenceTypeIds.HasEncoding, false, true))
                            {
                                DataEncodingCB.Items.Add(new EncodingInfo() { EncodingName = encoding.BrowseName });

                                if (nodesToRead[0].DataEncoding == encoding.BrowseName)
                                {
                                    DataEncodingCB.SelectedIndex = DataEncodingCB.Items.Count - 1;
                                }
                            }
                        }
                    }
                }

                // hide the data encodings if none to select.
                if (DataEncodingCB.Items.Count == 0)
                {
                    DataEncodingCB.Visible = false;
                    DataEncodingLB.Visible = false;
                    editDataEncoding = false;
                }
            }

            if (!editNode && !editAttribute && !editIndexRange && !editDataEncoding)
            {
                throw new ArgumentException("nodesToRead", "It is not possible to edit the current selection as a group.");
            }

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            // create the list of results.
            ReadValueId[] results = null;

            if (nodesToRead == null || nodesToRead.Length == 0)
            {
                results = new ReadValueId[1];
            }
            else
            {
                results = new ReadValueId[nodesToRead.Length];
            }

            // copy the controls into the results.
            for (int ii = 0; ii < results.Length; ii++)
            {
                // preserve the existing settings if they are not being changed.
                if (nodesToRead != null && nodesToRead.Length > 0)
                {
                    results[ii] = (ReadValueId)nodesToRead[ii].MemberwiseClone();
                }
                else
                {
                    results[ii] = new ReadValueId();
                }

                // only copy results that were actually being edited. 
                if (editNode)
                {
                    results[ii].NodeId = NodeBTN.SelectedNode;
                }

                if (editAttribute)
                {
                    results[ii].AttributeId = (uint)(AttributeCB.SelectedIndex + 1);
                }

                if (editIndexRange)
                {
                    results[ii].ParsedIndexRange = NumericRange.Parse(IndexRangeTB.Text);

                    if (NumericRange.Empty != results[ii].ParsedIndexRange)
                    {
                        results[ii].IndexRange = results[ii].ParsedIndexRange.ToString();
                    }
                    else
                    {
                        results[ii].IndexRange = String.Empty;
                    }
                }

                if (editDataEncoding)
                {
                    results[ii].DataEncoding = null;

                    EncodingInfo encoding = DataEncodingCB.SelectedItem as EncodingInfo;

                    if (encoding != null)
                    {
                        results[ii].DataEncoding = encoding.EncodingName;
                    }
                }
            }

            return results;
        }
        #endregion
        
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (IndexRangeTB.Visible)
                {
                    NumericRange.Parse(IndexRangeTB.Text);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
