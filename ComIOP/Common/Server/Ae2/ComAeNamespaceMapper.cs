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
    /// Maps the UA type model to a local 
    /// </summary>
    public class ComAeNamespaceMapper : ComNamespaceMapper
    {
        /// <summary>
        /// Initializes the mapper.
        /// </summary>
        public void Initialize(Session session, ComAe2ProxyConfiguration configuration)
        {
            base.Initialize(session, configuration);

            m_session = session;

            // discard the table.
            m_eventTypes = new NodeIdDictionary<AeEventCategory>();
            m_categories = new Dictionary<uint, AeEventCategory>();
            m_attributes = new Dictionary<uint, AeEventAttribute>();

            // load the well known types from an embedded resource.
            IndexWellKnownTypes();

            // browse the server for additional types.
            if (!configuration.UseOnlyBuiltInTypes)
            {
                IndexTypesFromServer(Opc.Ua.ObjectTypeIds.BaseEventType, OpcRcw.Ae.Constants.SIMPLE_EVENT);
            }
            
            // check for existing category mapping.
            NodeIdMappingSet mappingSet = configuration.GetMappingSet("EventCategories");

            // update mappings.
            UpdateEventTypeMappings(mappingSet);

            // update configuration.
            configuration.ReplaceMappingSet(mappingSet);

            // check for existing attribute mapping.
            mappingSet = configuration.GetMappingSet("EventAttributes");

            // update mappings.
            UpdateEventAttributeMappings(mappingSet);

            // update configuration.
            configuration.ReplaceMappingSet(mappingSet);
        }

        /// <summary>
        /// Gets the list of categories for the specified event types.
        /// </summary>
        public List<AeEventCategory> GetCategories(int eventType)
        {
            List<AeEventCategory> categories = new List<AeEventCategory>();

            foreach (AeEventCategory category in m_categories.Values)
            {
                if ((category.EventType & eventType) != 0)
                {
                    categories.Add(category);
                }
            }

            categories.Sort(CompareCategories);
            return categories;
        }

        /// <summary>
        /// Returns the category with the specified category id.
        /// </summary>
        public AeEventCategory GetCategory(uint categoryId)
        {
            AeEventCategory category = null;

            if (!m_categories.TryGetValue(categoryId, out category))
            {
                return null;
            }

            return category;
        }

        /// <summary>
        /// Returns the category with the specified event type id.
        /// </summary>
        public AeEventCategory GetCategory(NodeId typeId)
        {
            AeEventCategory category = null;

            if (!this.m_eventTypes.TryGetValue(typeId, out category))
            {
                return null;
            }

            return category;
        }

        /// <summary>
        /// Returns the attribute with the specified attribute id.
        /// </summary>
        public AeEventAttribute GetAttribute(uint attributeId)
        {
            AeEventAttribute attribute = null;

            if (!m_attributes.TryGetValue(attributeId, out attribute))
            {
                return null;
            }

            return attribute;
        }

        /// <summary>
        /// Returns the attribute with the specified event type id and browse path.
        /// </summary>
        public AeEventAttribute GetAttribute(NodeId typeId, params string[] browseNames)
        {
            AeEventCategory category = null;

            if (!this.m_eventTypes.TryGetValue(typeId, out category))
            {
                return null;
            }

            StringBuilder buffer = new StringBuilder();

            if (browseNames != null)
            {
                for (int ii = 0; ii < browseNames.Length; ii++)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append('/');
                    }

                    buffer.Append(browseNames[ii]);
                }
            }

            string targetPath = buffer.ToString();

            if (!String.IsNullOrEmpty(targetPath))
            {
                for (int ii = 0; ii < category.Attributes.Count; ii++)
                {
                    if (category.Attributes[ii].BrowsePathDisplayText == targetPath)
                    {
                        return category.Attributes[ii];
                    }
                }
            }

            return null;
        }
   
        /// <summary>
        /// Gets the list of categories for the specified event types.
        /// </summary>
        public List<AeEventAttribute> GetAttributes(uint categoryId)
        {
            AeEventCategory category = null;

            if (!m_categories.TryGetValue(categoryId, out category))
            {
                return null;
            }

            List<AeEventAttribute> attributes = new List<AeEventAttribute>();

            AeEventCategory subType = category;

            while (subType != null)
            {
                for (int ii = 0; ii < subType.Attributes.Count; ii++)
                {
                    AeEventAttribute attribute = subType.Attributes[ii];

                    if (attribute.OverriddenDeclaration == null)
                    {
                        if (!attribute.Hidden)
                        {
                            attributes.Add(attribute);
                        }
                    }
                }

                AeEventCategory superType = null;

                if (!m_eventTypes.TryGetValue(subType.SuperTypeId, out superType))
                {
                    break;
                }

                subType = superType;
            }

            attributes.Sort(CompareAttributes);
            return attributes;
        }

        #region Private Methods
        /// <summary>
        /// Uses the description to compare two categories.
        /// </summary>
        private static int CompareCategories(AeEventCategory x, AeEventCategory y)
        {
            if (Object.ReferenceEquals(x, y))
            {
                return 0;
            }

            if (Object.ReferenceEquals(x, null))
            {
                return (Object.ReferenceEquals(y, null))?0:-1;
            }
                     
            return x.Description.CompareTo(y.Description);
        }

        /// <summary>
        /// Uses the BrowsePathDisplayText to compare two attributes.
        /// </summary>
        private static int CompareAttributes(AeEventAttribute x, AeEventAttribute y)
        {
            if (Object.ReferenceEquals(x, y))
            {
                return 0;
            }

            if (Object.ReferenceEquals(x, null))
            {
                return (Object.ReferenceEquals(y, null)) ? 0 : -1;
            }

            return x.BrowsePathDisplayText.CompareTo(y.BrowsePathDisplayText);
        }
        
        /// <summary>
        /// Indexes the well known subtypes.
        /// </summary>
        private void IndexWellKnownTypes()
        {
            SystemContext context = new SystemContext();
            context.EncodeableFactory = m_session.MessageContext.Factory;
            context.NamespaceUris = m_session.NamespaceUris;
            context.ServerUris = m_session.ServerUris;

            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Stack.Generated.Opc.Ua.PredefinedNodes.uanodes", typeof(NodeState).Assembly, true);
            
            NodeIdDictionary<BaseTypeState> types = new NodeIdDictionary<BaseTypeState>();

            // collect the instance declarations for all types.
            for (int ii = 0; ii < predefinedNodes.Count; ii++)
            {
                BaseTypeState type = predefinedNodes[ii] as BaseTypeState;

                if (type != null)
                {
                    types.Add(type.NodeId, type);
                }
            }

            // index only those types which are subtypes of BaseEventType.
            foreach (BaseTypeState type in types.Values)
            {
                BaseTypeState subType = type;
                BaseTypeState superType = null;

                int eventType = 0;

                while (subType != null)
                {
                    if (subType.NodeId == Opc.Ua.ObjectTypeIds.ConditionType || subType.SuperTypeId == Opc.Ua.ObjectTypeIds.ConditionType)
                    {
                        eventType = OpcRcw.Ae.Constants.CONDITION_EVENT;
                    }

                    else if (subType.NodeId == Opc.Ua.ObjectTypeIds.AuditEventType || subType.SuperTypeId == Opc.Ua.ObjectTypeIds.AuditEventType)
                    {
                        eventType = OpcRcw.Ae.Constants.TRACKING_EVENT;
                    }

                    else if (subType.NodeId == Opc.Ua.ObjectTypeIds.BaseEventType || subType.SuperTypeId == Opc.Ua.ObjectTypeIds.BaseEventType)
                    {
                        eventType = OpcRcw.Ae.Constants.SIMPLE_EVENT;
                    }

                    // found an event, collect the attribute and index it.
                    if (eventType != 0)
                    {
                        List<AeEventAttribute> declarations = new List<AeEventAttribute>();
                        Dictionary<string, AeEventAttribute> map = new Dictionary<string, AeEventAttribute>();

                        ComAeUtils.CollectInstanceDeclarations(
                            m_session,
                            this,
                            type,
                            null,
                            declarations,
                            map);

                        AeEventCategory declaration = new AeEventCategory();
                        declaration.TypeId = type.NodeId;
                        declaration.SuperTypeId = type.SuperTypeId;
                        declaration.EventType = eventType;
                        declaration.Description = (LocalizedText.IsNullOrEmpty(type.DisplayName))?type.BrowseName.Name:type.DisplayName.Text;
                        declaration.Attributes = declarations;
                        m_eventTypes[declaration.TypeId] = declaration;
                        break;
                    }

                    // follow the tree to the parent.
                    if (!types.TryGetValue(subType.SuperTypeId, out superType))
                    {
                        break;
                    }

                    subType = superType;
                }
            }

            // hide the built in attributes.
            AeEventCategory category = GetCategory(Opc.Ua.ObjectTypeIds.BaseEventType);

            if (category != null)
            {
                for (int ii = 0; ii < category.Attributes.Count; ii++)
                {
                    switch (category.Attributes[ii].BrowsePathDisplayText)
                    {
                        case Opc.Ua.BrowseNames.Message:
                        case Opc.Ua.BrowseNames.Severity:
                        case Opc.Ua.BrowseNames.SourceName:
                        case Opc.Ua.BrowseNames.Time:
                        case Opc.Ua.BrowseNames.ReceiveTime:
                        case Opc.Ua.BrowseNames.LocalTime:
                        {
                            category.Attributes[ii].Hidden = true;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively populates the event types table.
        /// </summary>
        private void IndexTypesFromServer(NodeId baseTypeId, int eventType)
        {
            // check if event type needs to be revised.
            if (baseTypeId == Opc.Ua.ObjectTypeIds.ConditionType)
            {
                eventType = OpcRcw.Ae.Constants.CONDITION_EVENT;
            }

            else if (baseTypeId == Opc.Ua.ObjectTypeIds.AuditEventType)
            {
                eventType = OpcRcw.Ae.Constants.TRACKING_EVENT;
            }

            else if (baseTypeId == Opc.Ua.ObjectTypeIds.BaseEventType)
            {
                eventType = OpcRcw.Ae.Constants.SIMPLE_EVENT;
            }

            // browse for subtypes.
            BrowseDescription nodeToBrowse = new BrowseDescription();
            nodeToBrowse.NodeId = baseTypeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasSubtype;
            nodeToBrowse.IncludeSubtypes = false;
            nodeToBrowse.NodeClassMask = (uint)NodeClass.ObjectType;
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.BrowseName | BrowseResultMask.DisplayName | BrowseResultMask.NodeClass);

            ReferenceDescriptionCollection references = ComAeUtils.Browse(
                m_session,
                nodeToBrowse,
                false);

            for (int ii = 0; ii < references.Count; ii++)
            {
                // these types have t
                if (references[ii].NodeId.IsAbsolute)
                {
                    continue;
                }

                NodeId typeId = (NodeId)references[ii].NodeId;

                if (!m_eventTypes.ContainsKey(typeId))
                {
                    // collection the instances declared by the type.
                    List<AeEventAttribute> declarations = new List<AeEventAttribute>();
                    Dictionary<string, AeEventAttribute> map = new Dictionary<string, AeEventAttribute>();

                    ComAeUtils.CollectInstanceDeclarations(
                        m_session,
                        this,
                        (NodeId)references[ii].NodeId,
                        null,
                        declarations,
                        map);

                    AeEventCategory declaration = new AeEventCategory();
                    declaration.TypeId = (NodeId)references[ii].NodeId;
                    declaration.SuperTypeId = baseTypeId;
                    declaration.EventType = eventType;
                    declaration.Description = (LocalizedText.IsNullOrEmpty(references[ii].DisplayName)) ? references[ii].BrowseName.Name : references[ii].DisplayName.Text;
                    declaration.Attributes = declarations;
                    m_eventTypes[declaration.TypeId] = declaration;
                }

                // recursively look for subtypes.
                IndexTypesFromServer(typeId, eventType);
            }
        }

        /// <summary>
        /// Assigns a locally unique numeric id to each event type.
        /// </summary>
        private void UpdateEventTypeMappings(NodeIdMappingSet mappingSet)
        {
            NodeIdMappingCollection mappingsToKeep = new NodeIdMappingCollection();
            Dictionary<uint, AeEventCategory> categories = new Dictionary<uint, AeEventCategory>();

            for (int ii = 0; ii < mappingSet.Mappings.Count; ii++)
            {
                NodeIdMapping mapping = mappingSet.Mappings[ii];

                try
                {
                    // need to convert the cached type id to a remote id.
                    NodeId localId = NodeId.Parse(mapping.NodeId);
                    NodeId remoteId = this.GetRemoteNodeId(localId);

                    AeEventCategory eventType = null;

                    if (m_eventTypes.TryGetValue(remoteId, out eventType))
                    {
                        // check if the event already has an id.
                        if (eventType.LocalId == 0)
                        {
                            // must update the saved integer id.
                            if (!categories.ContainsKey(mapping.IntegerId))
                            {
                                eventType.LocalId = mapping.IntegerId;
                                categories[eventType.LocalId] = eventType;
                                mappingsToKeep.Add(mapping);
                            }

                            // must assign a new one if a duplicate found.
                            else
                            {
                                eventType.LocalId = 0;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // discard invalid mappings.
                }
            }

            // assign ids to any types which do not have mappings.
            uint nextId = 1;

            foreach (AeEventCategory eventType in m_eventTypes.Values)
            {
                if (eventType.LocalId == 0)
                {
                    // find a unique id.
                    while (categories.ContainsKey(nextId)) nextId++;

                    // assign the id.
                    eventType.LocalId = nextId;
                    categories[eventType.LocalId] = eventType;

                    // save the mapping.
                    NodeIdMapping mapping = new NodeIdMapping();
                    mapping.IntegerId = nextId;
                    mapping.NodeId = this.GetLocalNodeId(eventType.TypeId).ToString();

                    mappingsToKeep.Add(mapping);
                }
            }

            // update mappings.
            mappingSet.Mappings = mappingsToKeep;
            m_categories = categories;
        }

        /// <summary>
        /// Assigns a locally unique numeric id to each event type.
        /// </summary>
        private void UpdateEventAttributeMappings(NodeIdMappingSet mappingSet)
        {
            NodeIdMappingCollection mappingsToKeep = new NodeIdMappingCollection();

            // collect all unique declarations.
            List<AeEventAttribute> list = new List<AeEventAttribute>();

            foreach (AeEventCategory type in m_eventTypes.Values)
            {
                for (int ii = 0; ii < type.Attributes.Count; ii++)
                {
                    AeEventAttribute declaration = type.Attributes[ii];

                    // only variables can be attributes.
                    if (declaration.NodeClass != NodeClass.Variable)
                    {
                        continue;
                    }

                    // need to link attributes to any attributes that they override.
                    AeEventCategory subType = type;
                    AeEventCategory superType = null;

                    while (subType != null)
                    {
                        if (NodeId.IsNull(subType.SuperTypeId) || subType.TypeId == subType.SuperTypeId || subType.SuperTypeId == Opc.Ua.ObjectTypeIds.BaseObjectType)
                        {
                            list.Add(declaration);
                            break;
                        }

                        if (!m_eventTypes.TryGetValue(subType.SuperTypeId, out superType))
                        {
                            break;
                        }

                        for (int jj = 0; jj < superType.Attributes.Count; jj++)
                        {
                            if (superType.Attributes[jj].BrowsePathDisplayText == declaration.BrowsePathDisplayText)
                            {
                                declaration.OverriddenDeclaration = superType.Attributes[jj];
                                declaration = declaration.OverriddenDeclaration;
                                break;
                            }
                        }

                        subType = superType;
                    }
                }
            }

            // look up ids for all attributes in master list.
            Dictionary<uint, AeEventAttribute> attributes = new Dictionary<uint, AeEventAttribute>();

            for (int ii = 0; ii < list.Count; ii++)
            {
                AeEventAttribute declaration = list[ii];

                for (int jj = 0; jj < mappingSet.Mappings.Count; jj++)
                {
                    NodeIdMapping mapping = mappingSet.Mappings[jj];

                    try
                    {
                        // browse display paths always use local namespa indexes.
                        if (declaration.BrowsePathDisplayText != mapping.BrowePath)
                        {
                            continue;
                        }

                        // need to convert the cached type id to a remote id.
                        NodeId localId = NodeId.Parse(mapping.NodeId);
                        NodeId remoteId = this.GetRemoteNodeId(localId);

                        if (declaration.RootTypeId != remoteId)
                        {
                            continue;
                        }

                        // must update the saved integer id.
                        if (!attributes.ContainsKey(mapping.IntegerId))
                        {
                            declaration.LocalId = mapping.IntegerId;
                            attributes[declaration.LocalId] = declaration;
                            mappingsToKeep.Add(mapping);
                        }

                        // must assign a new one if a duplicate found.
                        else
                        {
                            declaration.LocalId = 0;
                        }
                    }
                    catch (Exception)
                    {
                        // ignore invalid mappings.
                    }
                }
            }

            // assign new ids.
            uint nextId = 1;

            for (int ii = 0; ii < list.Count; ii++)
            {
                AeEventAttribute declaration = list[ii];

                if (declaration.LocalId == 0)
                {
                    // find a unique id.
                    while (attributes.ContainsKey(nextId)) nextId++;

                    // assign the id.
                    declaration.LocalId = nextId;
                    attributes[declaration.LocalId] = declaration;

                    // save the mapping.
                    NodeIdMapping mapping = new NodeIdMapping();
                    mapping.IntegerId = nextId;
                    mapping.NodeId = this.GetLocalNodeId(declaration.RootTypeId).ToString();
                    mapping.BrowePath = declaration.BrowsePathDisplayText;

                    mappingsToKeep.Add(mapping);
                }
            }

            // update mapping set.
            mappingSet.Mappings = mappingsToKeep;
            m_attributes = attributes;
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeIdDictionary<AeEventCategory> m_eventTypes;
        private Dictionary<uint, AeEventCategory> m_categories;
        private Dictionary<uint,AeEventAttribute> m_attributes;
        #endregion
    }
}
