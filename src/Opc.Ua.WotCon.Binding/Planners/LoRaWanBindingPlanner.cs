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
using System.Globalization;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Planners
{
    /// <summary>
    /// The LoRaWAN binding planner (unofficial draft). It validates the
    /// <c>lorawan:</c> vocabulary (<c>DevEUI</c>, optional <c>fPort</c>) at the
    /// schema / document level and compiles immutable device addressing metadata.
    /// This build performs planning only; the binding is reported as
    /// non-executable.
    /// </summary>
    public sealed class LoRaWanBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The LoRaWAN binding vocabulary URI.</summary>
        public const string BindingUri = "https://www.w3.org/2019/wot/lorawan#";

        private static readonly string[] s_schemes = { "lorawan" };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.lorawan", "0.1-draft", BindingUri, "WoT LoRaWAN Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "WoT LoRaWAN Binding (unofficial draft)",
            WotBindingSources.LoRaWan,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty
            },
            new[] { "application/octet-stream", "application/json" },
            isExecutable: false);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "lorawan:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!form.TryGetString("lorawan:DevEUI", out string devEui) &&
                !form.TryGetString("lorawan:devEUI", out devEui))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A LoRaWAN form requires lorawan:DevEUI.",
                    form.Pointer("lorawan:DevEUI"), "lorawan:DevEUI"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (!IsHex(devEui, 16))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    "lorawan:DevEUI must be a 16-character hexadecimal identifier.",
                    form.Pointer("lorawan:DevEUI"), "lorawan:DevEUI"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            int fPort = 1;
            if (form.TryGetInt32("lorawan:fPort", out int parsedPort))
            {
                if (parsedPort is < 1 or > 223)
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        "lorawan:fPort must be between 1 and 223.",
                        form.Pointer("lorawan:fPort"), "lorawan:fPort"));
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
                fPort = parsedPort;
            }

            var metadata = ImmutableDictionary<string, string>.Empty
                .Add("devEui", devEui)
                .Add("fPort", fPort.ToString(CultureInfo.InvariantCulture));
            var addressing = new WotAddressingDescriptor(
                $"{devEui}/{fPort.ToString(CultureInfo.InvariantCulture)}", metadata);
            WotEndpointDescriptor endpoint = MakeEndpointOrSynthetic(form.Href, "lorawan");
            ResolveCodec(form, context, out WotPayloadDescriptor payload);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, capability.ToString());
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload,
                    ImmutableArray<WotCredentialReference>.Empty, Capability.IsExecutable));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), diagnostics.ToImmutableArray());
        }

        private static bool IsHex(string value, int length)
        {
            if (value is null || value.Length != length)
            {
                return false;
            }
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
