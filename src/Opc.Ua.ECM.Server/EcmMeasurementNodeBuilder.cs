// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Creates ECM measurement-value nodes for standalone ECM and Machinery models.
    /// </summary>
    internal static class EcmMeasurementNodeBuilder
    {
        /// <summary>
        /// Adds or updates a measurement value with its model metadata.
        /// </summary>
        /// <param name="context">The address-space context.</param>
        /// <param name="measurementPoint">The parent measurement point.</param>
        /// <param name="browseName">The qualified measurement name.</param>
        /// <param name="value">The value to publish.</param>
        /// <param name="dataTypeId">The declared OPC UA DataType.</param>
        /// <param name="referenceTypeId">The reference from the point to the value.</param>
        /// <param name="engineeringUnits">The optional engineering units.</param>
        /// <param name="measurementId">The optional standardized measurement identifier.</param>
        /// <param name="accuracyClass">The optional class within the accuracy domain.</param>
        /// <param name="accuracyDomain">The optional accuracy-domain NodeId, or <see cref="NodeId.Null"/>.</param>
        /// <param name="accuracyRange">The optional accuracy range.</param>
        /// <param name="statusCode">The measurement quality.</param>
        /// <param name="sourceTimestamp">The UTC acquisition time.</param>
        /// <param name="publishQuality">Whether to update quality and timestamp.</param>
        internal static EnergyMeasurementValueState AddMeasurement(
            ISystemContext context,
            EnergyMeasurementState measurementPoint,
            QualifiedName browseName,
            Variant value,
            NodeId dataTypeId,
            NodeId referenceTypeId,
            EUInformation? engineeringUnits = null,
            ushort? measurementId = null,
            uint? accuracyClass = null,
            NodeId accuracyDomain = default,
            float? accuracyRange = null,
            StatusCode statusCode = default,
            DateTime sourceTimestamp = default,
            bool publishQuality = false)
        {
            context.ThrowIfNull(nameof(context));
            measurementPoint.ThrowIfNull(nameof(measurementPoint));
            string name = browseName.Name ?? string.Empty;
            if (browseName.IsNull || string.IsNullOrEmpty(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A measurement value must carry a browse name.");
            }
            if (dataTypeId.IsNull)
            {
                throw new ArgumentException("A measurement DataType is required.", nameof(dataTypeId));
            }
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentException("A measurement reference type is required.", nameof(referenceTypeId));
            }

            Variant ownedValue = EcmOwnership.Own(value);
            NodeId ownedAccuracyDomain = EcmOwnership.Own(accuracyDomain);
            EUInformation? ownedUnits = EcmOwnership.Own(engineeringUnits, nameof(engineeringUnits));
            if (string.Equals(
                    context.NamespaceUris.GetString(browseName.NamespaceIndex),
                    Namespaces.ECM,
                    StringComparison.Ordinal) &&
                EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? standardMember))
            {
                NodeId expectedDataType = ExpandedNodeId.ToNodeId(
                    EcmMeasurementProfiles.DataTypeOf(standardMember.Kind),
                    context.NamespaceUris);
                if (dataTypeId != expectedDataType ||
                    (measurementId.HasValue && measurementId.Value != standardMember.MeasurementId) ||
                    (ownedUnits is not null &&
                        !EcmMeasurementProfiles.HasExpectedEngineeringUnits(standardMember, ownedUnits)))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "A standardized ECM measurement must retain its published DataType, MeasurementID and unit.");
                }
                measurementId = standardMember.MeasurementId;
                ownedUnits ??= EcmMeasurementProfiles.EngineeringUnitsOf(standardMember);
            }

            var children = new List<BaseInstanceState>();
            measurementPoint.GetChildren(context, children);
            EnergyMeasurementValueState? measurement = null;
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is EnergyMeasurementValueState candidate &&
                    (candidate.BrowseName == browseName ||
                        string.Equals(candidate.SymbolicName, browseName.Name, StringComparison.Ordinal)))
                {
                    measurement = candidate;
                    break;
                }
            }
            if (measurement == null)
            {
                measurement = context.CreateInstanceOfEnergyMeasurementValueType(measurementPoint, browseName);
                measurement.SymbolicName = name;
                measurement.BrowseName = browseName;
                measurement.DisplayName = new LocalizedText(name);
                measurement.ReferenceTypeId = referenceTypeId;
                measurement.ModellingRuleId = NodeId.Null;
                measurement.DataType = dataTypeId;
                measurement.ValueRank = ValueRanks.Scalar;
                measurementPoint.AddChild(measurement);
            }
            measurement.WrappedValue = ownedValue;
            if (ownedUnits != null)
            {
                measurement.AddEngineeringUnits(context, variable => variable.Value = ownedUnits);
            }
            if (measurementId.HasValue)
            {
                measurement.AddMeasurementID(context, variable => variable.Value = measurementId.Value);
            }
            if (accuracyClass.HasValue)
            {
                measurement.CreateOrReplaceAccuracyClass(context, null);
                Required(measurement.AccuracyClass).Value = accuracyClass.Value;
            }
            if (!ownedAccuracyDomain.IsNull)
            {
                measurement.CreateOrReplaceAccuracyDomain(context, null);
                Required(measurement.AccuracyDomain).Value = ownedAccuracyDomain;
            }
            if (accuracyRange.HasValue)
            {
                measurement.CreateOrReplaceAccuracyRange(context, null);
                Required(measurement.AccuracyRange).Value = accuracyRange.Value;
            }
            if (publishQuality)
            {
                measurement.StatusCode = statusCode;
                measurement.Timestamp = sourceTimestamp;
            }
            return measurement;
        }

        /// <summary>
        /// Gets a required generated property or throws if the model is incomplete.
        /// </summary>
        /// <typeparam name="T">The generated child type.</typeparam>
        private static T Required<T>(T? value) where T : class
        {
            return value ?? throw new InvalidOperationException("The generated ECM measurement model is incomplete.");
        }
    }
}
