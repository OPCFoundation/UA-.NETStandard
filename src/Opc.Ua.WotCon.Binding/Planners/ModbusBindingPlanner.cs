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

        /// <summary>The maximum addressable 16-bit Modbus register / bit address.</summary>
        private const int MaxAddress = 65535;

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

            // Resolve modv:function (string mnemonic or numeric code) to a canonical
            // function code, its entity and its read / write direction. A function is
            // optional, but when present it must be one of the exactly mapped codes
            // 1, 2, 3, 4, 5, 6, 15 or 16.
            bool functionPresent = form.FormElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                form.FormElement.TryGetProperty("modv:function", out _);
            ModbusFunctionInfo? function = null;
            if (functionPresent)
            {
                if (!TryResolveFunction(form, out ModbusFunctionInfo resolved))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        "modv:function must be one of the Modbus function codes 1, 2, 3, 4, 5, 6, 15 or 16 " +
                        "(or their canonical mnemonics).",
                        form.Pointer("modv:function"), "modv:function"));
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
                function = resolved;
            }

            if (entity is null && function is null)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "A Modbus form requires modv:entity or modv:function.",
                    form.Pointer("modv:entity"), "modv:entity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            // A form that declares both must be internally consistent: the function's
            // entity has to match the declared entity.
            if (entity is not null && function is not null &&
                !string.Equals(entity, function.Value.Entity, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.ConflictingFields,
                    $"modv:function '{function.Value.Mnemonic}' operates on '{function.Value.Entity}' " +
                    $"which conflicts with modv:entity '{entity}'.",
                    form.Pointer("modv:function"), "modv:function"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string effectiveEntity = entity ?? function!.Value.Entity;

            if (!form.TryGetInt32("modv:address", out int address) || address is < 0 or > MaxAddress)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    $"A Modbus form requires a modv:address between 0 and {MaxAddress}.",
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
            bool bitEntity =
                string.Equals(effectiveEntity, "coil", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveEntity, "discreteInput", StringComparison.OrdinalIgnoreCase);
            int maxQuantity = bitEntity ? context.Bounds.MaxCoilQuantity : context.Bounds.MaxRegisterQuantity;
            if (quantity > maxQuantity)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    $"modv:quantity {quantity} exceeds the maximum of {maxQuantity}.",
                    form.Pointer("modv:quantity"), "modv:quantity"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            // The addressed range must not run past the 16-bit Modbus address space.
            if (address + quantity - 1 > MaxAddress)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    $"The Modbus range starting at {address} for {quantity} items exceeds the " +
                    $"maximum address {MaxAddress}.",
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
                .Add("entity", effectiveEntity)
                .Add("address", address.ToString(CultureInfo.InvariantCulture))
                .Add("quantity", quantity.ToString(CultureInfo.InvariantCulture))
                .Add("unitId", unitId.ToString(CultureInfo.InvariantCulture));
            if (function is not null)
            {
                address4 = address4
                    .Add("function", function.Value.Mnemonic)
                    .Add("functionCode", function.Value.Code.ToString(CultureInfo.InvariantCulture));
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
                $"{effectiveEntity}:{address}:{quantity}@{unitId}", address4);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                bool isWriteOp = capability == WoTBindingCapabilityEnum.WriteProperty;
                if (isWriteOp && s_readOnlyEntities.Contains(effectiveEntity))
                {
                    // A read-only entity (discrete input / input register) commonly
                    // carries the default read+write property ops. Drop only the
                    // write op with a warning so the valid read binding is still
                    // compiled; an error here would set HasErrors and cause the
                    // whole form (read included) to be dropped as unsupported.
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.ConflictingFields,
                        $"The Modbus entity '{effectiveEntity}' is read-only; the write operation is not " +
                        "executable and was dropped while the read binding is preserved.",
                        form.Pointer("modv:entity"), "modv:entity"));
                    continue;
                }
                // Reject op / function direction mismatches: an explicit write
                // function cannot serve a read / observe op and vice versa. The
                // offending op is dropped (rejected) while any compatible op on the
                // same form is preserved.
                if (function is not null && function.Value.IsWrite != isWriteOp)
                {
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.ConflictingFields,
                        $"modv:function '{function.Value.Mnemonic}' is a " +
                        (function.Value.IsWrite ? "write" : "read") +
                        $" function and cannot serve the '{op}' operation; the operation was dropped.",
                        form.Pointer("modv:function"), "modv:function"));
                    continue;
                }
                string method = function is not null
                    ? function.Value.Mnemonic
                    : ModbusFunction(capability, effectiveEntity, quantity);
                var operation = new WotOperationDescriptor(capability, op, method);
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

        /// <summary>The canonical entity, direction and code for a Modbus function.</summary>
        private readonly struct ModbusFunctionInfo
        {
            public ModbusFunctionInfo(int code, string entity, bool isWrite, string mnemonic)
            {
                Code = code;
                Entity = entity;
                IsWrite = isWrite;
                Mnemonic = mnemonic;
            }

            public int Code { get; }

            public string Entity { get; }

            public bool IsWrite { get; }

            public string Mnemonic { get; }
        }

        /// <summary>
        /// Resolves the <c>modv:function</c> term (a string mnemonic or a numeric
        /// code) to one of the exactly mapped Modbus function codes 1, 2, 3, 4, 5,
        /// 6, 15 or 16. Returns <c>false</c> when the term is present but not a
        /// recognized function.
        /// </summary>
        private static bool TryResolveFunction(WotAffordanceForm form, out ModbusFunctionInfo info)
        {
            if (form.TryGetString("modv:function", out string text) && !string.IsNullOrEmpty(text))
            {
                switch (text.Trim().ToLowerInvariant())
                {
                    case "readcoil":
                    case "readcoils":
                        info = Function(1);
                        return true;
                    case "readdiscreteinput":
                    case "readdiscreteinputs":
                        info = Function(2);
                        return true;
                    case "readholdingregister":
                    case "readholdingregisters":
                        info = Function(3);
                        return true;
                    case "readinputregister":
                    case "readinputregisters":
                        info = Function(4);
                        return true;
                    case "writesinglecoil":
                        info = Function(5);
                        return true;
                    case "writesingleholdingregister":
                    case "writesingleregister":
                        info = Function(6);
                        return true;
                    case "writemultiplecoils":
                        info = Function(15);
                        return true;
                    case "writemultipleholdingregisters":
                    case "writemultipleregisters":
                        info = Function(16);
                        return true;
                    default:
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                            IsMappedCode(parsed))
                        {
                            info = Function(parsed);
                            return true;
                        }
                        break;
                }
            }
            else if (form.TryGetInt32("modv:function", out int code) && IsMappedCode(code))
            {
                info = Function(code);
                return true;
            }
            info = default;
            return false;
        }

        private static bool IsMappedCode(int code)
            => code is 1 or 2 or 3 or 4 or 5 or 6 or 15 or 16;

        private static ModbusFunctionInfo Function(int code)
        {
            return code switch
            {
                1 => new ModbusFunctionInfo(1, "coil", false, "readCoil"),
                2 => new ModbusFunctionInfo(2, "discreteInput", false, "readDiscreteInput"),
                3 => new ModbusFunctionInfo(3, "holdingRegister", false, "readHoldingRegisters"),
                4 => new ModbusFunctionInfo(4, "inputRegister", false, "readInputRegister"),
                5 => new ModbusFunctionInfo(5, "coil", true, "writeSingleCoil"),
                6 => new ModbusFunctionInfo(6, "holdingRegister", true, "writeSingleHoldingRegister"),
                15 => new ModbusFunctionInfo(15, "coil", true, "writeMultipleCoils"),
                _ => new ModbusFunctionInfo(16, "holdingRegister", true, "writeMultipleHoldingRegisters")
            };
        }
    }
}
