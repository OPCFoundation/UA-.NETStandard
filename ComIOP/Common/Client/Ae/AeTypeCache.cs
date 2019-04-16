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
using Opc.Ua;
using Opc.Ua.Server;
using OpcRcw.Ae;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the type information provided by the AE server.
    /// </summary>
    internal class AeTypeCache
    {
        /// <summary>
        /// A table of event types returned from the server.
        /// </summary>
        public List<EventType> EventTypes { get; set; }

        /// <summary>
        /// A table of attributes for each event category supported by the server,
        /// </summary>
        public Dictionary<int,int[]> Attributes { get; set; }

        /// <summary>
        /// A table of node reprenting the AE event catgories and condtions.
        /// </summary>
        public NodeIdDictionary<BaseObjectTypeState> EventTypeNodes { get; set; }

        /// <summary>
        /// Returns the node for the event type mapping identified by the node id.
        /// </summary>
        public AeEventTypeMappingState GetMappingNode(ServerSystemContext context, NodeId nodeId)
        {
            BaseObjectTypeState objectType = null;

            if (!EventTypeNodes.TryGetValue(nodeId, out objectType))
            {
                return null;
            }

            AeEventTypeMappingState mappingNode = objectType as AeEventTypeMappingState;

            if (mappingNode == null)
            {
                return null;
            }

            if (context.TypeTable.FindSubTypes(mappingNode.NodeId).Count == 0)
            {
                return null;
            }

            return mappingNode;
        }

        /// <summary>
        /// Returns the type identified by the category id and condition name.
        /// </summary>
        public AeEventTypeState FindType(ServerSystemContext context, NodeId nodeId)
        {
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            BaseObjectTypeState eventType = null;

            if (!EventTypeNodes.TryGetValue(nodeId, out eventType))
            {
                return null;
            }

            return eventType as AeEventTypeState;
        }

        /// <summary>
        /// Creates an instance of an event.
        /// </summary>
        public BaseEventState CreateInstance(ServerSystemContext context, AeEventTypeState eventType)
        {
            BaseEventState instance = null;

            switch (eventType.EventType.EventTypeMapping)
            {
                case EventTypeMapping.AlarmConditionType: { instance = new AlarmConditionState(null); break; }
                case EventTypeMapping.AuditEventType: { instance = new AuditEventState(null); break; }
                case EventTypeMapping.BaseEventType: { instance = new BaseEventState(null); break; }
                case EventTypeMapping.DeviceFailureEventType: { instance = new DeviceFailureEventState(null); break; }
                case EventTypeMapping.DiscreteAlarmType: { instance = new DiscreteAlarmState(null); break; }
                case EventTypeMapping.NonExclusiveDeviationAlarmType: { instance = new NonExclusiveDeviationAlarmState(null); break; }
                case EventTypeMapping.ExclusiveLevelAlarmType: { instance = new ExclusiveLevelAlarmState(null); break; }
                case EventTypeMapping.LimitAlarmType: { instance = new LimitAlarmState(null); break; }
                case EventTypeMapping.NonExclusiveLevelAlarmType: { instance = new NonExclusiveLevelAlarmState(null); break; }
                case EventTypeMapping.OffNormalAlarmType: { instance = new OffNormalAlarmState(null); break; }
                case EventTypeMapping.SystemEventType: { instance = new SystemEventState(null); break; }
                case EventTypeMapping.TripAlarmType: { instance = new TripAlarmState(null); break; }
            }

            return instance;
        }
        
        /// <summary>
        /// Updates the event types in cache with the most recent info fetched from the AE server.
        /// </summary>
        public void UpdateCache(ServerSystemContext context, ushort namespaceIndex)
        {
            // clear the existing nodes.
            EventTypeNodes = new NodeIdDictionary<BaseObjectTypeState>();
            Attributes = new Dictionary<int,int[]>();
            TypeTable typeTable = context.TypeTable as TypeTable;

            // rebuild from the recently fetched list.
            for (int ii = 0; ii < EventTypes.Count; ii++)
            {
                // save the attributes for use when creating filters.
                if (EventTypes[ii].EventTypeMapping != EventTypeMapping.ConditionClassType && !Attributes.ContainsKey(EventTypes[ii].CategoryId))
                {
                    EventType eventType = EventTypes[ii];

                    int[] attributeIds = new int[eventType.Attributes.Count];

                    for (int jj = 0; jj < attributeIds.Length; jj++)
                    {
                        attributeIds[jj] = eventType.Attributes[jj].Id;
                    }

                    Attributes.Add(EventTypes[ii].CategoryId, attributeIds);
                }

                AeEventTypeState node = new AeEventTypeState(EventTypes[ii], namespaceIndex);

                BaseObjectTypeState mappingNode = null;

                if (!EventTypeNodes.TryGetValue(node.SuperTypeId, out mappingNode))
                {
                    mappingNode = new AeEventTypeMappingState(node.EventType.EventTypeMapping, namespaceIndex);
                    EventTypeNodes.Add(mappingNode.NodeId, mappingNode);

                    // ensure the mapping node is in the type table.
                    if (typeTable != null)
                    {
                        if (!typeTable.IsKnown(mappingNode.NodeId))
                        {
                            typeTable.AddSubtype(mappingNode.NodeId, mappingNode.SuperTypeId);
                        }
                    }
                }

                EventTypeNodes.Add(node.NodeId, node);

                // ensure the type node is in the type table.
                if (typeTable != null)
                {
                    if (!typeTable.IsKnown(node.NodeId))
                    {
                        typeTable.AddSubtype(node.NodeId, mappingNode.NodeId);
                    }
                }
            }
        }

        /// <summary>
        /// Fetches the event type information from the AE server.
        /// </summary>
        public void LoadEventTypes(ComAeClient client)
        {
            EventTypes = new List<EventType>();
            LoadEventType(client, OpcRcw.Ae.Constants.SIMPLE_EVENT);
            LoadEventType(client, OpcRcw.Ae.Constants.TRACKING_EVENT);
            LoadEventType(client, OpcRcw.Ae.Constants.CONDITION_EVENT);
        }

        /// <summary>
        /// Fetches the event categories for the specified event type.
        /// </summary>
        public void LoadEventType(ComAeClient client, int eventTypeId)
        {
            int[] ids = null;
            string[] descriptions = null;

            try
            {
                client.GetEventCategories(eventTypeId, out ids, out descriptions);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error fetching event categories.");
            }

            if (ids != null)
            {
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    List<EventAttribute> attributes = LoadEventAttributes(client, eventTypeId, ids[ii]);

                    if (eventTypeId == OpcRcw.Ae.Constants.CONDITION_EVENT)
                    {
                        LoadConditionEvent(client, eventTypeId, ids[ii], descriptions[ii], attributes);
                        continue;
                    }

                    EventType eventType = new EventType();
                    eventType.EventTypeId = eventTypeId;
                    eventType.CategoryId = ids[ii];
                    eventType.Description = descriptions[ii];
                    eventType.ConditionName = null;
                    eventType.SubConditionNames = null;
                    eventType.Attributes = attributes;
                    DetermineMapping(eventType);
                    EventTypes.Add(eventType);
                }
            }
        }
        
        /// <summary>
        /// Uses the recommended names in the AE specification to map to predefined UA event types.
        /// </summary>
        private void DetermineMapping(EventType eventType)
        {
            for (int ii = 0; ii < eventType.Attributes.Count; ii++)
            {
                if (AeTypeCache.IsKnownName(eventType.Attributes[ii].Description, "ACK COMMENT"))
                {
                    eventType.AckComment = eventType.Attributes[ii];
                    break;
                }
            }

            eventType.EventTypeMapping = EventTypeMapping.BaseEventType;

            if (eventType.EventTypeId == OpcRcw.Ae.Constants.SIMPLE_EVENT)
            {
                if (AeTypeCache.IsKnownName(eventType.Description, "Device Failure"))
                {
                    eventType.EventTypeMapping = EventTypeMapping.DeviceFailureEventType;
                    return;
                }

                if (AeTypeCache.IsKnownName(eventType.Description, "System Message"))
                {
                    eventType.EventTypeMapping = EventTypeMapping.SystemEventType;
                    return;
                }

                eventType.EventTypeMapping = EventTypeMapping.BaseEventType;
                return;
            }

            if (eventType.EventTypeId == OpcRcw.Ae.Constants.TRACKING_EVENT)
            {
                eventType.EventTypeMapping = EventTypeMapping.AuditEventType;
                return;
            }

            if (eventType.EventTypeId == OpcRcw.Ae.Constants.CONDITION_EVENT)
            {
                if (eventType.ConditionName == null)
                {
                    eventType.EventTypeMapping = EventTypeMapping.ConditionClassType;
                    return;
                }

                eventType.EventTypeMapping = EventTypeMapping.AlarmConditionType;

                if (AeTypeCache.IsKnownName(eventType.Description, "Level"))
                {
                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "PVLEVEL"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.ExclusiveLevelAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "SPLEVEL"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.ExclusiveLevelAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "HI HI"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.NonExclusiveLevelAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "HI"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.NonExclusiveLevelAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "LO"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.NonExclusiveLevelAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "LO LO"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.NonExclusiveLevelAlarmType;
                        return;
                    }

                    eventType.EventTypeMapping = EventTypeMapping.LimitAlarmType;
                    return;
                }

                if (AeTypeCache.IsKnownName(eventType.Description, "Deviation"))
                {
                    eventType.EventTypeMapping = EventTypeMapping.NonExclusiveDeviationAlarmType;
                    return;
                }

                if (AeTypeCache.IsKnownName(eventType.Description, "Discrete"))
                {
                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "CFN"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.OffNormalAlarmType;
                        return;
                    }

                    if (AeTypeCache.IsKnownName(eventType.ConditionName, "TRIP"))
                    {
                        eventType.EventTypeMapping = EventTypeMapping.TripAlarmType;
                        return;
                    }

                    eventType.EventTypeMapping = EventTypeMapping.DiscreteAlarmType;
                    return;
                }
            }
        }

        private void LoadConditionEvent(ComAeClient client, int eventTypeId, int categoryId, string description, List<EventAttribute> attributes)
        {
            string[] conditionNames = null;

            try
            {
                client.GetConditionNames(categoryId, out conditionNames);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error fetching condition names.");
                conditionNames = null;
            }

            if (conditionNames != null)
            {
                // create a condition class for the category.
                EventType eventType = new EventType();
                eventType.EventTypeId = eventTypeId;
                eventType.CategoryId = categoryId;
                eventType.Description = description;
                eventType.ConditionName = null;
                eventType.SubConditionNames = null;
                eventType.Attributes = new List<EventAttribute>();
                DetermineMapping(eventType);
                EventTypes.Add(eventType);

                // create event types for each condition name.
                for (int ii = 0; ii < conditionNames.Length; ii++)
                {
                    eventType = new EventType();
                    eventType.EventTypeId = eventTypeId;
                    eventType.CategoryId = categoryId;
                    eventType.Description = description;
                    eventType.ConditionName = conditionNames[ii];
                    eventType.SubConditionNames = null;
                    eventType.Attributes = attributes;
                    DetermineMapping(eventType);

                    string[] subConditionNames = null;

                    try
                    {
                        client.GetSubConditionNames(eventType.ConditionName, out subConditionNames);
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error fetching sub-condition names.");
                        subConditionNames = null;
                    }

                    if (subConditionNames != null)
                    {
                        eventType.SubConditionNames = new List<string>(subConditionNames);
                    }

                    EventTypes.Add(eventType);
                }
            }
        }
        
        /// <summary>
        /// Fetches the attributes for the category from the AE server.
        /// </summary>
        private List<EventAttribute> LoadEventAttributes(ComAeClient client, int eventTypeId, int categoryId)
        {
            List<EventAttribute> attributes = new List<EventAttribute>();

            int[] ids = null;
            string[] descriptions = null;
            short[] dataTypes = null;

            try
            {
                client.GetEventAttributes(categoryId, out ids, out descriptions, out dataTypes);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error fetching event attributes.");
                ids = null;
            }

            if (ids != null)
            {
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    EventAttribute attribute = new EventAttribute();
                    attribute.Id = ids[ii];
                    attribute.Description = descriptions[ii];
                    attribute.VarType = dataTypes[ii];
                    attributes.Add(attribute);
                }
            }

            return attributes;
        }

        /// <summary>
        /// Checks for alternate spellings of a well known name defined by the AE specification.
        /// </summary>
        public static bool IsKnownName(string description, params string[] names)
        {
            if (names != null)
            {
                for (int ii = 0; ii < names.Length; ii++)
                {
                    string name = names[ii];

                    if (String.Compare(name, description, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }

                    name = names[ii].Replace(' ', '_');

                    if (String.Compare(name, description, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }

                    name = names[ii].Replace(" ", "");

                    if (String.Compare(name, description, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Stores the metadata about an event type defined by the AE server.
    /// </summary>
    internal class EventType
    {
        /// <summary>
        /// The AE event type.
        /// </summary>
        public int EventTypeId { get; set; }

        /// <summary>
        /// The AE event category.
        /// </summary>
        public int CategoryId { get; set; }

        /// <summary>
        /// The AE event category description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The AE condition name.
        /// </summary>
        public string ConditionName { get; set; }

        /// <summary>
        /// The AE sub-condition names.
        /// </summary>
        public List<string> SubConditionNames { get; set; }

        /// <summary>
        /// The list of AE attrivutes for the category.
        /// </summary>
        public List<EventAttribute> Attributes { get; set; }

        /// <summary>
        /// The AE attribute for the ACK COMMENT    `
        /// </summary>
        public EventAttribute AckComment { get; set; }

        /// <summary>
        /// The mapping to a UA event type.
        /// </summary>
        public EventTypeMapping EventTypeMapping { get; set; }
    }

    /// <summary>
    /// The set of possible UA event/condition class mappings.
    /// </summary>
    internal enum EventTypeMapping
    {
        BaseEventType,
        DeviceFailureEventType,
        SystemEventType,
        AuditEventType,
        AlarmConditionType,
        LimitAlarmType,
        ExclusiveLevelAlarmType,
        NonExclusiveLevelAlarmType,
        NonExclusiveDeviationAlarmType,
        DiscreteAlarmType,
        OffNormalAlarmType,
        TripAlarmType,
        ConditionClassType
    }

    /// <summary>
    /// Stores the metadata for an AE event attribute.
    /// </summary>
    internal class EventAttribute
    {
        /// <summary>
        /// The attribute id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The attribute description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The attribute COM data type.
        /// </summary>
        public short VarType { get; set; }
    }       
}
