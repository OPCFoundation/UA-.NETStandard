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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A declararion of an event filter.
    /// </summary>
    public class AeEventFilter
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of an AeEventFilter.
        /// </summary>
        public AeEventFilter(ComAeNamespaceMapper mapper)
        {
            m_mapper = mapper;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The event types requested by the client.
        /// </summary>
        public int EventTypes { get; private set; }

        /// <summary>
        /// The minimum severity for events to report.
        /// </summary>
        public ushort LowSeverity { get; private set; }

        /// <summary>
        /// The maxnimum severity for events to report.
        /// </summary>
        public ushort HighSeverity { get; private set; }

        /// <summary>
        /// The category ids requested by the client.
        /// </summary>
        public uint[] RequestedCategoryIds { get; private set; }

        /// <summary>
        /// The category ids requested by the client.
        /// </summary>
        public Dictionary<uint, uint[]> RequestedAttributeIds { get; private set; }

        /// <summary>
        /// The optimized list of category ids (duplicates removed).
        /// </summary>
        public List<AeEventCategory> RevisedCategories { get; set; }

        /// <summary>
        /// The sources selected.
        /// </summary>
        public List<NodeId> SelectedSources { get; set; }

        /// <summary>
        /// The optimized list of attributes ids (duplicates removed).
        /// </summary>
        public List<AeEventAttribute> SelectedAttributes { get; set; }

        /// <summary>
        /// Sets the categories.
        /// </summary>
        public void SetFilter(int eventTypes, ushort lowSeverity, ushort highSeverity, uint[] categoryIds, List<NodeId> sources)
        {
            RevisedCategories = new List<AeEventCategory>();

            // update list of sources.
            SelectedSources = new List<NodeId>();

            if (sources != null)
            {
                SelectedSources.AddRange(sources);
            }

            for (int ii = 0; categoryIds != null && ii < categoryIds.Length; ii++)
            {
                AeEventCategory category = m_mapper.GetCategory(categoryIds[ii]);

                // ignore unknown categories.
                if (category == null)
                {
                    continue;
                }

                // ignore categories if the event types mask filters them out.
                if ((category.EventType & eventTypes) == 0)
                {
                    continue;
                }

                // add category.
                RevisedCategories.Add(category);
            }

            // save the original ids.
            EventTypes = eventTypes;
            LowSeverity = lowSeverity;
            HighSeverity = highSeverity;
            RequestedCategoryIds = categoryIds;
            
            // update selected attributes.
            UpdatedSelectAttributes();
        }

        /// <summary>
        /// Selects the attributes for the category.
        /// </summary>
        public bool SelectAttributes(uint categoryId, uint[] attributeIds)
        {
            // validate category.
            if (m_mapper.GetCategory(categoryId) == null)
            {
                return false;
            }

            // validate attributes.
            if (attributeIds != null)
            {
                for (int ii = 0; ii < attributeIds.Length; ii++)
                {
                    if (m_mapper.GetAttribute(attributeIds[ii]) == null)
                    {
                        return false;
                    }
                }
            }

            // update list.
            if (RequestedAttributeIds == null)
            {
                RequestedAttributeIds = new Dictionary<uint, uint[]>();
            }

            if (attributeIds == null || attributeIds.Length == 0)
            {
                RequestedAttributeIds.Remove(categoryId);
            }
            else
            {
                RequestedAttributeIds[categoryId] = attributeIds;
            }

            UpdatedSelectAttributes();
            return true;
        }

        /// <summary>
        /// Returns the event filter defined by the filter declaration.
        /// </summary>
        public EventFilter GetFilter()
        {
            EventFilter filter = new EventFilter();
            filter.SelectClauses = GetSelectClause();
            filter.WhereClause = GetWhereClause();
            return filter;
        }

        /// <summary>
        /// Translates an event notification in an AE event.
        /// </summary>
        public AeEvent TranslateNotification(Session session, EventFieldList e)
        {
            AeEvent e2 = new AeEvent();

            // extract the required event fields.
            int index = 0;

            e2.EventId = ExtractField<byte[]>(e, index++, null);
            e2.EventType = ExtractField<NodeId>(e, index++, null);
            e2.SourceName = ExtractField<string>(e, index++, null);
            e2.Time = ExtractField<DateTime>(e, index++, DateTime.MinValue);
            e2.Message = ExtractField<LocalizedText>(e, index++, null);
            e2.Severity = ExtractField<ushort>(e, index++, 0);

            if ((EventTypes & OpcRcw.Ae.Constants.TRACKING_EVENT) != 0)
            {
                e2.AuditUserId = ExtractField<string>(e, index++, null);
            }

            if ((EventTypes & OpcRcw.Ae.Constants.CONDITION_EVENT) != 0)
            {
                e2.BranchId = ExtractField<byte[]>(e, index++, null);
                e2.ConditionName = ExtractField<string>(e, index++, null);
                e2.Quality = ExtractField<StatusCode>(e, index++, StatusCodes.Good);
                e2.Comment = ExtractField<LocalizedText>(e, index++, null);
                e2.CommentUserId = ExtractField<string>(e, index++, null);
                e2.EnabledState = ExtractField<bool>(e, index++, false);
                e2.AckedState = ExtractField<bool>(e, index++, false);
                e2.ActiveState = ExtractField<bool>(e, index++, false);
                e2.ActiveTime = ExtractField<DateTime>(e, index++, DateTime.MinValue);
                e2.LimitState = ExtractField<LocalizedText>(e, index++, null);
                e2.HighHighState = ExtractField<LocalizedText>(e, index++, null);
                e2.HighState = ExtractField<LocalizedText>(e, index++, null);
                e2.LowState = ExtractField<LocalizedText>(e, index++, null);
                e2.LowLowState = ExtractField<LocalizedText>(e, index++, null);

                // condition id is always last.
                e2.ConditionId = ExtractField<NodeId>(e, e.EventFields.Count - 1, null);
            }

            // find the category for the event.
            e2.Category = FindCategory(session, e2);

            // extract any additional attributes.
            if (RequestedAttributeIds != null)
            {
                uint[] attributeIds = null;

                if (!RequestedAttributeIds.TryGetValue(e2.Category.LocalId, out attributeIds))
                {
                    return e2;
                }

                // nothing more to do.
                if (attributeIds == null || attributeIds.Length == 0)
                {
                    return e2;
                }

                // search for the requested attributes.
                object[] values = new object[attributeIds.Length];

                for (int ii = 0; ii < attributeIds.Length; ii++)
                {
                    // look for matching attribute.
                    for (int jj = 0; jj < SelectedAttributes.Count; jj++)
                    {
                        if (jj >= e.EventFields.Count)
                        {
                            break;
                        }

                        AeEventAttribute attribute = SelectedAttributes[jj];

                        if (attribute == null || attribute.LocalId != attributeIds[ii])
                        {
                            continue;
                        }

                        values[ii] = GetLocalAttributeValue(e.EventFields[jj], attribute);
                    }
                }

                e2.AttributeValues = values;
            }

            return e2;
        }

        /// <summary>
        /// Converts an event field to a locally useable attribute value.
        /// </summary>
        private object GetLocalAttributeValue(Variant fieldValue, AeEventAttribute attribute)
        {
            // check for null.
            if (fieldValue == Variant.Null)
            {
                return null;
            }

            // check that the data type is what is expected.
            TypeInfo typeInfo = fieldValue.TypeInfo;

            if (typeInfo == null)
            {
                typeInfo = TypeInfo.Construct(fieldValue);
            }

            if (attribute.BuiltInType != BuiltInType.Variant && typeInfo.BuiltInType != attribute.BuiltInType)
            {
                return null;
            }

            // check for expected array dimension.
            if (attribute.ValueRank >= 0 && typeInfo.ValueRank == ValueRanks.Scalar)
            {
                return null;
            }

            if (typeInfo.ValueRank != ValueRanks.Scalar)
            {
                if (attribute.ValueRank == ValueRanks.ScalarOrOneDimension && typeInfo.ValueRank != 1)
                {
                    return null;
                }

                if (attribute.ValueRank != ValueRanks.Any && attribute.ValueRank != typeInfo.ValueRank)
                {
                    return null;
                }
            }

            // map to local value.
            return m_mapper.GetLocalValue(fieldValue);
        }

        /// <summary>
        /// Finds the category for the event.
        /// </summary>
        private AeEventCategory FindCategory(Session session, AeEvent e)
        {
            AeEventCategory category = m_mapper.GetCategory(e.EventType);

            NodeId subTypeId = e.EventType;
            NodeId superTypeId = null;

            // follow the type tree if type not recognized.
            while (category == null)
            {
                superTypeId = session.NodeCache.FindSuperType(subTypeId);

                if (!NodeId.IsNull(superTypeId))
                {
                    category = m_mapper.GetCategory(superTypeId);

                    if (category != null)
                    {
                        return category;
                    }
                }

                subTypeId = superTypeId;
            }

            // default to base event type.
            if (category == null)
            {
                category = m_mapper.GetCategory(Opc.Ua.ObjectTypeIds.BaseEventType);
            }

            return category;
        }

        /// <summary>
        /// Extracts a field value from an incoming event.
        /// </summary>
        private T ExtractField<T>(EventFieldList e, int index, T defaultValue)
        {
            if (e == null || index >= e.EventFields.Count || index < 0)
            {
                return defaultValue;
            }

            Variant value = e.EventFields[index];

            if (!typeof(T).IsInstanceOfType(value.Value))
            {
                return defaultValue;
            }

            return (T)value.Value;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the selected attributes for the current filters.
        /// </summary>
        private void UpdatedSelectAttributes()
        {
            SelectedAttributes = new List<AeEventAttribute>();

            // add attributes which always requested.
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.EventId));
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.EventType));
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.SourceName));
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Time));
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Message));
            SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Severity));

            // add tracking event attributes.
            if ((EventTypes & OpcRcw.Ae.Constants.TRACKING_EVENT) != 0)
            {
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.AuditEventType, Opc.Ua.BrowseNames.ClientUserId));
            }

            // add condition event attributes.
            if ((EventTypes & OpcRcw.Ae.Constants.CONDITION_EVENT) != 0)
            {
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.BranchId));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.ConditionName));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.Quality));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.Comment));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.ClientUserId));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ConditionType, Opc.Ua.BrowseNames.EnabledState, Opc.Ua.BrowseNames.Id));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.AcknowledgeableConditionType, Opc.Ua.BrowseNames.AckedState, Opc.Ua.BrowseNames.Id));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.AlarmConditionType, Opc.Ua.BrowseNames.ActiveState, Opc.Ua.BrowseNames.Id));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.AlarmConditionType, Opc.Ua.BrowseNames.ActiveState, Opc.Ua.BrowseNames.TransitionTime));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.ExclusiveLimitAlarmType, Opc.Ua.BrowseNames.LimitState, Opc.Ua.BrowseNames.CurrentState));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.NonExclusiveLimitAlarmType, Opc.Ua.BrowseNames.HighHighState));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.NonExclusiveLimitAlarmType, Opc.Ua.BrowseNames.HighState));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.NonExclusiveLimitAlarmType, Opc.Ua.BrowseNames.LowState));
                SelectedAttributes.Add(m_mapper.GetAttribute(Opc.Ua.ObjectTypeIds.NonExclusiveLimitAlarmType, Opc.Ua.BrowseNames.LowLowState));
            }

            if (RequestedAttributeIds != null)
            {
                // update list for all requested attributes.
                foreach (KeyValuePair<uint, uint[]> pair in RequestedAttributeIds)
                {
                    for (int ii = 0; ii < pair.Value.Length; ii++)
                    {
                        // check if already in list.
                        bool found = false;

                        for (int jj = 0; jj < SelectedAttributes.Count; jj++)
                        {
                            if (SelectedAttributes[jj] != null && SelectedAttributes[jj].LocalId == pair.Value[ii])
                            {
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            continue;
                        }

                        // verify that it exists.
                        AeEventAttribute attribute = m_mapper.GetAttribute(pair.Value[ii]);

                        if (attribute != null)
                        {
                            SelectedAttributes.Add(attribute);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a supertype was already selected.
        /// </summary>
        private bool IsSuperTypeSelected(AeEventCategory category, uint[] categoryIds)
        {
            AeEventCategory subType = category;
            AeEventCategory superType = null;

            while (subType != null)
            {
                superType = m_mapper.GetCategory(subType.SuperTypeId);

                if (superType == null)
                {
                    return false;
                }

                for (int ii = 0; ii < categoryIds.Length; ii++)
                {
                    if (categoryIds[ii] == superType.LocalId)
                    {
                        return true;
                    }
                }

                subType = superType;
            }

            return false;
        }

        /// <summary>
        /// Returns the select clause defined by the filter declaration.
        /// </summary>
        private SimpleAttributeOperandCollection GetSelectClause()
        {
            SimpleAttributeOperandCollection selectClause = new SimpleAttributeOperandCollection();

            // add the explicitly selected attributes.
            foreach (AeEventAttribute attribute in SelectedAttributes)
            {
                if (attribute != null)
                {
                    SimpleAttributeOperand operand = new SimpleAttributeOperand();
                    operand.TypeDefinitionId = attribute.RootTypeId;
                    operand.AttributeId = (attribute.NodeClass == NodeClass.Object) ? Attributes.NodeId : Attributes.Value;
                    operand.BrowsePath = attribute.BrowsePath;
                    selectClause.Add(operand);
                }
            }

            // need to request the condition id if condition events selected.
            if ((EventTypes & OpcRcw.Ae.Constants.CONDITION_EVENT) != 0)
            {
                SimpleAttributeOperand operand = new SimpleAttributeOperand();
                operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.ConditionType;
                operand.AttributeId = Attributes.NodeId;
                operand.BrowsePath.Clear();
                selectClause.Add(operand);
            }

            return selectClause;
        }

        /// <summary>
        /// Returns the where clause defined by the filter declaration.
        /// </summary>
        private ContentFilter GetWhereClause()
        {
            ContentFilter whereClause = new ContentFilter();
            
            ContentFilterElement element1 = null;

            // filter by source.
            if (SelectedSources != null && SelectedSources.Count > 0)
            {
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
                operand1.AttributeId = Attributes.Value;
                operand1.BrowsePath.Add(new QualifiedName(Opc.Ua.BrowseNames.SourceNode));

                for (int ii = 0; ii < SelectedSources.Count; ii++)
                {
                    LiteralOperand operand2 = new LiteralOperand();
                    operand2.Value = SelectedSources[ii];
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.Equals, operand1, operand2);
                    element1 = (element1 != null)?whereClause.Push(FilterOperator.Or, element1, element2):element2;
                }
            }

            // add condition/tracking categories if no other categories selected.
            if (RevisedCategories == null || RevisedCategories.Count == 0)
            {
                if (EventTypes  == (OpcRcw.Ae.Constants.SIMPLE_EVENT | OpcRcw.Ae.Constants.TRACKING_EVENT))
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.ConditionType);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.Not, element2);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element3) : element3;
                }
                                
                else if (EventTypes  == (OpcRcw.Ae.Constants.SIMPLE_EVENT | OpcRcw.Ae.Constants.CONDITION_EVENT))
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.AuditEventType);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.Not, element2);
                    ContentFilterElement element4 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.TransitionEventType);
                    ContentFilterElement element5 = whereClause.Push(FilterOperator.Not, element4);
                    ContentFilterElement element6 = whereClause.Push(FilterOperator.Or, element3, element5);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element6) : element6;
                }
                                
                else if (EventTypes  == (OpcRcw.Ae.Constants.TRACKING_EVENT | OpcRcw.Ae.Constants.CONDITION_EVENT))
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.AuditEventType);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.TransitionEventType);
                    ContentFilterElement element4 = whereClause.Push(FilterOperator.Or, element2, element3);
                    ContentFilterElement element5 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.ConditionType);
                    ContentFilterElement element6 = whereClause.Push(FilterOperator.Or, element4, element5);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element6) : element6;
                }

                else if (EventTypes == OpcRcw.Ae.Constants.TRACKING_EVENT)
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.AuditEventType);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.TransitionEventType);
                    ContentFilterElement element4 = whereClause.Push(FilterOperator.Or, element2, element3);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element4) : element4;
                }

                else if (EventTypes == OpcRcw.Ae.Constants.CONDITION_EVENT)
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.ConditionType);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element2) : element2;
                }

                else if (EventTypes == OpcRcw.Ae.Constants.SIMPLE_EVENT)
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.AuditEventType);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.Not, element2);
                    ContentFilterElement element4 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.TransitionEventType);
                    ContentFilterElement element5 = whereClause.Push(FilterOperator.Not, element4);
                    ContentFilterElement element6 = whereClause.Push(FilterOperator.Or, element3, element5);
                    ContentFilterElement element7 = whereClause.Push(FilterOperator.OfType, Opc.Ua.ObjectTypeIds.ConditionType);
                    ContentFilterElement element8 = whereClause.Push(FilterOperator.Not, element7);

                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element8) : element8;
                }
            }

            // filter by event type.
            if (RevisedCategories.Count > 0)
            {
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
                operand1.AttributeId = Attributes.Value;
                operand1.BrowsePath.Add(new QualifiedName(Opc.Ua.BrowseNames.EventType));

                ContentFilterElement element3 = null;

                for (int ii = 0; ii < RevisedCategories.Count; ii++)
                {
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.Equals, operand1, RevisedCategories[ii].TypeId);
                    element3 = (element3 != null) ? whereClause.Push(FilterOperator.Or, element2, element3) : element2;
                }

                element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element3) : element3;
            }

            // filter by severity.
            if (LowSeverity > 1 || HighSeverity < 1000)
            {
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
                operand1.AttributeId = Attributes.Value;
                operand1.BrowsePath.Add(new QualifiedName(Opc.Ua.BrowseNames.Severity));

                if (LowSeverity > 1)
                {
                    LiteralOperand operand2 = new LiteralOperand();
                    operand2.Value = LowSeverity;
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.GreaterThanOrEqual, operand1, operand2);
                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element2) : element2;
                }

                if (HighSeverity < 1000)
                {
                    LiteralOperand operand2 = new LiteralOperand();
                    operand2.Value = HighSeverity;
                    ContentFilterElement element2 = whereClause.Push(FilterOperator.LessThanOrEqual, operand1, operand2);
                    element1 = (element1 != null) ? whereClause.Push(FilterOperator.And, element1, element2) : element2;
                }
            }
            
            return whereClause;
        }
        #endregion

        #region Private Fields
        private ComAeNamespaceMapper m_mapper;
        #endregion
    }
}
