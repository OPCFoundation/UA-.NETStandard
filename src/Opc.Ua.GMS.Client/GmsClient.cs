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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Bounded native GMS workflows over generated proxies and the shared MachineTool/result clients.
    /// No method starts a measurement, calibration, job or correction.
    /// </summary>
    public sealed partial class GmsClient : IAsyncDisposable
    {
        /// <summary>
        /// Creates a facade without taking ownership of an application-supplied session.
        /// </summary>
        public GmsClient(
            ISession session, ITelemetryContext telemetry, GmsClientOptions? options = null,
            TimeProvider? timeProvider = null)
            : this(session, telemetry, false, options, timeProvider)
        {
        }

        /// <summary>
        /// Creates an owning facade only for a session opened by the explicit client factory.
        /// </summary>
        internal GmsClient(
            ISession session, ITelemetryContext telemetry, bool ownsSession, GmsClientOptions? options,
            TimeProvider? timeProvider)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new GmsClientOptions();
            m_options.Validate();
            m_ownsSession = ownsSession;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            Session.MessageContext.Factory.Builder.AddGmsTypeSystem().Commit();
            m_inherited = new MachineToolClient(session, telemetry, timeProvider);
            if (Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) >= 0)
            {
                var fields = new List<ArrayOf<QualifiedName>>();
                foreach (QualifiedName[] path in SensorWarningAlarmTypeRecord.Decoder.GetStandardFields(
                    Session.NamespaceUris))
                {
                    fields.Add(path.ToArrayOf());
                }
                m_inherited.WithEventDecoder(ObjectTypeIds.SensorWarningAlarmType, fields.ToArrayOf(),
                    static values => SensorWarningAlarmTypeRecord.Decoder.Decode(values.ToArray()!));
            }
        }

        /// <summary>
        /// Gets the underlying session, owned by the caller except when opened by GmsClientFactory.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the shared diagnostic context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Discovers actual GMS subtypes through the shared continuation-owning Machinery/DI discovery workflow.
        /// </summary>
        public async ValueTask<ArrayOf<GmsDeviceEntry>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            using Operation operation = Begin(cancellationToken);
            if (Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) < 0)
            {
                return [];
            }
            ArrayOf<MachineToolDeviceEntry> machines = await m_inherited.DiscoverAsync(
                m_options.MaximumElements, operation.Token).ConfigureAwait(false);
            var result = new List<GmsDeviceEntry>();
            for (int index = 0; index < machines.Count; index++)
            {
                MachineToolDeviceEntry entry = machines[index];
                if (await IsTypeAsync(entry.TypeDefinitionId, ObjectTypeIds.GMSType, operation.Token)
                    .ConfigureAwait(false))
                {
                    result.Add(new GmsDeviceEntry(
                        entry.NodeId, entry.TypeDefinitionId, entry.BrowseName, entry.DisplayName));
                }
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Gets the generated advanced GMS proxy without replacing its declared type or protocol.
        /// Normal typed reads additionally validate the actual instance's type definition.
        /// </summary>
        public GMSTypeClient GetProxy(NodeId nodeId)
        {
            EnsureActive();
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A GMS instance identifier is required.", nameof(nodeId));
            }
            if (Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The endpoint has no GMS model.");
            }
            return new GMSTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Composes the existing result client on this session and actual GMS root.
        /// The returned workflow does not acquire another connection or transfer protocol implementation.
        /// </summary>
        public GmsResultClient GetResults(NodeId nodeId, GmsResultLimits? limits = null)
        {
            _ = GetProxy(nodeId);
            return new GmsResultClient(Session, nodeId, Telemetry, limits);
        }

        /// <summary>
        /// Reads mandatory inherited monitoring and raw native identification, loading and active-tool fields.
        /// </summary>
        public async ValueTask<GmsSystemReadSnapshot> ReadSnapshotAsync(
            NodeId nodeId, CancellationToken cancellationToken = default)
        {
            using Operation operation = Begin(cancellationToken);
            CancellationToken ct = operation.Token;
            var budget = new ReadBudget(m_options, Session.MessageContext);
            GMSTypeClient root = await RequireGmsAsync(nodeId, ct).ConfigureAwait(false);
            MachineToolReadSnapshot inherited = await m_inherited.ReadSnapshotAsync(nodeId, ct).ConfigureAwait(false);
            budget.Capture([inherited.OperationMode, inherited.FeedOverride, inherited.IsWarmUp,
                inherited.ActiveProgramName, inherited.ActiveProgramState]);
            GMSIdentificationTypeClient identity = await root.GetIdentificationAsync(Telemetry, ct)
                .ConfigureAwait(false) ?? throw Missing(BrowseNames.GMSIdentificationType);
            ArrayOf<GmsVariableReading> identification = await ReadFieldsAsync(identity.ObjectId,
                [DiName(Opc.Ua.Di.BrowseNames.Manufacturer), DiName(Opc.Ua.Di.BrowseNames.SerialNumber),
                    DiName(Opc.Ua.Di.BrowseNames.ProductInstanceUri), Name(BrowseNames.SubDeviceClass),
                    Name(BrowseNames.Workspace)], budget, ct).ConfigureAwait(false);
            Require(identification[0], Opc.Ua.Di.BrowseNames.Manufacturer);
            Require(identification[1], Opc.Ua.Di.BrowseNames.SerialNumber);
            Require(identification[2], Opc.Ua.Di.BrowseNames.ProductInstanceUri);
            GMSMonitoringTypeClient monitoring = await root.GetMonitoringAsync(Telemetry, ct).ConfigureAwait(false) ??
                throw Missing(MachineTool.BrowseNames.Monitoring);
            LoadingMonitoringTypeClient? loading = await monitoring.GetLoadingMonitoringAsync(Telemetry, ct)
                .ConfigureAwait(false);
            GmsLoadingReadSnapshot? loadingRead = null;
            if (loading != null)
            {
                ArrayOf<GmsVariableReading> values = await ReadFieldsAsync(loading.ObjectId,
                    [Name(BrowseNames.LoadStatus), Name(BrowseNames.IsInLoadingPosition)], budget, ct)
                    .ConfigureAwait(false);
                Require(values[0], BrowseNames.LoadStatus);
                loadingRead = new GmsLoadingReadSnapshot(loading.ObjectId, values[0], values[1]);
            }
            ToolMonitoringTypeClient? tools = await monitoring.GetToolMonitoringAsync(Telemetry, ct)
                .ConfigureAwait(false);
            GmsToolMonitoringReadSnapshot? toolsRead = null;
            if (tools != null)
            {
                ArrayOf<GmsVariableReading> values = await ReadFieldsAsync(tools.ObjectId,
                    [Name(BrowseNames.ActiveTool)], budget, ct).ConfigureAwait(false);
                toolsRead = new GmsToolMonitoringReadSnapshot(tools.ObjectId, values[0]);
            }
            return new GmsSystemReadSnapshot(nodeId, inherited,
                new GmsIdentificationReadSnapshot(identity.ObjectId, identification[0], identification[1],
                    identification[2], identification[3], identification[4]), loadingRead, toolsRead);
        }

        /// <summary>
        /// Reads a native calibration descendant from the actual shared prognosis inventory, including vendor subtypes.
        /// Returns null only when it is not modeled; bad mandatory samples remain present in the result.
        /// </summary>
        public async ValueTask<GmsCalibrationReadSnapshot?> ReadCalibrationAsync(
            NodeId nodeId, CancellationToken cancellationToken = default)
        {
            using Operation operation = Begin(cancellationToken);
            CancellationToken ct = operation.Token;
            _ = await RequireGmsAsync(nodeId, ct).ConfigureAwait(false);
            MachineToolPrognosisReadSnapshot prognoses = await m_inherited.ReadPrognosesAsync(
                nodeId, m_options.MaximumElements, ct).ConfigureAwait(false);
            var budget = new ReadBudget(m_options, Session.MessageContext);
            GmsCalibrationReadSnapshot? result = null;
            for (int index = 0; index < prognoses.Entries.Count; index++)
            {
                MachineToolPrognosisReadEntry entry = prognoses.Entries[index];
                if (!await IsTypeAsync(entry.TypeDefinitionId, ObjectTypeIds.CalibrationPrognosisType, ct)
                    .ConfigureAwait(false))
                {
                    continue;
                }
                if (result != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated,
                        "The GMS instance exposes more than one native Calibration forecast.");
                }
                var proxy = new CalibrationPrognosisTypeClient(Session, entry.NodeId, Telemetry);
                ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(proxy.ObjectId,
                    [MachineName(MachineTool.BrowseNames.PredictedTime), Name(BrowseNames.Calibrated),
                        Name(BrowseNames.DateOfCalibration), Name(BrowseNames.CalibrationInterval),
                        Name(BrowseNames.CalibrationPreptime), Name(BrowseNames.CalibrationCertificate)], budget, ct)
                    .ConfigureAwait(false);
                Require(fields[0], MachineTool.BrowseNames.PredictedTime);
                Require(fields[1], BrowseNames.Calibrated);
                Require(fields[2], BrowseNames.DateOfCalibration);
                result = new GmsCalibrationReadSnapshot(entry.NodeId, entry.TypeDefinitionId,
                    fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
            }
            return result;
        }

        /// <summary>
        /// Observes generated native sensor-warning and inherited events through the existing bounded collector.
        /// </summary>
        public ValueTask<MachineToolEventObservation> ObserveEventsAsync(
            NodeId nodeId, MachineToolObservationOptions? options = null, CancellationToken cancellationToken = default)
        {
            return ObserveAsync(nodeId, options, null,
                (_, selected, ct) => m_inherited.ObserveEventsAsync(nodeId, selected, ct), cancellationToken);
        }

        /// <summary>
        /// Performs bounded standard ConditionRefresh and decodes native SensorWarningAlarmType fields.
        /// It never acknowledges, enables or clears conditions implicitly.
        /// </summary>
        public ValueTask<MachineToolEventObservation> ObserveConditionsAsync(
            NodeId nodeId, MachineToolObservationOptions? options = null, CancellationToken cancellationToken = default)
        {
            return ObserveAsync(nodeId, options, null,
                (_, selected, ct) => m_inherited.ObserveConditionsAsync(nodeId, selected, ct), cancellationToken);
        }

        /// <summary>
        /// Refuses a base MachineTool or unrelated same-namespace node as a native GMS instance.
        /// </summary>
        private async ValueTask<GMSTypeClient> RequireGmsAsync(NodeId nodeId, CancellationToken ct)
        {
            GMSTypeClient proxy = GetProxy(nodeId);
            INode node = await Session.NodeCache.GetNodeAsync(nodeId, ct).ConfigureAwait(false);
            NodeId type = ExpandedNodeId.ToNodeId(node.TypeDefinitionId, Session.NamespaceUris);
            if (node.NodeClass != NodeClass.Object || !await IsTypeAsync(type, ObjectTypeIds.GMSType, ct)
                .ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "The requested instance is not an actual GMSType or subtype.");
            }
            return proxy;
        }

        /// <summary>
        /// Resolves vendor inheritance through the standard asynchronous type cache.
        /// </summary>
        private ValueTask<bool> IsTypeAsync(NodeId type, ExpandedNodeId expected, CancellationToken ct) =>
            Session.NodeCache.IsTypeOfAsync(type, ExpandedNodeId.ToNodeId(expected, Session.NamespaceUris), ct);

        /// <summary>The shared inherited workflows and bounded subscription implementation.</summary>
        private readonly MachineToolClient m_inherited;

        /// <summary>The immutable per-workflow resource limits.</summary>
        private readonly GmsClientOptions m_options;

        /// <summary>Whether this facade closes a session explicitly opened by its factory.</summary>
        private readonly bool m_ownsSession;

        /// <summary>The injected clock used to bound preparation as well as subscription observation.</summary>
        private readonly TimeProvider m_timeProvider;
    }
}
