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
                return null;
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
            context.Template.AddReplacement(Tokens.BrowseName, browseName.Value);

            return context.Template.Render();
        }

        private bool WriteTemplate_NamespaceUriStrings(IWriteContext context)
        {
            if (context.Target is not string uri)
            {
                return false;
            }

            for (int ii = 0; ii < m_context.ModelDesign.Namespaces.Length; ii++)
            {
                Namespace ns = m_context.ModelDesign.Namespaces[ii];

                if (uri != ns.Value && uri != ns.XmlNamespace)
                {
                    continue;
                }

                context.Template.AddReplacement(Tokens.NamespaceUri, uri);
                context.Template.AddReplacement(Tokens.CodeName, ns.Prefix);

                if (uri != ns.XmlNamespace)
                {
                    context.Template.AddReplacement(Tokens.Name, ns.Name);
                }
                else
                {
                    context.Template.AddReplacement(Tokens.Name, ns.Name + "Xsd");
                }
            }

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

                    if (variable.DecodedValue is QualifiedName qname)
                    {
                        browseNames[qname.Name] = qname.Name;
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

                if (child is InstanceDesign instance)
                {
                    CollectBrowseNames(child, browseNames);
                }
            }
        }

        private List<string> GetNamespaceUris()
        {
            List<string> namespaceUris = [];
            for (int ii = 0; ii < m_context.ModelDesign.Namespaces.Length; ii++)
            {
                namespaceUris.Add(m_context.ModelDesign.Namespaces[ii].Value);
                if (!string.IsNullOrEmpty(m_context.ModelDesign.Namespaces[ii].XmlNamespace))
                {
                    namespaceUris.Add(m_context.ModelDesign.Namespaces[ii].XmlNamespace);
                }
            }
            return namespaceUris;
        }

        private readonly IGeneratorContext m_context;
    }
}
