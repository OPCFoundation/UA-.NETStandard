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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Wot;
using Opc.Ua.XRegistry;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed class RegistryLifecycleTask : CompanionTaskInput
    {
        public RegistryLifecycleTask(
            CompanionTarget target, string operationId, uint epoch, string identifier = "",
            string version = "", ByteString document = default, bool enabled = false, string group = "",
            RegistryMutationScope? scope = null)
        {
            Target = target;
            OperationId = operationId;
            Epoch = epoch;
            Identifier = identifier;
            Version = version;
            m_document = document.Copy();
            Enabled = enabled;
            Group = group;
            Scope = scope;
        }

        public CompanionTarget Target { get; }
        public string OperationId { get; }
        public uint Epoch { get; }
        public string Identifier { get; }
        public string Version { get; }
        public ByteString Document => m_document.Copy();
        public bool Enabled { get; }
        public string Group { get; }
        public RegistryMutationScope? Scope { get; }

        public override string Review =>
            $"Operation: {OperationId}; observed {Scope?.EpochName ?? "epoch"}: {Epoch}.\n" +
            $"Identifier: {Identifier}; version: {Version}; group: {Group}.\n" +
            (Scope is null ? string.Empty : $"Scope: {Scope.Description}; Xid: {Scope.Xid}.\n") +
            (m_document.IsNull ? string.Empty :
                $"Document bytes: {m_document.Length}; " +
                $"SHA-256: {Convert.ToHexString(SHA256.HashData(m_document.Span))}.\n") +
            (OperationId.StartsWith("delete-", StringComparison.Ordinal)
                ? "Deletion applies only to the reviewed scope; server default/last-version and dependency rules apply."
                : "No existing version is overwritten. " +
                    "Server policy and any AutoRefresh behavior remain authoritative.");

        private readonly ByteString m_document;
    }

    internal sealed record RegistryMutationScope(string Xid, string EpochName, string Description);

    internal static class RegistryTaskForms
    {
        public static CompanionOperation Create(
            string id, string title, ArrayOf<CompanionInputDefinition> inputs)
        {
            return new CompanionOperation(id, title, CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = inputs
            };
        }

        public static CompanionInputDefinition Epoch { get; } =
            new("epoch", "Expected nonzero epoch", BuiltInType.UInt32,
                "Use MetaEpoch for logical resource changes, Epoch for a Version or group. Zero is not allowed.");

        public static CompanionInputDefinition Document { get; } =
            new("document", "JSON document", BuiltInType.String, "At most 64 KiB of UTF-8 JSON.", IsMultiline: true);

        public static CompanionInputDefinition Identifier(string name, string title)
        {
            return new CompanionInputDefinition(name, title, BuiltInType.String,
                "An exact identifier, at most 128 characters; no path separators or control characters.");
        }

        public static CompanionInputDefinition DeleteConfirmation { get; } =
            new("includeChildren", "Confirm deletion scope", BuiltInType.Boolean,
                "Confirm the scope shown by preparation, including applicable default/last-version rules.");

        public static Variant Field(ArrayOf<CompanionValue> inputs, string name)
        {
            Variant value = default;
            bool found = false;
            foreach (CompanionValue input in inputs)
            {
                if (input is null)
                {
                    throw new ArgumentException("Task input fields cannot be null.", nameof(inputs));
                }
                if (input.Name != name)
                {
                    continue;
                }
                if (found)
                {
                    throw new ArgumentException("Duplicate task input.");
                }
                found = true;
                value = input.Value;
            }
            return found ? value : throw new ArgumentException($"The {name} field is required.");
        }

        public static string Text(ArrayOf<CompanionValue> inputs, string name)
        {
            if (!Field(inputs, name).TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException($"The {name} field must be a nonempty String.");
            }
            return text;
        }

        public static string Id(ArrayOf<CompanionValue> inputs, string name)
        {
            string value = Text(inputs, name);
            if (value.Length > XRegistryIdentifier.MaxLength || value is "." or "..")
            {
                throw new ArgumentException("Invalid registry identifier.");
            }
            foreach (char character in value)
            {
                if (char.IsControl(character) || character is '/' or '\\')
                {
                    throw new ArgumentException("Registry identifiers cannot contain paths or control characters.");
                }
            }
            return value;
        }

        public static uint ExpectedEpoch(ArrayOf<CompanionValue> inputs)
        {
            return Field(inputs, "epoch").TryGetValue(out uint value) && value != 0
                ? value
                : throw new ArgumentException(
                    "The expected epoch must be nonzero UInt32; zero disables concurrency checks.");
        }

        public static bool Boolean(ArrayOf<CompanionValue> inputs, string name)
        {
            return Field(inputs, name).TryGetValue(out bool value)
                ? value
                : throw new ArgumentException($"The {name} field must be Boolean.");
        }

        public static ByteString Json(
            ArrayOf<CompanionValue> inputs, bool wot = false, WotDocumentKind? requiredKind = null)
        {
            string text = Text(inputs, "document");
            if (s_utf8.GetByteCount(text) > 65536)
            {
                throw new ArgumentException("Registry documents are limited to 64 KiB.");
            }
            byte[] bytes = s_utf8.GetBytes(text);
            using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A registry document must be a JSON object.");
            }
            ValidateMembers(parsed.RootElement);
            if (wot)
            {
                using var document = WotDocument.Parse(bytes, new WotNodeSetConverterOptions
                {
                    MaxJsonDepth = 32,
                    MaxJsonDocumentSize = 65536
                });
                if (!document.TryGetContext(out _) || string.IsNullOrWhiteSpace(document.Title))
                {
                    throw new ArgumentException("A WoT document requires @context and title.");
                }
                if ((requiredKind == WotDocumentKind.ThingModel && document.Kind != WotDocumentKind.ThingModel) ||
                    (requiredKind == WotDocumentKind.ThingDescription && document.Kind == WotDocumentKind.ThingModel))
                {
                    throw new ArgumentException("The WoT document kind does not match the selected asset/group.");
                }
            }
            return ByteString.From(bytes);
        }

        public static ValueTask RequireEpochAsync(
            CompanionContext context, NodeId target, uint expected, CancellationToken cancellationToken)
        {
            return RequireEpochAsync(context, target, expected, "Epoch", cancellationToken);
        }

        public static async ValueTask RequireEpochAsync(
            CompanionContext context, NodeId target, uint expected, string epochName,
            CancellationToken cancellationToken)
        {
            if (expected == 0)
            {
                throw new ArgumentException("A nonzero epoch is required for registry mutation.", nameof(expected));
            }
            ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, target, XRegistryWellKnown.XRegistryNamespaceUri, [epochName], cancellationToken)
                .ConfigureAwait(false);
            if (fields[0].Value.TypeInfo.BuiltInType != BuiltInType.UInt32 ||
                !fields[0].Value.TryGetValue(out uint actual))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The registry epoch is unavailable.");
            }
            if (actual != expected)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The registry epoch changed.");
            }
        }

        public static async ValueTask<RegistryMutationScope> ReadScopeAsync(
            CompanionContext context, NodeId target, bool resource, bool logicalMetadata,
            CancellationToken cancellationToken)
        {
            if (!resource)
            {
                return new RegistryMutationScope(string.Empty, "Epoch", "selected registry or group and its children");
            }
            ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, target, XRegistryWellKnown.XRegistryNamespaceUri, ["Xid"], cancellationToken)
                .ConfigureAwait(false);
            if (!fields[0].Value.TryGetValue(out string? xid) ||
                string.IsNullOrWhiteSpace(xid) ||
                xid.Length > 4096 ||
                !xid.StartsWith('/'))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "A canonical Resource or Version Xid is required to determine the mutation scope.");
            }
            string[] segments = xid.Split('/');
            bool resourcePath = segments.Length == 3 ||
                (segments.Length == 5 && segments[1] == "groups" && segments[3] == "resources");
            bool versionPath = (segments.Length == 5 && segments[3] == "versions") ||
                (segments.Length == 7 &&
                    segments[1] == "groups" &&
                    segments[3] == "resources" &&
                    segments[5] == "versions");
            if (!resourcePath && !versionPath)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The resource Xid has an unknown scope.");
            }
            for (int index = 1; index < segments.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(segments[index]) || segments[index] is "." or "..")
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The resource Xid is not canonical.");
                }
                foreach (char character in segments[index])
                {
                    if (char.IsControl(character) || character is '\\' or '?' or '#')
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The resource Xid is not canonical.");
                    }
                }
            }
            bool logical = logicalMetadata || resourcePath;
            return new RegistryMutationScope(xid, logical ? "MetaEpoch" : "Epoch",
                logical ? "logical Resource, all versions and shared metadata" : "selected Version only");
        }

        public static async ValueTask RequireMethodAsync(
            CompanionContext context, NodeId target, string namespaceUri, string method,
            CancellationToken cancellationToken)
        {
            if (!await IndustrialCompanionAccess.IsExecutableAsync(
                context, target, namespaceUri, method, cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadNotExecutable, "The requested method is unavailable.");
            }
        }

        private static void ValidateMembers(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new ArgumentException("Registry documents cannot contain duplicate JSON members.");
                    }
                    ValidateMembers(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in value.EnumerateArray())
                {
                    ValidateMembers(element);
                }
            }
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
