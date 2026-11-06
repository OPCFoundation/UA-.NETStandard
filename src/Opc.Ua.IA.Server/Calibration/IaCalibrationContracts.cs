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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Supplies persisted calibration-target information, not a physical calibration service.
    /// </summary>
    /// <remarks>
    /// Configuration is fixed for the lifetime of a binding. External applications update the
    /// provider's authoritative store and call RefreshAsync. This binding does not accept UA writes,
    /// dispose the provider, retrieve certificates, or infer physical measurements.
    /// </remarks>
    public interface IIaCalibrationTargetProvider
    {
        /// <summary>
        /// Gets the fixed information-model configuration.
        /// </summary>
        IaCalibrationTargetConfiguration Configuration { get; }

        /// <summary>
        /// Reads one coherent, absolute snapshot from the authoritative store.
        /// </summary>
        ValueTask<IaCalibrationTargetSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Concrete categories defined by OPC 10000-200, section 7.4.
    /// </summary>
    public enum IaCalibrationTargetCategory
    {
        /// <summary>
        /// An individually measured item; a replacement item requires a new target instance.
        /// </summary>
        Dynamic,

        /// <summary>
        /// A consumable target or batch destroyed by use.
        /// </summary>
        OneTime,

        /// <summary>
        /// A reusable target with a mandatory last-validation date.
        /// </summary>
        Reusable,

        /// <summary>
        /// A reusable device with a mandatory last-validation date.
        /// </summary>
        ReusableDevice
    }

    /// <summary>
    /// Optional target properties selected before address-space registration.
    /// </summary>
    [Flags]
    public enum IaCalibrationTargetFields
    {
        /// <summary>
        /// No optional target properties.
        /// </summary>
        None = 0,

        /// <summary>
        /// The date of the last validation; automatically selected for reusable categories.
        /// </summary>
        LastValidationDate = 1,

        /// <summary>
        /// The next validation date, which may legitimately be in the past.
        /// </summary>
        NextValidationDate = 2,

        /// <summary>
        /// Application-defined quality between zero and one hundred.
        /// </summary>
        Quality = 4,

        /// <summary>
        /// A certificate URI published as information only.
        /// </summary>
        CertificateUri = 8
    }

    /// <summary>
    /// The two standardized calibration-feature representations.
    /// </summary>
    public enum IaCalibrationFeatureKind
    {
        /// <summary>
        /// A numeric scalar, array, or matrix representing one overall calibration value.
        /// </summary>
        CalibrationValue,

        /// <summary>
        /// A scalar range and a positive resolution describing a scale.
        /// </summary>
        CapacityRange
    }

    /// <summary>
    /// Immutable engineering-unit information.
    /// </summary>
    /// <param name="NamespaceUri">The engineering-unit dictionary URI.</param>
    /// <param name="UnitId">The dictionary's unit identifier.</param>
    /// <param name="DisplayName">The unit symbol or name.</param>
    /// <param name="Description">The optional expanded unit name.</param>
    public sealed record IaCalibrationUnit(
        string NamespaceUri,
        int UnitId,
        LocalizedText DisplayName,
        LocalizedText Description = default);

    /// <summary>
    /// Immutable DI vendor and tag nameplate information; unset members are omitted.
    /// </summary>
    public sealed record IaCalibrationIdentification
    {
        /// <summary>
        /// Gets the manufacturer.
        /// </summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>
        /// Gets the manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Gets the model.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Gets the product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// Gets the hardware revision.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// Gets the software revision.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// Gets the device revision.
        /// </summary>
        public string? DeviceRevision { get; init; }

        /// <summary>
        /// Gets the device manual URI.
        /// </summary>
        public string? DeviceManual { get; init; }

        /// <summary>
        /// Gets the device class.
        /// </summary>
        public string? DeviceClass { get; init; }

        /// <summary>
        /// Gets the serial number, identifying the item or consumable batch.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Gets the recommended globally unique product-instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>
        /// Gets the revision counter.
        /// </summary>
        public int? RevisionCounter { get; init; }

        /// <summary>
        /// Gets the asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// Gets the component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; }
    }

    /// <summary>
    /// Declares one calibration feature or vendor-defined operational-condition value.
    /// </summary>
    /// <param name="BrowseName">The unique browse name within its containing folder.</param>
    /// <param name="Kind">The standardized variable type.</param>
    /// <param name="EngineeringUnits">The mandatory unit information.</param>
    /// <param name="NumericType">The exact numeric built-in type for a calibration value.</param>
    /// <param name="ValueRank">Scalar, an exact positive rank, or Any for a calibration value.</param>
    /// <param name="Resolution">The mandatory positive finite step for a capacity range.</param>
    /// <param name="ValuePrecision">The optional nonnegative finite value precision.</param>
    public sealed record IaCalibrationFeature(
        QualifiedName BrowseName,
        IaCalibrationFeatureKind Kind,
        IaCalibrationUnit EngineeringUnits,
        BuiltInType NumericType = BuiltInType.Double,
        int ValueRank = ValueRanks.Scalar,
        double? Resolution = null,
        double? ValuePrecision = null);

    /// <summary>
    /// Fixed topology of one calibration target, applied before registration with its node manager.
    /// </summary>
    /// <param name="Category">A concrete target category.</param>
    /// <param name="Identification">The DI nameplate values.</param>
    /// <param name="Features">
    /// Named standard features; custom feature components may be supplied by ConfigureCustomFeatures.
    /// </param>
    public sealed record IaCalibrationTargetConfiguration(
        IaCalibrationTargetCategory Category,
        IaCalibrationIdentification Identification,
        ArrayOf<IaCalibrationFeature> Features)
    {
        /// <summary>
        /// Gets the selected optional target properties.
        /// </summary>
        public IaCalibrationTargetFields OptionalFields { get; init; }

        /// <summary>
        /// Gets the callback that adds application-defined components to CalibrationTargetFeatures.
        /// </summary>
        public Action<ISystemContext, BaseObjectState>? ConfigureCustomFeatures { get; init; }

        /// <summary>
        /// Gets vendor-defined operational conditions represented using typed calibration variables.
        /// </summary>
        public ArrayOf<IaCalibrationFeature> OperationalConditions { get; init; }

        /// <summary>
        /// Gets the callback that adds application-defined components to OperationalConditions.
        /// </summary>
        public Action<ISystemContext, BaseObjectState>? ConfigureCustomOperationalConditions { get; init; }

        /// <summary>
        /// Gets reference measurement instruments for a dynamic target.
        /// Empty means that no instrument relationship is known.
        /// </summary>
        public ArrayOf<NodeId> ReferenceMeasurementInstruments { get; init; }
    }

    /// <summary>
    /// A typed absolute reading with the original quality and source timestamp.
    /// </summary>
    /// <typeparam name="T">The scalar value type.</typeparam>
    /// <param name="Value">The authoritative value.</param>
    /// <param name="StatusCode">Its original quality, including uncertain or bad quality.</param>
    /// <param name="SourceTimestamp">Its original UTC source timestamp.</param>
    public sealed record IaCalibrationSample<T>(T Value, StatusCode StatusCode, DateTimeUtc SourceTimestamp);

    /// <summary>
    /// A feature reading with its actual numeric or Range value, quality and timestamps.
    /// </summary>
    /// <param name="BrowseName">The configured feature's browse name.</param>
    /// <param name="Value">The absolute source reading; null values are allowed only with bad quality.</param>
    public readonly record struct IaCalibrationReading(QualifiedName BrowseName, DataValue Value);

    /// <summary>
    /// One coherent provider image; every configured feature and optional property must be supplied.
    /// </summary>
    /// <param name="Features">Absolute calibration-feature readings.</param>
    public sealed record IaCalibrationTargetSnapshot(ArrayOf<IaCalibrationReading> Features)
    {
        /// <summary>
        /// Gets the operational-condition readings, if configured.
        /// </summary>
        public ArrayOf<IaCalibrationReading> OperationalConditions { get; init; }

        /// <summary>
        /// Gets the last validation date, mandatory for reusable categories.
        /// </summary>
        public IaCalibrationSample<DateTimeUtc>? LastValidationDate { get; init; }

        /// <summary>
        /// Gets the next validation date.
        /// </summary>
        public IaCalibrationSample<DateTimeUtc>? NextValidationDate { get; init; }

        /// <summary>
        /// Gets the application-defined quality percentage.
        /// </summary>
        public IaCalibrationSample<byte>? Quality { get; init; }

        /// <summary>
        /// Gets the certificate URI, which the binding never retrieves.
        /// </summary>
        public IaCalibrationSample<string>? CertificateUri { get; init; }
    }
}
