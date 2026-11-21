// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Maps an explicit allowlist of UN/CEFACT codes to QUDT units and conventional quantity kinds.
    /// </summary>
    /// <remarks>
    /// Unit identities follow the published QUDT UN/CEFACT common-code annotations.
    /// Quantity kinds are explicit defaults, not deductions from a unit symbol or a physical dimension.
    /// Applications with more specific process semantics can replace this provider.
    /// </remarks>
    public sealed class WotQudtUnitMappingProvider : IWotUnitMappingProvider
    {
        /// <summary>
        /// Creates the explicit UN/CEFACT mapping provider.
        /// </summary>
        public WotQudtUnitMappingProvider()
        {
        }

        /// <inheritdoc/>
        public bool TryGetMapping(string namespaceUri, int unitId, out string unitIri, out string? quantityKindIri)
        {
            if (namespaceUri is null)
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }
            unitIri = string.Empty;
            quantityKindIri = null;
            if (!string.Equals(namespaceUri.TrimEnd('/'),
                "http://www.opcfoundation.org/UA/units/un/cefact", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            uint encoded = unchecked((uint)unitId);
            Span<char> code = stackalloc char[4];
            int first = 4;
            for (int index = 3; index >= 0; index--)
            {
                code[index] = (char)(encoded & 0xFF);
                encoded >>= 8;
                if (code[index] != 0)
                {
                    first = index;
                }
            }
            if (first == 4)
            {
                return false;
            }
            for (int index = first; index < code.Length; index++)
            {
                if (code[index] is < '!' or > '~')
                {
                    return false;
                }
            }
            if (!s_mappings.TryGetValue(code[first..].ToString(), out (string Unit, string? Kind) mapping))
            {
                return false;
            }
            unitIri = "http://qudt.org/vocab/unit/" + mapping.Unit;
            quantityKindIri = mapping.Kind is null ? null : WotUnitMigration.QuantityKindNamespace + mapping.Kind;
            return true;
        }

        /// <summary>
        /// Explicit common-code identities and conventional quantity kinds; percent has no unique quantity kind.
        /// </summary>
        private static readonly Dictionary<string, (string Unit, string? Kind)> s_mappings =
            new(StringComparer.Ordinal)
            {
                ["CEL"] = ("DEG_C", "Temperature"),
                ["FAH"] = ("DEG_F", "Temperature"),
                ["KEL"] = ("K", "Temperature"),
                ["MTR"] = ("M", "Length"),
                ["CMT"] = ("CentiM", "Length"),
                ["MMT"] = ("MilliM", "Length"),
                ["4H"] = ("MicroM", "Length"),
                ["C45"] = ("NanoM", "Length"),
                ["KMT"] = ("KiloM", "Length"),
                ["KTM"] = ("KiloM", "Length"),
                ["INH"] = ("IN", "Length"),
                ["FOT"] = ("FT", "Length"),
                ["YRD"] = ("YD", "Length"),
                ["SMI"] = ("MI", "Length"),
                ["MTK"] = ("M2", "Area"),
                ["CMK"] = ("CentiM2", "Area"),
                ["MMK"] = ("MilliM2", "Area"),
                ["MTQ"] = ("M3", "Volume"),
                ["LTR"] = ("L", "Volume"),
                ["MLT"] = ("MilliL", "Volume"),
                ["MTS"] = ("M-PER-SEC", "Velocity"),
                ["C16"] = ("MilliM-PER-SEC", null),
                ["MSK"] = ("M-PER-SEC2", "Acceleration"),
                ["KMH"] = ("KiloM-PER-HR", "Velocity"),
                ["FS"] = ("FT-PER-SEC", "Velocity"),
                ["MQS"] = ("M3-PER-SEC", "VolumeFlowRate"),
                ["MQH"] = ("M3-PER-HR", "VolumeFlowRate"),
                ["L2"] = ("L-PER-MIN", "VolumeFlowRate"),
                ["KGM"] = ("KiloGM", "Mass"),
                ["GRM"] = ("GM", "Mass"),
                ["MGM"] = ("MilliGM", "Mass"),
                ["KGS"] = ("KiloGM-PER-SEC", null),
                ["F27"] = ("GM-PER-HR", null),
                ["TNE"] = ("TONNE", "Mass"),
                ["KMQ"] = ("KiloGM-PER-M3", "MassDensity"),
                ["SEC"] = ("SEC", "Time"),
                ["C26"] = ("MilliSEC", "Time"),
                ["B98"] = ("MicroSEC", "Time"),
                ["MIN"] = ("MIN", "Time"),
                ["HUR"] = ("HR", "Time"),
                ["DAY"] = ("DAY", "Time"),
                ["HTZ"] = ("HZ", "Frequency"),
                ["KHZ"] = ("KiloHZ", "Frequency"),
                ["MHZ"] = ("MegaHZ", "Frequency"),
                ["C94"] = ("PER-MIN", null),
                ["C97"] = ("PER-SEC", null),
                ["M46"] = ("REV-PER-MIN", "AngularVelocity"),
                ["C81"] = ("RAD", "PlaneAngle"),
                ["DD"] = ("DEG", "PlaneAngle"),
                ["P1"] = ("PERCENT", null),
                ["2N"] = ("DeciB", null),
                ["C65"] = ("PA-SEC", null),
                ["S4"] = ("M2-PER-SEC", null),
                ["G01"] = ("PA-M3-PER-SEC", null),
                ["D44"] = ("V-A_Reactive", null),
                ["PAL"] = ("PA", "Pressure"),
                ["KPA"] = ("KiloPA", "Pressure"),
                ["MPA"] = ("MegaPA", "Pressure"),
                ["BAR"] = ("BAR", "Pressure"),
                ["MBR"] = ("MilliBAR", "Pressure"),
                ["PS"] = ("PSI", "Pressure"),
                ["NEW"] = ("N", "Force"),
                ["B47"] = ("KiloN", "Force"),
                ["NU"] = ("N-M", "Torque"),
                ["JOU"] = ("J", "Energy"),
                ["KJO"] = ("KiloJ", "Energy"),
                ["WTT"] = ("W", "Power"),
                ["KWT"] = ("KiloW", "Power"),
                ["MAW"] = ("MegaW", "Power"),
                ["WHR"] = ("W-HR", "Energy"),
                ["KWH"] = ("KiloW-HR", "Energy"),
                ["AMP"] = ("A", "ElectricCurrent"),
                ["4K"] = ("MilliA", "ElectricCurrent"),
                ["B84"] = ("MicroA", "ElectricCurrent"),
                ["VLT"] = ("V", "ElectricPotential"),
                ["2Z"] = ("MilliV", "ElectricPotential"),
                ["KVT"] = ("KiloV", "ElectricPotential"),
                ["OHM"] = ("OHM", "Resistance"),
                ["B49"] = ("KiloOHM", "Resistance"),
                ["B75"] = ("MegaOHM", "Resistance"),
                ["FAR"] = ("FARAD", "Capacitance"),
                ["C10"] = ("MilliFARAD", "Capacitance"),
                ["4O"] = ("MicroFARAD", "Capacitance"),
                ["C41"] = ("NanoFARAD", "Capacitance"),
                ["4T"] = ("PicoFARAD", "Capacitance"),
                ["81"] = ("H", "Inductance"),
                ["C14"] = ("MilliH", "Inductance"),
                ["SIE"] = ("S", "Conductance"),
                ["C27"] = ("MilliS", "Conductance"),
                ["WEB"] = ("WB", "MagneticFlux"),
                ["D33"] = ("T", "MagneticFluxDensity")
            };
    }
}
