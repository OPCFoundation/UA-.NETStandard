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
using System.Text;
using Opc.Ua;

namespace Quickstarts.HistoricalEvents.Client
{
    public class FilterDefinition
    {
        /// <summary>
        /// The type of event.
        /// </summary>
        public NodeId EventTypeId;

        /// <summary>
        /// The fields belonging to the event.
        /// </summary>
        public List<FilterDefinitionField> Fields;
        
        /// <summary>
        /// Sets a default filter.
        /// </summary>
        public void SetDefault()
        {
            EventTypeId = Opc.Ua.ObjectTypeIds.BaseEventType;
            Fields = new List<FilterDefinitionField>();

            FilterDefinitionField field = new FilterDefinitionField();
            field.DisplayName = Opc.Ua.BrowseNames.EventType;
            field.DataType = Opc.Ua.DataTypeIds.NodeId;
            field.ValueRank = ValueRanks.Scalar;
            field.BuiltInType = BuiltInType.NodeId;
            field.DataTypeDisplayName = field.BuiltInType.ToString();
            field.Operand = new SimpleAttributeOperand();
            field.Operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            field.Operand.BrowsePath.Add(Opc.Ua.BrowseNames.EventType);
            field.Operand.AttributeId = Attributes.Value;
            field.ShowColumn = true;
            Fields.Add(field);

            field = new FilterDefinitionField();
            field.DisplayName = Opc.Ua.BrowseNames.SourceNode;
            field.DataType = Opc.Ua.DataTypeIds.NodeId;
            field.ValueRank = ValueRanks.Scalar;
            field.BuiltInType = BuiltInType.NodeId;
            field.DataTypeDisplayName = field.BuiltInType.ToString();
            field.Operand = new SimpleAttributeOperand();
            field.Operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            field.Operand.BrowsePath.Add(Opc.Ua.BrowseNames.SourceNode);
            field.Operand.AttributeId = Attributes.Value;
            field.ShowColumn = true;
            Fields.Add(field);

            field = new FilterDefinitionField();
            field.DisplayName = Opc.Ua.BrowseNames.Time;
            field.DataType = Opc.Ua.DataTypeIds.DateTime;
            field.ValueRank = ValueRanks.Scalar;
            field.BuiltInType = BuiltInType.DateTime;
            field.DataTypeDisplayName = field.BuiltInType.ToString();
            field.Operand = new SimpleAttributeOperand();
            field.Operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            field.Operand.BrowsePath.Add(Opc.Ua.BrowseNames.Time);
            field.Operand.AttributeId = Attributes.Value;
            field.ShowColumn = true;
            Fields.Add(field);

            field = new FilterDefinitionField();
            field.DisplayName = Opc.Ua.BrowseNames.EventId;
            field.DataType = Opc.Ua.DataTypeIds.ByteString;
            field.ValueRank = ValueRanks.Scalar;
            field.BuiltInType = BuiltInType.ByteString;
            field.DataTypeDisplayName = field.BuiltInType.ToString();
            field.Operand = new SimpleAttributeOperand();
            field.Operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            field.Operand.BrowsePath.Add(Opc.Ua.BrowseNames.EventId);
            field.Operand.AttributeId = Attributes.Value;
            field.ShowColumn = false;
            Fields.Add(field);

            field = new FilterDefinitionField();
            field.DisplayName = Opc.Ua.BrowseNames.Message;
            field.DataType = Opc.Ua.DataTypeIds.LocalizedText;
            field.ValueRank = ValueRanks.Scalar;
            field.BuiltInType = BuiltInType.LocalizedText;
            field.DataTypeDisplayName = field.BuiltInType.ToString();
            field.Operand = new SimpleAttributeOperand();
            field.Operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            field.Operand.BrowsePath.Add(Opc.Ua.BrowseNames.Message);
            field.Operand.AttributeId = Attributes.Value;
            field.ShowColumn = true;
            Fields.Add(field);
        }

        /// <summary>
        /// Returns the subscription filter to use.
        /// </summary>
        public EventFilter GetFilter()
        {
            ContentFilter whereClause = new ContentFilter();
            ContentFilterElement element1 = whereClause.Push(FilterOperator.OfType, EventTypeId);

            EventFilter filter = new EventFilter();

            for (int ii = 0; ii < Fields.Count; ii++)
            {
                filter.SelectClauses.Add(Fields[ii].Operand);

                if (Fields[ii].FilterValue != Variant.Null)
                {
                    LiteralOperand operand = new LiteralOperand();
                    operand.Value = Fields[ii].FilterValue;
                    ContentFilterElement element2 = whereClause.Push(Fields[ii].FilterOperator, Fields[ii].Operand, operand);
                    element1 = whereClause.Push(FilterOperator.And, element1, element2);
                }
            }

            filter.WhereClause = whereClause;

            return filter;
        }
    }
}
