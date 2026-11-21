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
using System.IO;
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits <c>AggregateFunctionIds.All</c>, the NodeId and BrowseName of every
    /// instance of <c>AggregateFunctionType</c> the core model declares, in the
    /// order the model declares them.
    /// </summary>
    /// <remarks>
    /// The list lets a server register the OPC 10000-13 aggregate functions from
    /// the model instead of from a hand-maintained table. It is emitted for the
    /// core model only: other models expose their object ids as
    /// <c>ExpandedNodeId</c>, and no companion model declares aggregate functions
    /// a server registers by default.
    /// </remarks>
    internal sealed class AggregateFunctionIdsGenerator : IGenerator
    {
        private const string kAggregateFunctionTypeName = "AggregateFunctionType";

        private readonly IGeneratorContext m_context;

        public AggregateFunctionIdsGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            if (!string.Equals(
                    m_context.ModelDesign.TargetNamespace.Value,
                    Namespaces.OpcUa,
                    StringComparison.Ordinal))
            {
                return [];
            }

            List<ObjectDesign> aggregates = CollectAggregateFunctions();
            if (aggregates.Count == 0)
            {
                return [];
            }

            string namespacePrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.AggregateFunctionIds.g.cs", namespacePrefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, AggregateFunctionIdsTemplates.File);

            template.AddReplacement(Tokens.Namespace, namespacePrefix);
            template.AddReplacement(
                Tokens.ListOfIdentifiers,
                AggregateFunctionIdsTemplates.Entry,
                aggregates,
                null,
                context => WriteTemplateEntry(context, namespacePrefix));

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        private static bool WriteTemplateEntry(IWriteContext context, string namespacePrefix)
        {
            // Emit only ever renders the list of ObjectDesign collected below.
            var aggregate = (ObjectDesign)context.Target!;

            context.Template.AddReplacement(Tokens.NamespacePrefix, namespacePrefix);
            context.Template.AddReplacement(Tokens.SymbolicId, aggregate.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.SymbolicName, aggregate.SymbolicName.Name);
            return context.Template.Render();
        }

        /// <summary>
        /// Collects the objects whose type definition is AggregateFunctionType or a
        /// subtype of it, in model order. Skips the same nodes NodeIdGenerator skips
        /// (excluded nodes and instances of an excluded type definition), so every
        /// entry has an ObjectIds constant to reference.
        /// </summary>
        private List<ObjectDesign> CollectAggregateFunctions()
        {
            var result = new List<ObjectDesign>();
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is ObjectDesign instance &&
                    !m_context.ModelDesign.IsExcluded(instance) &&
                    (instance.TypeDefinitionNode == null ||
                        !m_context.ModelDesign.IsExcluded(instance.TypeDefinitionNode)) &&
                    instance.SymbolicId?.Name != null &&
                    instance.SymbolicName?.Name != null &&
                    IsAggregateFunction(instance))
                {
                    result.Add(instance);
                }
            }
            return result;
        }

        private static bool IsAggregateFunction(ObjectDesign instance)
        {
            if (IsAggregateFunctionType(instance.TypeDefinition))
            {
                return true;
            }

            TypeDesign? current = instance.TypeDefinitionNode;
            while (current != null)
            {
                if (IsAggregateFunctionType(current.SymbolicId))
                {
                    return true;
                }
                current = current.BaseTypeNode;
            }
            return false;
        }

        private static bool IsAggregateFunctionType(XmlQualifiedName? name)
        {
            return name != null &&
                string.Equals(name.Name, kAggregateFunctionTypeName, StringComparison.Ordinal) &&
                string.Equals(name.Namespace, Namespaces.OpcUa, StringComparison.Ordinal);
        }
    }
}
