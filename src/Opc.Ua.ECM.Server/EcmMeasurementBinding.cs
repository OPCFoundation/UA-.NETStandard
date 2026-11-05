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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.IA.Server;
using Opc.Ua.Redaction;
using Opc.Ua.Server;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Binds an application-owned measurement provider to a generated ECM measurement point.
    /// </summary>
    /// <remarks>
    /// Refreshes and bound ApplicationTag writes remain serialized until their node-change notifications complete,
    /// including notifications that mark measurements unavailable. Notification sinks on
    /// <see cref="MeasurementPoint"/> or its descendants must not await or synchronously block on this binding's
    /// <see cref="RefreshAsync"/>, a bound ApplicationTag write, or <see cref="DisposeAsync"/>.
    /// Those operations cannot complete until the active operation and its notification sink finish.
    /// Synchronous sinks run inline and may block the caller before an asynchronous operation returns.
    /// </remarks>
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
        /// <param name="findNode">Resolves published accuracy domains in the owning address space.
        /// Required when provider readings reference accuracy domains.</param>
        public EcmMeasurementBinding(
            ISystemContext context,
            EnergyMeasurementState measurementPoint,
            IEcmMeasurementProvider provider,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null,
            Func<NodeId, NodeState?>? findNode = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            MeasurementPoint = measurementPoint ?? throw new ArgumentNullException(nameof(measurementPoint));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_exclusiveAccess = exclusiveAccess;
            m_authorize = authorize;
            m_findNode = findNode;
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

        /// <summary>Gets the complete profiles exposed by this measurement point.</summary>
        internal IReadOnlyList<string> ProfileNames => m_profileNames;

        /// <summary>Gets whether this binding has been disposed.</summary>
        internal bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

        /// <summary>Gets the server associated with this binding's system context.</summary>
        internal IServerInternal? Server => (m_context as ServerSystemContext)?.Server;

        /// <summary>
        /// Reads and publishes one provider snapshot.
        /// </summary>
        /// <remarks>
        /// Publication awaits notification sinks within the serialized operation.
        /// Sinks must follow the same-binding operation restrictions documented on <see cref="EcmMeasurementBinding"/>.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the acquisition.</param>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            await m_operations.ExecuteAsync(RefreshGuardedAsync, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops callbacks after admitted provider operations complete. The provider remains application-owned.
        /// </summary>
        /// <remarks>
        /// Notification sinks must not await or synchronously block on disposal of this binding.
        /// Disposal waits for the active operation, including its notification sinks, to finish.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref m_disposed, 1);
            try
            {
                await m_operations.StopAsync(RemoveCallbacks).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    Interlocked.Exchange(ref m_statistics, null)?.Dispose();
                }
                finally
                {
                    await m_operations.DisposeAsync().ConfigureAwait(false);
                }
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
                    m_logger.MeasurementProviderFailed(exception.ToString());
                }
                await MarkUnavailableAsync().ConfigureAwait(false);
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
            bool hasStatistics = false;
            foreach (string name in readings.Keys)
            {
                EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? member);
                hasStatistics |= member!.IsStatistic;
            }
            if (hasStatistics &&
                (m_provider is not IEcmStatisticsProvider || snapshot.StatisticsStartTime.IsNull))
            {
                throw new InvalidOperationException(
                    "Statistic members require an IEcmStatisticsProvider and a StatisticsStartTime.");
            }

            bool topologyBuiltNow = false;
            if (!m_topologyBuilt)
            {
                if (m_topologyFaulted)
                {
                    throw new InvalidOperationException(
                        "A previous topology cleanup failed; this binding cannot be initialized again.");
                }
                var existing = new List<BaseInstanceState>();
                MeasurementPoint.GetChildren(m_context, existing);
                var existingReferences = new List<IReference>();
                MeasurementPoint.GetReferences(m_context, existingReferences);
                try
                {
                    BuildTopology(readings, cancellationToken);
                }
                catch
                {
                    RollbackTopology(
                        existing,
                        [.. existingReferences.Select(r => (r.ReferenceTypeId, r.IsInverse, r.TargetId))]);
                    throw;
                }
                m_topologyBuilt = true;
                topologyBuiltNow = true;
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
                    if (!m_measurements.ContainsKey(pair.Key) ||
                        !m_metadata.TryGetValue(pair.Key, out EcmMeasurementMetadata? metadata) ||
                        metadata != MetadataOf(pair.Value))
                    {
                        throw new InvalidOperationException(
                            "A provider cannot replace published profile measurements or their metadata.");
                    }
                }
            }

            if (m_statistics != null && m_adapter != null)
            {
                m_adapter.Pending = CreateStatisticsSnapshot(
                    snapshot.StatisticsStartTime,
                    readings,
                    CreateCompanionValues(readings, snapshot.ApplicationTag));
                try
                {
                    await m_statistics.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    m_adapter.Pending = null;
                }
            }
            if (m_statistics == null)
            {
                if (!topologyBuiltNow)
                {
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
            }
            // The snapshot is committed; cancellation must not interrupt its notifications.
            foreach (KeyValuePair<string, EnergyMeasurementValueState> pair in m_measurements)
            {
                if (!m_statisticNames.Contains(pair.Key))
                {
                    await pair.Value.ClearChangeMasksAsync(m_context, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            await MeasurementPoint
                .ClearChangeMasksAsync(m_context, true, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the profile references and measurement nodes and constructs the shared statistics runtime.
        /// </summary>
        private void BuildTopology(
            Dictionary<string, EcmMeasurementReading> readings,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EcmProfileDefinition> declaredProfiles = EcmMeasurementProfiles.Declared(readings.Keys);
            m_profileNames = new string[declaredProfiles.Count];
            for (int ii = 0; ii < declaredProfiles.Count; ii++)
            {
                EcmProfileDefinition profile = declaredProfiles[ii];
                m_profileNames[ii] = profile.Name;
                AddProfileReference(ProfileType(profile));
            }
            var components = new List<BaseVariableState>();
            var companions = new List<BaseVariableState>();
            foreach (string name in new SortedSet<string>(readings.Keys, StringComparer.Ordinal))
            {
                EcmMeasurementReading reading = readings[name];
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
                m_measurements.Add(name, node);
                m_metadata.Add(name, MetadataOf(reading));
                EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? member);
                if (member!.IsStatistic)
                {
                    m_statisticNames.Add(name);
                    components.Add(node);
                }
                else
                {
                    m_companionNames.Add(name);
                    companions.Add(node);
                }
            }
            if (components.Count > 0)
            {
                companions.Add(Required(MeasurementPoint.ApplicationTag));
                m_companionNodes = companions.ToArray();
                m_adapter = new StatisticsAdapter(this, (IEcmStatisticsProvider)m_provider);
                m_statistics = new IaStatistics(
                    m_context,
                    MeasurementPoint,
                    components.ToArray(),
                    m_adapter,
                    new IaStatisticsOptions
                    {
                        Composition = new IaStatisticsComposition { Nodes = m_companionNodes }
                    },
                    cancellationToken: cancellationToken);
                MethodState reset = m_statistics.ResetStatistics;
                GenericMethodCalledEventHandler2Async? inner = reset.OnCallMethod2Async;
                if (inner != null)
                {
                    reset.OnCallMethod2Async = (context, method, objectId, inputs, outputs, ct) =>
                        m_operations.ExecuteAsync(
                            innerToken => inner(context, method, objectId, inputs, outputs, innerToken), ct);
                }
            }
        }

        /// <summary>
        /// Removes the children and interface references added by a failed topology construction, including the
        /// shared statistics children, so that a retry starts clean. Every step runs independently. When a step
        /// fails, the bookkeeping is retained, the failure is logged and later initialization attempts are rejected
        /// explicitly. The caller rethrows the original failure.
        /// </summary>
        private void RollbackTopology(
            List<BaseInstanceState> existing,
            HashSet<(NodeId, bool, ExpandedNodeId)> existingReferences)
        {
            bool clean = true;
            clean &= TryCleanup(() =>
            {
                if (m_statistics != null)
                {
                    m_statistics.ResetStatistics.OnCallMethod2Async = null;
                }
            });
            bool statisticsDisposed = false;
            clean &= TryCleanup(() =>
            {
                if (m_statistics != null)
                {
                    m_statistics.Dispose();
                    statisticsDisposed = true;
                }
            });
            if (statisticsDisposed)
            {
                m_statistics = null;
            }
            foreach (BaseInstanceState child in OwnedChildren(existing))
            {
                BaseInstanceState owned = child;
                clean &= TryCleanup(() => MeasurementPoint.RemoveChild(owned));
            }
            foreach (IReference reference in OwnedReferences(existingReferences))
            {
                IReference owned = reference;
                clean &= TryCleanup(() => MeasurementPoint.RemoveReference(
                    owned.ReferenceTypeId, owned.IsInverse, owned.TargetId));
            }
            if (clean && (OwnedChildren(existing).Count > 0 || OwnedReferences(existingReferences).Count > 0))
            {
                clean = false;
                if (m_logger.IsEnabled(LogLevel.Warning))
                {
                    m_logger.MeasurementTopologyCleanupFailed("Owned children or references remain after cleanup.");
                }
            }
            if (!clean)
            {
                m_topologyFaulted = true;
                return;
            }
            m_adapter = null;
            m_measurements.Clear();
            m_metadata.Clear();
            m_statisticNames.Clear();
            m_companionNames.Clear();
            m_companionNodes = [];
        }

        /// <summary>
        /// Returns the current children that were not present before topology construction, by instance identity.
        /// </summary>
        private List<BaseInstanceState> OwnedChildren(List<BaseInstanceState> existing)
        {
            var children = new List<BaseInstanceState>();
            MeasurementPoint.GetChildren(m_context, children);
            children.RemoveAll(child => existing.Exists(known => ReferenceEquals(known, child)));
            return children;
        }

        /// <summary>
        /// Returns the current references that were not present before topology construction.
        /// </summary>
        private List<IReference> OwnedReferences(HashSet<(NodeId, bool, ExpandedNodeId)> existingReferences)
        {
            var references = new List<IReference>();
            MeasurementPoint.GetReferences(m_context, references);
            references.RemoveAll(reference => existingReferences.Contains(
                (reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)));
            return references;
        }

        /// <summary>
        /// Runs one cleanup step and logs a failure without letting it replace the original failure.
        /// </summary>
        private bool TryCleanup(Action step)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (m_logger.IsEnabled(LogLevel.Warning))
                {
                    m_logger.MeasurementTopologyCleanupFailed(exception.ToString());
                }
                return false;
            }
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
                if (!EcmMeasurementProfiles.TryGetMember(name, out _) || readings.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        "The ECM measurement provider returned an unsupported or duplicate profile.");
                }
                readings.Add(name, ValidateReading(reading, name));
            }
            if (readings.Count == 0)
            {
                throw new InvalidOperationException(
                    "The ECM measurement provider returned no supported profile measurements.");
            }
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (EcmProfileDefinition profile in EcmMeasurementProfiles.Declared(readings.Keys))
            {
                foreach (EcmProfileMember member in profile.Members)
                {
                    covered.Add(member.Name);
                }
            }
            foreach (string name in readings.Keys)
            {
                if (!covered.Contains(name))
                {
                    throw new InvalidOperationException(
                        "Every provided measurement must belong to a complete E0, E1, E2, E3, D0 or D1 profile.");
                }
            }
            return readings;
        }

        /// <summary>
        /// Validates one reading against its profile member and returns a detached copy of it.
        /// </summary>
        private EcmMeasurementReading ValidateReading(EcmMeasurementReading reading, string name)
        {
            if (!EcmMeasurementProfiles.TryGetMember(name, out EcmProfileMember? member))
            {
                throw new InvalidOperationException(
                    "The ECM measurement provider returned an unsupported or duplicate profile.");
            }
            EUInformation? ownedUnits = EcmOwnership.Own(
                reading.EngineeringUnits,
                nameof(reading.EngineeringUnits));
            if (reading.DataType != EcmMeasurementProfiles.DataTypeOf(member.Kind) ||
                reading.SourceTimestamp.Kind != DateTimeKind.Utc ||
                float.IsNaN(reading.AccuracyRange.GetValueOrDefault()) ||
                float.IsInfinity(reading.AccuracyRange.GetValueOrDefault()) ||
                reading.AccuracyRange < 0 ||
                (reading.AccuracyRange.HasValue && !member.AllowsRange) ||
                (reading.MeasurementId.HasValue && reading.MeasurementId.Value != member.MeasurementId) ||
                (ownedUnits is not null &&
                    !EcmMeasurementProfiles.HasExpectedEngineeringUnits(member, ownedUnits)) ||
                !IsValidAccuracyMetadata(reading))
            {
                throw new InvalidOperationException(
                    "The ECM measurement provider returned invalid profile metadata.");
            }
            if (StatusCode.IsGood(reading.StatusCode) &&
                !EcmMeasurementProfiles.HasExpectedValue(member.Kind, reading.Value))
            {
                throw new InvalidOperationException(
                    "A good ECM measurement must contain its declared typed value.");
            }
            if (!StatusCode.IsGood(reading.StatusCode) &&
                !reading.Value.IsNull && !EcmMeasurementProfiles.HasExpectedValue(member.Kind, reading.Value))
            {
                throw new InvalidOperationException(
                    "A non-good ECM measurement must contain its declared typed value or a null Variant.");
            }
            return reading with
            {
                Value = EcmOwnership.Own(reading.Value),
                AccuracyDomain = EcmOwnership.Own(reading.AccuracyDomain),
                EngineeringUnits = ownedUnits ?? EcmOwnership.Own(
                    EcmMeasurementProfiles.EngineeringUnitsOf(member),
                    nameof(reading.EngineeringUnits)),
                MeasurementId = member.MeasurementId
            };
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
                    // Persistence and node state are committed; finish notification if the caller cancels.
                    await MeasurementPoint.ClearChangeMasksAsync(
                        m_context, true, CancellationToken.None).ConfigureAwait(false);
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
                    m_logger.MeasurementProviderFailed(exception.ToString());
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
            if (m_statistics != null)
            {
                m_statistics.ResetStatistics.OnCallMethod2Async = null;
            }
        }

        /// <summary>
        /// Marks published values unavailable while retaining their last values and timestamps.
        /// </summary>
        private async ValueTask MarkUnavailableAsync()
        {
            foreach (EnergyMeasurementValueState node in m_measurements.Values)
            {
                node.StatusCode = StatusCodes.BadNoCommunication;
                await node.ClearChangeMasksAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
            }
            Required(MeasurementPoint.ApplicationTag).StatusCode = StatusCodes.BadNoCommunication;
            // Unavailable state is committed and must be reported before the triggering failure escapes.
            await MeasurementPoint.ClearChangeMasksAsync(
                m_context, true, CancellationToken.None).ConfigureAwait(false);
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
        /// Resolves the profile interface NodeId.
        /// </summary>
        private NodeId ProfileType(EcmProfileDefinition profile)
        {
            return ExpandedNodeId.ToNodeId(profile.TypeId, m_context.NamespaceUris);
        }

        /// <summary>
        /// Converts committed reset readings into a shared statistics snapshot after validating them.
        /// </summary>
        private IaStatisticsSnapshot ToStatisticsSnapshot(EcmStatisticsSnapshot reset)
        {
            if (reset == null || reset.StartTime.IsNull || reset.Measurements.Count != m_statisticNames.Count)
            {
                throw new InvalidOperationException("The ECM statistics reset returned an incomplete snapshot.");
            }
            var owned = new Dictionary<string, EcmMeasurementReading>(StringComparer.Ordinal);
            for (int ii = 0; ii < reset.Measurements.Count; ii++)
            {
                EcmMeasurementReading reading = reset.Measurements[ii] ??
                    throw new InvalidOperationException("The ECM statistics reset returned a null reading.");
                string name = reading.BrowseName ?? string.Empty;
                if (!m_statisticNames.Contains(name) || owned.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        "The ECM statistics reset returned an unsupported or duplicate statistic.");
                }
                EcmMeasurementReading detached = ValidateReading(reading, name);
                if (!m_metadata.TryGetValue(name, out EcmMeasurementMetadata? metadata) ||
                    metadata != MetadataOf(detached))
                {
                    throw new InvalidOperationException(
                        "A statistics reset cannot replace published profile measurements or their metadata.");
                }
                owned.Add(name, detached);
            }
            return CreateStatisticsSnapshot(reset.StartTime, owned, CaptureCompanionValues());
        }

        /// <summary>
        /// Builds the companion readings of one refresh in the published companion order, ApplicationTag last.
        /// </summary>
        private DataValue[] CreateCompanionValues(
            Dictionary<string, EcmMeasurementReading> readings,
            string applicationTag)
        {
            var values = new DataValue[m_companionNames.Count + 1];
            for (int ii = 0; ii < m_companionNames.Count; ii++)
            {
                EcmMeasurementReading reading = readings[m_companionNames[ii]];
                values[ii] = new DataValue(reading.Value, reading.StatusCode, reading.SourceTimestamp);
            }
            values[m_companionNames.Count] = new DataValue(
                Variant.From(applicationTag),
                StatusCodes.Good,
                DateTime.UtcNow);
            return values;
        }

        /// <summary>
        /// Captures the currently published companion readings, including timestamps, without provider I/O.
        /// </summary>
        private DataValue[] CaptureCompanionValues()
        {
            var values = new DataValue[m_companionNodes.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                BaseVariableState node = m_companionNodes[ii];
                values[ii] = new DataValue(node.WrappedValue, node.StatusCode, node.Timestamp);
            }
            return values;
        }

        /// <summary>
        /// Builds the shared snapshot in published component order.
        /// </summary>
        private IaStatisticsSnapshot CreateStatisticsSnapshot(
            DateTimeUtc startTime,
            Dictionary<string, EcmMeasurementReading> readings,
            DataValue[] companions)
        {
            var values = new DataValue[m_statisticNames.Count];
            for (int ii = 0; ii < values.Length; ii++)
            {
                EcmMeasurementReading reading = readings[m_statisticNames[ii]];
                values[ii] = new DataValue(reading.Value, reading.StatusCode, reading.SourceTimestamp);
            }
            return new IaStatisticsSnapshot
            {
                StartTime = startTime,
                Values = values,
                CompanionValues = companions
            };
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
            if (m_findNode?.Invoke(reading.AccuracyDomain) is not AccuracyDomainState domain)
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
                    engineeringUnits.DisplayName,
                    engineeringUnits.Description);
        }

        /// <summary>
        /// Gets a required generated child or throws if the published model is incomplete.
        /// </summary>
        /// <typeparam name="T">The generated child type.</typeparam>
        private static T Required<T>(T? value) where T : class
        {
            return value ?? throw new InvalidOperationException("The generated ECM measurement model is incomplete.");
        }

        /// <summary>The owning address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The owning address space's node resolver.</summary>
        private readonly Func<NodeId, NodeState?>? m_findNode;

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

        /// <summary>Whether the first provider snapshot has built the measurement topology.</summary>
        private bool m_topologyBuilt;

        /// <summary>Whether failed topology cleanup prevents another initialization attempt.</summary>
        private bool m_topologyFaulted;

        /// <summary>Statistic member names in shared component order.</summary>
        private readonly List<string> m_statisticNames = [];

        // TODO: Remove the pragma when CA2213 recognizes fields disposed by an asynchronous teardown path.
#pragma warning disable CA2213 // Disposed after admitted operations drain in DisposeAsync.
        /// <summary>The shared IA statistics runtime, present when statistic members are published.</summary>
        private IaStatistics? m_statistics;
#pragma warning restore CA2213

        /// <summary>Non-statistic member names published with the statistics, in companion order.</summary>
        private readonly List<string> m_companionNames = [];

        /// <summary>The companion nodes in fixed publication order, ending with ApplicationTag.</summary>
        private BaseVariableState[] m_companionNodes = [];

        /// <summary>The adapter that feeds validated snapshots to the shared runtime.</summary>
        private StatisticsAdapter? m_adapter;

        /// <summary>Complete profile names exposed by the built measurement topology.</summary>
        private string[] m_profileNames = [];

        /// <summary>Whether disposal has started.</summary>
        private int m_disposed;

        /// <summary>Stable measurement metadata after initial publication.</summary>
        private sealed record EcmMeasurementMetadata(
            ExpandedNodeId DataType,
            EngineeringUnitsMetadata? EngineeringUnits,
            ushort? MeasurementId,
            uint? AccuracyClass,
            NodeId AccuracyDomain,
            float? AccuracyRange);

        /// <summary>Value snapshot of engineering units; it must only hold already detached text.</summary>
        private sealed record EngineeringUnitsMetadata(
            string? NamespaceUri,
            int UnitId,
            LocalizedText DisplayName,
            LocalizedText Description);

        /// <summary>
        /// Adapts the ECM statistics provider to the shared IA statistics runtime.
        /// </summary>
        private sealed class StatisticsAdapter : IIaStatisticsProvider
        {
            /// <summary>
            /// Creates the adapter.
            /// </summary>
            internal StatisticsAdapter(EcmMeasurementBinding owner, IEcmStatisticsProvider provider)
            {
                m_owner = owner;
                m_provider = provider;
            }

            /// <summary>
            /// Gets or sets the validated snapshot that the next shared refresh publishes.
            /// </summary>
            internal IaStatisticsSnapshot? Pending { get; set; }

            /// <inheritdoc/>
            public ValueTask<IaStatisticsSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                IaStatisticsSnapshot snapshot = Pending ??
                    throw new InvalidOperationException("No validated statistics snapshot is pending.");
                return new ValueTask<IaStatisticsSnapshot>(snapshot);
            }

            /// <inheritdoc/>
            public async ValueTask<ServiceResult> AuthorizeResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken = default)
            {
                if (!await EcmPowerAccess.IsAuthorizedAsync(
                    requestContext,
                    m_owner.MeasurementPoint.NodeId,
                    m_owner.m_authorize,
                    m_owner.m_exclusiveAccess,
                    cancellationToken).ConfigureAwait(false))
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }
                return await m_provider.AuthorizeStatisticsResetAsync(requestContext, cancellationToken)
                    .ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask<IaStatisticsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
            {
                EcmStatisticsSnapshot reset = await m_provider.ResetStatisticsAsync(cancellationToken)
                    .ConfigureAwait(false);
                return m_owner.ToStatisticsSnapshot(reset);
            }

            private readonly EcmMeasurementBinding m_owner;
            private readonly IEcmStatisticsProvider m_provider;
        }
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
        public static partial void MeasurementProviderFailed(this ILogger logger, [SensitiveText] string error);

        /// <summary>
        /// Reports a cleanup failure after a failed topology construction; the original failure is rethrown.
        /// </summary>
        [LoggerMessage(
            EventId = EcmServerEventIds.EcmMeasurementBinding + 1,
            Level = LogLevel.Warning,
            Message = "The ECM measurement topology cleanup failed: {Error}")]
        public static partial void MeasurementTopologyCleanupFailed(
            this ILogger logger,
            [SensitiveText] string error);
    }
}
