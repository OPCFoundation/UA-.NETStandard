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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Redaction;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Binds an application-owned measurement provider to a generated ECM measurement point.
    /// </summary>
    public sealed class EcmMeasurementBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates a binding for an unregistered generated measurement point.
        /// </summary>
        /// <param name="context">The server address-space context.</param>
        /// <param name="measurementPoint">The generated measurement point.</param>
        /// <param name="provider">The borrowed application-owned provider.</param>
        /// <param name="exclusiveAccess">The optional DI lock service.</param>
        /// <param name="authorize">The optional application authorization policy.</param>
        public EcmMeasurementBinding(
            ISystemContext context,
            EnergyMeasurementState measurementPoint,
            IEcmMeasurementProvider provider,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            MeasurementPoint = measurementPoint ?? throw new ArgumentNullException(nameof(measurementPoint));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_exclusiveAccess = exclusiveAccess;
            m_authorize = authorize;
            m_logger = context.Telemetry.CreateLogger<EcmMeasurementBinding>();
            m_ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);

            Required(MeasurementPoint.ApplicationTag).AccessLevel = AccessLevels.CurrentReadOrWrite;
            Required(MeasurementPoint.ApplicationTag).UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            Required(MeasurementPoint.ApplicationTag).OnSimpleWriteValueAsync = WriteApplicationTagAsync;
        }

        /// <summary>
        /// Gets the generated measurement point.
        /// </summary>
        public EnergyMeasurementState MeasurementPoint { get; }

        /// <summary>
        /// Reads and publishes one provider snapshot.
        /// </summary>
        /// <param name="cancellationToken">Cancels the acquisition.</param>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            await m_operations.ExecuteAsync(RefreshGuardedAsync, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops callbacks after admitted provider operations complete. The provider remains application-owned.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_operations.StopAsync(RemoveCallbacks).ConfigureAwait(false);
            }
            finally
            {
                await m_operations.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Logs acquisition failures, marks the measurements unavailable and retains their last values.
        /// </summary>
        private async ValueTask<bool> RefreshGuardedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.ProviderFailed(exception.ToString());
                }
                MarkUnavailable();
                throw;
            }
        }

        /// <summary>
        /// Reads, validates and publishes one coherent measurement snapshot.
        /// </summary>
        private async ValueTask RefreshCoreAsync(CancellationToken cancellationToken)
        {
            EcmMeasurementSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, EcmMeasurementReading> readings = ValidateSnapshot(snapshot);

            if (!m_initialized)
            {
                foreach (KeyValuePair<string, EcmMeasurementReading> pair in readings)
                {
                    EcmMeasurementReading reading = pair.Value;
                    AddProfileReference(RequiredProfileType(pair.Key));
                    EnergyMeasurementValueState node = EcmMeasurementNodeBuilder.AddMeasurement(
                        m_context,
                        MeasurementPoint,
                        new QualifiedName(reading.BrowseName, m_ecmNamespace),
                        reading.Value,
                        ExpandedNodeId.ToNodeId(reading.DataType, m_context.NamespaceUris),
                        Opc.Ua.ReferenceTypeIds.HasComponent,
                        reading.EngineeringUnits,
                        reading.MeasurementId,
                        reading.AccuracyClass,
                        reading.AccuracyDomain,
                        reading.AccuracyRange,
                        reading.StatusCode,
                        reading.SourceTimestamp,
                        publishQuality: true);
                    m_measurements.Add(pair.Key, node);
                    m_metadata.Add(pair.Key, MetadataOf(reading));
                }
            }
            else
            {
                if (readings.Count != m_measurements.Count)
                {
                    throw new InvalidOperationException(
                        "A provider cannot add or remove published profile measurements.");
                }
                foreach (KeyValuePair<string, EcmMeasurementReading> pair in readings)
                {
                    if (!m_measurements.TryGetValue(pair.Key, out EnergyMeasurementValueState? node) ||
                        !m_metadata.TryGetValue(pair.Key, out EcmMeasurementMetadata? metadata) ||
                        metadata != MetadataOf(pair.Value))
                    {
                        throw new InvalidOperationException(
                            "A provider cannot replace published profile measurements or their metadata.");
                    }
                }
                foreach (KeyValuePair<string, EcmMeasurementReading> pair in readings)
                {
                    EnergyMeasurementValueState node = m_measurements[pair.Key];
                    node.WrappedValue = pair.Value.Value;
                    SetQuality(node, pair.Value.StatusCode, pair.Value.SourceTimestamp);
                }
            }

            Required(MeasurementPoint.ApplicationTag).Value = snapshot.ApplicationTag;
            Required(MeasurementPoint.ApplicationTag).StatusCode = StatusCodes.Good;
            Required(MeasurementPoint.ApplicationTag).Timestamp = DateTime.UtcNow;
            foreach (EnergyMeasurementValueState node in m_measurements.Values)
            {
                node.ClearChangeMasks(m_context, true);
            }
            MeasurementPoint.ClearChangeMasks(m_context, true);
            m_initialized = true;
        }

        /// <summary>
        /// Validates profile names, exact data types, metadata and source timestamps before publication.
        /// </summary>
        private Dictionary<string, EcmMeasurementReading> ValidateSnapshot(EcmMeasurementSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ApplicationTag == null)
            {
                throw new InvalidOperationException("The ECM measurement provider returned an incomplete snapshot.");
            }
            var readings = new Dictionary<string, EcmMeasurementReading>(StringComparer.Ordinal);
            for (int ii = 0; ii < snapshot.Measurements.Count; ii++)
            {
                EcmMeasurementReading reading = snapshot.Measurements[ii] ??
                    throw new InvalidOperationException("The ECM measurement provider returned a null reading.");
                string name = reading.BrowseName ?? string.Empty;
                if (!IsSupportedMeasurement(name) || !readings.TryAdd(name, reading))
                {
                    throw new InvalidOperationException(
                        "The ECM measurement provider returned an unsupported or duplicate profile.");
                }
                if (reading.DataType != RequiredDataType(name) ||
                    reading.SourceTimestamp.Kind != DateTimeKind.Utc ||
                    float.IsNaN(reading.AccuracyRange.GetValueOrDefault()) ||
                    float.IsInfinity(reading.AccuracyRange.GetValueOrDefault()) ||
                    reading.AccuracyRange < 0 ||
                    !IsValidAccuracyMetadata(reading))
                {
                    throw new InvalidOperationException(
                        "The ECM measurement provider returned invalid profile metadata.");
                }
                if (StatusCode.IsGood(reading.StatusCode) && !HasExpectedValue(name, reading.Value))
                {
                    throw new InvalidOperationException(
                        "A good ECM measurement must contain its declared typed value.");
                }
            }
            if (readings.Count == 0)
            {
                throw new InvalidOperationException(
                    "The ECM measurement provider returned no supported profile measurements.");
            }
            return readings;
        }

        /// <summary>
        /// Writes ApplicationTag through authorization and the provider before changing the published node.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WriteApplicationTagAsync(
            ISystemContext caller,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!value.TryGetValue(out string? applicationTag) || applicationTag == null)
            {
                return new AttributeWriteResult(StatusCodes.BadTypeMismatch);
            }
            try
            {
                bool authorized = await EcmPowerAccess.IsAuthorizedAsync(
                    caller,
                    MeasurementPoint.NodeId,
                    m_authorize,
                    m_exclusiveAccess,
                    cancellationToken).ConfigureAwait(false);
                if (!authorized)
                {
                    return new AttributeWriteResult(StatusCodes.BadUserAccessDenied);
                }
                bool persisted = await m_operations.ExecuteAsync(async ct =>
                {
                    if (!await EcmPowerAccess.IsAuthorizedAsync(
                        caller,
                        MeasurementPoint.NodeId,
                        m_authorize,
                        m_exclusiveAccess,
                        ct).ConfigureAwait(false))
                    {
                        return false;
                    }
                    await m_provider.WriteApplicationTagAsync(applicationTag, ct).ConfigureAwait(false);
                    Required(MeasurementPoint.ApplicationTag).Value = applicationTag;
                    Required(MeasurementPoint.ApplicationTag).StatusCode = StatusCodes.Good;
                    Required(MeasurementPoint.ApplicationTag).Timestamp = DateTime.UtcNow;
                    MeasurementPoint.ClearChangeMasks(m_context, true);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
                return persisted
                    ? new AttributeWriteResult(StatusCodes.Good) { SuppressCacheUpdate = true }
                    : new AttributeWriteResult(StatusCodes.BadUserAccessDenied);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.ProviderFailed(exception.ToString());
                }
                return new AttributeWriteResult(StatusCodes.BadUnexpectedError) { SuppressCacheUpdate = true };
            }
        }

        /// <summary>
        /// Removes callbacks before synchronization resources are released.
        /// </summary>
        private void RemoveCallbacks()
        {
            Required(MeasurementPoint.ApplicationTag).OnSimpleWriteValueAsync = null;
        }

        /// <summary>
        /// Marks published values unavailable while retaining their last values and timestamps.
        /// </summary>
        private void MarkUnavailable()
        {
            foreach (EnergyMeasurementValueState node in m_measurements.Values)
            {
                node.StatusCode = StatusCodes.BadNoCommunication;
                node.ClearChangeMasks(m_context, true);
            }
            Required(MeasurementPoint.ApplicationTag).StatusCode = StatusCodes.BadNoCommunication;
            MeasurementPoint.ClearChangeMasks(m_context, true);
        }

        /// <summary>
        /// Adds one inverse HasInterface reference for the selected profile type.
        /// </summary>
        private void AddProfileReference(NodeId profileType)
        {
            if (!MeasurementPoint.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasInterface, false, profileType))
            {
                MeasurementPoint.AddReference(Opc.Ua.ReferenceTypeIds.HasInterface, false, profileType);
            }
        }

        /// <summary>
        /// Resolves the standardized profile type for one supported reading.
        /// </summary>
        private NodeId RequiredProfileType(string measurementName)
        {
            uint identifier = measurementName switch
            {
                nameof(BrowseNames.AcCurrentPe) => ObjectTypeIds.IEnergyProfileE0Type,
                nameof(BrowseNames.AcActivePowerTotal) => ObjectTypeIds.IEnergyProfileE1Type,
                nameof(BrowseNames.DcCurrent) => ObjectTypeIds.IEnergyProfileD0Type,
                _ => throw new InvalidOperationException("The measurement is not part of a supported ECM profile.")
            };
            return new NodeId(identifier, m_ecmNamespace);
        }

        /// <summary>
        /// Resolves the exact DataType required by one supported profile measurement.
        /// </summary>
        private static ExpandedNodeId RequiredDataType(string measurementName)
        {
            return measurementName switch
            {
                nameof(BrowseNames.AcCurrentPe) => new ExpandedNodeId(DataTypeIds.AcPeDataType, Namespaces.ECM),
                nameof(BrowseNames.AcActivePowerTotal) or nameof(BrowseNames.DcCurrent) =>
                    new ExpandedNodeId(new NodeId(Opc.Ua.DataTypeIds.Float)),
                _ => ExpandedNodeId.Null
            };
        }

        /// <summary>
        /// Checks the runtime Variant type for a supported profile measurement.
        /// </summary>
        private static bool HasExpectedValue(string measurementName, Variant value)
        {
            return measurementName switch
            {
                nameof(BrowseNames.AcCurrentPe) => value.TryGetValue(out AcPeDataType? _),
                nameof(BrowseNames.AcActivePowerTotal) or nameof(BrowseNames.DcCurrent) =>
                    value.TryGetValue(out float _),
                _ => false
            };
        }

        /// <summary>
        /// Checks whether a browse name belongs to one of the independently supported profiles.
        /// </summary>
        private static bool IsSupportedMeasurement(string measurementName)
        {
            return measurementName is nameof(BrowseNames.AcCurrentPe) or
                nameof(BrowseNames.AcActivePowerTotal) or nameof(BrowseNames.DcCurrent);
        }

        /// <summary>
        /// Confirms that accuracy metadata references a published domain and a declared class.
        /// </summary>
        private bool IsValidAccuracyMetadata(EcmMeasurementReading reading)
        {
            if (reading.AccuracyDomain.IsNull)
            {
                return !reading.AccuracyClass.HasValue;
            }
            if (m_context.FindPredefinedNode(reading.AccuracyDomain) is not AccuracyDomainState domain)
            {
                return false;
            }
            if (!reading.AccuracyClass.HasValue)
            {
                return true;
            }
            if (domain.EnumValues == null || domain.EnumValues.Value.IsNull)
            {
                return false;
            }
            ArrayOf<EnumValueType> enumValues = domain.EnumValues.Value;
            for (int ii = 0; ii < enumValues.Count; ii++)
            {
                EnumValueType? value = enumValues[ii];
                if (value != null && value.Value == reading.AccuracyClass.Value)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Applies quality and the acquisition timestamp without changing measurement metadata.
        /// </summary>
        private static void SetQuality(BaseVariableState node, StatusCode statusCode, DateTime sourceTimestamp)
        {
            node.StatusCode = statusCode;
            node.Timestamp = sourceTimestamp;
        }

        /// <summary>
        /// Captures the invariant metadata that must remain stable after publication.
        /// </summary>
        private static EcmMeasurementMetadata MetadataOf(EcmMeasurementReading reading)
        {
            return new EcmMeasurementMetadata(
                reading.DataType,
                EngineeringUnitsOf(reading.EngineeringUnits),
                reading.MeasurementId,
                reading.AccuracyClass,
                reading.AccuracyDomain,
                reading.AccuracyRange);
        }

        /// <summary>
        /// Captures the mutable engineering-unit fields as immutable value metadata.
        /// </summary>
        private static EngineeringUnitsMetadata? EngineeringUnitsOf(EUInformation? engineeringUnits)
        {
            return engineeringUnits is null
                ? null
                : new EngineeringUnitsMetadata(
                    engineeringUnits.NamespaceUri,
                    engineeringUnits.UnitId,
                    engineeringUnits.DisplayName?.Locale,
                    engineeringUnits.DisplayName?.Text,
                    engineeringUnits.Description?.Locale,
                    engineeringUnits.Description?.Text);
        }

        /// <summary>
        /// Gets a required generated child or throws if the published model is incomplete.
        /// </summary>
        private static T Required<T>(T? value) where T : class
        {
            return value ?? throw new InvalidOperationException("The generated ECM measurement model is incomplete.");
        }

        /// <summary>The owning address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The borrowed application-owned provider.</summary>
        private readonly IEcmMeasurementProvider m_provider;

        /// <summary>The optional DI lock service.</summary>
        private readonly ILockService? m_exclusiveAccess;

        /// <summary>The optional application policy.</summary>
        private readonly Func<ISystemContext, CancellationToken, ValueTask<bool>>? m_authorize;

        /// <summary>The logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The ECM namespace index.</summary>
        private readonly ushort m_ecmNamespace;

        /// <summary>Serializes callbacks and joins admitted work at shutdown.</summary>
        private readonly EcmBindingOperations m_operations = new();

        /// <summary>Published measurement variables keyed by standardized browse name.</summary>
        private readonly Dictionary<string, EnergyMeasurementValueState> m_measurements = new(StringComparer.Ordinal);

        /// <summary>Immutable metadata for published measurement variables.</summary>
        private readonly Dictionary<string, EcmMeasurementMetadata> m_metadata = new(StringComparer.Ordinal);

        /// <summary>Whether the first provider snapshot has been published.</summary>
        private bool m_initialized;

        /// <summary>Stable measurement metadata after initial publication.</summary>
        private sealed record EcmMeasurementMetadata(
            ExpandedNodeId DataType,
            EngineeringUnitsMetadata? EngineeringUnits,
            ushort? MeasurementId,
            uint? AccuracyClass,
            NodeId AccuracyDomain,
            float? AccuracyRange);

        /// <summary>Value snapshot of the fields that define engineering units.</summary>
        private sealed record EngineeringUnitsMetadata(
            string? NamespaceUri,
            int UnitId,
            string? DisplayNameLocale,
            string? DisplayNameText,
            string? DescriptionLocale,
            string? DescriptionText);
    }

    /// <summary>
    /// Source-generated ECM measurement binding diagnostics.
    /// </summary>
    internal static partial class EcmMeasurementBindingLog
    {
        /// <summary>
        /// Reports a provider failure without including application identifiers or measurement values.
        /// </summary>
        [LoggerMessage(
            EventId = EcmServerEventIds.EcmMeasurementBinding,
            Level = LogLevel.Error,
            Message = "The ECM measurement provider operation failed: {Error}")]
        public static partial void ProviderFailed(this ILogger logger, [SensitiveText] string error);
    }
}
