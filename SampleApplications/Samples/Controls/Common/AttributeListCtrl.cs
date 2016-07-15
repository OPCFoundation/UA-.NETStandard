/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class AttributeListCtrl : BaseListCtrl
    {

        #region Private Fields
        private Session m_session;
        private NodeId m_nodeId;
        private bool m_readOnly;
		#endregion

        #region Public Interface
        /// <summary>
        /// 
        /// </summary>
        public bool ReadOnly
        {
            get { return m_readOnly;  }
            set { m_readOnly = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
        }

        /// <summary>
        /// Sets the nodes in the control.
        /// </summary>
        public void Initialize(Session session, ExpandedNodeId nodeId)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            Clear();

            if (nodeId == null)
            {
                return;                
            }
            
            m_session = session;
            m_nodeId  = (NodeId)nodeId;

            INode node = m_session.NodeCache.Find(m_nodeId);

            if (node != null && (node.NodeClass & (NodeClass.Variable | NodeClass.Object)) != 0)
            {
                AddReferences(ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward);
                AddReferences(ReferenceTypeIds.HasModellingRule,  BrowseDirection.Forward);
            }

            AddAttributes();
            AddProperties();
        }
		#endregion
        
        #region NodeField Class
        /// <summary>
        /// A field associated with a node.
        /// </summary>
        private class NodeField
        {            
            public string         Name;
            public object         Value;
            public StatusCode     StatusCode;
            public DiagnosticInfo DiagnosticInfo;
            public ReadValueId    ValueId;
        }
		#endregion

        #region Private Methods
        /// <summary>
        /// Adds the attributes to the control.
        /// </summary>
        private void AddAttributes()
        {
            // build list of attributes to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                ReadValueId valueId = new ReadValueId();

                valueId.NodeId       = m_nodeId;
                valueId.AttributeId  = attributeId;
                valueId.IndexRange   = null;
                valueId.DataEncoding = null;

                nodesToRead.Add(valueId);
            }

            // read attributes.
            DataValueCollection values;
            DiagnosticInfoCollection diagnosticInfos;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // update control.
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                // check if node supports attribute.
                if (values[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                {
                    continue;
                }

                NodeField field = new NodeField();

                field.ValueId        = nodesToRead[ii];
                field.Name           = Attributes.GetBrowseName(nodesToRead[ii].AttributeId);
                field.Value          = values[ii].Value;
                field.StatusCode     = values[ii].StatusCode;

                if (diagnosticInfos != null && diagnosticInfos.Count > ii)
                {
                    field.DiagnosticInfo = diagnosticInfos[ii];
                }

                AddItem(field, "SimpleItem", -1);
            }
        }

        private void AddItem(NodeField field, string v1, int v2)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds the properties to the control.
        /// </summary>
        private void AddProperties()
        {
            // build list of properties to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            Browser browser = new Browser(m_session);
            
            browser.BrowseDirection   = BrowseDirection.Forward;
            browser.ReferenceTypeId   = ReferenceTypeIds.HasProperty;
            browser.IncludeSubtypes   = true;
            browser.NodeClassMask     = (int)NodeClass.Variable;
            browser.ContinueUntilDone = true;

            ReferenceDescriptionCollection references = browser.Browse(m_nodeId);

            foreach (ReferenceDescription reference in references)
            {
                ReadValueId valueId = new ReadValueId();

                valueId.NodeId       = (NodeId)reference.NodeId;
                valueId.AttributeId  = Attributes.Value;
                valueId.IndexRange   = null;
                valueId.DataEncoding = null;

                nodesToRead.Add(valueId);
            }

            // check for empty list.
            if (nodesToRead.Count == 0)
            {
                return;
            }

            // read values.
            DataValueCollection values;
            DiagnosticInfoCollection diagnosticInfos;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);           

            // update control.
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                NodeField field = new NodeField();

                field.ValueId    = nodesToRead[ii];
                field.Name       = references[ii].ToString();
                field.Value      = values[ii].Value;
                field.StatusCode = values[ii].StatusCode;

                if (diagnosticInfos != null && diagnosticInfos.Count > ii)
                {
                    field.DiagnosticInfo = diagnosticInfos[ii];
                }

                AddItem(field, "Property", -1);
            }
        }

        /// <summary>
        /// Adds the targets of references to the control.
        /// </summary>
        private void AddReferences(NodeId referenceTypeId, BrowseDirection browseDirection)
        {
            // fetch the attributes for the reference type.
            INode referenceType = m_session.NodeCache.Find(referenceTypeId);

            if (referenceType == null)
            {
                return;
            }
            
            // browse for the references.
            Browser browser = new Browser(m_session);
            
            browser.BrowseDirection   = browseDirection;
            browser.ReferenceTypeId   = referenceTypeId;
            browser.IncludeSubtypes   = true;
            browser.NodeClassMask     = 0;
            browser.ContinueUntilDone = true;

            ReferenceDescriptionCollection references = browser.Browse(m_nodeId);

            // add results to list.
            foreach (ReferenceDescription reference in references)
            {
                NodeField field = new NodeField();

                field.Name       = referenceType.ToString();     
                field.Value      = reference.ToString();
                field.StatusCode = StatusCodes.Good;

                AddItem(field, "ReferenceType", -1);
            }
        }

        /// <summary>
        /// Formats the value of an attribute.
        /// </summary>
        private string FormatAttributeValue(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.NodeClass:
                {
                    if (value != null)
                    {
                        return String.Format("{0}", Enum.ToObject(typeof(NodeClass), value));
                    }

                    return "(null)";
                }
                    
                case Attributes.DataType:
                {
                    NodeId datatypeId = value as NodeId;

                    if (datatypeId != null)
                    {
                        INode datatype = m_session.NodeCache.Find(datatypeId);

                        if (datatype != null)
                        {
                            return String.Format("{0}", datatype.DisplayName.Text);
                        }
                        else
                        {
                            return String.Format("{0}", datatypeId);
                        }
                    }
                
                    return String.Format("{0}", value);
                }
                      
                case Attributes.ValueRank:
                {
                    int? valueRank = value as int?;

                    if (valueRank != null)
                    {
                        switch (valueRank.Value)
                        {
                            case ValueRanks.Scalar:              return "Scalar";
                            case ValueRanks.OneDimension:        return "OneDimension";
                            case ValueRanks.OneOrMoreDimensions: return "OneOrMoreDimensions";
                            case ValueRanks.Any:                 return "Any";

                            default:
                            {
                                return String.Format("{0}", valueRank.Value);
                            }
                        }                            
                    }

                    return String.Format("{0}", value);
                }
                      
                case Attributes.MinimumSamplingInterval:
                {
                    double? minimumSamplingInterval = value as double?;

                    if (minimumSamplingInterval != null)
                    {
                        if (minimumSamplingInterval.Value == MinimumSamplingIntervals.Indeterminate)
                        {
                            return "Indeterminate";
                        }

                        else if (minimumSamplingInterval.Value == MinimumSamplingIntervals.Continuous)
                        {
                            return "Continuous";
                        }

                       return String.Format("{0}", minimumSamplingInterval.Value);
                    }

                    return String.Format("{0}", value);
                }

                case Attributes.AccessLevel:
                case Attributes.UserAccessLevel:
                {
                    byte accessLevel = Convert.ToByte(value);

                    StringBuilder bits = new StringBuilder();

                    if ((accessLevel & AccessLevels.CurrentRead) != 0)
                    {
                        bits.Append("Readable");
                    }
                    
                    if ((accessLevel & AccessLevels.CurrentWrite) != 0)
                    {
                        if (bits.Length > 0)
                        {
                            bits.Append(" | ");
                        }
                           
                        bits.Append("Writeable");
                    }
                    
                    if ((accessLevel & AccessLevels.HistoryRead) != 0)
                    {
                        if (bits.Length > 0)
                        {
                            bits.Append(" | ");
                        }
                           
                        bits.Append("History");
                    }
                    
                    if ((accessLevel & AccessLevels.HistoryWrite) != 0)
                    {
                        if (bits.Length > 0)
                        {
                            bits.Append(" | ");
                        }
                           
                        bits.Append("History Update");
                    }
                    
                    if (bits.Length == 0)
                    {
                        bits.Append("No Access");
                    }

                    return String.Format("{0}", bits);
                }
               
                case Attributes.EventNotifier:
                {
                    byte notifier = Convert.ToByte(value);

                    StringBuilder bits = new StringBuilder();

                    if ((notifier & EventNotifiers.SubscribeToEvents) != 0)
                    {
                        bits.Append("Subscribe");
                    }
                    
                    if ((notifier & EventNotifiers.HistoryRead) != 0)
                    {
                        if (bits.Length > 0)
                        {
                            bits.Append(" | ");
                        }
                           
                        bits.Append("History");
                    }
                    
                    if ((notifier & EventNotifiers.HistoryWrite) != 0)
                    {
                        if (bits.Length > 0)
                        {
                            bits.Append(" | ");
                        }
                           
                        bits.Append("History Update");
                    }
                    
                    if (bits.Length == 0)
                    {
                        bits.Append("No Access");
                    }

                    return String.Format("{0}", bits);
                }

                default:
                {
                    return String.Format("{0}", value);
                }
            }
        }
		#endregion
        
        #region Overridden Methods
        /// <see cref="BaseListCtrl.GetDataToDrag" />
        protected override object GetDataToDrag()
        {
            ReadValueIdCollection valueIds = new ReadValueIdCollection();

            foreach (ListViewItem listItem in ItemsLV.SelectedItems)
            {
                NodeField field = listItem.Tag as NodeField;

                if (field != null && field.ValueId != null)
                {
                    valueIds.Add(field.ValueId);
                }
            }

            return valueIds;
        }

        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            EditMI_Click(this, null);
        }

        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            NodeField[] items = GetSelectedItems(typeof(NodeField)) as NodeField[];
		}

        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			NodeField field = item as NodeField;

			if (field == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

			listItem.Tag = item;
        }
        #endregion

        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {
                NodeField[] items = GetSelectedItems(typeof(NodeField)) as NodeField[];

                if (items != null && items.Length == 1)
                {                  
                    object value = GuiUtils.EditValue(m_session, items[0].Value);

                    if (!m_readOnly)
                    {
                        if (value != null)
                        {
                            items[0].Value = value;
                            UpdateItem((ListViewItem) ItemsLV.SelectedItems[0], items[0]);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
    }
}
