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
using System.Text;
using System.Windows.Forms;


namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of attributes and their values.
    /// </summary>
    public partial class AttributeListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AttributeListCtrl"/> class.
        /// </summary>
        public AttributeListCtrl()
        {
            InitializeComponent();
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
       
		// The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Name",  HorizontalAlignment.Left, null },  
			new object[] { "Value", HorizontalAlignment.Left, null }
		};

        private class ItemInfo
        {
            public NodeId NodeId;
            public uint AttributeId;
            public string Name;
            public DataValue Value;
        }
		#endregion
            
        /// <summary>
        /// Initializes the control with a set of items.
        /// </summary>
        public void Initialize(Session session, ExpandedNodeId nodeId)
        {
            ItemsLV.Items.Clear();
            m_session = session;

            if (m_session == null)
            {
                return;
            }

            ILocalNode node = m_session.NodeCache.Find(nodeId) as ILocalNode;

            if (node == null)
            {
                return;
            }

            uint[] attributesIds = Attributes.GetIdentifiers();

            for (int ii = 0; ii < attributesIds.Length; ii++)
            {
                uint attributesId = attributesIds[ii];

                if (!node.SupportsAttribute(attributesId))
                {
                    continue;
                }

                ItemInfo info = new ItemInfo();

                info.NodeId = node.NodeId;
                info.AttributeId = attributesId;
                info.Name = Attributes.GetBrowseName(attributesId);
                info.Value = new DataValue(StatusCodes.BadWaitingForInitialData);
                
                ServiceResult result = node.Read(null, attributesId, info.Value);

                if (ServiceResult.IsBad(result))
                {
                    info.Value = new DataValue(result.StatusCode);
                }

                AddItem(info);
            }

            IList<IReference> references = node.References.Find(ReferenceTypes.HasProperty, false, true, m_session.TypeTree);

            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];

                ILocalNode property = m_session.NodeCache.Find(reference.TargetId) as ILocalNode;

                if (property == null)
                {
                    return;
                }

                ItemInfo info = new ItemInfo();

                info.NodeId = property.NodeId;
                info.AttributeId = Attributes.Value;
                info.Name = Utils.Format("{0}", property.DisplayName);
                info.Value = new DataValue(StatusCodes.BadWaitingForInitialData);
                
                ServiceResult result = property.Read(null, Attributes.Value, info.Value);

                if (ServiceResult.IsBad(result))
                {
                    info.Value = new DataValue(result.StatusCode);
                }

                AddItem(info);
            }

            UpdateValues();
        }

        /// <summary>
        /// Updates the values from the server.
        /// </summary>
        private void UpdateValues()
        {
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            foreach (ListViewItem item in ItemsLV.Items)
            {
                ItemInfo info = item.Tag as ItemInfo;

			    if (info == null)
			    {
                    continue;
                }

                ReadValueId valueToRead = new ReadValueId();

                valueToRead.NodeId = info.NodeId;
                valueToRead.AttributeId = info.AttributeId;
                valueToRead.Handle = item;

                valuesToRead.Add(valueToRead);
            }

            DataValueCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                valuesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, valuesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            for (int ii = 0; ii < valuesToRead.Count; ii++)
            {
                ListViewItem item = (ListViewItem)valuesToRead[ii].Handle;
                ItemInfo info = (ItemInfo)item.Tag;
                info.Value = results[ii];
                UpdateItem(item, info);
            }

            AdjustColumns();
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
                           
                        bits.Append("History Read");
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

        #region Overridden Methods
        /// <see cref="Opc.Ua.Client.Controls.BaseListCtrl.UpdateItem(ListViewItem,object)" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ItemInfo info = item as ItemInfo;

			if (info == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
            
			listItem.SubItems[0].Text = Utils.Format("{0}", info.Name);

            if (StatusCode.IsBad(info.Value.StatusCode))
            {
			    listItem.SubItems[1].Text = Utils.Format("{0}", info.Value.StatusCode);
            }
            else
            {
			    listItem.SubItems[1].Text = FormatAttributeValue(info.AttributeId, info.Value.Value);
            }

            if (info.AttributeId != Attributes.Value)
            {
                listItem.ImageKey = GuiUtils.Icons.Attribute;
            }
            else
            {
                listItem.ImageKey = GuiUtils.Icons.Property;
            }

			listItem.Tag = info;
        }
        #endregion
    }
}
