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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays browse tree.
    /// </summary>
    public partial class AttributesListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public AttributesListViewCtrl()
        {
            InitializeComponent();
            AttributesLV.SmallImageList = new ClientUtils().ImageList;
        }
        #endregion

        #region Private Fields
        private Session m_session;
        #endregion

        #region Public Interface
        /// <summary>
        /// The view to use.
        /// </summary>
        public ViewDescription View { get; set; }

        /// <summary>
        /// Changes the session used by the control.
        /// </summary>
        /// <param name="session">The session.</param>
        public void ChangeSession(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Gets or sets the context menu for the attributes list.
        /// </summary>
        public ContextMenuStrip AttributesMenuStrip
        {
            get { return AttributesLV.ContextMenuStrip; }
            set { AttributesLV.ContextMenuStrip = value; }
        }

        /// <summary>
        /// Returns the attribute at the specified index.
        /// </summary>
        public ReadValueId GetSelectedAttribute(int index)
        {
            if (index >=0 && index < AttributesLV.SelectedItems.Count)
            {
                AttributeInfo info = AttributesLV.SelectedItems[index].Tag as AttributeInfo;

                if (info != null)
                {
                    return info.NodeToRead;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the attributes for the node.
        /// </summary>
        public void ReadAttributes(NodeId nodeId, bool showProperties)
        {
            AttributesLV.Items.Clear();

            if (NodeId.IsNull(nodeId))
            {
                return;
            }

            // build list of attributes to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = attributeId;
                nodesToRead.Add(nodeToRead);
            }

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

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                // check for error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    if (results[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        continue;
                    }
                }

                // add the metadata for the attribute.
                uint attributeId = nodesToRead[ii].AttributeId;
                ListViewItem item = new ListViewItem(Attributes.GetBrowseName(attributeId));
                item.SubItems.Add(Attributes.GetBuiltInType(attributeId).ToString());

                if (Attributes.GetValueRank(attributeId) >= 0)
                {
                    item.SubItems[0].Text += "[]";
                }

                // add the value.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    item.SubItems.Add(results[ii].StatusCode.ToString());
                }
                else
                {
                    item.SubItems.Add(ClientUtils.GetAttributeDisplayText(m_session, attributeId, results[ii].WrappedValue));
                }

                item.Tag = new AttributeInfo() { NodeToRead = nodesToRead[ii], Value = results[ii] };
                item.ImageIndex = ClientUtils.GetImageIndex(nodesToRead[ii].AttributeId, results[ii].Value);

                // display in list.
                AttributesLV.Items.Add(item);
            }

            if (showProperties)
            {
                ReadProperties(nodeId);
            }

            // set the column widths.
            for (int ii = 0; ii < AttributesLV.Columns.Count; ii++)
            {
                AttributesLV.Columns[ii].Width = -2;
            }
        }
        #endregion

        #region AttributeInfo Class
        /// <summary>
        /// The saved information for an attribute/property displayed in the control. 
        /// </summary>
        private class AttributeInfo
        {
            public ReadValueId NodeToRead;
            public DataValue Value;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the properties for the node.
        /// </summary>
        private void ReadProperties(NodeId nodeId)
        {
            // build list of references to browse.
            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)NodeClass.Variable;
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            nodesToBrowse.Add(nodeToBrowse);

            // find properties.
            ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, View, nodesToBrowse, false);

            // build list of properties to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; references != null && ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                // ignore out of server references.
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = (NodeId)reference.NodeId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.Handle = reference;
                nodesToRead.Add(nodeToRead);
            }

            if (nodesToRead.Count == 0)
            {
                return;
            }

            // read the properties.
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

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                ReferenceDescription reference = (ReferenceDescription)nodesToRead[ii].Handle;

                TypeInfo typeInfo = TypeInfo.Construct(results[ii].Value);

                // add the metadata for the attribute.
                ListViewItem item = new ListViewItem(reference.ToString());
                item.SubItems.Add(typeInfo.BuiltInType.ToString());

                if (typeInfo.ValueRank >= 0)
                {
                    item.SubItems[1].Text += "[]";
                }

                // add the value.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    item.SubItems.Add(results[ii].StatusCode.ToString());
                }
                else
                {
                    item.SubItems.Add(results[ii].WrappedValue.ToString());
                }

                item.Tag = new AttributeInfo() { NodeToRead = nodesToRead[ii], Value = results[ii] };
                item.ImageIndex = ClientUtils.GetImageIndex(m_session, NodeClass.Variable, Opc.Ua.VariableTypeIds.PropertyType, false);

                // display in list.
                AttributesLV.Items.Add(item);
            }
        }
        #endregion

        #region Event Handlers
        private void AttributesLV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (AttributesLV.SelectedItems.Count == 0)
                {
                    return;
                }

                AttributeInfo info = AttributesLV.SelectedItems[0].Tag as AttributeInfo;

                if (info == null || info.Value == null)
                {
                    return;
                }

                new EditComplexValueDlg().ShowDialog(
                    m_session,
                    info.NodeToRead.NodeId,
                    info.NodeToRead.AttributeId,
                    null, 
                    info.Value.Value,
                    true,
                    "View Attribute Value");
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
