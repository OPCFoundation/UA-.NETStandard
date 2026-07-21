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
    /// The W3C WoT Modbus binding planner (Editor's Draft). It validates the
    /// <c>modbus+tcp</c> href scheme and the <c>modv:</c> vocabulary
    /// (<c>entity</c>, <c>function</c>, <c>address</c>, <c>quantity</c>,
    /// <c>unitID</c>, <c>type</c>), enforces read-only entity and quantity bounds,
    /// checks <c>op</c> compatibility, and compiles the form into immutable Modbus
    /// register addressing metadata. It is executable when the Modbus executor is
    /// registered.
    /// </summary>
    public sealed class ModbusBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The Modbus binding vocabulary URI.</summary>
        public const string BindingUri = "https://www.w3.org/2019/wot/modbus#";

        private static readonly string[] s_schemes = { "modbus+tcp", "modbus" };
        private static readonly HashSet<string> s_readOnlyEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "discreteinput", "inputregister"
        };
        private static readonly HashSet<string> s_entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "coil", "discreteinput", "holdingregister", "inputregister"
        };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.modbus", "1.0-ed", BindingUri, "W3C WoT Modbus Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "W3C WoT Modbus TCP Binding (Editor's Draft)",
            WotBindingSources.Modbus,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty
            },
            new[] { "application/octet-stream", "application/json" },
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "modv:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href))
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (!TryParseUri(href, out Uri uri) ||
                (!string.Equals(uri.Scheme, "modbus+tcp", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, "modbus", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid absolute modbus+tcp URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string? entity = form.TryGetString("modv:entity", out string entityValue) ? entityValue : null;
            if (entity is not null && !s_entities.Contains(entity))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    $"'{entity}' is not a valid Modbus entity.", form.Pointer("modv:entity"), "modv:entity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            bool hasFunction = form.TryGetString("modv:function", out string function) ||
                form.TryGetInt32("modv:function", out int _);
            if (entity is null && !hasFunction)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A Modbus form requires modv:entity or modv:function.",
                    form.Pointer("modv:entity"), "modv:entity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            if (!form.TryGetInt32("modv:address", out int address) || address < 0)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A Modbus form requires a non-negative modv:address.",
                    form.Pointer("modv:address"), "modv:address"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            int quantity = form.TryGetInt32("modv:quantity", out int parsedQuantity) ? parsedQuantity : 1;
            if (quantity < 1)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    "modv:quantity must be at least 1.", form.Pointer("modv:quantity"), "modv:quantity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            bool bitEntity = entity is not null &&
                (string.Equals(entity, "coil", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(entity, "discreteInput", StringComparison.OrdinalIgnoreCase));
            int maxQuantity = bitEntity ? context.Bounds.MaxCoilQuantity : context.Bounds.MaxRegisterQuantity;
            if (quantity > maxQuantity)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    $"modv:quantity {quantity} exceeds the maximum of {maxQuantity}.",
                    form.Pointer("modv:quantity"), "modv:quantity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            int unitId = form.TryGetInt32("modv:unitID", out int parsedUnit) ? parsedUnit : ParseUnitFromPath(uri);
            if (unitId is < 0 or > 255)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidFieldValue,
                    "modv:unitID must be between 0 and 255.", form.Pointer("modv:unitID"), "modv:unitID"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string dataType = form.TryGetString("modv:type", out string type) ? type : "uint16";
            bool msbFirst = !form.TryGetBoolean("modv:mostSignificantByte", out bool msb) || msb;
            bool mswFirst = !form.TryGetBoolean("modv:mostSignificantWord", out bool msw) || msw;

            ImmutableDictionary<string, string> address4 = ImmutableDictionary<string, string>.Empty
                .Add("entity", entity ?? string.Empty)
                .Add("address", address.ToString(CultureInfo.InvariantCulture))
                .Add("quantity", quantity.ToString(CultureInfo.InvariantCulture))
                .Add("unitId", unitId.ToString(CultureInfo.InvariantCulture));
            if (form.TryGetString("modv:function", out string functionText))
            {
                address4 = address4.Add("function", functionText);
            }
            else if (form.TryGetInt32("modv:function", out int functionCode))
            {
                address4 = address4.Add("function", functionCode.ToString(CultureInfo.InvariantCulture));
            }

            var payloadMetadata = ImmutableDictionary<string, string>.Empty
                .Add("type", dataType)
                .Add("mostSignificantByte", msbFirst ? "true" : "false")
                .Add("mostSignificantWord", mswFirst ? "true" : "false");
            var payload = new WotPayloadDescriptor(
                string.IsNullOrEmpty(form.ContentType) ? "application/octet-stream" : form.ContentType!,
                OctetStreamWotPayloadCodec.Instance.Id, payloadMetadata);

            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(
                $"{entity ?? function}:{address}:{quantity}@{unitId}", address4);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                if (capability == WoTBindingCapabilityEnum.WriteProperty &&
                    entity is not null && s_readOnlyEntities.Contains(entity))
                {
                    // A read-only entity (discrete input / input register) commonly
                    // carries the default read+write property ops. Drop only the
                    // write op with a warning so the valid read binding is still
                    // compiled; an error here would set HasErrors and cause the
                    // whole form (read included) to be dropped as unsupported.
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.ConflictingFields,
                        $"The Modbus entity '{entity}' is read-only; the write operation is not " +
                        "executable and was dropped while the read binding is preserved.",
                        form.Pointer("modv:entity"), "modv:entity"));
                    continue;
                }
                var operation = new WotOperationDescriptor(capability, op, ModbusFunction(capability, entity, quantity));
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

        private static int ParseUnitFromPath(Uri uri)
        {
            string path = uri.AbsolutePath.Trim('/');
            return int.TryParse(path, NumberStyles.Integer, CultureInfo.InvariantCulture, out int unit) ? unit : 1;
        }

        private static string ModbusFunction(WoTBindingCapabilityEnum operation, string? entity, int quantity)
        {
            bool coil = string.Equals(entity, "coil", StringComparison.OrdinalIgnoreCase);
            bool discrete = string.Equals(entity, "discreteInput", StringComparison.OrdinalIgnoreCase);
            bool input = string.Equals(entity, "inputRegister", StringComparison.OrdinalIgnoreCase);
            if (operation == WoTBindingCapabilityEnum.WriteProperty)
            {
                return coil
                    ? (quantity > 1 ? "writeMultipleCoils" : "writeSingleCoil")
                    : (quantity > 1 ? "writeMultipleHoldingRegisters" : "writeSingleHoldingRegister");
            }
            if (coil)
            {
                return "readCoil";
            }
            if (discrete)
            {
                return "readDiscreteInput";
            }
            if (input)
            {
                return "readInputRegister";
            }
            return "readHoldingRegisters";
        }
    }
}
