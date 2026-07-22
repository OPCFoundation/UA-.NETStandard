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

namespace Opc.Ua.WotCon.Binding.Planners
{
    /// <summary>
    /// The W3C WoT BACnet binding planner (Editor's Draft). It validates the
    /// <c>bacv:</c> vocabulary (<c>objectType</c>, <c>instanceNumber</c>,
    /// <c>propertyIdentifier</c>, optional <c>usePriority</c>) at the schema /
    /// document level and compiles immutable object-reference metadata. This build
    /// performs planning only; the binding is reported as non-executable.
    /// </summary>
    public sealed class BacnetBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The BACnet binding vocabulary URI.</summary>
        public const string BindingUri = "https://www.w3.org/2019/wot/bacnet#";

        private static readonly string[] s_schemes = { "bacnet" };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.bacnet", "1.0-ed", BindingUri, "W3C WoT BACnet Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "W3C WoT BACnet Binding (Editor's Draft)",
            WotBindingSources.Bacnet,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty
            },
            new[] { "application/json" },
            isExecutable: false);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "bacv:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!form.TryGetString("bacv:objectType", out string objectType))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A BACnet form requires bacv:objectType.",
                    form.Pointer("bacv:objectType"), "bacv:objectType"));
            }
            if (!form.TryGetInt32("bacv:instanceNumber", out int instanceNumber) || instanceNumber < 0)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A BACnet form requires a non-negative bacv:instanceNumber.",
                    form.Pointer("bacv:instanceNumber"), "bacv:instanceNumber"));
            }
            if (!form.TryGetString("bacv:propertyIdentifier", out string propertyId))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A BACnet form requires bacv:propertyIdentifier.",
                    form.Pointer("bacv:propertyIdentifier"), "bacv:propertyIdentifier"));
            }
            if (form.TryGetInt32("bacv:usePriority", out int priority) && (priority is < 1 or > 16))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    "bacv:usePriority must be between 1 and 16.",
                    form.Pointer("bacv:usePriority"), "bacv:usePriority"));
            }

            foreach (WotBindingDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.IsError)
                {
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
            }

            var metadata = ImmutableDictionary<string, string>.Empty
                .Add("objectType", objectType)
                .Add("instanceNumber", instanceNumber.ToString(CultureInfo.InvariantCulture))
                .Add("propertyIdentifier", propertyId);
            var addressing = new WotAddressingDescriptor(
                $"{objectType}:{instanceNumber.ToString(CultureInfo.InvariantCulture)}:{propertyId}", metadata);
            WotEndpointDescriptor endpoint = MakeEndpointOrSynthetic(form.Href, "bacnet");
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
    }
}
