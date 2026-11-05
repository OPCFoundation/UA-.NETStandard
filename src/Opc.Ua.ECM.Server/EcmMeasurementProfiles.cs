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
 *
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

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// The declared value type of one profile member.
    /// </summary>
    internal enum EcmMeasurementKind
    {
        /// <summary>A scalar Float.</summary>
        Float,

        /// <summary>A scalar Double.</summary>
        Double,

        /// <summary>The AcPeDataType phase-to-neutral structure.</summary>
        AcPe,

        /// <summary>The AcPpDataType phase-to-phase structure.</summary>
        AcPp
    }

    /// <summary>
    /// A standardized engineering unit used by an OPC 34100 measurement.
    /// </summary>
    internal enum EcmMeasurementUnit
    {
        /// <summary>The ampere.</summary>
        Ampere,

        /// <summary>The watt.</summary>
        Watt,

        /// <summary>The watt hour.</summary>
        WattHour,

        /// <summary>The ampere hour.</summary>
        AmpereHour,

        /// <summary>The percent.</summary>
        Percent,

        /// <summary>The volt.</summary>
        Volt,

        /// <summary>The volt ampere reactive.</summary>
        ReactivePower,

        /// <summary>The volt ampere reactive hour.</summary>
        ReactiveEnergy
    }

    /// <summary>
    /// One mandatory member of a published OPC 34100 energy profile.
    /// </summary>
    /// <param name="Name">The ECM BrowseName.</param>
    /// <param name="Kind">The declared value type.</param>
    /// <param name="IsStatistic">Whether the profile declares the member with HasStatisticComponent.</param>
    /// <param name="MeasurementId">The standardized measurement identifier.</param>
    /// <param name="AllowsRange">Whether the profile declares an AccuracyRange property.</param>
    /// <param name="Unit">The standardized engineering unit, or null when the member has no units.</param>
    internal sealed record EcmProfileMember(
        string Name,
        EcmMeasurementKind Kind,
        bool IsStatistic,
        ushort MeasurementId,
        bool AllowsRange,
        EcmMeasurementUnit? Unit);

    /// <summary>
    /// One standalone energy profile interface and its mandatory members.
    /// </summary>
    /// <param name="Name">The profile name, for example E2.</param>
    /// <param name="TypeId">The abstract profile interface ObjectType.</param>
    /// <param name="Members">The mandatory members.</param>
    internal sealed record EcmProfileDefinition(
        string Name,
        ExpandedNodeId TypeId,
        IReadOnlyList<EcmProfileMember> Members);

    /// <summary>
    /// The published E0, E1, E2, E3, D0 and D1 profile catalogue of OPC 34100.
    /// </summary>
    internal static class EcmMeasurementProfiles
    {
        /// <summary>
        /// Gets the catalogue in publication order.
        /// </summary>
        internal static IReadOnlyList<EcmProfileDefinition> All { get; } = Create();

        /// <summary>
        /// Gets a member by BrowseName across all profiles.
        /// </summary>
        /// <param name="name">The ECM BrowseName.</param>
        /// <param name="member">The merged member declaration.</param>
        internal static bool TryGetMember(string name, out EcmProfileMember member)
        {
            return s_members.Value.TryGetValue(name, out member!);
        }

        /// <summary>
        /// Gets the exact OPC UA DataType for a member kind.
        /// </summary>
        /// <param name="kind">The member kind.</param>
        internal static ExpandedNodeId DataTypeOf(EcmMeasurementKind kind)
        {
            return kind switch
            {
                EcmMeasurementKind.Float => new ExpandedNodeId(Opc.Ua.DataTypeIds.Float),
                EcmMeasurementKind.Double => new ExpandedNodeId(Opc.Ua.DataTypeIds.Double),
                EcmMeasurementKind.AcPe => DataTypeIds.AcPeDataType,
                _ => DataTypeIds.AcPpDataType
            };
        }

        /// <summary>
        /// Creates the standardized EngineeringUnits value for a profile member.
        /// </summary>
        /// <param name="member">The profile member.</param>
        internal static EUInformation? EngineeringUnitsOf(EcmProfileMember member)
        {
            ArgumentNullException.ThrowIfNull(member, nameof(member));
            return member.Unit switch
            {
                EcmMeasurementUnit.Ampere => Units("A", "ampere", 4_279_632),
                EcmMeasurementUnit.Watt => Units("W", "watt", 5_723_220),
                EcmMeasurementUnit.WattHour => Units("W·h", "watt hour", 5_720_146),
                EcmMeasurementUnit.AmpereHour => Units("A·h", "ampere hour", 4_279_624),
                EcmMeasurementUnit.Percent => Units("%", "percent", 20_529),
                EcmMeasurementUnit.Volt => Units("V", "volt", 5_655_636),
                EcmMeasurementUnit.ReactivePower => Units("var", "var", 4_469_812),
                EcmMeasurementUnit.ReactiveEnergy => new EUInformation(
                    "var·h",
                    "volt ampere reactive hour",
                    "http://opcfoundation.org/UA/ECM/")
                {
                    UnitId = 1
                },
                _ => null
            };
        }

        /// <summary>
        /// Checks the unit identity against the published profile member.
        /// </summary>
        /// <param name="member">The profile member.</param>
        /// <param name="engineeringUnits">The provider's value.</param>
        internal static bool HasExpectedEngineeringUnits(
            EcmProfileMember member,
            EUInformation engineeringUnits)
        {
            EUInformation? expected = EngineeringUnitsOf(member);
            return expected != null &&
                string.Equals(
                    engineeringUnits.NamespaceUri,
                    expected.NamespaceUri,
                    StringComparison.Ordinal) &&
                engineeringUnits.UnitId == expected.UnitId;
        }

        /// <summary>
        /// Checks the runtime Variant type of a measurement against its declared kind.
        /// </summary>
        /// <param name="kind">The declared kind.</param>
        /// <param name="value">The value to check.</param>
        internal static bool HasExpectedValue(EcmMeasurementKind kind, Variant value)
        {
            return kind switch
            {
                EcmMeasurementKind.Float => value.TryGetValue(out float _),
                EcmMeasurementKind.Double => value.TryGetValue(out double _),
                EcmMeasurementKind.AcPe => value.TryGetStructure<AcPeDataType>(out AcPeDataType? phases) &&
                    phases is not null && phases.GetType() == typeof(AcPeDataType),
                _ => value.TryGetStructure<AcPpDataType>(out AcPpDataType? lines) &&
                    lines is not null && lines.GetType() == typeof(AcPpDataType)
            };
        }

        /// <summary>
        /// Gets the profiles whose complete mandatory member sets are present.
        /// </summary>
        /// <param name="names">The published member names.</param>
        internal static List<EcmProfileDefinition> Declared(ICollection<string> names)
        {
            var declared = new List<EcmProfileDefinition>();
            foreach (EcmProfileDefinition profile in All)
            {
                bool complete = true;
                foreach (EcmProfileMember member in profile.Members)
                {
                    complete &= names.Contains(member.Name);
                }
                if (complete)
                {
                    declared.Add(profile);
                }
            }
            return declared;
        }

        /// <summary>
        /// Builds the catalogue from the published OPC 34100 ECM NodeSet.
        /// </summary>
        private static List<EcmProfileDefinition> Create()
        {
            return
            [
                new("E0", ObjectTypeIds.IEnergyProfileE0Type,
                [
                    Component(
                        nameof(BrowseNames.AcCurrentPe),
                        EcmMeasurementKind.AcPe,
                        1218,
                        EcmMeasurementUnit.Ampere)
                ]),
                new("E1", ObjectTypeIds.IEnergyProfileE1Type,
                [
                    Component(
                        nameof(BrowseNames.AcActivePowerTotal),
                        EcmMeasurementKind.Float,
                        1412,
                        EcmMeasurementUnit.Watt)
                ]),
                new("E2", ObjectTypeIds.IEnergyProfileE2Type,
                [
                    Statistic(
                        nameof(BrowseNames.AcActiveEnergyTotalExportLp),
                        EcmMeasurementKind.Float,
                        1004,
                        EcmMeasurementUnit.WattHour),
                    Statistic(
                        nameof(BrowseNames.AcActiveEnergyTotalImportLp),
                        EcmMeasurementKind.Float,
                        1001,
                        EcmMeasurementUnit.WattHour),
                    Component(
                        nameof(BrowseNames.AcActivePowerTotal),
                        EcmMeasurementKind.Float,
                        1412,
                        EcmMeasurementUnit.Watt)
                ]),
                new("E3", ObjectTypeIds.IEnergyProfileE3Type,
                [
                    Statistic(
                        nameof(BrowseNames.AcActiveEnergyTotalExportHp),
                        EcmMeasurementKind.Double,
                        1005,
                        EcmMeasurementUnit.WattHour),
                    Statistic(
                        nameof(BrowseNames.AcActiveEnergyTotalImportHp),
                        EcmMeasurementKind.Double,
                        1002,
                        EcmMeasurementUnit.WattHour),
                    Component(
                        nameof(BrowseNames.AcActivePowerPe),
                        EcmMeasurementKind.AcPe,
                        1409,
                        EcmMeasurementUnit.Watt),
                    Component(
                        nameof(BrowseNames.AcCurrentPe),
                        EcmMeasurementKind.AcPe,
                        1218,
                        EcmMeasurementUnit.Ampere),
                    Component(
                        nameof(BrowseNames.AcPowerFactorPe),
                        EcmMeasurementKind.AcPe,
                        1709,
                        null),
                    Statistic(
                        nameof(BrowseNames.AcReactiveEnergyTotalExportHp),
                        EcmMeasurementKind.Double,
                        1014,
                        EcmMeasurementUnit.ReactiveEnergy),
                    Statistic(
                        nameof(BrowseNames.AcReactiveEnergyTotalImportHp),
                        EcmMeasurementKind.Double,
                        1011,
                        EcmMeasurementUnit.ReactiveEnergy),
                    Component(
                        nameof(BrowseNames.AcReactivePowerPe),
                        EcmMeasurementKind.AcPe,
                        1618,
                        EcmMeasurementUnit.ReactivePower),
                    Component(
                        nameof(BrowseNames.AcVoltagePe),
                        EcmMeasurementKind.AcPe,
                        1118,
                        EcmMeasurementUnit.Volt),
                    Component(
                        nameof(BrowseNames.AcVoltagePp),
                        EcmMeasurementKind.AcPp,
                        1145,
                        EcmMeasurementUnit.Volt)
                ]),
                new("D0", ObjectTypeIds.IEnergyProfileD0Type,
                [
                    Component(
                        nameof(BrowseNames.DcCurrent),
                        EcmMeasurementKind.Float,
                        1033,
                        EcmMeasurementUnit.Ampere)
                ]),
                new("D1", ObjectTypeIds.IEnergyProfileD1Type,
                [
                    Component(
                        nameof(BrowseNames.DcActivePower),
                        EcmMeasurementKind.Float,
                        1032,
                        EcmMeasurementUnit.Watt,
                        range: false),
                    Component(
                        nameof(BrowseNames.DcCurrent),
                        EcmMeasurementKind.Float,
                        1033,
                        EcmMeasurementUnit.Ampere),
                    Statistic(
                        nameof(BrowseNames.DcElectricalCharge),
                        EcmMeasurementKind.Float,
                        1030,
                        EcmMeasurementUnit.AmpereHour,
                        range: false),
                    Statistic(
                        nameof(BrowseNames.DcEnergyTotalExportLp),
                        EcmMeasurementKind.Float,
                        1025,
                        EcmMeasurementUnit.WattHour,
                        range: false),
                    Statistic(
                        nameof(BrowseNames.DcEnergyTotalImportLp),
                        EcmMeasurementKind.Float,
                        1022,
                        EcmMeasurementUnit.WattHour,
                        range: false),
                    Statistic(
                        nameof(BrowseNames.DcRelativeCharge),
                        EcmMeasurementKind.Float,
                        1031,
                        EcmMeasurementUnit.Percent,
                        range: false),
                    Component(
                        nameof(BrowseNames.DcVoltage),
                        EcmMeasurementKind.Float,
                        1034,
                        EcmMeasurementUnit.Volt,
                        range: false)
                ])
            ];
        }

        /// <summary>
        /// Merges shared members and verifies that their standardized identifiers and units agree.
        /// </summary>
        private static Dictionary<string, EcmProfileMember> CreateMembers()
        {
            var members = new Dictionary<string, EcmProfileMember>(StringComparer.Ordinal);
            foreach (EcmProfileDefinition profile in All)
            {
                foreach (EcmProfileMember member in profile.Members)
                {
                    if (members.TryGetValue(member.Name, out EcmProfileMember? existing))
                    {
                        if (existing.MeasurementId != member.MeasurementId || existing.Unit != member.Unit)
                        {
                            throw new InvalidOperationException(
                                "A shared ECM BrowseName must have identical standardized metadata in every profile.");
                        }
                        members[member.Name] = existing with
                        {
                            AllowsRange = existing.AllowsRange || member.AllowsRange
                        };
                    }
                    else
                    {
                        members.Add(member.Name, member);
                    }
                }
            }
            return members;
        }

        /// <summary>
        /// Declares a member exposed with HasComponent.
        /// </summary>
        private static EcmProfileMember Component(
            string name,
            EcmMeasurementKind kind,
            ushort measurementId,
            EcmMeasurementUnit? unit,
            bool range = true)
        {
            return new EcmProfileMember(name, kind, false, measurementId, range, unit);
        }

        /// <summary>
        /// Declares a member exposed with HasStatisticComponent.
        /// </summary>
        private static EcmProfileMember Statistic(
            string name,
            EcmMeasurementKind kind,
            ushort measurementId,
            EcmMeasurementUnit? unit,
            bool range = true)
        {
            return new EcmProfileMember(name, kind, true, measurementId, range, unit);
        }

        /// <summary>
        /// Creates a UN/CEFACT engineering unit value.
        /// </summary>
        private static EUInformation Units(string symbol, string name, int unitId)
        {
            return new EUInformation(symbol, name, s_cefactUnitsNamespaceUri) { UnitId = unitId };
        }

        /// <summary>Lazily merged members, created after the catalogue exists.</summary>
        private static readonly Lazy<Dictionary<string, EcmProfileMember>> s_members = new(CreateMembers);

        private const string s_cefactUnitsNamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact";
    }
}
