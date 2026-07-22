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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// A request to validate and compile a single resource's interaction forms
    /// into a binding plan. The request is side-effect free and carries the
    /// extracted forms, the secret-free security definitions, the base URI and the
    /// explicit binder selection pinned on the resource.
    /// </summary>
    public sealed class WotBindingPlanRequest
    {
        /// <summary>Initializes a new plan request.</summary>
        public WotBindingPlanRequest(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ImmutableArray<WotAffordanceForm> forms,
            ImmutableDictionary<string, WotSecurityDefinition>? securityDefinitions = null,
            string? baseUri = null,
            WotBindingSelectionContext? selection = null)
        {
            ResourceXid = resourceXid ?? string.Empty;
            Kind = kind;
            Forms = forms.IsDefault ? ImmutableArray<WotAffordanceForm>.Empty : forms;
            SecurityDefinitions = securityDefinitions ?? ImmutableDictionary<string, WotSecurityDefinition>.Empty;
            BaseUri = baseUri;
            Selection = selection ?? WotBindingSelectionContext.Empty;
        }

        /// <summary>Gets the resource xid.</summary>
        public string ResourceXid { get; }

        /// <summary>Gets the document kind.</summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>Gets the affordance forms parsed from the document.</summary>
        public ImmutableArray<WotAffordanceForm> Forms { get; }

        /// <summary>Gets the secret-free security definitions declared by the document.</summary>
        public ImmutableDictionary<string, WotSecurityDefinition> SecurityDefinitions { get; }

        /// <summary>Gets the Thing base URI used for relative href resolution, if any.</summary>
        public string? BaseUri { get; }

        /// <summary>Gets the explicit binder selection pinned on the resource.</summary>
        public WotBindingSelectionContext Selection { get; }

        /// <summary>Builds a plan context from this request.</summary>
        public WotBindingPlanContext CreateContext(IWotCodecRegistry codecs, WotBindingBounds bounds)
            => new WotBindingPlanContext(SecurityDefinitions, codecs, Kind, BaseUri, bounds);

        /// <summary>
        /// Builds a plan request from a WoT document: it extracts the forms, the
        /// base URI and the secret-free security definitions.
        /// </summary>
        public static WotBindingPlanRequest FromDocument(
            string resourceXid,
            WoTDocumentKindEnum kind,
            ReadOnlyMemory<byte> document,
            int maxJsonDepth = 64,
            WotBindingSelectionContext? selection = null)
        {
            ImmutableArray<WotAffordanceForm> forms = WotFormExtractor.Extract(document, maxJsonDepth);
            ImmutableDictionary<string, WotSecurityDefinition> definitions = ReadSecurityDefinitions(document, maxJsonDepth);
            string? baseUri = ReadBase(document, maxJsonDepth);
            return new WotBindingPlanRequest(resourceXid, kind, forms, definitions, baseUri, selection);
        }

        private static ImmutableDictionary<string, WotSecurityDefinition> ReadSecurityDefinitions(
            ReadOnlyMemory<byte> document, int maxJsonDepth)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, WotSecurityDefinition>(StringComparer.Ordinal);
            try
            {
                var options = new JsonDocumentOptions { MaxDepth = maxJsonDepth <= 0 ? 64 : maxJsonDepth };
                using JsonDocument json = JsonDocument.Parse(document, options);
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("securityDefinitions", out JsonElement definitions) &&
                    definitions.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty definition in definitions.EnumerateObject())
                    {
                        builder[definition.Name] = WotSecurityDefinition.Parse(definition.Name, definition.Value);
                    }
                }
            }
            catch (JsonException)
            {
            }
            return builder.ToImmutable();
        }

        private static string? ReadBase(ReadOnlyMemory<byte> document, int maxJsonDepth)
        {
            try
            {
                var options = new JsonDocumentOptions { MaxDepth = maxJsonDepth <= 0 ? 64 : maxJsonDepth };
                using JsonDocument json = JsonDocument.Parse(document, options);
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("base", out JsonElement baseElement) &&
                    baseElement.ValueKind == JsonValueKind.String)
                {
                    return baseElement.GetString();
                }
            }
            catch (JsonException)
            {
            }
            return null;
        }
    }

    /// <summary>
    /// The immutable result of preparing bindings for one resource. It holds the
    /// participating capability snapshots, the compiled (executable and
    /// non-executable) forms, the forms no binder validated and the structured
    /// diagnostics. A strict closure fails when <see cref="FullySupported"/> is
    /// <c>false</c>; otherwise unsupported forms materialize as degraded nodes.
    /// </summary>
    public sealed class WotBindingPlan
    {
        /// <summary>Initializes a new immutable binding plan.</summary>
        public WotBindingPlan(
            string resourceXid,
            ImmutableArray<WoTBindingCapabilityDataType> capabilities,
            ImmutableArray<WotCompiledForm> compiledForms,
            ImmutableArray<WotAffordanceForm> unsupportedForms,
            ImmutableArray<WotBindingDiagnostic> diagnostics)
        {
            ResourceXid = resourceXid ?? string.Empty;
            Capabilities = capabilities.IsDefault
                ? ImmutableArray<WoTBindingCapabilityDataType>.Empty : capabilities;
            CompiledForms = compiledForms.IsDefault
                ? ImmutableArray<WotCompiledForm>.Empty : compiledForms;
            UnsupportedForms = unsupportedForms.IsDefault
                ? ImmutableArray<WotAffordanceForm>.Empty : unsupportedForms;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<WotBindingDiagnostic>.Empty : diagnostics;
        }

        /// <summary>An empty plan (no forms, no capabilities).</summary>
        public static WotBindingPlan Empty { get; } = new WotBindingPlan(
            string.Empty,
            ImmutableArray<WoTBindingCapabilityDataType>.Empty,
            ImmutableArray<WotCompiledForm>.Empty,
            ImmutableArray<WotAffordanceForm>.Empty,
            ImmutableArray<WotBindingDiagnostic>.Empty);

        /// <summary>Gets the resource xid the plan was prepared for.</summary>
        public string ResourceXid { get; }

        /// <summary>Gets the participating binding capability snapshots.</summary>
        public ImmutableArray<WoTBindingCapabilityDataType> Capabilities { get; }

        /// <summary>Gets the compiled (executable and non-executable) forms.</summary>
        public ImmutableArray<WotCompiledForm> CompiledForms { get; }

        /// <summary>Gets the forms no binder validated.</summary>
        public ImmutableArray<WotAffordanceForm> UnsupportedForms { get; }

        /// <summary>Gets the structured diagnostics produced during Prepare.</summary>
        public ImmutableArray<WotBindingDiagnostic> Diagnostics { get; }

        /// <summary>Gets whether every form was validated by a binder.</summary>
        public bool FullySupported => UnsupportedForms.IsEmpty;

        /// <summary>Gets whether the plan compiled at least one executable form.</summary>
        public bool HasExecutableForms => CompiledForms.Any(f => f.IsExecutable);

        /// <summary>Gets whether the plan compiled at least one non-executable form.</summary>
        public bool HasNonExecutableForms => CompiledForms.Any(f => !f.IsExecutable);
    }
}
