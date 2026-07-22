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
    /// The W3C WoT HTTP binding planner. It validates the <c>http</c> / <c>https</c>
    /// href scheme and the normative <c>htv:</c> vocabulary of TD 1.1, checks
    /// <c>op</c> compatibility, content type and required fields, and compiles the
    /// form into immutable endpoint / addressing / operation / payload metadata.
    /// It is executable when the HTTP executor is registered.
    /// </summary>
    public sealed class HttpBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The HTTP binding vocabulary URI.</summary>
        public const string BindingUri = "http://www.w3.org/2011/http#";

        private static readonly string[] s_schemes = { "http", "https" };
        private static readonly HashSet<string> s_methods = new HashSet<string>(StringComparer.Ordinal)
        {
            "GET", "PUT", "POST", "DELETE", "PATCH", "HEAD", "OPTIONS"
        };
        private static readonly HashSet<string> s_subprotocols = new HashSet<string>(StringComparer.Ordinal)
        {
            "longpoll", "sse", "websub"
        };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.http", "1.1", BindingUri, "W3C WoT HTTP Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "W3C WoT HTTP Binding (TD 1.1)",
            WotBindingSources.Http,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            },
            new[] { "application/json", "text/plain", "application/octet-stream" },
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "htv:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href))
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (!TryParseUri(href, out Uri uri) ||
                (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid absolute http(s) URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string? methodOverride = null;
            if (form.TryGetString("htv:methodName", out string method))
            {
                if (!s_methods.Contains(method))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        $"'{method}' is not a valid HTTP method.", form.Pointer("htv:methodName"), "htv:methodName"));
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
                methodOverride = method;
            }

            if (!string.IsNullOrEmpty(form.Subprotocol) && !s_subprotocols.Contains(form.Subprotocol!))
            {
                diagnostics.Add(WotBindingDiagnostic.Warning(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    $"The subprotocol '{form.Subprotocol}' is not a recognized HTTP subprotocol.",
                    form.Pointer("subprotocol"), "subprotocol"));
            }

            ResolveCodec(form, context, out WotPayloadDescriptor payload);
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(uri.AbsoluteUri);
            ImmutableArray<WotCredentialReference> security =
                ResolveSecurity(form, context, uri.GetLeftPart(UriPartial.Authority), diagnostics);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                string httpMethod = methodOverride ?? DefaultMethod(capability);
                var operation = new WotOperationDescriptor(
                    capability, op, httpMethod,
                    ImmutableDictionary<string, string>.Empty.Add("subprotocol", form.Subprotocol ?? string.Empty));
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
