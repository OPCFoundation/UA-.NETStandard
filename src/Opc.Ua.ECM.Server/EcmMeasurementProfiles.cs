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
    /// One mandatory member of a published OPC 34100 energy profile.
    /// </summary>
    /// <param name="Name">The ECM BrowseName.</param>
    /// <param name="Kind">The declared value type.</param>
    /// <param name="IsStatistic">Whether the profile declares the member with HasStatisticComponent.</param>
    /// <param name="AllowsRange">Whether the profile declares an AccuracyRange property.</param>
    /// <param name="AllowsUnits">Whether the profile declares an EngineeringUnits property.</param>
    internal sealed record EcmProfileMember(
        string Name,
        EcmMeasurementKind Kind,
        bool IsStatistic,
        bool AllowsRange,
        bool AllowsUnits);

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
                EcmMeasurementKind.AcPe => value.TryGetStructure(out AcPeDataType? phases) &&
                    phases is not null && phases.GetType() == typeof(AcPeDataType),
                _ => value.TryGetStructure(out AcPpDataType? lines) &&
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
                    Component(nameof(BrowseNames.AcCurrentPe), EcmMeasurementKind.AcPe)
                ]),
                new("E1", ObjectTypeIds.IEnergyProfileE1Type,
                [
                    Component(nameof(BrowseNames.AcActivePowerTotal), EcmMeasurementKind.Float)
                ]),
                new("E2", ObjectTypeIds.IEnergyProfileE2Type,
                [
                    Statistic(nameof(BrowseNames.AcActiveEnergyTotalExportLp), EcmMeasurementKind.Float),
                    Statistic(nameof(BrowseNames.AcActiveEnergyTotalImportLp), EcmMeasurementKind.Float),
                    Component(nameof(BrowseNames.AcActivePowerTotal), EcmMeasurementKind.Float)
                ]),
                new("E3", ObjectTypeIds.IEnergyProfileE3Type,
                [
                    Statistic(nameof(BrowseNames.AcActiveEnergyTotalExportHp), EcmMeasurementKind.Double),
                    Statistic(nameof(BrowseNames.AcActiveEnergyTotalImportHp), EcmMeasurementKind.Double),
                    Component(nameof(BrowseNames.AcActivePowerPe), EcmMeasurementKind.AcPe),
                    Component(nameof(BrowseNames.AcCurrentPe), EcmMeasurementKind.AcPe),
                    Component(nameof(BrowseNames.AcPowerFactorPe), EcmMeasurementKind.AcPe, units: false),
                    Statistic(nameof(BrowseNames.AcReactiveEnergyTotalExportHp), EcmMeasurementKind.Double),
                    Statistic(nameof(BrowseNames.AcReactiveEnergyTotalImportHp), EcmMeasurementKind.Double),
                    Component(nameof(BrowseNames.AcReactivePowerPe), EcmMeasurementKind.AcPe),
                    Component(nameof(BrowseNames.AcVoltagePe), EcmMeasurementKind.AcPe),
                    Component(nameof(BrowseNames.AcVoltagePp), EcmMeasurementKind.AcPp)
                ]),
                new("D0", ObjectTypeIds.IEnergyProfileD0Type,
                [
                    Component(nameof(BrowseNames.DcCurrent), EcmMeasurementKind.Float)
                ]),
                new("D1", ObjectTypeIds.IEnergyProfileD1Type,
                [
                    Component(nameof(BrowseNames.DcActivePower), EcmMeasurementKind.Float, range: false),
                    Component(nameof(BrowseNames.DcCurrent), EcmMeasurementKind.Float),
                    Statistic(nameof(BrowseNames.DcElectricalCharge), EcmMeasurementKind.Float, range: false),
                    Statistic(nameof(BrowseNames.DcEnergyTotalExportLp), EcmMeasurementKind.Float, range: false),
                    Statistic(nameof(BrowseNames.DcEnergyTotalImportLp), EcmMeasurementKind.Float, range: false),
                    Statistic(nameof(BrowseNames.DcRelativeCharge), EcmMeasurementKind.Float, range: false),
                    Component(nameof(BrowseNames.DcVoltage), EcmMeasurementKind.Float, range: false)
                ])
            ];
        }

        /// <summary>
        /// Merges members shared between profiles; a property is allowed when any profile declares it.
        /// </summary>
        private static Dictionary<string, EcmProfileMember> CreateMembers()
        {
            var members = new Dictionary<string, EcmProfileMember>(StringComparer.Ordinal);
            foreach (EcmProfileDefinition profile in All)
            {
                foreach (EcmProfileMember member in profile.Members)
                {
                    members[member.Name] = members.TryGetValue(member.Name, out EcmProfileMember? existing)
                        ? existing with
                        {
                            AllowsRange = existing.AllowsRange || member.AllowsRange,
                            AllowsUnits = existing.AllowsUnits || member.AllowsUnits
                        }
                        : member;
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
            bool range = true,
            bool units = true)
        {
            return new EcmProfileMember(name, kind, false, range, units);
        }

        /// <summary>
        /// Declares a member exposed with HasStatisticComponent.
        /// </summary>
        private static EcmProfileMember Statistic(
            string name,
            EcmMeasurementKind kind,
            bool range = true,
            bool units = true)
        {
            return new EcmProfileMember(name, kind, true, range, units);
        }

        /// <summary>Lazily merged members, created after the catalogue exists.</summary>
        private static readonly Lazy<Dictionary<string, EcmProfileMember>> s_members = new(CreateMembers);
    }
}
