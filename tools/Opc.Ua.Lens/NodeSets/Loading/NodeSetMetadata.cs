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
using System.Linq;
using System.Threading;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Export;
using XmlElement = System.Xml.XmlElement;

namespace UaLens.NodeSets.Loading
{
    /// <summary>
    /// Reads only discovery metadata; the stack remains responsible for parsing NodeSet2 documents.
    /// </summary>
    internal static class NodeSetMetadata
    {
        internal static ArrayOf<ModelTableEntry> ReadHeader(
            Stream content,
            Action<string>? modelDiscovered = null)
        {
            using var reader = XmlReader.Create(content, NodeSetDocumentReader.CreateXmlSettings());
            reader.MoveToContent();
            if (reader.LocalName != "UANodeSet" || reader.NamespaceURI != XmlNamespace)
            {
                return [];
            }
            var models = new List<ModelTableEntry>();
            var namespaceUris = new List<string>();
            bool namespaceTable = false;
            bool modelsTable = false;
            while (reader.Read())
            {
                if (reader.Depth > NodeSetDocumentReader.MaxXmlDepth)
                {
                    throw new InvalidDataException("The NodeSet2 metadata exceeds the XML depth limit.");
                }
                if (reader.NamespaceURI != XmlNamespace)
                {
                    continue;
                }
                if (reader.LocalName == "NamespaceUris" && reader.Depth == 1)
                {
                    namespaceTable = reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement;
                }
                if (reader.LocalName == "Models" && reader.Depth == 1)
                {
                    modelsTable = reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement;
                }
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Models" && reader.Depth == 1)
                {
                    if (models.Count > 0)
                    {
                        return models.ToArray();
                    }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                if (namespaceTable && reader.Depth == 2 && reader.LocalName == "Uri")
                {
                    using XmlReader value = reader.ReadSubtree();
                    value.MoveToContent();
                    namespaceUris.Add(value.ReadElementContentAsString());
                }
                if (modelsTable && reader.Depth == 2 && reader.LocalName == "Model")
                {
                    string uri = reader.GetAttribute("ModelUri") ??
                        throw new InvalidDataException("A NodeSet2 Model has no ModelUri.");
                    modelDiscovered?.Invoke(uri);
                    string? date = reader.GetAttribute("PublicationDate");
                    models.Add(new ModelTableEntry
                    {
                        ModelUri = uri,
                        Version = reader.GetAttribute("Version"),
                        ModelVersion = reader.GetAttribute("ModelVersion"),
                        PublicationDate = date is null ? default :
                            XmlConvert.ToDateTime(date, XmlDateTimeSerializationMode.Utc),
                        PublicationDateSpecified = date is not null
                    });
                }
                if (reader.Depth == 1 && reader.LocalName.StartsWith("UA", StringComparison.Ordinal))
                {
                    if (models.Count == 0)
                    {
                        if (namespaceUris.Count == 0)
                        {
                            namespaceUris.Add(Namespaces.OpcUa);
                        }
                        foreach (string uri in namespaceUris)
                        {
                            modelDiscovered?.Invoke(uri);
                            models.Add(new ModelTableEntry { ModelUri = uri });
                        }
                    }
                    return models.ToArray();
                }
            }
            return models.ToArray();
        }

        internal static ArrayOf<ModelTableEntry> ProvidedModels(NodeSetDocument document)
        {
            if (document.NodeSet.Models is { Length: > 0 } models)
            {
                var uris = new HashSet<string>(StringComparer.Ordinal);
                foreach (ModelTableEntry model in models)
                {
                    if (string.IsNullOrWhiteSpace(model.ModelUri) || !uris.Add(model.ModelUri))
                    {
                        throw new InvalidDataException(
                            $"'{document.Source}' has missing or duplicate ModelUri metadata.");
                    }
                }
                return models;
            }
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (UANode node in document.NodeSet.Items ?? [])
            {
                if (!NodeId.TryParse(node.NodeId!, out NodeId id))
                {
                    throw new InvalidDataException($"'{document.Source}' has an invalid NodeId '{node.NodeId}'.");
                }
                owned.Add(Namespace(document.NodeSet, id.NamespaceIndex));
            }
            return owned.Select(static uri => new ModelTableEntry { ModelUri = uri }).ToArray();
        }

        /// <summary>
        /// Supplements declared requirements with namespaces used by node attributes, references, and values.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The metadata exceeds supported bounds, or a used identifier or alias is invalid.
        /// </exception>
        internal static ArrayOf<NodeSetRequirement> Requirements(
            NodeSetDocument document,
            CancellationToken cancellationToken)
        {
            var requirements = new List<NodeSetRequirement>();
            if (document.NodeSet.Models is { Length: > 0 } models)
            {
                var pending = new Queue<(ModelTableEntry Model, int Depth)>(
                    models.Select(static model => (model, 0)));
                int count = 0;
                while (pending.TryDequeue(out (ModelTableEntry Model, int Depth) entry))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++count > 4096 || entry.Depth > NodeSetLoader.MaxDependencyDepth)
                    {
                        throw new InvalidDataException(
                            $"'{document.Source}' exceeds the RequiredModel metadata limit.");
                    }
                    foreach (ModelTableEntry required in entry.Model.RequiredModel ?? [])
                    {
                        requirements.Add(ToRequirement(required));
                        pending.Enqueue((required, entry.Depth + 1));
                    }
                }
            }
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> aliases = (document.NodeSet.Aliases ?? [])
                .ToDictionary(static alias => alias.Alias!, static alias => alias.Value!, StringComparer.Ordinal);
            foreach (UANode node in document.NodeSet.Items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddQualifiedName(node.BrowseName);
                if (node is UAInstance instance)
                {
                    AddIdentifier(instance.ParentNodeId);
                }
                foreach (Reference reference in node.References ?? [])
                {
                    AddIdentifier(reference.ReferenceType);
                    AddIdentifier(reference.Value);
                }
                XmlElement? value = null;
                switch (node)
                {
                    case UAVariable variable:
                        AddIdentifier(variable.DataType);
                        value = variable.Value;
                        break;
                    case UAVariableType variableType:
                        AddIdentifier(variableType.DataType);
                        value = variableType.Value;
                        break;
                    case UAMethod method:
                        AddIdentifier(method.MethodDeclarationId);
                        break;
                    case UADataType dataType when dataType.Definition is not null:
                        AddQualifiedName(dataType.Definition.BaseType);
                        foreach (DataTypeField field in dataType.Definition.Field ?? [])
                        {
                            AddIdentifier(field.DataType);
                        }
                        break;
                }
                if (value is not null)
                {
                    foreach (XmlNode identifier in value.GetElementsByTagName("Identifier", Namespaces.OpcUaXsd))
                    {
                        AddIdentifier(identifier.InnerText);
                    }
                    foreach (XmlNode index in value.GetElementsByTagName("NamespaceIndex", Namespaces.OpcUaXsd))
                    {
                        if (ushort.TryParse(index.InnerText, CultureInfo.InvariantCulture, out ushort number))
                        {
                            namespaces.Add(Namespace(document.NodeSet, number));
                        }
                    }
                }
            }
            var declared = new HashSet<string>(
                requirements.Select(static requirement => requirement.ModelUri), StringComparer.Ordinal);
            foreach (string uri in namespaces)
            {
                if (!declared.Contains(uri))
                {
                    requirements.Add(new NodeSetRequirement(uri));
                }
            }
            return requirements.ToArray();

            void AddQualifiedName(string? text)
            {
                int separator = text?.IndexOf(':', StringComparison.Ordinal) ?? -1;
                if (separator > 0 &&
                    ushort.TryParse(text.AsSpan(0, separator), CultureInfo.InvariantCulture, out ushort index))
                {
                    namespaces.Add(Namespace(document.NodeSet, index));
                }
            }

            void AddIdentifier(string? text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (aliases.TryGetValue(text, out string? alias))
                {
                    if (!visited.Add(text))
                    {
                        throw new InvalidDataException($"'{document.Source}' contains an alias cycle.");
                    }
                    text = alias;
                }
                if (!ExpandedNodeId.TryParse(text, out ExpandedNodeId id))
                {
                    throw new InvalidDataException(
                        $"'{document.Source}' contains an invalid reference '{text}'. " +
                        "Use a valid NodeId or declare the name in <Aliases>.");
                }
                if (id.ServerIndex == 0)
                {
                    namespaces.Add(string.IsNullOrEmpty(id.NamespaceUri) ?
                        Namespace(document.NodeSet, id.NamespaceIndex) : id.NamespaceUri);
                }
            }
        }

        internal static NodeSetRequirement ToRequirement(ModelTableEntry model)
        {
            return new NodeSetRequirement(
                model.ModelUri ?? throw new InvalidDataException("RequiredModel has no ModelUri."),
                model.Version,
                model.PublicationDateSpecified ? model.PublicationDate : null,
                model.ModelVersion);
        }

        internal static void Verify(NodeSetDocument document, NodeSetRequirement requirement)
        {
            ModelTableEntry? model = (ProvidedModels(document).ToArray() ?? [])
                .FirstOrDefault(model => string.Equals(model.ModelUri, requirement.ModelUri, StringComparison.Ordinal));
            if (model is null || !Satisfies(model, requirement))
            {
                throw new InvalidDataException(
                    $"'{document.Source}' does not supply required model '{requirement.ModelUri}' " +
                    $"(Version '{requirement.Version}', PublicationDate '{requirement.PublicationDate:O}', " +
                    $"ModelVersion '{requirement.ModelVersion}').");
            }
        }

        internal static bool SameRevision(ModelTableEntry first, ModelTableEntry second)
        {
            if (!Satisfies(first, ToRequirement(second)) || !Satisfies(second, ToRequirement(first)))
            {
                return false;
            }
            if (!first.PublicationDateSpecified && string.IsNullOrEmpty(first.ModelVersion))
            {
                // Without ordered revision metadata, conflicting labels cannot establish a common revision.
                return string.Equals(first.Version, second.Version, StringComparison.Ordinal);
            }
            return true;
        }

        internal static bool Satisfies(ModelTableEntry model, NodeSetRequirement requirement)
        {
            if (!string.Equals(model.ModelUri, requirement.ModelUri, StringComparison.Ordinal))
            {
                return false;
            }
            bool actualHasVersion = !string.IsNullOrEmpty(model.ModelVersion);
            bool requiredHasVersion = !string.IsNullOrEmpty(requirement.ModelVersion);
            if (actualHasVersion || requiredHasVersion)
            {
                if (actualHasVersion != requiredHasVersion)
                {
                    return actualHasVersion;
                }
                if (!TrySemanticVersion(model.ModelVersion, out string[] actual, out string actualPrerelease) ||
                    !TrySemanticVersion(requirement.ModelVersion, out string[] minimum, out string minimumPrerelease))
                {
                    throw new InvalidDataException(
                        $"Model '{model.ModelUri}' has invalid semantic ModelVersion metadata.");
                }
                for (int ii = 0; ii < actual.Length; ii++)
                {
                    int comparison = CompareNumericIdentifier(actual[ii], minimum[ii]);
                    if (comparison != 0)
                    {
                        return comparison > 0;
                    }
                }
                int prerelease = ComparePrerelease(actualPrerelease, minimumPrerelease);
                if (prerelease != 0)
                {
                    return prerelease > 0;
                }
            }
            // Part 6, Annex F.2: dates break semantic-version ties; Version is only a human-readable label.
            if (requirement.PublicationDate is { } date)
            {
                return model.PublicationDateSpecified &&
                    model.PublicationDate.ToUniversalTime() >= date.ToUniversalTime();
            }
            return true;
        }

        private static string Namespace(UANodeSet nodeSet, ushort index)
        {
            if (index == 0)
            {
                return Namespaces.OpcUa;
            }
            if (nodeSet.NamespaceUris is null || index > nodeSet.NamespaceUris.Length)
            {
                throw new InvalidDataException($"NodeSet2 namespace index {index} is outside NamespaceUris.");
            }
            return nodeSet.NamespaceUris[index - 1];
        }

        private static bool TrySemanticVersion(
            string? text,
            out string[] version,
            out string prerelease)
        {
            string[] build = (text ?? string.Empty).Split('+');
            string[] parts = build[0].Split('-', 2);
            version = parts[0].Split('.');
            prerelease = parts.Length > 1 ? parts[1] : string.Empty;
            return build.Length <= 2 &&
                (build.Length == 1 || ValidIdentifiers(build[1], false)) &&
                version.Length == 3 &&
                version.All(static part =>
                    part.Length > 0 && part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0')) &&
                (parts.Length == 1 || ValidIdentifiers(prerelease, true));
        }

        private static bool ValidIdentifiers(string text, bool prohibitLeadingZero)
        {
            return text.Split('.').All(part => part.Length > 0 &&
                part.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') &&
                (!prohibitLeadingZero || part.Length == 1 || part[0] != '0' || !part.All(char.IsAsciiDigit)));
        }

        private static int CompareNumericIdentifier(string actual, string minimum)
        {
            return actual.Length != minimum.Length ? actual.Length.CompareTo(minimum.Length) :
                string.CompareOrdinal(actual, minimum);
        }

        private static int ComparePrerelease(string actual, string minimum)
        {
            if (actual.Length == 0 || minimum.Length == 0)
            {
                return actual.Length == minimum.Length ? 0 : actual.Length == 0 ? 1 : -1;
            }
            string[] left = actual.Split('.');
            string[] right = minimum.Split('.');
            for (int ii = 0; ii < Math.Min(left.Length, right.Length); ii++)
            {
                bool leftNumeric = left[ii].All(char.IsAsciiDigit);
                bool rightNumeric = right[ii].All(char.IsAsciiDigit);
                int comparison = leftNumeric && rightNumeric ? CompareNumericIdentifier(left[ii], right[ii]) :
                    leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 :
                    string.CompareOrdinal(left[ii], right[ii]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return left.Length.CompareTo(right.Length);
        }

        internal const string XmlNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
