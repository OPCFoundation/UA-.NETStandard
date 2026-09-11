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
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates namespace and browse name constants.
    /// TODO: Use resource generator infrastructure.
    /// </summary>
    internal sealed class ConstantsGenerator : IGenerator
    {
        /// <summary>
        /// Create constants generator
        /// </summary>
        public ConstantsGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<ConstantsGenerator>();
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            SortedDictionary<string, string> browseNames = [];
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                CollectBrowseNames(node, browseNames);
            }

            if (browseNames.Count == 0)
            {
                // Nothing to do
                return [];
            }

            string fileName = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.Constants.g.cs",
                m_context.ModelDesign.TargetNamespace.Prefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ConstantsTemplates.File);
            template.AddReplacement(
                Tokens.Namespace,
                m_context.ModelDesign.TargetNamespace.Prefix);
            template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    m_context.ModelDesign.TargetNamespace.Value));

            template.AddReplacement(
                Tokens.ListOfNamespaceUris,
                ConstantsTemplates.NamespaceUri,
                GetNamespaceUris(),
                WriteTemplate_NamespaceUriStrings);

            template.AddReplacement(
                Tokens.ListOfBrowseNames,
                ConstantsTemplates.BrowseName,
                browseNames.ToArray(),
                LoadTemplate_BrowseNames,
                WriteTemplate_BrowseNames);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        private TemplateString LoadTemplate_BrowseNames(ILoadContext context)
        {
            if (context.Target is not KeyValuePair<string, string> browseName ||
                browseName.Value == null)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_BrowseNames(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, string> browseName ||
                browseName.Value == null)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.SymbolicName, browseName.Key);
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                browseName.Value,
                m_logger);

            return context.Template.Render();
        }

        private static bool WriteTemplate_NamespaceUriStrings(IWriteContext context)
        {
            if (context.Target is not NamespaceUriConstant constant)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.NamespaceUri, constant.Uri);
            context.Template.AddReplacement(Tokens.CodeName, constant.Prefix);
            context.Template.AddReplacement(Tokens.Name, constant.Name);

            return context.Template.Render();
        }

        private void CollectBrowseNames(
            NodeDesign node,
            SortedDictionary<string, string> browseNames)
        {
            if (m_context.ModelDesign.IsExcluded(node))
            {
                return;
            }

            if (node.SymbolicName.Namespace == m_context.ModelDesign.TargetNamespace.Value)
            {
                browseNames[node.SymbolicName.Name] = node.BrowseName;
            }

            if (node.Children?.Items == null)
            {
                return;
            }

            foreach (NodeDesign child in node.Children.Items)
            {
                if (m_context.ModelDesign.IsExcluded(child))
                {
                    continue;
                }

                if (child.SymbolicName == new XmlQualifiedName(BrowseNames.DefaultInstanceBrowseName, Namespaces.OpcUa))
                {
                    var variable = (VariableDesign)child;

                    if (variable.DecodedValue is QualifiedName qname &&
                        !string.IsNullOrEmpty(qname.Name))
                    {
                        // The default instance browse name is authored data, not a
                        // symbolic name, so it can contain spaces and punctuation.
                        // The constant name has to be a legal C# identifier while the
                        // constant value stays the browse name verbatim.
                        // Sanitizing can land on a constant name another node
                        // already claimed ("Device Set" and "DeviceSet" both
                        // yield "DeviceSet"), and this dictionary is keyed by
                        // symbolic name everywhere else, so a hit here is not
                        // necessarily a design error. Keep the entry that is
                        // already there rather than overwriting it with a
                        // different value or failing the whole model.
                        string constantName = qname.Name.ToCSharpIdentifierPreserveCase();

                        if (!browseNames.ContainsKey(constantName))
                        {
                            browseNames[constantName] = qname.Name;
                        }
                    }

                    continue;
                }

                if (child.SymbolicName.Namespace == m_context.ModelDesign.TargetNamespace.Value)
                {
                    if (browseNames.TryGetValue(child.SymbolicName.Name, out string browseName))
                    {
                        if (browseName != child.BrowseName)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadTypeMismatch,
                                "Two nodes with the same symbolic name have different browse names: {0} != {1}.",
                                browseName,
                                child.BrowseName);
                        }

                        continue;
                    }

                    browseNames[child.SymbolicName.Name] = child.BrowseName;
                }

                if (child is InstanceDesign)
                {
                    CollectBrowseNames(child, browseNames);
                }
            }
        }

        private List<NamespaceUriConstant> GetNamespaceUris()
        {
            List<NamespaceUriConstant> namespaceUris = [];

            // Keyed by name *and* URI: two namespaces can legitimately sanitize
            // to the same constant name (GetNameFromUri drops the host, so
            // "http://a.org/UA/Robotics/" and "http://b.org/UA/Robotics/" both
            // become "Robotics"). Suppressing the second one there would make
            // every reference to it silently resolve to the first one's URI, so
            // only an exact repeat of the same (name, URI) pair is dropped.
            var emitted = new HashSet<(string Name, string Uri)>();
            for (int ii = 0; ii < m_context.ModelDesign.Namespaces.Length; ii++)
            {
                Namespace ns = m_context.ModelDesign.Namespaces[ii];

                if (!string.IsNullOrEmpty(ns.Value) && emitted.Add((ns.Name, ns.Value)))
                {
                    namespaceUris.Add(new NamespaceUriConstant(ns.Name, ns.Prefix, ns.Value));
                }

                // Only emit the "...Xsd" companion constant when the XML namespace
                // actually differs from the namespace URI. When they are equal the
                // plain constant above already covers it - emitting both here used
                // to produce two "...Xsd" constants and no plain one.
                // GetConstantForXmlNamespace applies the same condition, so the
                // name it references is always one that was emitted.
                if (!string.IsNullOrEmpty(ns.XmlNamespace) &&
                    !string.Equals(ns.XmlNamespace, ns.Value, StringComparison.Ordinal) &&
                    emitted.Add((ns.Name + "Xsd", ns.XmlNamespace)))
                {
                    namespaceUris.Add(
                        new NamespaceUriConstant(ns.Name + "Xsd", ns.Prefix, ns.XmlNamespace));
                }
            }
            return namespaceUris;
        }

        /// <summary>
        /// A single namespace URI constant to emit: the C# constant name, the
        /// namespace prefix it belongs to and the URI value.
        /// </summary>
        private sealed class NamespaceUriConstant
        {
            public NamespaceUriConstant(string name, string prefix, string uri)
            {
                Name = name;
                Prefix = prefix;
                Uri = uri;
            }

            public string Name { get; }
            public string Prefix { get; }
            public string Uri { get; }
        }

        private readonly IGeneratorContext m_context;
        private readonly Microsoft.Extensions.Logging.ILogger m_logger;
    }
}
