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
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A class that builds NodeIds used by the DataAccess NodeManager
    /// </summary>
    public static class HdaModelUtils
    {
        /// <summary>
        /// The RootType for a branch.
        /// </summary>
        public const int HdaBranch = 1;

        /// <summary>
        /// The RootType for an item.
        /// </summary>
        public const int HdaItem = 2;

        /// <summary>
        /// The RootType for an item attribute.
        /// </summary>
        public const int HdaItemAttribute = 3;

        /// <summary>
        /// The RootType for a HA configuration belonging to an item.
        /// </summary>
        public const int HdaItemConfiguration = 4;

        /// <summary>
        /// The RootType for a HA annotations belonging to an item.
        /// </summary>
        public const int HdaItemAnnotations = 5;

        /// <summary>
        /// The RootType for a node defined by the UA server.
        /// </summary>
        public const int InternalNode = 6;

        /// <summary>
        /// The RootType for a aggregate function node.
        /// </summary>
        public const int HdaAggregate = 7;
        
        /// <summary>
        /// Constructs the node id for a branch.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public static NodeId ConstructIdForHdaBranch(string itemId, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = itemId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = HdaBranch;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the node id for an item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public static NodeId ConstructIdForHdaItem(string itemId, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = itemId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = HdaItem;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Converts a HDA Aggregate ID to a UA aggregate ID
        /// </summary>
        /// <param name="aggregateId">The aggregate id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId HdaAggregateToUaAggregate(uint aggregateId, ushort namespaceIndex)
        {
            NodeId nodeId = ComUtils.GetHdaAggregateId(aggregateId);

            if (nodeId != null)
            {
                return nodeId;
            }

            return HdaParsedNodeId.Construct(HdaAggregate, aggregateId.ToString(), null, namespaceIndex);
        }

        /// <summary>
        /// Converts a UA Aggregate ID to a HDA aggregate ID
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static uint HdaAggregateToUaAggregate(NodeId nodeId, ushort namespaceIndex)
        {
            // check for valid node.
            if (nodeId == null)
            {
                return 0;
            }

            // check for built in aggregate.
            uint aggregateId = ComUtils.GetHdaAggregateId(nodeId);

            if (aggregateId != 0)
            {
                return aggregateId;
            }

            // parse the node id.
            HdaParsedNodeId parsedNodeId = HdaParsedNodeId.Parse(nodeId);

            if (parsedNodeId == null || namespaceIndex != parsedNodeId.NamespaceIndex || parsedNodeId.RootType != HdaModelUtils.HdaAggregate)
            {
                return 0;
            }

            return parsedNodeId.AggregateId;
        }

        /// <summary>
        /// Constructs the node id for an aggregate function.
        /// </summary>
        /// <param name="aggregateId">The aggregate id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId ConstructIdForHdaAggregate(uint aggregateId, ushort namespaceIndex)
        {
            // check for built in aggregates.
            NodeId nodeId = ComUtils.GetHdaAggregateId(aggregateId);

            if (nodeId != null)
            {
                return nodeId;
            }

            // server specific aggregates.
            ParsedNodeId parsedNodeId = new ParsedNodeId();
            
            parsedNodeId.RootId = aggregateId.ToString();
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = HdaAggregate;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the node id for an item attribute.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId ConstructIdForHdaItemAttribute(string itemId, uint attributeId, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = itemId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = HdaItemAttribute;
            parsedNodeId.ComponentPath = attributeId.ToString();

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs a NodeId from the BrowseName of an internal node.
        /// </summary>
        /// <param name="browseName">The browse name.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The node id.</returns>
        public static NodeId ConstructIdForInternalNode(QualifiedName browseName, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = browseName.Name;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = InternalNode;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the node identifier for a component.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The node identifier for a component.</returns>
        public static NodeId ConstructIdForComponent(NodeState component, ushort namespaceIndex)
        {
            return ParsedNodeId.ConstructIdForComponent(component, namespaceIndex);
        }

        /// <summary>
        /// Gets the item configuration node.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static BaseInstanceState GetItemConfigurationNode(string itemId, ushort namespaceIndex)
        {
            if (itemId == null)
            {
                return null;
            }

            FolderState component = new FolderState(null);

            component.NodeId = HdaParsedNodeId.Construct(HdaModelUtils.HdaItemConfiguration, itemId, null, namespaceIndex);
            component.SymbolicName = Opc.Ua.BrowseNames.HAConfiguration;
            component.BrowseName = new QualifiedName(component.SymbolicName);
            component.DisplayName = component.BrowseName.Name;
            component.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            component.TypeDefinitionId = Opc.Ua.ObjectTypeIds.HistoricalDataConfigurationType;
            component.EventNotifier = EventNotifiers.None;

            component.AddReference(ReferenceTypeIds.HasHistoricalConfiguration, true, HdaModelUtils.ConstructIdForHdaItem(itemId, namespaceIndex));
            component.AddReference(ReferenceTypeIds.HasComponent, false, HdaModelUtils.ConstructIdForInternalNode(Opc.Ua.BrowseNames.AggregateConfiguration, namespaceIndex));

            return component;
        }

        /// <summary>
        /// Gets the item annotations node.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static PropertyState GetItemAnnotationsNode(string itemId, ushort namespaceIndex)
        {
            if (itemId == null)
            {
                return null;
            }

            PropertyState component = new PropertyState(null);

            component.NodeId = HdaParsedNodeId.Construct(HdaModelUtils.HdaItemAnnotations, itemId, null, namespaceIndex);
            component.SymbolicName = Opc.Ua.BrowseNames.Annotations;
            component.BrowseName = new QualifiedName(component.SymbolicName);
            component.DisplayName = component.BrowseName.Name;
            component.DataType = DataTypeIds.Annotation;
            component.ValueRank = ValueRanks.Scalar;
            component.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            component.Historizing = false;
            component.AccessLevel = AccessLevels.HistoryRead;
            component.UserAccessLevel = AccessLevels.HistoryRead;
            component.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            component.TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType;

            component.AddReference(ReferenceTypeIds.HasProperty, true, HdaModelUtils.ConstructIdForHdaItem(itemId, namespaceIndex));

            return component;
        }
    }
}
