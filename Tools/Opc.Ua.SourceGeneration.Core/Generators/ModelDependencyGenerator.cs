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
using System.Globalization;
using System.IO;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits one <c>[assembly: Opc.Ua.ModelDependencyAttribute(...)]</c> line
    /// per model the generated assembly emits (self-declaration) and one per
    /// dependency model declared by the <see cref="IModelDesign"/>. The
    /// resulting file lets downstream source-generator consumers discover
    /// the model closure of a referenced assembly without re-walking
    /// AdditionalFiles.
    /// </summary>
    internal sealed class ModelDependencyGenerator : IGenerator
    {
        public ModelDependencyGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            Namespace target = m_context.ModelDesign.TargetNamespace;
            if (target == null ||
                string.IsNullOrEmpty(target.Value) ||
                string.IsNullOrEmpty(target.Prefix))
            {
                return [];
            }

            List<Entry> entries = CollectEntries(target);
            if (entries.Count == 0)
            {
                return [];
            }

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.ModelDependencies.g.cs", target.Prefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ModelDependencyTemplates.File);

            template.AddReplacement(
                Tokens.ListOfModelDependencies,
                ModelDependencyTemplates.Entry,
                entries,
                WriteEntry);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        private List<Entry> CollectEntries(Namespace target)
        {
            var entries = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Self-declaration: one entry for the target namespace using the
            // generator-resolved target version / publication date so that
            // downstream tie-breaks have authoritative metadata.
            string selfVersion = m_context.ModelDesign.TargetVersion ?? target.Version;
            string selfPubDate = FormatDate(m_context.ModelDesign.TargetPublicationDate)
                ?? target.PublicationDate;
            entries.Add(new Entry(target.Value, target.Prefix, selfVersion, selfPubDate));
            seen.Add(target.Value);

            // Re-emit dependencies declared on the model (transitive closure).
            // Skip the OpcUa root namespace (always implicit) and skip self.
            foreach (Namespace ns in m_context.ModelDesign.Namespaces ?? [])
            {
                if (ns == null ||
                    string.IsNullOrEmpty(ns.Value) ||
                    string.IsNullOrEmpty(ns.Prefix))
                {
                    continue;
                }
                if (ns.Value == Ua.Types.Namespaces.OpcUa)
                {
                    continue;
                }
                if (!seen.Add(ns.Value))
                {
                    continue;
                }
                entries.Add(new Entry(ns.Value, ns.Prefix, ns.Version, ns.PublicationDate));
            }

            // Re-emit the closure picked up from referenced assemblies so
            // downstream consumers see one merged closure on this assembly.
            foreach (KeyValuePair<string, ModelDependencyReference> entry
                in m_context.ReferencedModels)
            {
                ModelDependencyReference r = entry.Value;
                if (!r.IsValid)
                {
                    continue;
                }
                if (r.ModelUri == Ua.Types.Namespaces.OpcUa)
                {
                    continue;
                }
                if (!seen.Add(r.ModelUri))
                {
                    continue;
                }
                entries.Add(new Entry(r.ModelUri, r.Prefix, r.Version, r.PublicationDate));
            }

            return entries;
        }

        private static bool WriteEntry(IWriteContext context)
        {
            if (context.Target is not Entry entry)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ModelUri, EscapeForString(entry.ModelUri));
            context.Template.AddReplacement(Tokens.Prefix, EscapeForString(entry.Prefix));
            context.Template.AddReplacement(Tokens.ModelVersion, FormatNullableLiteral(entry.Version));
            context.Template.AddReplacement(
                Tokens.ModelPublicationDate,
                FormatNullableLiteral(entry.PublicationDate));

            return context.Template.Render();
        }

        private static string EscapeForString(string value)
        {
            // Escape backslash and quote; everything else is safe for short URI / prefix strings.
            return value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string FormatNullableLiteral(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "null"
                : CoreUtils.Format("\"{0}\"", EscapeForString(value));
        }

        private static string FormatDate(DateTime? d)
        {
            return d.HasValue
                ? d.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : null;
        }

        private readonly record struct Entry(
            string ModelUri,
            string Prefix,
            string Version,
            string PublicationDate);

        private readonly IGeneratorContext m_context;
    }
}
