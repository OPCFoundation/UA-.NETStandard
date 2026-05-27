/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using Opc.Ua.SourceGeneration.Snapshot;
using Opc.Ua.Types;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Snapshot-import surface on <see cref="ModelDesignValidator"/>:
    /// lets a downstream consumer's source generator ingest the type
    /// table from a referenced assembly's
    /// <c>[assembly: ModelSnapshotAttribute]</c> without the consumer
    /// having to re-add the upstream NodeSet2/ModelDesign XML to
    /// <c>AdditionalFiles</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each snapshot entry is materialised as a <see cref="TypeDesign"/>
    /// subclass and registered in the validator's node table by
    /// <see cref="NodeDesign.SymbolicId"/>. Downstream BaseType /
    /// TypeDefinition lookups then resolve through the same
    /// <c>m_nodes</c> dictionary the XML-loading path populates.
    /// </para>
    /// <para>
    /// The validator's internal tables are initialised lazily by the
    /// main <c>Validate</c> entry point. <see cref="ImportSnapshot"/>
    /// is therefore safe to call only AFTER initialisation has set
    /// <c>m_nodes</c> / <c>m_designFilePaths</c> / namespace tables.
    /// The convention is to import each referenced snapshot from
    /// <c>Generators.GenerateCode</c> right before opening the model
    /// design, so the lazy structures are already in place.
    /// </para>
    /// </remarks>
    public partial class ModelDesignValidator
    {
        /// <summary>
        /// Imports a model snapshot into the validator's node table.
        /// Safe to call multiple times. Stored snapshots are
        /// re-applied automatically inside <c>ValidateModel</c>
        /// after the validator's internal tables are reset, so the
        /// snapshot types are visible to the dependency-loading
        /// pass.
        /// </summary>
        /// <param name="snapshot">The deserialised snapshot.</param>
        /// <param name="prefix">The C# prefix the producing assembly
        /// uses for the snapshot's model (so the synthetic Namespace
        /// entry matches what downstream emitters expect to see).</param>
        /// <param name="name">The C# identifier the producing assembly
        /// used inside its <c>Namespaces</c> class for the snapshot's
        /// model (e.g. <c>"OpcUaDi"</c>).</param>
        public void ImportSnapshot(
            ModelSnapshotV1 snapshot,
            string prefix,
            string name)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            if (string.IsNullOrEmpty(snapshot.ModelUri))
            {
                throw new ArgumentException(
                    "Snapshot has no ModelUri.",
                    nameof(snapshot));
            }
            m_pendingSnapshots ??= [];
            m_pendingSnapshots.Add(new PendingSnapshot(snapshot, prefix, name));
        }

        /// <summary>
        /// Called from inside <c>ValidateModel</c> right after the
        /// node-table reset and the built-in OpcUa model load, so all
        /// stored snapshot entries are registered before dependency
        /// design files are processed.
        /// </summary>
        internal void ApplyPendingSnapshots()
        {
            if (m_pendingSnapshots == null || m_pendingSnapshots.Count == 0)
            {
                return;
            }
            EnsureSnapshotTablesInitialised();
            foreach (PendingSnapshot pending in m_pendingSnapshots)
            {
                ApplySnapshot(pending.Snapshot);
            }
            // Cross-snapshot references can now resolve.
            LinkSnapshotChildren();
        }

        private void ApplySnapshot(ModelSnapshotV1 snapshot)
        {
            // Mark the snapshot's namespace as "resolved without a backing
            // file" so dependency-missing errors do not fire.
            m_designFilePaths[snapshot.ModelUri] = string.Empty;

            // Allocate or recover a namespace index for the snapshot model.
            int nsIndex = m_context.NamespaceUris.GetIndexOrAppend(snapshot.ModelUri);

            foreach (SnapshotNode entry in snapshot.Nodes)
            {
                NodeDesign design = MaterialiseSnapshotNode(entry);
                if (design == null)
                {
                    continue;
                }

                XmlQualifiedName symbolicId = design.SymbolicId;
                if (symbolicId == null || string.IsNullOrEmpty(symbolicId.Name))
                {
                    continue;
                }

                // Do not overwrite an entry that the validator already loaded
                // from its own AdditionalFiles — the explicit AdditionalFile
                // always wins.
                if (m_nodes.ContainsKey(symbolicId))
                {
                    continue;
                }
                m_nodes[symbolicId] = design;

                if (entry.NumericId != 0)
                {
                    var nodeId = new NodeId(entry.NumericId, (ushort)nsIndex);
                    if (!m_nodesByNodeId.ContainsKey(nodeId))
                    {
                        m_nodesByNodeId[nodeId] = design;
                    }
                    m_symbolicIdToNodeId[symbolicId] = nodeId;
                }
            }
        }

        /// <summary>
        /// Ensures the snapshot-targeted dictionaries exist. The full
        /// validation path creates them as part of regular loading;
        /// when <see cref="ImportSnapshot"/> is called before validation
        /// the tables must be available so the snapshot is not lost.
        /// </summary>
        private void EnsureSnapshotTablesInitialised()
        {
            m_nodes ??= [];
            m_nodesByNodeId ??= [];
            m_identifiers ??= [];
            m_namespaceTables ??= [];
            m_designFilePaths ??= [];
        }

        private static NodeDesign MaterialiseSnapshotNode(SnapshotNode entry)
        {
            var symbolicId = new XmlQualifiedName(
                entry.SymbolicName ?? string.Empty,
                entry.SymbolicNamespace ?? string.Empty);

            XmlQualifiedName baseType = null;
            if (!string.IsNullOrEmpty(entry.BaseTypeName))
            {
                baseType = new XmlQualifiedName(
                    entry.BaseTypeName,
                    entry.BaseTypeNamespace ?? string.Empty);
            }

            TypeDesign design = entry.Kind switch
            {
                SnapshotNodeKind.ObjectType => new ObjectTypeDesign(),
                SnapshotNodeKind.VariableType => new VariableTypeDesign(),
                SnapshotNodeKind.ReferenceType => new ReferenceTypeDesign(),
                SnapshotNodeKind.DataType => new DataTypeDesign(),
                _ => null
            };
            if (design == null)
            {
                return null;
            }

            design.SymbolicName = symbolicId;
            design.SymbolicId = symbolicId;
            design.BrowseName = entry.SymbolicName ?? string.Empty;
            design.DisplayName = new LocalizedText { Value = entry.SymbolicName ?? string.Empty };
            design.ClassName = string.IsNullOrEmpty(entry.ClassName)
                ? entry.SymbolicName ?? string.Empty
                : entry.ClassName;
            design.BaseType = baseType;
            design.IsAbstract = entry.IsAbstract;
            if (entry.NumericId != 0)
            {
                design.NumericId = entry.NumericId;
                design.NumericIdSpecified = true;
            }
            if (!string.IsNullOrEmpty(entry.StringId))
            {
                design.StringId = entry.StringId;
            }

            if (design is DataTypeDesign dt && entry.Fields != null && entry.Fields.Count > 0)
            {
                var parameters = new Parameter[entry.Fields.Count];
                for (int i = 0; i < entry.Fields.Count; i++)
                {
                    SnapshotDataField f = entry.Fields[i];
                    parameters[i] = new Parameter
                    {
                        Name = f.Name,
                        DataType = new XmlQualifiedName(
                            f.DataTypeName ?? string.Empty,
                            f.DataTypeNamespace ?? string.Empty),
                        ValueRank = (ValueRank)f.ValueRank,
                        Description = new LocalizedText()
                    };
                }
                dt.Fields = parameters;
                dt.HasFields = true;
            }
            else if (entry.Children != null && entry.Children.Count > 0)
            {
                // Reconstruct Children so the consumer's
                // SetOverriddenNodes walk (which descends through
                // BaseTypeNode → Children.Items) can recognise the
                // upstream's inherited members and properly set
                // OveriddenNode on downstream re-declarations.
                // TypeDefinitionNode + DataTypeNode references are
                // resolved later by LinkSnapshotChildren() after all
                // snapshots are applied.
                var instanceItems = new InstanceDesign[entry.Children.Count];
                for (int i = 0; i < entry.Children.Count; i++)
                {
                    instanceItems[i] = MaterialiseSnapshotChild(symbolicId, entry.Children[i]);
                }
                design.Children = new ListOfChildren { Items = instanceItems };
                design.HasChildren = true;
            }

            // Mark as an external/upstream declaration so consumer
            // generators don't try to re-emit this type locally.
            design.IsDeclaration = true;

            return design;
        }

        private static InstanceDesign MaterialiseSnapshotChild(
            XmlQualifiedName parentSymbolicId,
            SnapshotChild c)
        {
            InstanceDesign instance = c.InstanceKind switch
            {
                3 => new PropertyDesign(),
                2 => new VariableDesign(),
                4 => new MethodDesign(),
                _ => new ObjectDesign()
            };
            instance.BrowseName = c.BrowseName ?? string.Empty;
            var childSymbolicId = new XmlQualifiedName(
                string.IsNullOrEmpty(c.SymbolicName) ? c.BrowseName : c.SymbolicName,
                parentSymbolicId.Namespace);
            instance.SymbolicName = childSymbolicId;
            instance.SymbolicId = new XmlQualifiedName(
                NodeDesign.CreateSymbolicId(
                    parentSymbolicId.Name,
                    childSymbolicId.Name),
                parentSymbolicId.Namespace);
            instance.ModellingRule = c.ModellingRule switch
            {
                1 => ModellingRule.Mandatory,
                2 => ModellingRule.Optional,
                3 => ModellingRule.OptionalPlaceholder,
                4 => ModellingRule.MandatoryPlaceholder,
                5 => ModellingRule.ExposesItsArray,
                _ => ModellingRule.None
            };
            instance.ModellingRuleSpecified = c.ModellingRule != 0;
            instance.DisplayName = new LocalizedText
            {
                Value = c.BrowseName ?? string.Empty,
                IsAutogenerated = true
            };
            if (!string.IsNullOrEmpty(c.TypeDefinitionName))
            {
                instance.TypeDefinition = new XmlQualifiedName(
                    c.TypeDefinitionName,
                    c.TypeDefinitionNamespace ?? string.Empty);
            }
            if (instance is VariableDesign variable)
            {
                if (!string.IsNullOrEmpty(c.DataTypeName))
                {
                    variable.DataType = new XmlQualifiedName(
                        c.DataTypeName,
                        c.DataTypeNamespace ?? string.Empty);
                }
                variable.ValueRank = (ValueRank)c.ValueRank;
                variable.ValueRankSpecified = c.ValueRank != (int)ValueRank.Scalar;
            }
            else if (instance is MethodDesign method)
            {
                // Methods carry their own argument lists; the TypeDefinition
                // would point at a MethodType (a separate MethodDesign in
                // the upstream model) which is NOT carried by the snapshot,
                // so clear it to avoid a downstream FindNode<MethodDesign>
                // failure during ValidateInstance.
                method.TypeDefinition = null;
                method.InputArguments = MaterialiseMethodArgs(c.InputArguments);
                method.OutputArguments = MaterialiseMethodArgs(c.OutputArguments);
                method.HasArguments = method.InputArguments.Length > 0
                    || method.OutputArguments.Length > 0;
            }
            // Mark as inherited-declaration so consumer code paths
            // that iterate children for emission can short-circuit.
            instance.IsDeclaration = true;
            return instance;
        }

        private static Parameter[] MaterialiseMethodArgs(
            IReadOnlyList<SnapshotMethodArg> args)
        {
            if (args == null || args.Count == 0)
            {
                return [];
            }
            var result = new Parameter[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                SnapshotMethodArg a = args[i];
                result[i] = new Parameter
                {
                    Name = a.Name ?? string.Empty,
                    DataType = new XmlQualifiedName(
                        a.DataTypeName ?? string.Empty,
                        a.DataTypeNamespace ?? string.Empty),
                    ValueRank = (ValueRank)a.ValueRank,
                    Description = new LocalizedText()
                };
            }
            return result;
        }

        /// <summary>
        /// Resolve <c>TypeDefinitionNode</c> / <c>DataTypeNode</c> /
        /// <c>BaseTypeNode</c> references on snapshot-materialised
        /// types and their children. Run after all snapshots have
        /// been applied so cross-snapshot references can resolve.
        /// </summary>
        private void LinkSnapshotChildren()
        {
            if (m_pendingSnapshots == null || m_pendingSnapshots.Count == 0)
            {
                return;
            }
            foreach (NodeDesign node in m_nodes.Values)
            {
                if (node is not TypeDesign type)
                {
                    continue;
                }
                if (!type.IsDeclaration)
                {
                    continue;
                }
                // Resolve BaseTypeNode.
                if (type.BaseType != null &&
                    type.BaseTypeNode == null &&
                    m_nodes.TryGetValue(type.BaseType, out NodeDesign baseDesign))
                {
                    type.BaseTypeNode = baseDesign as TypeDesign;
                }
                // Resolve children's TypeDefinitionNode / DataTypeNode.
                if (!type.HasChildren || type.Children?.Items == null)
                {
                    continue;
                }
                foreach (InstanceDesign instance in type.Children.Items)
                {
                    if (instance.TypeDefinition != null &&
                        instance.TypeDefinitionNode == null &&
                        m_nodes.TryGetValue(instance.TypeDefinition, out NodeDesign tdNode))
                    {
                        instance.TypeDefinitionNode = tdNode as TypeDesign;
                    }
                    if (instance is VariableDesign variable &&
                        variable.DataType != null &&
                        variable.DataTypeNode == null &&
                        m_nodes.TryGetValue(variable.DataType, out NodeDesign dtNode))
                    {
                        variable.DataTypeNode = dtNode as DataTypeDesign;
                    }
                }
            }
        }

        private List<PendingSnapshot> m_pendingSnapshots;

        private sealed record PendingSnapshot(ModelSnapshotV1 Snapshot, string Prefix, string Name);
    }
}
