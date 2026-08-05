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
using System.Collections.Immutable;

namespace Opc.Ua.WotCon.Bindings.Planners
{
    /// <summary>
    /// The OPC UA WoT Connectivity binding planner (OPC 10101). It validates the
    /// portable <c>uav:id</c> / <c>opc.tcp</c> href and the <c>uav:componentOf</c>
    /// containment reference, checks <c>op</c> compatibility, and compiles the
    /// form into immutable endpoint and NodeId addressing metadata. It is
    /// executable when the OPC UA executor is registered. The OPC 10101 §6.5.4
    /// target-mapping terms (<c>uav:mapToNodeId</c> / <c>uav:mapToType</c> /
    /// <c>uav:mapByFieldPath</c>) are property-affordance-level and
    /// protocol-neutral; they are validated centrally by
    /// <see cref="WotProtocolBinderRegistry"/> for every protocol, not parsed
    /// here.
    /// </summary>
    public sealed class OpcUaBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>
        /// The OPC UA WoT binding vocabulary URI.
        /// </summary>
        public const string BindingUri = "http://opcfoundation.org/UA/WoT-Binding/";

        private static readonly string[] s_schemes = ["opc.tcp", "opc.https", "opc.wss"];

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("opc.opcua", "10101", BindingUri, "OPC UA WoT Connectivity Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "OPC UA WoT Connectivity Binding (OPC 10101)",
            WotBindingSources.OpcUa,
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            ],
            ["application/json", "application/opcua+json", "application/octet-stream"],
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
        {
            return MatchStandard(form, context, "uav:");
        }

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();

            WotEndpointDescriptor endpoint;
            string? authority;
            if (!string.IsNullOrEmpty(form.Href) && TryParseUri(form.Href!, out Uri uri))
            {
                if (!IsOpcScheme(uri.Scheme))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.UnsupportedScheme,
                        $"'{uri.Scheme}' is not an OPC UA transport scheme.", form.Pointer("href")));
                    return WotBindingCompilation.Unsupported([.. diagnostics]);
                }
                endpoint = MakeEndpoint(uri);
                authority = uri.GetLeftPart(UriPartial.Authority);
            }
            else if (!string.IsNullOrEmpty(context.BaseUri) &&
                TryParseUri(context.BaseUri!, out Uri baseUri) &&
                IsOpcScheme(baseUri.Scheme))
            {
                endpoint = MakeEndpoint(baseUri);
                authority = baseUri.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "An OPC UA form requires an opc.tcp href or a Thing base opc.tcp endpoint.",
                    form.Pointer("href")));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            string? nodeId = ResolveNodeId(form);
            if (string.IsNullOrEmpty(nodeId))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "An OPC UA form requires uav:id or a NodeId in the href path.",
                    form.Pointer("uav:id"), "uav:id"));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            ImmutableDictionary<string, string> metadata = ImmutableDictionary<string, string>.Empty
                .Add("nodeId", nodeId!);
            metadata = AddIfPresent(form, "uav:componentOf", "componentOf", metadata);

            // uav:eventFields is a non-standard extension of this implementation
            // rather than release 1.1 vocabulary: Section 8 carries an event's
            // fields in the event data schema and its delivery configuration in
            // uav:eventConfiguration, and defines no eventFields term. It is
            // still read so that a form already authored against this
            // implementation keeps working, but a portable document should not
            // rely on it.
            if (form.Kind == WotAffordanceKind.Event &&
                form.TryGetStringArray("uav:eventFields", out ImmutableArray<string> eventFields))
            {
                // '|' joins the binding-authored select-clause browse paths; a
                // browse name legally contains '/' (nested paths) but never '|'.
                metadata = metadata.Add("eventFields", string.Join("|", eventFields));
            }

            if (!ResolveCodec(form, context, diagnostics, out WotPayloadDescriptor payload))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }
            var addressing = new WotAddressingDescriptor(nodeId!, metadata);
            ImmutableArray<WotCredentialReference> security = ResolveSecurity(form, context, authority, diagnostics);

            ImmutableArray<WotCompiledForm>.Builder entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, OpcUaService(capability));
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload, security, Capability.IsExecutable));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), [.. diagnostics]);
        }

        private static bool IsOpcScheme(string scheme)
        {
            foreach (string handled in s_schemes)
            {
                if (string.Equals(scheme, handled, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string? ResolveNodeId(WotAffordanceForm form)
        {
            if (form.TryGetString("uav:id", out string id) && !string.IsNullOrEmpty(id))
            {
                return id;
            }
            if (!string.IsNullOrEmpty(form.Href) && TryParseUri(form.Href!, out Uri uri))
            {
                string path = uri.AbsolutePath.Trim('/');
                if (LooksLikeNodeId(path))
                {
                    return Uri.UnescapeDataString(path);
                }
                string query = uri.Query.TrimStart('?');
                if (query.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(query[3..]);
                }
            }
            return null;
        }

        private static bool LooksLikeNodeId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            // A textual OPC UA NodeId always carries an identifier assignment
            // (for example "i=", "s=", "g=", "b=" or a namespace "ns=").
            foreach (char c in value)
            {
                if (c == '=')
                {
                    return true;
                }
            }
            return false;
        }

        private static ImmutableDictionary<string, string> AddIfPresent(
            WotAffordanceForm form, string term, string key, ImmutableDictionary<string, string> metadata)
        {
            return form.TryGetString(term, out string value) ? metadata.Add(key, value) : metadata;
        }

        private static string OpcUaService(WoTBindingCapabilityEnum operation)
        {
            return operation switch
            {
                WoTBindingCapabilityEnum.WriteProperty => "Write",
                WoTBindingCapabilityEnum.ObserveProperty => "Subscribe",
                WoTBindingCapabilityEnum.InvokeAction => "Call",
                WoTBindingCapabilityEnum.SubscribeEvent => "EventSubscribe",
                WoTBindingCapabilityEnum.UnsubscribeEvent => "EventSubscribe",
                _ => "Read"
            };
        }
    }
}
