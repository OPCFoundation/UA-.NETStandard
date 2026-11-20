/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Opc.Ua.WotCon.Bindings.Planners
{
    /// <summary>
    /// The PROFINET binding planner (unofficial draft). It validates the
    /// <c>pnv:</c> vocabulary (<c>slot</c>, <c>subslot</c>, <c>index</c>, optional
    /// <c>api</c>) at the schema / document level and compiles immutable
    /// slot / subslot / index addressing metadata. This build performs planning
    /// only; the binding is reported as non-executable.
    /// </summary>
    public sealed class ProfinetBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>
        /// The PROFINET binding vocabulary URI.
        /// </summary>
        public const string BindingUri = "https://www.w3.org/2019/wot/profinet#";

        private static readonly string[] s_schemes = ["profinet"];

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.profinet", "0.1-draft", BindingUri, "WoT PROFINET Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "WoT PROFINET Binding (unofficial draft)",
            WotBindingSources.Profinet,
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty
            ],
            ["application/octet-stream"],
            isExecutable: false);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
        {
            return MatchStandard(form, context, "pnv:");
        }

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            bool ok = RequirePositive(form, "pnv:slot", diagnostics, out int slot);
            ok &= RequirePositive(form, "pnv:subslot", diagnostics, out int subslot);
            ok &= RequirePositive(form, "pnv:index", diagnostics, out int index);
            if (!ok)
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            ImmutableDictionary<string, string> metadata = ImmutableDictionary<string, string>.Empty
                .Add("slot", slot.ToString(CultureInfo.InvariantCulture))
                .Add("subslot", subslot.ToString(CultureInfo.InvariantCulture))
                .Add("index", index.ToString(CultureInfo.InvariantCulture));
            if (form.TryGetInt32("pnv:api", out int api))
            {
                metadata = metadata.Add("api", api.ToString(CultureInfo.InvariantCulture));
            }

            var addressing = new WotAddressingDescriptor(
                $"slot:{slot.ToString(CultureInfo.InvariantCulture)}/subslot:" +
                $"{subslot.ToString(CultureInfo.InvariantCulture)}/index:" +
                index.ToString(CultureInfo.InvariantCulture), metadata);
            WotEndpointDescriptor endpoint = MakeEndpointOrSynthetic(form.Href, "profinet");
            if (!ResolveCodec(form, context, diagnostics, out WotPayloadDescriptor payload))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            ImmutableArray<WotCompiledForm>.Builder entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, capability.ToString());
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload,
                    [], Capability.IsExecutable));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), [.. diagnostics]);
        }

        private bool RequirePositive(
            WotAffordanceForm form, string term, List<WotBindingDiagnostic> diagnostics, out int value)
        {
            if (!form.TryGetInt32(term, out value) || value < 0)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    $"A PROFINET form requires a non-negative {term}.", form.Pointer(term), term));
                return false;
            }
            return true;
        }
    }
}
