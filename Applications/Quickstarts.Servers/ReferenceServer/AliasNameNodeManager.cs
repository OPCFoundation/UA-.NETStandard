/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// A simple OPC UA AliasName provider that registers two alias categories
    /// (TagVariables and Topics) under the standard <c>Aliases</c> object
    /// (i=23470) defined by OPC UA Part 17 (Aliases). The categories expose a
    /// <c>FindAlias</c> method that performs OPC UA Like-pattern matching
    /// against the contained alias instances.
    /// </summary>
    public sealed class AliasNameNodeManager : CustomNodeManager2
    {
        // Standard NodeIds defined by OPC UA Part 17.
        private const uint AliasNameTypeId = 23455;
        private const uint AliasNameCategoryTypeId = 23456;
        private const uint AliasNameDataTypeId = 23468;
        private const uint AliasForReferenceTypeId = 23469;
        private const uint AliasesObjectId = 23470;

        /// <summary>
        /// Namespace URI used for the alias instances created by this manager.
        /// </summary>
        public const string NamespaceUri
            = "http://opcfoundation.org/Quickstarts/AliasName";

        private readonly List<AliasInstance> _allAliases = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="AliasNameNodeManager"/> class.
        /// </summary>
        /// <param name="server">The server internal interface.</param>
        /// <param name="configuration">The application configuration.</param>
        public AliasNameNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, NamespaceUri)
        {
        }

        /// <inheritdoc/>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            int refServerNsIndex = Server.NamespaceUris.GetIndex(
                Namespaces.ReferenceServer);
            ushort refServerNs = refServerNsIndex >= 0
                ? (ushort)refServerNsIndex
                : ushort.MaxValue;

            BaseObjectState tagVariables = CreateCategory("TagVariables");
            BaseObjectState topics = CreateCategory("Topics");

            // Link from the standard Aliases object (ns=0) into our categories.
            var aliasesNodeId = new NodeId(AliasesObjectId);
            AddExternalReference(
                aliasesNodeId, ReferenceTypeIds.Organizes, false,
                tagVariables.NodeId, externalReferences);
            AddExternalReference(
                aliasesNodeId, ReferenceTypeIds.Organizes, false,
                topics.NodeId, externalReferences);
            // The reverse references on the children point back to Aliases.
            tagVariables.AddReference(
                ReferenceTypeIds.Organizes, true, aliasesNodeId);
            topics.AddReference(
                ReferenceTypeIds.Organizes, true, aliasesNodeId);

            // TagVariables aliases — references into the ReferenceServer namespace.
            if (refServerNs != ushort.MaxValue)
            {
                CreateAlias(tagVariables, "TIC101_Setpoint",
                    [new NodeId("Scalar_Static_Double", refServerNs)]);
                CreateAlias(tagVariables, "TIC101_PV",
                    [new NodeId("Scalar_Static_Float", refServerNs)]);
                CreateAlias(tagVariables, "FIC202_Flow",
                    [new NodeId("Scalar_Simulation_Double", refServerNs)]);
                CreateAlias(tagVariables, "Pump1_Status",
                    [new NodeId("Scalar_Static_Boolean", refServerNs)]);
                CreateAlias(tagVariables, "Heater_Power",
                    [new NodeId("Scalar_Static_Int32", refServerNs)]);
                CreateAlias(tagVariables, "MultiRefAlias",
                    [
                        new NodeId("Scalar_Static_Double", refServerNs),
                        new NodeId("Scalar_Static_Int32", refServerNs)
                    ]);
            }

            // Topics aliases — references into the well-known ns=0 nodes.
            CreateAlias(topics, "ServerEvents", [ObjectIds.Server]);
            CreateAlias(topics, "AuditEvents",
                [new NodeId(ObjectTypes.AuditEventType)]);

            AddPredefinedNode(SystemContext, tagVariables);
            AddPredefinedNode(SystemContext, topics);
        }

        private BaseObjectState CreateCategory(string name)
        {
            var nodeId = new NodeId(name, NamespaceIndex);
            var category = new BaseObjectState(null)
            {
                SymbolicName = name,
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = new NodeId(AliasNameCategoryTypeId),
                ReferenceTypeId = ReferenceTypeIds.Organizes
            };

            // FindAlias method — instance with method-declaration to the type.
            var findAlias = new MethodState(category)
            {
                SymbolicName = "FindAlias",
                NodeId = new NodeId(name + "_FindAlias", NamespaceIndex),
                BrowseName = new QualifiedName("FindAlias", NamespaceIndex),
                DisplayName = new LocalizedText("FindAlias"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                Executable = true,
                UserExecutable = true
            };

            findAlias.InputArguments =
                new PropertyState<ArrayOf<Argument>>
                    .Implementation<StructureBuilder<Argument>>(findAlias)
                {
                    NodeId = new NodeId(name + "_FindAlias_In", NamespaceIndex),
                    BrowseName = QualifiedName.From(BrowseNames.InputArguments),
                    DisplayName = LocalizedText.From(BrowseNames.InputArguments),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Argument,
                    ValueRank = ValueRanks.OneDimension
                };
            findAlias.InputArguments.Value = new Argument[]
            {
                new()
                {
                    Name = "AliasNameSearchPattern",
                    Description = LocalizedText.From("AliasNameSearchPattern"),
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar
                },
                new()
                {
                    Name = "ReferenceTypeFilter",
                    Description = LocalizedText.From("ReferenceTypeFilter"),
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.Scalar
                }
            }.ToArrayOf();

            findAlias.OutputArguments =
                new PropertyState<ArrayOf<Argument>>
                    .Implementation<StructureBuilder<Argument>>(findAlias)
                {
                    NodeId = new NodeId(name + "_FindAlias_Out", NamespaceIndex),
                    BrowseName = QualifiedName.From(BrowseNames.OutputArguments),
                    DisplayName = LocalizedText.From(BrowseNames.OutputArguments),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Argument,
                    ValueRank = ValueRanks.OneDimension
                };
            findAlias.OutputArguments.Value = new Argument[]
            {
                new()
                {
                    Name = "AliasNodeList",
                    Description = LocalizedText.From("AliasNodeList"),
                    DataType = new NodeId(AliasNameDataTypeId),
                    ValueRank = ValueRanks.OneDimension
                }
            }.ToArrayOf();

            findAlias.OnCallMethod2 =
                new GenericMethodCalledEventHandler2(OnFindAlias);

            category.AddChild(findAlias);
            return category;
        }

        private void CreateAlias(
            BaseObjectState parent,
            string name,
            NodeId[] targets)
        {
            var nodeId = new NodeId(parent.BrowseName.Name + "_" + name,
                NamespaceIndex);

            var alias = new BaseObjectState(parent)
            {
                SymbolicName = name,
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = new NodeId(AliasNameTypeId),
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };

            var aliasForRef = new NodeId(AliasForReferenceTypeId);
            foreach (NodeId target in targets)
            {
                alias.AddReference(aliasForRef, false, target);
            }

            parent.AddChild(alias);

            _allAliases.Add(new AliasInstance(
                parent.NodeId, name, aliasForRef, targets));
        }

        /// <summary>
        /// Implements the FindAlias method per OPC UA Part 17 §6.3.2.
        /// </summary>
        private ServiceResult OnFindAlias(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            if (!inputArguments[0].TryGetValue(out string pattern) ||
                pattern == null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            if (!inputArguments[1].TryGetValue(out NodeId refTypeFilter) ||
                refTypeFilter.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            var results = new List<ExtensionObject>();

            if (pattern.Length == 0)
            {
                outputArguments[0] = new Variant(results.ToArray());
                return ServiceResult.Good;
            }

            try
            {
                CollectMatches(objectId, pattern, refTypeFilter, results);
            }
            catch (ArgumentException)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            outputArguments[0] = new Variant(results.ToArray());
            return ServiceResult.Good;
        }

        /// <summary>
        /// Recursively collects matching alias instances within
        /// <paramref name="categoryId"/> and any sub-categories.
        /// </summary>
        private void CollectMatches(
            NodeId categoryId,
            string pattern,
            NodeId refTypeFilter,
            List<ExtensionObject> results)
        {
            var aliasForRef = new NodeId(AliasForReferenceTypeId);
            var categoryTypeId = new NodeId(AliasNameCategoryTypeId);

            foreach (AliasInstance alias in _allAliases)
            {
                if (alias.CategoryId != categoryId)
                {
                    continue;
                }

                if (!AliasNameWildcardMatcher.IsMatch(alias.Name, pattern))
                {
                    continue;
                }

                bool matchesFilter = aliasForRef == refTypeFilter
                    || Server.TypeTree.IsTypeOf(aliasForRef, refTypeFilter);

                if (!matchesFilter)
                {
                    continue;
                }

                var data = new AliasNameDataTypeRecord(
                    new QualifiedName(alias.Name, NamespaceIndex),
                    alias.Targets);
                results.Add(new ExtensionObject(data));
            }

            // Walk into sub-categories (children of category type).
            BaseObjectState? category =
                FindPredefinedNode<BaseObjectState>(categoryId);
            if (category == null)
            {
                return;
            }

            var children = new List<BaseInstanceState>();
            category.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseObjectState obj
                    && obj.TypeDefinitionId == categoryTypeId)
                {
                    CollectMatches(
                        obj.NodeId, pattern, refTypeFilter, results);
                }
            }
        }

        private sealed record AliasInstance(
            NodeId CategoryId,
            string Name,
            NodeId ReferenceTypeId,
            NodeId[] Targets);
    }

    /// <summary>
    /// In-memory representation of the <c>AliasNameDataType</c> structure
    /// (i=23468) defined by OPC UA Part 17. Encoded as the Body of an
    /// <see cref="ExtensionObject"/> when returned from <c>FindAlias</c>.
    /// </summary>
    internal sealed class AliasNameDataTypeRecord : IEncodeable
    {
        public AliasNameDataTypeRecord(QualifiedName aliasName, NodeId[] targets)
        {
            AliasName = aliasName;
            ReferencedNodes = targets;
        }

        public QualifiedName AliasName { get; }
        public NodeId[] ReferencedNodes { get; }

        public ExpandedNodeId TypeId => new(23468u);
        public ExpandedNodeId BinaryEncodingId => new(23499u);
        public ExpandedNodeId XmlEncodingId => new(23505u);

        public void Encode(IEncoder encoder)
        {
            encoder.WriteQualifiedName("AliasName", AliasName);

            // ReferencedNodes is defined as NodeId[] in the spec.
            ArrayOf<NodeId> ids = ReferencedNodes != null
                ? ReferencedNodes.ToArrayOf()
                : default;
            encoder.WriteNodeIdArray("ReferencedNodes", ids);
        }

        public void Decode(IDecoder decoder)
        {
            // Server-side only — decode is not used.
            throw new NotSupportedException();
        }

        public bool IsEqual(IEncodeable? encodeable)
        {
            return ReferenceEquals(this, encodeable);
        }

        public object Clone() => this;
    }
}
