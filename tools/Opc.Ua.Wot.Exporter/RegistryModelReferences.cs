// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter
{
    /// <summary>
    /// Assigns registry addresses to converted models without changing native identities or source payloads.
    /// </summary>
    internal sealed class RegistryModelReferences(string registryBaseUrl, string groupId)
    {
        /// <summary>
        /// Registers actual document IDs before any source's outgoing links are written.
        /// </summary>
        public void Add(
            UANodeSet nodeSet,
            WotDocumentSet documents,
            Dictionary<ExpandedNodeId, string> generatedIds)
        {
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                WotDocument document = entry.Document;
                if (document.Kind != WotDocumentKind.ThingModel ||
                    !TryGetIdentity(document.RootElement, out ExpandedNodeId identity) ||
                    !generatedIds.TryGetValue(identity, out string? generatedId))
                {
                    continue;
                }
                string id = document.Id ?? generatedId;
                string? version = null;
                foreach (ModelTableEntry model in nodeSet.Models ?? [])
                {
                    if (model.ModelUri == identity.NamespaceUri)
                    {
                        version = model.Version;
                        break;
                    }
                }
                var target = new Model(id, identity, version);
                m_models.TryAdd(identity, target);
                AddAlias(id, target);
                AddAlias(identity.ToString(), target);
                if (identity.NamespaceUri == Types.Namespaces.OpcUa)
                {
                    AddAlias(identity.InnerNodeId.ToString(), target);
                }
            }
        }

        /// <summary>
        /// Rewrites a source in memory so missing target versions fail before any of its files are written.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public ArrayOf<ReadOnlyMemory<byte>> Write(WotDocumentSet documents, CancellationToken cancellationToken)
        {
            var aliases = new Dictionary<string, Model>(StringComparer.Ordinal);
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetModel(entry.Document) is { } model)
                {
                    ValidateModel(model);
                    aliases[entry.Href] = model;
                }
            }
            var result = new List<ReadOnlyMemory<byte>>(documents.Entries.Count);
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Model? model = GetModel(entry.Document);
                using var output = new MemoryStream();
                using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
                {
                    WriteNode(writer, entry.Document.RootElement, aliases, model);
                }
                result.Add(output.ToArray());
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Finds lightweight metadata for a freshly converted model without retaining its document.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private Model? GetModel(WotDocument document)
        {
            if (document.Kind != WotDocumentKind.ThingModel)
            {
                return null;
            }
            if (TryGetIdentity(document.RootElement, out ExpandedNodeId identity) &&
                m_models.TryGetValue(identity, out Model? model))
            {
                return model;
            }
            throw new FormatException("No native model identity for a Thing Model.");
        }

        /// <summary>
        /// Rejects ambiguous IDs instead of choosing a target by source-file order.
        /// </summary>
        private void AddAlias(string alias, Model model)
        {
            if (m_aliases.TryGetValue(alias, out Model? existing) && existing != model)
            {
                m_ambiguous.Add(alias);
            }
            else
            {
                m_aliases[alias] = model;
            }
        }

        /// <summary>
        /// Writes only WoT document, affordance and schema positions; literal values and native payloads stay intact.
        /// </summary>
        private void WriteNode(
            Utf8JsonWriter writer, JsonElement node, Dictionary<string, Model> aliases, Model? model = null)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                node.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            if (model is not null)
            {
                writer.WriteString("id", model.Id);
                writer.WritePropertyName("version");
                writer.WriteStartObject();
                writer.WriteString("model", model.Version);
                if (node.TryGetProperty("version", out JsonElement version) &&
                    version.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in version.EnumerateObject())
                    {
                        if (property.Name != "model")
                        {
                            property.WriteTo(writer);
                        }
                    }
                }
                writer.WriteEndObject();
            }
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (model is not null && property.Name is "id" or "version")
                {
                    continue;
                }
                writer.WritePropertyName(property.Name);
                if (property.Name == "links" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray();
                    foreach (JsonElement link in property.Value.EnumerateArray())
                    {
                        WriteLink(writer, link, aliases);
                    }
                    writer.WriteEndArray();
                }
                else if (property.Name is "tm:ref" or "$ref" &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    TryResolve(property.Value.GetString()!, aliases, out string href))
                {
                    writer.WriteStringValue(href);
                }
                else if (property.Name is "properties" or "actions" or "events" or "schemaDefinitions" or
                    "uriVariables" or "$defs" or "definitions" or "patternProperties" &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty child in property.Value.EnumerateObject())
                    {
                        writer.WritePropertyName(child.Name);
                        WriteNode(writer, child.Value, aliases);
                    }
                    writer.WriteEndObject();
                }
                else if (property.Name is "input" or "output" or "data" or "items" or "dataResponse" or
                    "additionalProperties" or "not" or "subscription" or "cancellation" or
                    "if" or "then" or "else" or "contains" or "propertyNames" &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    WriteNode(writer, property.Value, aliases);
                }
                else if (property.Name is "oneOf" or "anyOf" or "allOf" or "items" or "uav:eventSelectClauses" &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray();
                    foreach (JsonElement child in property.Value.EnumerateArray())
                    {
                        WriteNode(writer, child, aliases);
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Rewrites model link targets and identifies their representation without changing other link metadata.
        /// </summary>
        private void WriteLink(Utf8JsonWriter writer, JsonElement link, Dictionary<string, Model> aliases)
        {
            if (link.ValueKind != JsonValueKind.Object ||
                !link.TryGetProperty("href", out JsonElement reference) ||
                reference.ValueKind != JsonValueKind.String ||
                !TryResolve(reference.GetString()!, aliases, out string href))
            {
                link.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            foreach (JsonProperty property in link.EnumerateObject())
            {
                if (property.Name == "href")
                {
                    writer.WriteString("href", href);
                }
                else if (property.Name == "rel" &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() == "ua:HasSupertype")
                {
                    writer.WriteString("rel", "tm:extends");
                }
                else if (property.Name != "type")
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteString("type", "application/tm+json");
            writer.WriteEndObject();
        }

        /// <summary>
        /// Resolves native IDs, authored IDs and local model file names, preserving JSON Pointer fragments.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private bool TryResolve(string reference, Dictionary<string, Model> aliases, out string href)
        {
            string fragment = string.Empty;
            Model? model = FindTarget(reference, aliases);
            if (model is null &&
                ExpandedNodeId.TryParse(reference, out ExpandedNodeId nativeIdentity) &&
                nativeIdentity.IdType == IdType.String)
            {
                // A hash can belong to a string NodeId; only an explicit JSON Pointer starts a fragment.
                int separator = reference.Length;
                while (model is null && separator > 0)
                {
                    separator = reference.AsSpan(0, separator).LastIndexOf("#/", StringComparison.Ordinal);
                    if (separator >= 0)
                    {
                        model = FindTarget(reference[..separator], aliases);
                        fragment = reference[separator..];
                    }
                }
            }
            else
            {
                int separator = reference.Length;
                while (model is null && separator > 0)
                {
                    separator = reference.AsSpan(0, separator).LastIndexOf('#');
                    if (separator >= 0)
                    {
                        model = FindTarget(reference[..separator], aliases);
                        fragment = reference[separator..];
                    }
                }
            }
            if (model is null)
            {
                href = reference;
                return false;
            }
            ValidateModel(model);
            string resourceId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(model.Id)));
            href = m_prefix + resourceId + "/versions/" + Uri.EscapeDataString(model.Version!) + fragment;
            return true;
        }

        /// <summary>
        /// Gives source-local file names precedence over the shared catalogue without copying it.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private Model? FindTarget(string reference, Dictionary<string, Model> aliases)
        {
            if (aliases.TryGetValue(reference, out Model? local))
            {
                return local;
            }
            if (m_ambiguous.Contains(reference))
            {
                throw new FormatException($"The model reference is ambiguous: {reference}");
            }
            m_aliases.TryGetValue(reference, out Model? model);
            return model;
        }

        /// <summary>
        /// Rejects conflicting registry claims before publishing a model or a reference to it.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private void ValidateModel(Model model)
        {
            if (m_ambiguous.Contains(model.Id) || m_ambiguous.Contains(model.NativeIdentity.ToString()))
            {
                throw new FormatException($"The model identity is ambiguous: {model.Id} ({model.NativeIdentity}).");
            }
            ValidateVersion(model);
        }

        /// <summary>
        /// Requires the target namespace's explicit version rather than inventing a registry version.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static void ValidateVersion(Model model)
        {
            if (string.IsNullOrWhiteSpace(model.Version) || model.Version is "." or "..")
            {
                throw new FormatException(
                    $"No valid Model Version for namespace '{model.NativeIdentity.NamespaceUri}' " +
                    $"(Thing Model '{model.Id}').");
            }
        }

        /// <summary>
        /// Normalizes namespace-zero and namespace-URI spellings of native document identities.
        /// </summary>
        private static bool TryGetIdentity(JsonElement root, out ExpandedNodeId identity)
        {
            if (root.TryGetProperty("uav:id", out JsonElement nativeId) &&
                nativeId.ValueKind == JsonValueKind.String &&
                ExpandedNodeId.TryParse(nativeId.GetString(), out identity))
            {
                identity = new ExpandedNodeId(
                    identity.InnerNodeId.WithNamespaceIndex(0), identity.NamespaceUri ?? Types.Namespaces.OpcUa);
                return true;
            }
            identity = default;
            return false;
        }

        /// <summary>
        /// Holds the unchanged WoT identity and its owning namespace's version.
        /// </summary>
        private sealed record Model(string Id, ExpandedNodeId NativeIdentity, string? Version);

        /// <summary>Stores the configured registry resource collection.</summary>
        private readonly string m_prefix = registryBaseUrl.TrimEnd('/') +
            "/thingmodelgroups/" +
            Uri.EscapeDataString(groupId) +
            "/thingmodels/";

        /// <summary>Indexes native identities without keeping conversion payloads alive.</summary>
        private readonly Dictionary<ExpandedNodeId, Model> m_models = [];

        /// <summary>Indexes global model and native identities.</summary>
        private readonly Dictionary<string, Model> m_aliases = new(StringComparer.Ordinal);

        /// <summary>Tracks aliases shared by distinct native types or incompatible model versions.</summary>
        private readonly HashSet<string> m_ambiguous = new(StringComparer.Ordinal);
    }
}
