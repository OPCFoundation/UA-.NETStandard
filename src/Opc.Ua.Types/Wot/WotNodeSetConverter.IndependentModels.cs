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
 *
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
using System.Threading;
using Opc.Ua.Export;
using Opc.Ua.Types;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        private static bool ImportsIndependentReadableModels(
            WotDocumentSet documents,
            WotNodeSetConverterOptions options)
        {
            if (options.DocumentSetMode != WotDocumentSetMode.IndependentReadableModels)
            {
                return false;
            }
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                if (TakesRestorePath(entry.Document))
                {
                    return false;
                }
            }
            return true;
        }

        private static List<WotDiagnostic> CreateDocumentSetDiagnostics(WotNodeSetConverterOptions options)
        {
            return
            [
                new WotDiagnostic(
                    WotDiagnosticSeverity.Info,
                    WotDiagnosticCode.DocumentSetModeSelected,
                    $"Document-set processing mode: {options.DocumentSetMode}.")
            ];
        }

        private static void NormalizeIndependentModelParts(
            WotDocumentSet documents,
            List<UANodeSet> parts,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var uris = new SortedSet<string>(s_namespaceUriComparer);
            var owners = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UANodeSet part = parts[index];
                foreach (string uri in part.NamespaceUris ?? [])
                {
                    if (string.IsNullOrEmpty(uri))
                    {
                        ReportIndependentConflict("A namespace URI is empty.", index);
                    }
                    else if (uri != WotVocabulary.OpcUaNamespace)
                    {
                        uris.Add(uri);
                    }
                }
                string? root = GetIndependentRootIdentity(
                    documents.Entries[index].Document, part, cancellationToken);
                if (root is null)
                {
                    ReportIndependentConflict("An independent document has no identifiable converted root.", index);
                }
                else if (!owners.Add(NormalizeExpandedNodeId(root)))
                {
                    ReportIndependentConflict("Independent documents claim the same root identity.", index);
                }
                if (index > 0 && !EqualDocumentSetNamespaces(parts[0].ServerUris, part.ServerUris))
                {
                    ReportIndependentConflict("Independent model ServerUris tables disagree.", index);
                }
            }
            if (uris.Count >= ushort.MaxValue)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverLimitExceeded,
                    "The namespace union exceeds the supported namespace index range."));
            }
            if (HasErrors(diagnostics))
            {
                return;
            }

            string[] namespaces = [.. uris];
            var target = new UANodeSet
            {
                NamespaceUris = namespaces,
                ServerUris = parts.Count == 0 ? null : parts[0].ServerUris
            };
            ServiceMessageContext valueContext = CreateIndependentValueContext(target, options);
            var indices = new Dictionary<string, ushort>(StringComparer.Ordinal)
            {
                [WotVocabulary.OpcUaNamespace] = 0
            };
            for (int index = 0; index < namespaces.Length; index++)
            {
                indices.Add(namespaces[index], checked((ushort)(index + 1)));
            }
            for (int index = 0; index < parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UANodeSet part = parts[index];
                string? nodeId = null;
                try
                {
                    INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(
                        part, WotNodeSetAliases.Instance);
                    var remapping = new IndependentNamespaceMapping(
                        part.NamespaceUris, indices, aliases, part.ServerUris?.Length ?? 0);
                    var mappedAliases = new List<NodeIdAlias>();
                    var declaredAliases = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (NodeIdAlias alias in part.Aliases ?? [])
                    {
                        if (string.IsNullOrEmpty(alias.Alias) || string.IsNullOrEmpty(alias.Value))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid, "An alias has no name or identity.");
                        }
                        string? mapped = remapping.NodeId(alias.Value);
                        if (declaredAliases.TryGetValue(alias.Alias!, out string? previous))
                        {
                            if (previous != mapped)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadNodeIdInvalid, "An alias has conflicting declarations.");
                            }
                            continue;
                        }
                        declaredAliases.Add(alias.Alias!, mapped);
                        mappedAliases.Add(new NodeIdAlias { Alias = alias.Alias, Value = mapped });
                    }
                    foreach (ModelTableEntry model in part.Models ?? [])
                    {
                        RemapIndependentModel(model, remapping, options.MaxXmlDepth);
                    }
                    bool changed = !EqualDocumentSetNamespaces(part.NamespaceUris, namespaces);
                    foreach (UANode node in part.Items ?? [])
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        nodeId = node.NodeId;
                        if (string.IsNullOrEmpty(nodeId))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid, "An owned Node has no identity.");
                        }
                        RemapIndependentNode(node, remapping);
                        if (node is UAVariable { Value: { } value } variable)
                        {
                            variable.Value = part.RebaseValue(value, target, valueContext, changed);
                        }
                        else if (node is UAVariableType { Value: { } typeValue } variableType)
                        {
                            variableType.Value = part.RebaseValue(typeValue, target, valueContext, changed);
                        }
                    }
                    part.NamespaceUris = namespaces;
                    part.Aliases = [.. mappedAliases];
                }
                catch (Exception exception) when (exception is
                    ServiceResultException or ArgumentException or FormatException or OverflowException)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NamespaceRebaseUnsupported,
                        exception.Message,
                        new WotLocation(nodeId: nodeId, reference: documents.Entries[index].Href)));
                }
            }

            void ReportIndependentConflict(string message, int index)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    message,
                    new WotLocation(reference: documents.Entries[index].Href)));
            }
        }

        private static string? GetIndependentRootIdentity(
            WotDocument document,
            UANodeSet part,
            CancellationToken cancellationToken)
        {
            string? authored = GetUavString(document, "id");
            if (authored is not null)
            {
                return authored;
            }
            var identityContext = new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = DeriveModelUri(document) }],
                NamespaceUris = part.NamespaceUris is null ? null : (string[])part.NamespaceUris.Clone()
            };
            string generated = NormalizeExpandedNodeId(
                ToPortableNodeId(GenerateRootNodeId(document, identityContext), identityContext.NamespaceUris)!);
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(part, WotNodeSetAliases.Instance);
            foreach (UANode node in part.Items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? actual = ToPortableNodeId(ResolveArchivedAlias(node.NodeId, aliases), part.NamespaceUris);
                if (actual is not null && NormalizeExpandedNodeId(actual) == generated)
                {
                    return actual;
                }
            }
            return null;
        }

        private static void RemapIndependentModel(
            ModelTableEntry model,
            IndependentNamespaceMapping remapping,
            int remainingDepth)
        {
            if (remainingDepth <= 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The model dependency metadata exceeds the XML depth bound.");
            }
            RemapIndependentPermissions(model.RolePermissions, remapping);
            foreach (ModelTableEntry required in model.RequiredModel ?? [])
            {
                RemapIndependentModel(required, remapping, remainingDepth - 1);
            }
        }

        private static void RemapIndependentNode(UANode node, IndependentNamespaceMapping remapping)
        {
            node.NodeId = remapping.NodeId(node.NodeId);
            node.BrowseName = remapping.QualifiedName(node.BrowseName);
            RemapIndependentPermissions(node.RolePermissions, remapping);
            foreach (Reference reference in node.References ?? [])
            {
                reference.ReferenceType = remapping.NodeId(reference.ReferenceType);
                reference.Value = remapping.NodeId(reference.Value, allowRemote: true);
            }
            if (node is UAInstance instance)
            {
                instance.ParentNodeId = remapping.NodeId(instance.ParentNodeId);
            }
            switch (node)
            {
                case UAVariable variable:
                    variable.DataType = remapping.NodeId(variable.DataType);
                    break;
                case UAVariableType variableType:
                    variableType.DataType = remapping.NodeId(variableType.DataType);
                    break;
                case UAMethod method:
                    method.MethodDeclarationId = remapping.NodeId(method.MethodDeclarationId);
                    break;
                case UADataType { Definition: { } definition }:
                    definition.Name = remapping.QualifiedName(definition.Name);
                    definition.BaseType = remapping.QualifiedName(definition.BaseType);
                    foreach (DataTypeField field in definition.Field ?? [])
                    {
                        field.DataType = remapping.NodeId(field.DataType);
                    }
                    break;
            }
        }

        private static void RemapIndependentPermissions(
            RolePermission[]? permissions,
            IndependentNamespaceMapping remapping)
        {
            foreach (RolePermission permission in permissions ?? [])
            {
                permission.Value = remapping.NodeId(permission.Value);
            }
        }

        private static UANodeSet MergeIndependentModelParts(
            WotDocumentSet documents,
            List<UANodeSet> parts,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var result = new UANodeSet
            {
                NamespaceUris = parts.Count == 0 ? [] : parts[0].NamespaceUris,
                ServerUris = parts.Count == 0 ? null : parts[0].ServerUris
            };
            var models = new SortedDictionary<string, ModelTableEntry>(s_namespaceUriComparer);
            var nodes = new SortedDictionary<string, UANode>(StringComparer.Ordinal);
            var extensions = new List<System.Xml.XmlElement>();
            var order = new List<int>(parts.Count);
            for (int index = 0; index < parts.Count; index++)
            {
                order.Add(index);
            }
            order.Sort((left, right) => string.CompareOrdinal(
                documents.Entries[left].Href, documents.Entries[right].Href));
            foreach (int index in order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UANodeSet part = parts[index];
                if (part.LastModifiedSpecified)
                {
                    if (result.LastModifiedSpecified && result.LastModified != part.LastModified)
                    {
                        ReportConflict("Independent model LastModified assertions disagree.", index);
                    }
                    result.LastModified = part.LastModified;
                    result.LastModifiedSpecified = true;
                }
                foreach (ModelTableEntry model in part.Models ?? [])
                {
                    if (string.IsNullOrEmpty(model.ModelUri))
                    {
                        ReportConflict("A model declaration has no namespace URI.", index);
                    }
                    else if (models.TryGetValue(model.ModelUri!, out ModelTableEntry? previous))
                    {
                        if (!NodeSetComparer.CompareEquivalent(
                            new UANodeSet { NamespaceUris = result.NamespaceUris, Models = [previous] },
                            new UANodeSet { NamespaceUris = result.NamespaceUris, Models = [model] },
                            options.ToComparisonOptions()).AreEquivalent)
                        {
                            ReportConflict(
                                $"Independent declarations of model '{model.ModelUri}' disagree.", index);
                        }
                    }
                    else
                    {
                        models.Add(model.ModelUri!, model);
                    }
                }
                foreach (UANode node in part.Items ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (nodes.TryGetValue(node.NodeId!, out UANode? previous))
                    {
                        if (!NodeSetComparer.CompareEquivalent(
                            new UANodeSet { NamespaceUris = result.NamespaceUris, Items = [previous] },
                            new UANodeSet { NamespaceUris = result.NamespaceUris, Items = [node] },
                            options.ToComparisonOptions()).AreEquivalent)
                        {
                            ReportConflict(
                                "Independent models contain conflicting facts for one Node.", index, node.NodeId);
                        }
                    }
                    else
                    {
                        nodes.Add(node.NodeId!, node);
                    }
                }
                extensions.AddRange(part.Extensions ?? []);
            }
            result.Models = [.. models.Values];
            result.Items = [.. nodes.Values];
            result.Aliases = MergeIndependentAliases(parts);
            result.Extensions = extensions.Count == 0 ? null : [.. extensions];
            return NodeSetAliasCompleter.Complete(result, WotNodeSetAliases.Instance)!;

            void ReportConflict(string message, int index, string? nodeId = null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    message,
                    new WotLocation(nodeId: nodeId, reference: documents.Entries[index].Href)));
            }
        }

        private static NodeIdAlias[] MergeIndependentAliases(List<UANodeSet> parts)
        {
            var values = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (UANodeSet part in parts)
            {
                foreach (NodeIdAlias alias in part.Aliases ?? [])
                {
                    if (!values.TryGetValue(alias.Alias!, out SortedSet<string>? identities))
                    {
                        identities = new SortedSet<string>(StringComparer.Ordinal);
                        values.Add(alias.Alias!, identities);
                    }
                    identities.Add(alias.Value!);
                }
            }
            var names = new HashSet<string>(values.Keys, StringComparer.Ordinal);
            var aliases = new List<NodeIdAlias>();
            foreach (KeyValuePair<string, SortedSet<string>> entry in values)
            {
                int ordinal = 0;
                foreach (string identity in entry.Value)
                {
                    string name = entry.Key;
                    if (entry.Value.Count > 1)
                    {
                        do
                        {
                            name = entry.Key + "_" + (++ordinal).ToString(CultureInfo.InvariantCulture);
                        }
                        while (!names.Add(name));
                    }
                    aliases.Add(new NodeIdAlias { Alias = name, Value = identity });
                }
            }
            return [.. aliases];
        }

        private static int CompareNamespaceUris(string? left, string? right)
        {
            if (left is null || right is null)
            {
                return string.CompareOrdinal(left, right);
            }
            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                int leftCodePoint = char.ConvertToUtf32(left, leftIndex);
                int rightCodePoint = char.ConvertToUtf32(right, rightIndex);
                int comparison = leftCodePoint.CompareTo(rightCodePoint);
                if (comparison != 0)
                {
                    return comparison;
                }
                leftIndex += leftCodePoint > char.MaxValue ? 2 : 1;
                rightIndex += rightCodePoint > char.MaxValue ? 2 : 1;
            }
            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }

        private static EncodeableFactory CreateIndependentValueFactory()
        {
            var factory = new EncodeableFactory();
            factory.Builder.AddEncodeableType<Argument>().Commit();
            return factory;
        }

        private static ServiceMessageContext CreateIndependentValueContext(
            UANodeSet target,
            WotNodeSetConverterOptions options)
        {
            IServiceMessageContext? configured = options.ValueEncodingContext;
            var context = configured is null
                ? new ServiceMessageContext(new LoggerUtils.NullTelemetryContext(), s_independentValueFactory)
                : new ServiceMessageContext(configured, configured.Telemetry);
            // Registered structure field definitions may use the factory context's indexes.
            foreach (string uri in target.NamespaceUris ?? [])
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            if (context.ServerUris.Count == 0)
            {
                context.ServerUris.Update([string.Empty]);
            }
            foreach (string uri in target.ServerUris ?? [])
            {
                context.ServerUris.GetIndexOrAppend(uri);
            }
            context.MaxStringLength = Bound(configured?.MaxStringLength ?? 0, options.MaxNodeSetSize);
            context.MaxByteStringLength = Bound(configured?.MaxByteStringLength ?? 0, options.MaxNodeSetSize);
            context.MaxArrayLength = Bound(configured?.MaxArrayLength ?? 0, options.MaxNodeSetSize);
            context.MaxMessageSize = Bound(configured?.MaxMessageSize ?? 0, options.MaxNodeSetSize);
            context.MaxEncodingNestingLevels = Bound(configured?.MaxEncodingNestingLevels ?? 0, options.MaxXmlDepth);
            return context;

            static int Bound(int configured, int limit)
            {
                return configured > 0 ? Math.Min(configured, limit) : limit;
            }
        }

        private sealed class IndependentNamespaceMapping(
            string[]? source,
            Dictionary<string, ushort> target,
            INodeSetAliasResolver aliases,
            int serverCount)
        {
            public string? NodeId(string? text, bool allowRemote = false)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return text;
                }
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (aliases.TryResolve(text!, out string resolved))
                {
                    if (!visited.Add(text!))
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "An alias contains a cycle.");
                    }
                    text = resolved;
                }
                ExpandedNodeId value = ExpandedNodeId.Parse(text!);
                if (!allowRemote && value.ServerIndex != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A local identity names a remote server.");
                }
                if (value.ServerIndex > serverCount)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "An expanded identity uses an undeclared server index.");
                }
                ushort index = value.NamespaceUri is { Length: > 0 } uri
                    ? GetIndex(uri)
                    : MapIndex(value.NamespaceIndex);
                NodeId id = value.InnerNodeId.WithNamespaceIndex(index);
                return value.ServerIndex == 0
                    ? id.ToString()
                    : new ExpandedNodeId(id, null, value.ServerIndex).ToString();
            }

            public string? QualifiedName(string? text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return text;
                }
                QualifiedName name = Ua.QualifiedName.Parse(text!);
                return name.WithNamespaceIndex(MapIndex(name.NamespaceIndex)).ToString();
            }

            private ushort MapIndex(ushort index)
            {
                if (index == 0)
                {
                    return 0;
                }
                if (source is null || index > source.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, $"Namespace index {index} is not declared by its source.");
                }
                return GetIndex(source[index - 1]);
            }

            private ushort GetIndex(string uri)
            {
                if (!target.TryGetValue(uri, out ushort index))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, $"Namespace URI '{uri}' is not declared by its source.");
                }
                return index;
            }
        }

        private static readonly IComparer<string> s_namespaceUriComparer =
            Comparer<string>.Create(CompareNamespaceUris);
        private static readonly EncodeableFactory s_independentValueFactory = CreateIndependentValueFactory();
    }
}
