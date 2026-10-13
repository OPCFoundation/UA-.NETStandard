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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The immutable identity of exactly one authored form, not a query for a fallback endpoint.
    /// </summary>
    public sealed record WotRuntimeSelection
    {
        /// <summary>
        /// Creates a selection for direct construction with explicit required identity fields.
        /// </summary>
        public WotRuntimeSelection()
        {
        }

        /// <summary>
        /// Preserves omitted optional defaults during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public WotRuntimeSelection(
            string sha256, string affordance, string bindingId, string bindingVersion,
            string documentReference = "", WotAffordanceKind kind = WotAffordanceKind.Property, int formIndex = 0)
        {
            Sha256 = sha256;
            Affordance = affordance;
            BindingId = bindingId;
            BindingVersion = bindingVersion;
            DocumentReference = documentReference;
            Kind = kind;
            FormIndex = formIndex;
        }

        /// <summary>
        /// Gets the exact document-set member reference; required for a multi-document archive.
        /// </summary>
        [Description("Exact archive member reference, or empty for a single source document.")]
        public string DocumentReference { get; init; } = string.Empty;

        /// <summary>
        /// Gets the expected SHA-256 digest of the selected document's original UTF-8 bytes.
        /// </summary>
        [Description("Expected 64-digit SHA-256 digest of the selected document, not its reformatted JSON.")]
        public required string Sha256 { get; init; }

        /// <summary>
        /// Gets the selected property, action or event affordance kind.
        /// </summary>
        [Description("Property, Action or Event; must match the named affordance.")]
        public WotAffordanceKind Kind { get; init; } = WotAffordanceKind.Property;

        /// <summary>
        /// Gets the exact, case-sensitive affordance name.
        /// </summary>
        [Description("Exact case-sensitive property, action or event name.")]
        public required string Affordance { get; init; }

        /// <summary>
        /// Gets the zero-based index in the selected affordance's authored forms array.
        /// </summary>
        [Description("Zero-based index in this affordance's forms array; no fallback form is tried.")]
        public int FormIndex { get; init; }

        /// <summary>
        /// Gets the exact existing binder identifier.
        /// </summary>
        [Description("Pinned binder ID: w3c.http, w3c.mqtt, w3c.modbus or opc.opcua.")]
        public required string BindingId { get; init; }

        /// <summary>
        /// Gets the exact binder version advertised by the runtime capabilities tool.
        /// </summary>
        [Description("Exact binder version returned by wot_runtime_capabilities.")]
        public required string BindingVersion { get; init; }
    }

    /// <summary>
    /// Scalar document and existing-session choices; no option can raise a host ceiling.
    /// </summary>
    public sealed record WotRuntimeOpenOptions
    {
        /// <summary>
        /// Creates default local-document preparation options for direct callers.
        /// </summary>
        public WotRuntimeOpenOptions()
        {
        }

        /// <summary>
        /// Preserves omitted optional defaults during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        public WotRuntimeOpenOptions(
            string baseUri = "https://mcp.invalid/root.json",
            bool documentSetArchive = false, string? sessionName = null)
        {
            BaseUri = baseUri;
            DocumentSetArchive = documentSetArchive;
            SessionName = sessionName;
        }

        /// <summary>
        /// Gets the logical source-document address used for local relative references, never a URL to fetch.
        /// </summary>
        [Description("Logical absolute document address for relative references; never fetched.")]
        public string BaseUri { get; init; } = "https://mcp.invalid/root.json";

        /// <summary>
        /// Gets whether the source is a root-confined WoT document-set ZIP.
        /// </summary>
        [Description("True only for a TransferRoot document-set ZIP produced by the document tools.")]
        public bool DocumentSetArchive { get; init; }

        /// <summary>
        /// Gets the explicitly authorized existing named OPC UA session, required only for OPC UA.
        /// </summary>
        [Description("Existing named ManagedSession for OPC UA; not an endpoint alias or a new connection request.")]
        public string? SessionName { get; init; }
    }

    /// <summary>
    /// Maps one TD security definition to host-authorized secret references, never raw material or store paths.
    /// </summary>
    public sealed record WotRuntimeCredentialInput
    {
        /// <summary>
        /// Gets the exact security definition name authored in the selected TD.
        /// </summary>
        [Description("Exact securityDefinitions name used by the selected form.")]
        public required string SchemeName { get; init; }

        /// <summary>
        /// Gets an issued or alias reference for the password, token or API key.
        /// </summary>
        [Description("issued: reference or authorized alias: reference; never a password, token or store path.")]
        public required string SecretReference { get; init; }

        /// <summary>
        /// Gets a separate secret reference for a Basic or MQTT username.
        /// </summary>
        [Description("Separate authorized username secret reference for Basic/MQTT authentication.")]
        public string? UserNameReference { get; init; }
    }

    /// <summary>
    /// One UA JSON typed input, decoded by the stack rather than inferred from JSON number formatting.
    /// </summary>
    public sealed record WotRuntimeValue
    {
        /// <summary>
        /// Gets the UA built-in type of the scalar or array elements.
        /// </summary>
        [Description("UA built-in type, such as Boolean, UInt16, Double, String, ByteString or ExtensionObject.")]
        public required BuiltInType Type { get; init; }

        /// <summary>
        /// Gets the value in the standard OPC UA JSON representation, including actual JSON arrays.
        /// </summary>
        [Description("UA JSON value of Type; arrays remain JSON arrays, not strings containing JSON.")]
        public required JsonElement Value { get; init; }
    }

    /// <summary>
    /// Closed source-generated array binding for the live runtime's DTO elements.
    /// </summary>
    /// <typeparam name="T">One of the registered input contracts.</typeparam>
    public sealed class WotRuntimeArrayJsonConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            WotRuntimeInputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ??
            throw new NotSupportedException("The type is not a registered live WoT input.");
    }

    /// <summary>
    /// AOT-safe metadata for the complete live runtime input catalogue.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UseStringEnumConverter = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = new[]
        {
            typeof(McpStringArrayJsonConverter),
            typeof(WotRuntimeArrayJsonConverter<WotDocumentSource>),
            typeof(WotRuntimeArrayJsonConverter<WotRuntimeCredentialInput>),
            typeof(WotRuntimeArrayJsonConverter<WotRuntimeValue>)
        })]
    [JsonSerializable(typeof(WotDocumentSource))]
    [JsonSerializable(typeof(WotRuntimeSelection))]
    [JsonSerializable(typeof(WotRuntimeOpenOptions))]
    [JsonSerializable(typeof(WotRuntimeCredentialInput))]
    [JsonSerializable(typeof(WotRuntimeValue))]
    [JsonSerializable(typeof(ArrayOf<WotDocumentSource>))]
    [JsonSerializable(typeof(ArrayOf<WotRuntimeCredentialInput>))]
    [JsonSerializable(typeof(ArrayOf<WotRuntimeValue>))]
    [JsonSerializable(typeof(ArrayOf<string>))]
    internal sealed partial class WotRuntimeInputJsonContext : JsonSerializerContext;
}
