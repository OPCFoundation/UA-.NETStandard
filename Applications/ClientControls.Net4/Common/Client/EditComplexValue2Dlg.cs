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
using System.Drawing;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditComplexValue2Dlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditComplexValue2Dlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_variableId;
        private Variant m_value;
        private bool m_textChanged;
        private QualifiedName m_encodingName;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit a value.
        /// </summary>
        public Variant ShowDialog(Session session, NodeId variableId, Variant value, string caption)
        {
            if (caption != null)
            {
                this.Text = caption;
            }

            m_session = session;
            m_variableId = variableId;

            SetValue(value);

            if (ShowDialog() != DialogResult.OK)
            {
                return Variant.Null;
            }

            return GetValue();
        }
        #endregion

        /// <summary>
        /// Sets the value shown in the control.
        /// </summary>
        private void SetValue(Variant value)
        {
            ValueTB.ForeColor = Color.Empty;
            ValueTB.Font = new Font(ValueTB.Font, FontStyle.Regular);

            m_textChanged = false;

            // check for null.
            if (Variant.Null == value)
            {
                ValueTB.Text = String.Empty;
                m_value = Variant.Null;
                return;
            }

            // get the source type.
            TypeInfo sourceType = value.TypeInfo;

            if (sourceType == null)
            {
                sourceType = TypeInfo.Construct(value.Value);
            }

            m_value = new Variant(value.Value, sourceType);
            
            // display value as text.
            StringBuilder buffer = new StringBuilder();
            XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true });
            XmlEncoder encoder = new XmlEncoder(new XmlQualifiedName("Value", Namespaces.OpcUaXsd), writer, m_session.MessageContext);
            encoder.WriteVariantContents(m_value.Value, m_value.TypeInfo);
            writer.Close();

            ValueTB.Text = buffer.ToString();

            // extract the encoding id from the value.
            ExpandedNodeId encodingId = null;
            ExtensionObjectEncoding encoding = ExtensionObjectEncoding.None;

            if (sourceType.BuiltInType == BuiltInType.ExtensionObject)
            {
                ExtensionObject extension = null;

                if (sourceType.ValueRank == ValueRanks.Scalar)
                {
                    extension = (ExtensionObject)m_value.Value;
                }
                else
                {
                    // only use the first item in the list for arrays.
                    ExtensionObject[] list = (ExtensionObject[])m_value.Value;

                    if (list.Length > 0)
                    {
                        extension = list[0];
                    }
                }

                encodingId = extension.TypeId;
                encoding = extension.Encoding;
            }

            if (encodingId == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // check if the encoding is known.
            IObject encodingNode = m_session.NodeCache.Find(encodingId) as IObject;

            if (encodingNode == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // update the encoding shown.
            if (encoding == ExtensionObjectEncoding.EncodeableObject)
            {
                EncodingCB.Text = "(Converted to XML by Client)";
            }
            else
            {
                EncodingCB.Text = m_session.NodeCache.GetDisplayText(encodingNode);
            }

            m_encodingName = encodingNode.BrowseName;

            // find the data type for the encoding.
            IDataType dataTypeNode = null;

            foreach (INode node in m_session.NodeCache.Find(encodingNode.NodeId, Opc.Ua.ReferenceTypeIds.HasEncoding, true, false))
            {
                dataTypeNode = node as IDataType;

                if (dataTypeNode != null)
                {
                    break;
                }
            }

            if (dataTypeNode == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // update data type display.
            DataTypeTB.Text = m_session.NodeCache.GetDisplayText(dataTypeNode);
            DataTypeTB.Tag = dataTypeNode;

            // update encoding drop down.
            EncodingCB.DropDownItems.Clear();

            foreach (INode node in m_session.NodeCache.Find(dataTypeNode.NodeId, Opc.Ua.ReferenceTypeIds.HasEncoding, false, false))
            {
                IObject encodingNode2 = node as IObject;

                if (encodingNode2 != null)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(m_session.NodeCache.GetDisplayText(encodingNode2));
                    item.Tag = encodingNode2;
                    item.Click += new EventHandler(EncodingCB_Item_Click);
                    EncodingCB.DropDownItems.Add(item);
                }
            }

            StatusCTRL.Visible = true;
        }

        /// <summary>
        /// Converts the XML back to a value.
        /// </summary>
        private Variant GetValue()
        {
            if (!m_textChanged)
            {
                return m_value;
            }

            XmlDocument document = new XmlDocument();
            document.InnerXml = ValueTB.Text;
            
            // find the first element.
            XmlElement element = null;
            
            for (XmlNode node = document.DocumentElement.FirstChild; node != null; node = node.NextSibling)
            {
                element = node as XmlElement;

                if (element != null)
                {
                    break;
                }
            }

            XmlDecoder decoder = new XmlDecoder(element, m_session.MessageContext);

            decoder.PushNamespace(Namespaces.OpcUaXsd);
            TypeInfo typeInfo = null;
            object value = decoder.ReadVariantContents(out typeInfo);

            return new Variant(value, typeInfo);
        }

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion

        private void EncodingCB_Item_Click(object sender, EventArgs e)
        {
            try
            {
                ToolStripMenuItem item = sender as ToolStripMenuItem;

                if (item != null)
                {
                    IObject encodingNode = item.Tag as IObject;
                    m_encodingName = encodingNode.BrowseName;
                    EncodingCB.Text = item.Text;
                    ValueTB.Text = null;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void RefreshBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = m_variableId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.DataEncoding = m_encodingName;


                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                nodesToRead.Add(nodeToRead);

                // read the attributes.
                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // check for error.
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    ValueTB.Text = results[0].StatusCode.ToString();
                    ValueTB.ForeColor = Color.Red;
                    ValueTB.Font = new Font(ValueTB.Font, FontStyle.Bold);
                    return;
                }

                SetValue(results[0].WrappedValue);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void UpdateBTN_Click(object sender, EventArgs e)
        {
            try
            {
                WriteValue nodeToWrite = new WriteValue();
                nodeToWrite.NodeId = m_variableId;
                nodeToWrite.AttributeId = Attributes.Value;
                nodeToWrite.Value = new DataValue();
                nodeToWrite.Value.WrappedValue = GetValue();

                WriteValueCollection nodesToWrite = new WriteValueCollection();
                nodesToWrite.Add(nodeToWrite);

                // read the attributes.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = m_session.Write(
                    null,
                    nodesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                // check for error.
                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ValueTB_TextChanged(object sender, EventArgs e)
        {
            m_textChanged = true;
        }

        private void EncodingCB_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
