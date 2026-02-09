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
using System.IO;
using System.Linq;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates node identifier and corresponding NodeId/ExpandedNodeId constants.
    /// </summary>
    internal sealed class NodeIdGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeIdGenerator"/> class.
        /// </summary>
        public NodeIdGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            SortedDictionary<string, List<NodeDesign>> identifiers = GetIdentifiers();
            if (identifiers.Count == 0)
            {
                // Nothing to do
                return null;
            }
            string fileName = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.Identifiers.g.cs",
                m_context.ModelDesign.TargetNamespace.Prefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, NodeIdTemplates.File);
            template.AddReplacement(
                Tokens.Namespace,
                m_context.ModelDesign.TargetNamespace.Prefix);
            template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    m_context.ModelDesign.TargetNamespace.Value));

            template.AddReplacement(
                Tokens.ListOfIdentifiers,
                NodeIdTemplates.IdsPerNodeClass,
                identifiers,
                LoadTemplate_IdsPerNodeClass,
                WriteTemplate_IdsPerNodeClass);

            template.AddReplacement(
                Tokens.ListOfNodeIds,
                NodeIdTemplates.NodeIdPerNodeClass,
                identifiers,
                LoadTemplate_IdsPerNodeClass,
                WriteTemplate_NodeIdPerNodeClass);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        private TemplateString LoadTemplate_IdsPerNodeClass(ILoadContext context)
        {
            if (context.Target is not KeyValuePair<string, List<NodeDesign>> nodes)
            {
                return null;
            }

            if (nodes.Value == null || nodes.Value.Count == 0)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_IdsPerNodeClass(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, List<NodeDesign>> nodes ||
                nodes.Value == null)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.NodeClass, nodes.Key);
            context.Template.AddReplacement(
                Tokens.NamespacePrefix,
                m_context.ModelDesign.TargetNamespace.Prefix);
            context.Template.AddReplacement(
                Tokens.Namespace,
                m_context.ModelDesign.TargetNamespace.Value);

            context.Template.AddReplacement(
                Tokens.ListOfIdentifiers,
                NodeIdTemplates.IdDeclaration,
                nodes.Value,
                WriteTemplate_IdDeclaration);

            // Collection reflection lookups.  Note if the identifiers are not assigned
            // or duplicate, the reflection will be pointing to the last node design.
            var numericIds = new Dictionary<uint, NodeDesign>();
            foreach (NodeDesign item in nodes.Value.Where(n => n.NumericIdSpecified))
            {
                numericIds[item.NumericId] = item;
            }
            var stringIds = new Dictionary<string, NodeDesign>();
            // Save time by not looping again if all numeric
            if (numericIds.Count != nodes.Value.Count)
            {
                foreach (NodeDesign item in nodes.Value.Where(n => !n.NumericIdSpecified))
                {
                    if (!string.IsNullOrEmpty(item.StringId))
                    {
                        stringIds[item.StringId] = item;
                    }
                    else if (!string.IsNullOrEmpty(item.SymbolicId.Name))
                    {
                        stringIds[item.SymbolicId.Name] = item;
                    }
                }
            }

            // For simplicity do not emit reflection for mixed identifiers (yet)
            // Prefer numeric if both are present and more numeric ids than strings
            if (numericIds.Count > stringIds.Count)
            {
                context.Template.AddReplacement(
                    Tokens.IdentifierReflection,
                    NodeIdTemplates.Reflection,
                    [new KeyValuePair<string, List<NodeDesign>>(nodes.Key, [.. numericIds.Values])],
                    WriteTemplate_IdClassReflection);
            }
            else if (stringIds.Count > 0)
            {
                context.Template.AddReplacement(
                    Tokens.IdentifierReflection,
                    NodeIdTemplates.Reflection,
                    [new KeyValuePair<string, List<NodeDesign>>(nodes.Key, [.. stringIds.Values])],
                    WriteTemplate_IdClassReflection);
            }

            return context.Template.Render();
        }

        private bool WriteTemplate_NodeIdPerNodeClass(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, List<NodeDesign>> nodes ||
                nodes.Value == null)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.NodeClass, nodes.Key);
            context.Template.AddReplacement(
                Tokens.NamespacePrefix,
                m_context.ModelDesign.TargetNamespace.Prefix);
            context.Template.AddReplacement(
                Tokens.Namespace,
                m_context.ModelDesign.TargetNamespace.Value);

            context.Template.AddReplacement(
                Tokens.ListOfIdentifiers,
                m_context.ModelDesign.TargetNamespace.Value != Namespaces.OpcUa ?
                    NodeIdTemplates.NodeIdDeclarationAbsolute :
                    NodeIdTemplates.NodeIdDeclaration,
                nodes.Value,
                WriteTemplate_IdDeclaration);

            context.Template.AddReplacement(
                Tokens.IdentifierReflection,
                NodeIdTemplates.Reflection,
                [nodes],
                WriteTemplate_NodeIdReflection);

            return context.Template.Render();
        }

        private bool WriteTemplate_IdDeclaration(IWriteContext context)
        {
            if (context.Target is not NodeDesign node)
            {
                return false;
            }

            object id;
            string idType;
            if (node.NumericIdSpecified)
            {
                id = node.NumericId;
                idType = "uint";
            }
            else if (!string.IsNullOrEmpty(node.StringId))
            {
                id = $"\"{node.StringId}\""; // TODO: Make string resource
                idType = "string";
            }
            else
            {
                id = $"\"{node.SymbolicId.Name}\""; // TODO: Make string resource
                idType = "string";
            }

            context.Template.AddReplacement(Tokens.NodeClass, node.GetNodeClassAsString());
            context.Template.AddReplacement(Tokens.SymbolicName, node.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.Identifier, id);
            context.Template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    node.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.NamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    node.SymbolicId.Namespace));
            context.Template.AddReplacement(Tokens.IdType, idType);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_IdentifierLookup(ILoadContext context)
        {
            if (context.Target is not NodeDesign node)
            {
                return null;
            }

            string symbolicId = node.SymbolicId.Name; // See above - should be SymbolicName.Name?

            if (context.Token == Tokens.ListOfIdentifersToNames)
            {
                context.Out.WriteLine("lookup[{0}] = \"{0}\";", symbolicId);
            }
            else if (context.Token == Tokens.ListOfNamesToIdentifiers)
            {
                context.Out.WriteLine("lookup[\"{0}\"] = {0};", symbolicId);
            }
            return null;
        }

        private bool WriteTemplate_IdClassReflection(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, List<NodeDesign>> nodes)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ClassName, nodes.Key);
            context.Template.AddReplacement(Tokens.IdType,
                nodes.Value[0].NumericIdSpecified ?
                    "uint" :
                    "string");

            context.Template.AddReplacement(
                Tokens.ListOfIdentifersToNames,
                nodes.Value,
                LoadTemplate_IdentifierLookup);

            context.Template.AddReplacement(
                Tokens.ListOfNamesToIdentifiers,
                nodes.Value,
                LoadTemplate_IdentifierLookup);

            return context.Template.Render();
        }

        private bool WriteTemplate_NodeIdReflection(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, List<NodeDesign>> nodes)
            {
                return false;
            }

            context.Template.AddReplacement(
                Tokens.ClassName,
                CoreUtils.Format("{0}Ids", nodes.Key));

            context.Template.AddReplacement(
                Tokens.IdType,
                m_context.ModelDesign.TargetNamespace.Value == Namespaces.OpcUa ?
                    "global::Opc.Ua.NodeId" :
                    "global::Opc.Ua.ExpandedNodeId");

            context.Template.AddReplacement(
                Tokens.ListOfIdentifersToNames,
                nodes.Value,
                LoadTemplate_IdentifierLookup);

            context.Template.AddReplacement(
                Tokens.ListOfNamesToIdentifiers,
                nodes.Value,
                LoadTemplate_IdentifierLookup);

            return context.Template.Render();
        }

        private bool IsParentExcluded(NodeDesign root, KeyValuePair<string, HierarchyNode> child)
        {
            string parentId = child.Key;

            while (parentId != null)
            {
                int index = parentId.LastIndexOf('_');

                if (index > 0)
                {
                    parentId = parentId[..index];
                }

                if (!root.Hierarchy.Nodes.TryGetValue(parentId, out HierarchyNode parent))
                {
                    return false;
                }

                if (m_context.ModelDesign.IsExcluded(parent.Instance))
                {
                    return true;
                }

                if (index <= 0)
                {
                    break;
                }
            }

            return false;
        }

        private SortedDictionary<string, List<NodeDesign>> GetIdentifiers()
        {
            SortedDictionary<string, List<NodeDesign>> identifiers = [];

            for (int ii = 0; ii < m_context.ModelDesign.Nodes.Length; ii++)
            {
                NodeDesign node = m_context.ModelDesign.Nodes[ii];

                if (m_context.ModelDesign.IsExcluded(node))
                {
                    continue;
                }

                if (node is InstanceDesign instance &&
                    instance.TypeDefinitionNode != null &&
                    m_context.ModelDesign.IsExcluded(instance.TypeDefinitionNode))
                {
                    continue;
                }

                string nodeClass = node.GetNodeClassAsString();

                if (!identifiers.TryGetValue(nodeClass, out List<NodeDesign> nodesWithinClass))
                {
                    identifiers[nodeClass] = nodesWithinClass = [];
                }

                if (!nodesWithinClass.Contains(node))
                {
                    nodesWithinClass.Add(node);
                }

                if (node.Hierarchy == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, HierarchyNode> current in node.Hierarchy.Nodes)
                {
                    if (string.IsNullOrEmpty(current.Key))
                    {
                        continue;
                    }

                    if (m_context.ModelDesign.IsExcluded(current.Value.Instance))
                    {
                        continue;
                    }

                    if (IsParentExcluded(node, current))
                    {
                        continue;
                    }

                    var method = current.Value.Instance as MethodDesign;

                    if (method?.MethodDeclarationNode != null &&
                        m_context.ModelDesign.IsExcluded(method?.MethodDeclarationNode))
                    {
                        continue;
                    }

                    if (node is TypeDesign)
                    {
                        if (!current.Value.ExplicitlyDefined)
                        {
                            if (current.Value.Inherited &&
                                (current.Value.Instance == null ||
                                    current.Value.Instance.BrowseName == current.Value.RelativePath))
                            {
                                continue;
                            }

                            if (current.Value.Instance is InstanceDesign child &&
                                child.ModellingRule != ModellingRule.Mandatory)
                            {
                                continue;
                            }
                        }
                    }

                    if (node is InstanceDesign)
                    {
                        if (current.Value.Instance is not InstanceDesign child)
                        {
                            continue;
                        }

                        if (child.ModellingRule != ModellingRule.Mandatory)
                        {
                            continue;
                        }
                    }

                    if (current.Value.Instance.NumericIdSpecified ?
                        current.Value.Instance.NumericId == 0 :
                        current.Value.Instance.StringId == null)
                    {
                        continue;
                    }

                    nodeClass = current.Value.Instance.GetNodeClassAsString();

                    if (!identifiers.TryGetValue(nodeClass, out nodesWithinClass))
                    {
                        identifiers[nodeClass] = nodesWithinClass = [];
                    }

                    nodesWithinClass.Add(current.Value.Instance);
                }
            }

            return identifiers;
        }

        private readonly IGeneratorContext m_context;
    }
}
