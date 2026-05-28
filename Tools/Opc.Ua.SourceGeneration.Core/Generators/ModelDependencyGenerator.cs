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
using Opc.Ua.Schema.Types;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits one <c>[assembly: Opc.Ua.ModelDependencyAttribute(...)]</c> line
    /// per model the generated assembly emits (self-declaration) and one per
    /// dependency model declared by the <see cref="IModelDesign"/>. The
    /// self-declaration entry additionally carries a base64-encoded
    /// Deflate-compressed <see cref="ModelDependencyV1"/> payload that
    /// downstream source generators can decode to resolve cross-assembly
    /// type references without re-walking <c>AdditionalFiles</c>.
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
            // downstream tie-breaks have authoritative metadata. This is the
            // only entry that carries a payload — the compact type-table
            // ModelDependencyV1 blob.
            string selfVersion = m_context.ModelDesign.TargetVersion ?? target.Version;
            string selfPubDate = FormatDate(m_context.ModelDesign.TargetPublicationDate)
                ?? target.PublicationDate;
            string selfPayload = BuildSelfPayload(target);
            entries.Add(new Entry(
                target.Value, target.Prefix,
                selfVersion, selfPubDate, target.Name,
                selfPayload));
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
                if (ns.Value == Types.Namespaces.OpcUa)
                {
                    continue;
                }
                if (!seen.Add(ns.Value))
                {
                    continue;
                }
                entries.Add(new Entry(
                    ns.Value, ns.Prefix, ns.Version, ns.PublicationDate, ns.Name,
                    null));
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
                if (r.ModelUri == Types.Namespaces.OpcUa)
                {
                    continue;
                }
                if (!seen.Add(r.ModelUri))
                {
                    continue;
                }
                entries.Add(new Entry(
                    r.ModelUri, r.Prefix, r.Version, r.PublicationDate, r.Name,
                    null));
            }

            return entries;
        }

        private string BuildSelfPayload(Namespace target)
        {
            // OpcUa root is implicit to every consumer; do not emit a payload.
            if (target.Value == Opc.Ua.Types.Namespaces.OpcUa)
            {
                return null;
            }
            ModelDependencyV1 payload = BuildPayload(target);
            if (payload.Nodes.Count == 0)
            {
                return null;
            }
            return payload.ToBase64Payload();
        }

        private ModelDependencyV1 BuildPayload(Namespace target)
        {
            var payload = new ModelDependencyV1 { ModelUri = target.Value };
            string targetUri = target.Value;
            foreach (NodeDesign node in m_context.ModelDesign.Nodes ?? [])
            {
                if (m_context.ModelDesign.IsExcluded(node))
                {
                    continue;
                }
                if (node.SymbolicId is null ||
                    !string.Equals(node.SymbolicId.Namespace, targetUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (node is not TypeDesign type)
                {
                    continue;
                }
                DependencyNodeKind kind = type switch
                {
                    ObjectTypeDesign => DependencyNodeKind.ObjectType,
                    VariableTypeDesign => DependencyNodeKind.VariableType,
                    ReferenceTypeDesign => DependencyNodeKind.ReferenceType,
                    DataTypeDesign => DependencyNodeKind.DataType,
                    _ => DependencyNodeKind.Unknown
                };
                if (kind == DependencyNodeKind.Unknown)
                {
                    continue;
                }
                var entry = new DependencyNode
                {
                    SymbolicName = type.SymbolicId.Name ?? string.Empty,
                    SymbolicNamespace = type.SymbolicId.Namespace ?? string.Empty,
                    ClassName = type.ClassName ?? type.SymbolicId.Name ?? string.Empty,
                    Kind = kind,
                    BaseTypeName = type.BaseType?.Name,
                    BaseTypeNamespace = type.BaseType?.Namespace,
                    NumericId = type.NumericIdSpecified ? type.NumericId : 0u,
                    StringId = string.IsNullOrEmpty(type.StringId) ? null : type.StringId,
                    IsAbstract = type.IsAbstract
                };
                if (type is DataTypeDesign dataType)
                {
                    entry.IsEnumeration = dataType.IsEnumeration;
                    if (dataType.Fields != null && dataType.Fields.Length > 0)
                    {
                        var fields = new List<DependencyDataField>(dataType.Fields.Length);
                        foreach (Parameter field in dataType.Fields)
                        {
                            if (field == null) { continue; }
                            fields.Add(new DependencyDataField(
                                name: field.Name ?? string.Empty,
                                dataTypeName: field.DataType?.Name ?? string.Empty,
                                dataTypeNamespace: field.DataType?.Namespace ?? string.Empty,
                                valueRank: (int)field.ValueRank));
                        }
                        entry.Fields = fields;
                    }
                }
                else
                {
                    // For ObjectType / VariableType / ReferenceType, carry
                    // their declared InstanceDesign children so downstream
                    // consumers can recognise inherited members and suppress
                    // duplicate emission.
                    if (type.HasChildren && type.Children?.Items != null)
                    {
                        var children = new List<DependencyChild>(type.Children.Items.Length);
                        foreach (InstanceDesign child in type.Children.Items)
                        {
                            if (child == null) { continue; }
                            byte instanceKind = child switch
                            {
                                PropertyDesign => (byte)3,
                                VariableDesign => (byte)2,
                                MethodDesign => (byte)4,
                                _ => (byte)1
                            };
                            byte modellingRule = child.ModellingRule switch
                            {
                                ModellingRule.Mandatory => (byte)1,
                                ModellingRule.Optional => (byte)2,
                                ModellingRule.OptionalPlaceholder => (byte)3,
                                ModellingRule.MandatoryPlaceholder => (byte)4,
                                ModellingRule.ExposesItsArray => (byte)5,
                                _ => (byte)0
                            };
                            var entryChild = new DependencyChild
                            {
                                BrowseName = child.BrowseName ?? child.SymbolicName?.Name ?? string.Empty,
                                SymbolicName = child.SymbolicName?.Name ?? string.Empty,
                                TypeDefinitionName = child.TypeDefinition?.Name ?? string.Empty,
                                TypeDefinitionNamespace = child.TypeDefinition?.Namespace ?? string.Empty,
                                ModellingRule = modellingRule,
                                InstanceKind = instanceKind
                            };
                            if (child is VariableDesign variable)
                            {
                                entryChild.DataTypeName = variable.DataType?.Name ?? string.Empty;
                                entryChild.DataTypeNamespace = variable.DataType?.Namespace ?? string.Empty;
                                entryChild.ValueRank = (int)variable.ValueRank;
                            }
                            else if (child is MethodDesign method)
                            {
                                entryChild.InputArguments = DependencyMethodArgs(method.InputArguments);
                                entryChild.OutputArguments = DependencyMethodArgs(method.OutputArguments);
                            }
                            children.Add(entryChild);
                        }
                        if (children.Count > 0)
                        {
                            entry.Children = children;
                        }
                    }
                }
                payload.Nodes.Add(entry);
            }
            // Deterministic order so the assembly is byte-reproducible.
            payload.Nodes.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.SymbolicNamespace, b.SymbolicNamespace);
                return c != 0 ? c : string.CompareOrdinal(a.SymbolicName, b.SymbolicName);
            });
            return payload;
        }

        private static List<DependencyMethodArg> DependencyMethodArgs(Parameter[] args)
        {
            if (args == null || args.Length == 0)
            {
                return [];
            }
            var result = new List<DependencyMethodArg>(args.Length);
            foreach (Parameter a in args)
            {
                if (a == null) { continue; }
                result.Add(new DependencyMethodArg(
                    name: a.Name ?? string.Empty,
                    dataTypeName: a.DataType?.Name ?? string.Empty,
                    dataTypeNamespace: a.DataType?.Namespace ?? string.Empty,
                    valueRank: (int)a.ValueRank));
            }
            return result;
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
            context.Template.AddReplacement(Tokens.ModelName, FormatNullableLiteral(entry.Name));
            context.Template.AddReplacement(Tokens.ModelPayload, FormatNullableLiteral(entry.Payload));

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
            return d?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private readonly record struct Entry(
            string ModelUri,
            string Prefix,
            string Version,
            string PublicationDate,
            string Name,
            string Payload);

        private readonly IGeneratorContext m_context;
    }
}
