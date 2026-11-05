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

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Validates complete provider images before any cached value is changed.
    /// </summary>
    internal static class IaCalibrationValidation
    {
        /// <summary>
        /// Checks the category, immutable topology and value metadata.
        /// </summary>
        internal static void ValidateConfiguration(IaCalibrationTargetConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (configuration.Identification == null ||
                configuration.Category is not IaCalibrationTargetCategory.Dynamic and
                    not IaCalibrationTargetCategory.OneTime and not IaCalibrationTargetCategory.Reusable and
                    not IaCalibrationTargetCategory.ReusableDevice ||
                (configuration.OptionalFields & ~AllFields) != 0)
            {
                throw new ArgumentException("The category, identification or selected properties are invalid.");
            }
            if (configuration.Features.Count == 0 && configuration.ConfigureCustomFeatures == null)
            {
                throw new ArgumentException("A calibration target requires at least one feature.");
            }
            ValidateFeatures(configuration.Features);
            ValidateFeatures(configuration.OperationalConditions);
            IaCalibrationIdentification identification = configuration.Identification;
            if (configuration.Category is IaCalibrationTargetCategory.Reusable or
                IaCalibrationTargetCategory.ReusableDevice &&
                (identification.Manufacturer.IsNull || identification.Model.IsNull ||
                    string.IsNullOrWhiteSpace(identification.SerialNumber)))
            {
                throw new ArgumentException("Reusable targets require Manufacturer, Model and SerialNumber.");
            }
            ValidateOptionalUri(identification.ManufacturerUri);
            ValidateOptionalUri(identification.ProductInstanceUri);
            ValidateOptionalUri(identification.DeviceManual);

            var instruments = new HashSet<NodeId>();
            foreach (NodeId instrument in configuration.ReferenceMeasurementInstruments)
            {
                if (configuration.Category != IaCalibrationTargetCategory.Dynamic ||
                    instrument.IsNull || !instruments.Add(instrument))
                {
                    throw new ArgumentException("Reference instruments require unique NodeIds on a dynamic target.");
                }
            }
        }

        /// <summary>
        /// Gets the effective properties, including the category-dependent mandatory validation date.
        /// </summary>
        internal static IaCalibrationTargetFields EffectiveFields(IaCalibrationTargetConfiguration configuration)
        {
            return configuration.Category is
                IaCalibrationTargetCategory.Reusable or IaCalibrationTargetCategory.ReusableDevice
                ? configuration.OptionalFields | IaCalibrationTargetFields.LastValidationDate
                : configuration.OptionalFields;
        }

        /// <summary>
        /// Checks that one snapshot supplies exactly the configured information.
        /// </summary>
        internal static void ValidateSnapshot(
            IaCalibrationTargetConfiguration configuration,
            IaCalibrationTargetSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            ValidateReadings(configuration.Features, snapshot.Features);
            ValidateReadings(configuration.OperationalConditions, snapshot.OperationalConditions);
            IaCalibrationTargetFields fields = EffectiveFields(configuration);
            ValidatePresence(fields, IaCalibrationTargetFields.LastValidationDate, snapshot.LastValidationDate != null);
            ValidatePresence(fields, IaCalibrationTargetFields.NextValidationDate, snapshot.NextValidationDate != null);
            ValidatePresence(fields, IaCalibrationTargetFields.Quality, snapshot.Quality != null);
            ValidatePresence(fields, IaCalibrationTargetFields.CertificateUri, snapshot.CertificateUri != null);
            ValidateDate(snapshot.LastValidationDate);
            ValidateDate(snapshot.NextValidationDate);
            ValidateTimestamp(snapshot.LastValidationDate);
            ValidateTimestamp(snapshot.NextValidationDate);
            ValidateTimestamp(snapshot.Quality);
            ValidateTimestamp(snapshot.CertificateUri);
            if (snapshot.Quality is { Value: > 100 })
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshot),
                    "Quality must be between zero and one hundred.");
            }
            if (snapshot.CertificateUri != null)
            {
                ValidateUri(snapshot.CertificateUri.Value);
            }
        }

        /// <summary>
        /// Checks each feature's unique name, exact data type, units, rank and optional precision.
        /// </summary>
        private static void ValidateFeatures(ArrayOf<IaCalibrationFeature> features)
        {
            var names = new HashSet<QualifiedName>();
            foreach (IaCalibrationFeature feature in features)
            {
                if (feature == null || feature.BrowseName.IsNull ||
                    string.IsNullOrWhiteSpace(feature.BrowseName.Name) || !names.Add(feature.BrowseName))
                {
                    throw new ArgumentException("Each feature requires a unique, nonempty browse name.");
                }
                if (feature.EngineeringUnits == null || feature.EngineeringUnits.DisplayName.IsNull)
                {
                    throw new ArgumentException("Each feature requires engineering-unit information.");
                }
                ValidateUri(feature.EngineeringUnits.NamespaceUri);
                if (feature.ValuePrecision is double precision && (!Finite(precision) || precision < 0))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(features),
                        "Value precision must be nonnegative and finite.");
                }
                switch (feature.Kind)
                {
                    case IaCalibrationFeatureKind.CalibrationValue:
                        if (!Numeric(feature.NumericType) || feature.Resolution.HasValue ||
                            (feature.ValueRank != ValueRanks.Any &&
                                feature.ValueRank != ValueRanks.Scalar && feature.ValueRank < 1))
                        {
                            throw new ArgumentException(
                                "A calibration value requires a numeric type and valid value rank.");
                        }
                        break;
                    case IaCalibrationFeatureKind.CapacityRange:
                        if (feature.ValueRank != ValueRanks.Scalar ||
                            feature.Resolution is not double resolution || !Finite(resolution) || resolution <= 0)
                        {
                            throw new ArgumentException(
                                "A capacity range requires scalar rank and positive finite resolution.");
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(features), "Unknown calibration-feature kind.");
                }
            }
        }

        /// <summary>
        /// Validates a complete, exact set of typed feature values.
        /// </summary>
        private static void ValidateReadings(
            ArrayOf<IaCalibrationFeature> features,
            ArrayOf<IaCalibrationReading> readings)
        {
            if (features.Count != readings.Count)
            {
                throw new ArgumentException("A snapshot must supply every configured feature exactly once.");
            }
            var definitions = new Dictionary<QualifiedName, IaCalibrationFeature>();
            foreach (IaCalibrationFeature feature in features)
            {
                definitions.Add(feature.BrowseName, feature);
            }
            foreach (IaCalibrationReading reading in readings)
            {
                if (reading.Value.IsNull || reading.Value.SourceTimestamp.IsNull)
                {
                    throw new ArgumentException("Every calibration reading requires an explicit source timestamp.");
                }
                if (!definitions.TryGetValue(reading.BrowseName, out IaCalibrationFeature? feature))
                {
                    throw new ArgumentException("The snapshot contains an unknown or duplicate feature.");
                }
                definitions.Remove(reading.BrowseName);
                Variant value = reading.Value.WrappedValue;
                if (value.IsNull && StatusCode.IsBad(reading.Value.StatusCode))
                {
                    continue;
                }
                if (feature.Kind == IaCalibrationFeatureKind.CapacityRange)
                {
                    if (!value.TryGetStructure<Range>(out Range? range) || range == null ||
                        !Finite(range.Low) || !Finite(range.High) || range.Low > range.High)
                    {
                        throw new ArgumentException("A capacity reading requires a finite, ordered scalar Range.");
                    }
                }
                else if (value.IsNull || value.TypeInfo.BuiltInType != feature.NumericType ||
                    (feature.ValueRank != ValueRanks.Any && value.TypeInfo.ValueRank != feature.ValueRank))
                {
                    throw new ArgumentException(
                        "A calibration reading does not match its declared numeric type or rank.");
                }
                else
                {
                    ValidateFloatingPoint(value);
                }
            }
        }

        /// <summary>
        /// Rejects nonfinite scalar, array and matrix floating-point values without boxing.
        /// </summary>
        private static void ValidateFloatingPoint(Variant value)
        {
            bool valid = true;
            if (value.TryGetValue(out double scalar))
            {
                valid = Finite(scalar);
            }
            else if (value.TryGetValue(out float single))
            {
                valid = Finite(single);
            }
            else if (value.TryGetValue(out ArrayOf<double> doubles))
            {
                foreach (double element in doubles)
                {
                    valid &= Finite(element);
                }
            }
            else if (value.TryGetValue(out ArrayOf<float> singles))
            {
                foreach (float element in singles)
                {
                    valid &= Finite(element);
                }
            }
            else if (value.TryGetValue(out MatrixOf<double> doubleMatrix))
            {
                foreach (double element in doubleMatrix)
                {
                    valid &= Finite(element);
                }
            }
            else if (value.TryGetValue(out MatrixOf<float> singleMatrix))
            {
                foreach (float element in singleMatrix)
                {
                    valid &= Finite(element);
                }
            }
            if (!valid)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Calibration readings must be finite.");
            }
        }

        /// <summary>
        /// Checks whether a built-in type derives from Number.
        /// </summary>
        private static bool Numeric(BuiltInType type)
        {
            return type is BuiltInType.SByte or BuiltInType.Byte or BuiltInType.Int16 or BuiltInType.UInt16 or
                BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or BuiltInType.UInt64 or
                BuiltInType.Float or BuiltInType.Double;
        }

        /// <summary>
        /// Checks finite values on every supported target framework.
        /// </summary>
        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Rejects silent omission or introduction of configured target properties.
        /// </summary>
        private static void ValidatePresence(
            IaCalibrationTargetFields fields,
            IaCalibrationTargetFields field,
            bool present)
        {
            if (((fields & field) != 0) != present)
            {
                throw new ArgumentException("Snapshot property presence must match the fixed target configuration.");
            }
        }

        /// <summary>
        /// Rejects an unknown date presented as a successful validation date.
        /// </summary>
        private static void ValidateDate(IaCalibrationSample<DateTimeUtc>? sample)
        {
            if (sample != null && sample.Value == DateTimeUtc.MinValue && !StatusCode.IsBad(sample.StatusCode))
            {
                throw new ArgumentException("A known validation date is required unless its quality is bad.");
            }
        }

        /// <summary>
        /// Rejects missing source acquisition times for optional target information.
        /// </summary>
        /// <typeparam name="T">The scalar information type.</typeparam>
        private static void ValidateTimestamp<T>(IaCalibrationSample<T>? sample)
        {
            if (sample != null && sample.SourceTimestamp.IsNull)
            {
                throw new ArgumentException("Target information requires a source timestamp.");
            }
        }

        /// <summary>
        /// Checks an optional URI without dereferencing it.
        /// </summary>
        private static void ValidateOptionalUri(string? value)
        {
            if (value != null)
            {
                ValidateUri(value);
            }
        }

        /// <summary>
        /// Checks an RFC 3986 URI without accessing any network resource.
        /// </summary>
        private static void ValidateUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                !uri.IsWellFormedOriginalString())
            {
                throw new ArgumentException("A well-formed absolute URI is required.");
            }
        }

        /// <summary>All supported optional target fields.</summary>
        private const IaCalibrationTargetFields AllFields =
            IaCalibrationTargetFields.LastValidationDate |
            IaCalibrationTargetFields.NextValidationDate |
            IaCalibrationTargetFields.Quality |
            IaCalibrationTargetFields.CertificateUri;
    }
}
