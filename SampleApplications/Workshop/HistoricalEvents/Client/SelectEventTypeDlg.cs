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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SelectEventTypeDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SelectEventTypeDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private FilterDefinition m_filter;
        private FilterDefinition m_newFilter;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public FilterDefinition ShowDialog(Session session, FilterDefinition filter)
        {
            m_session = session;
            m_filter = filter;

            TreeNode root = new TreeNode(Opc.Ua.BrowseNames.BaseEventType);
            root.Nodes.Add(new TreeNode());
            BrowseTV.Nodes.Add(root);
            root.Expand();

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            // ensure selection is valid.
            if (BrowseTV.SelectedNode == null)
            {
                return null;
            }

            // update selected fields.
            for (int ii = 0; ii < EventFieldsLV.Items.Count; ii++)
            {
                FilterDefinitionField field = EventFieldsLV.Items[ii].Tag as FilterDefinitionField;

                if (field != null)
                {
                    field.ShowColumn = EventFieldsLV.Items[ii].Checked;
                }
            }

            // return the result.
            return m_newFilter;
        }
        #endregion
        
        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the DoubleClick event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void BrowseTV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (BrowseTV.SelectedNode == null)
                {
                    return;
                }

                if (OkBTN.Enabled)
                {
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Handles the AfterSelect event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewEventArgs"/> instance containing the event data.</param>
        private void BrowseTV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                EventFieldsLV.Items.Clear();

                FilterDefinition filter = m_newFilter = new FilterDefinition();
                filter.EventTypeId = null;
                filter.Fields = new List<FilterDefinitionField>();

                if (e.Node == null)
                {
                    OkBTN.Enabled = false;
                    return;
                }

                OkBTN.Enabled = true;

                // get the currently selected event.
                NodeId eventTypeId = Opc.Ua.ObjectTypeIds.BaseEventType;
                ReferenceDescription reference = e.Node.Tag as ReferenceDescription;

                if (reference != null)
                {
                    eventTypeId = (NodeId)reference.NodeId;
                }

                filter.EventTypeId = eventTypeId;

                // collect all of the fields defined for the event.
                SimpleAttributeOperandCollection fields = new SimpleAttributeOperandCollection();
                List<NodeId> declarationIds = new List<NodeId>();
                FormUtils.CollectFieldsForType(m_session, eventTypeId, fields, declarationIds);
                
                // need to read the description and datatype for each field. 
                ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

                for (int ii = 0; ii < declarationIds.Count; ii++)
                {
                    ReadValueId valueToRead = new ReadValueId();
                    valueToRead.NodeId = declarationIds[ii];
                    valueToRead.AttributeId = Attributes.Description;
                    valuesToRead.Add(valueToRead);

                    valueToRead = new ReadValueId();
                    valueToRead.NodeId = declarationIds[ii];
                    valueToRead.AttributeId = Attributes.DataType;
                    valuesToRead.Add(valueToRead);

                    valueToRead = new ReadValueId();
                    valueToRead.NodeId = declarationIds[ii];
                    valueToRead.AttributeId = Attributes.ValueRank;
                    valuesToRead.Add(valueToRead);
                }

                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    valuesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                // collect values. ignore errors since data used for display only.
                List<LocalizedText> descriptions = new List<LocalizedText>();
                List<NodeId> datatypes = new List<NodeId>();
                List<int> valueRanks = new List<int>();

                for (int ii = 0; ii < declarationIds.Count*3; ii += 3)
                {
                    descriptions.Add(results[ii].GetValue<LocalizedText>(LocalizedText.Null));
                    datatypes.Add(results[ii+1].GetValue<NodeId>(NodeId.Null));
                    valueRanks.Add(results[ii+2].GetValue<int>(ValueRanks.Any));
                }

                // populate the list box.
                for (int ii = 0; ii < fields.Count; ii++)
                {
                    FilterDefinitionField field = new FilterDefinitionField();
                    filter.Fields.Add(field);

                    field.Operand = fields[ii];

                    StringBuilder displayName = new StringBuilder();

                    for (int jj = 0; jj < field.Operand.BrowsePath.Count; jj++)
                    {
                        if (displayName.Length > 0)
                        {
                            displayName.Append('/');
                        }

                        displayName.Append(field.Operand.BrowsePath[jj].Name);
                    }

                    field.DisplayName = displayName.ToString();
                    field.DataType = datatypes[ii];
                    field.ValueRank = valueRanks[ii];
                    field.BuiltInType = DataTypes.GetBuiltInType(field.DataType, m_session.TypeTree);
                    field.Description = descriptions[ii].ToString();

                    // preserve previous settings.
                    for (int jj = 0; jj < m_filter.Fields.Count; jj++)
                    {
                        if (m_filter.Fields[jj].DisplayName == field.DisplayName)
                        {
                            field.ShowColumn = m_filter.Fields[jj].ShowColumn;
                            field.FilterValue = m_filter.Fields[jj].FilterValue;
                            break;
                        }
                    }

                    ListViewItem item = new ListViewItem(field.DisplayName);
                    item.SubItems.Add(String.Empty);
                    item.SubItems.Add(String.Empty);
                    item.Checked = field.ShowColumn;
                    item.Tag = field;

                    INode dataType = m_session.NodeCache.Find(datatypes[ii]);

                    if (dataType != null)
                    {
                        displayName = new StringBuilder();
                        displayName.Append(dataType.ToString());

                        if (valueRanks[ii] >= 0)
                        {
                            displayName.Append("[]");
                        }

                        field.DataTypeDisplayName = displayName.ToString();
                        item.SubItems[1].Text = field.DataTypeDisplayName;
                    }

                    item.SubItems[2].Text = descriptions[ii].ToString();
                    EventFieldsLV.Items.Add(item);
                }

                // resize columns to fit text.
                for (int ii = 0; ii < EventFieldsLV.Columns.Count; ii++)
                {
                    EventFieldsLV.Columns[ii].Width = -2;
                } 
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Handles the BeforeExpand event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewCancelEventArgs"/> instance containing the event data.</param>
        private void BrowseTV_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;
                e.Node.Nodes.Clear();

                // browse HasEventSource to display the sources but it won't be possible to select them.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = Opc.Ua.ObjectTypeIds.BaseEventType;
                nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false;
                nodeToBrowse.NodeClassMask = 0;
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                if (reference != null)
                {
                    nodeToBrowse.NodeId = (NodeId)reference.NodeId;
                }
                
                // add the childen to the control.
                ReferenceDescriptionCollection references = FormUtils.Browse(m_session, nodeToBrowse, false);
                
                for (int ii = 0; ii < references.Count; ii++)
                {
                    reference = references[ii];

                    // ignore out of server references.
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    TreeNode child = new TreeNode(reference.ToString());
                    child.Nodes.Add(new TreeNode());
                    child.Tag = reference;

                    e.Node.Nodes.Add(child);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion
    }
}
