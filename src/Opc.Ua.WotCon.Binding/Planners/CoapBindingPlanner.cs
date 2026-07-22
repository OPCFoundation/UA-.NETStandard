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

namespace Opc.Ua.WotCon.Binding.Planners
{
    /// <summary>
    /// The W3C WoT CoAP binding planner (Editor's Draft). It validates the
    /// <c>coap</c> / <c>coaps</c> href scheme and the <c>cov:</c> vocabulary
    /// (<c>method</c>, <c>observe</c>, <c>contentFormat</c>, <c>accept</c>) and
    /// compiles the form into immutable metadata. This build ships the CoAP
    /// planner only; the binding is reported as non-executable.
    /// </summary>
    public sealed class CoapBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The CoAP binding vocabulary URI.</summary>
        public const string BindingUri = "http://www.w3.org/2019/wot/coap#";

        private static readonly string[] s_schemes = { "coap", "coaps" };
        private static readonly HashSet<string> s_methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "PUT", "POST", "DELETE", "FETCH", "PATCH", "iPATCH"
        };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.coap", "1.0-ed", BindingUri, "W3C WoT CoAP Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "W3C WoT CoAP Binding (Editor's Draft)",
            WotBindingSources.Coap,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            },
            new[] { "application/json", "application/cbor", "text/plain" },
            isExecutable: false);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "cov:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href))
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (!TryParseUri(href, out Uri uri) ||
                (!string.Equals(uri.Scheme, "coap", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, "coaps", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid absolute coap(s) URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string? methodOverride = null;
            if (form.TryGetString("cov:method", out string method))
            {
                if (!s_methods.Contains(method))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        $"'{method}' is not a valid CoAP method.", form.Pointer("cov:method"), "cov:method"));
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
                methodOverride = method;
            }
            if (form.FormElement.TryGetProperty("cov:contentFormat", out System.Text.Json.JsonElement cf) &&
                cf.ValueKind == System.Text.Json.JsonValueKind.Number && !cf.TryGetInt32(out int _))
            {
                diagnostics.Add(WotBindingDiagnostic.Warning(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    "cov:contentFormat should be an integer Content-Format code.",
                    form.Pointer("cov:contentFormat"), "cov:contentFormat"));
            }

            ResolveCodec(form, context, out WotPayloadDescriptor payload);
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(uri.AbsoluteUri);
            ImmutableArray<WotCredentialReference> security =
                ResolveSecurity(form, context, uri.GetLeftPart(UriPartial.Authority), diagnostics);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                string coapMethod = methodOverride ?? DefaultMethod(capability);
                var operation = new WotOperationDescriptor(capability, op, coapMethod);
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload, security, Capability.IsExecutable));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), diagnostics.ToImmutableArray());
        }

        private static string DefaultMethod(WoTBindingCapabilityEnum operation)
        {
            return operation switch
            {
                WoTBindingCapabilityEnum.WriteProperty => "PUT",
                WoTBindingCapabilityEnum.InvokeAction => "POST",
                _ => "GET"
            };
        }
    }
}
