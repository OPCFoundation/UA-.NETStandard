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
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsClient
    {
        /// <summary>
        /// Observes inherited machine/program monitoring and selected native geometry, loading, qualification,
        /// calibration and accessory samples through the shared bounded subscription collector.
        /// More than 256 resolved variables rejects setup instead of silently selecting a subset.
        /// </summary>
        public ValueTask<MachineToolDataObservation> ObserveDataAsync(
            NodeId nodeId, MachineToolObservationOptions? options = null, CancellationToken cancellationToken = default)
        {
            return ObserveAsync(nodeId, options, ResolveObservationAsync,
                (nodes, selected, ct) => m_inherited.ObserveValuesAsync(nodes, selected, ct), cancellationToken);
        }

        /// <summary>
        /// Includes native resolution in the requested window, while reusing the shared collector's normal expiry.
        /// </summary>
        /// <typeparam name="T">The shared data or event observation result.</typeparam>
        private async ValueTask<T> ObserveAsync<T>(
            NodeId node, MachineToolObservationOptions? options,
            Func<GMSTypeClient, CancellationToken, ValueTask<ArrayOf<NodeId>>>? resolve,
            Func<ArrayOf<NodeId>, MachineToolObservationOptions, CancellationToken, ValueTask<T>> observe,
            CancellationToken cancellationToken)
        {
            MachineToolObservationOptions selected = options ?? new MachineToolObservationOptions();
            selected.Validate();
            using Operation operation = Begin(cancellationToken);
            long started = m_timeProvider.GetTimestamp();
            using CancellationTokenSource setupTimeout = m_timeProvider.CreateCancellationTokenSource(selected.Window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, setupTimeout.Token);
            ArrayOf<NodeId> nodes;
            try
            {
                GMSTypeClient root = await RequireGmsAsync(node, linked.Token).ConfigureAwait(false);
                nodes = resolve == null ? [] : await resolve(root, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (
                setupTimeout.IsCancellationRequested && !operation.Token.IsCancellationRequested)
            {
                throw new TimeoutException("The GMS observation window expired during native resolution.", error);
            }
            catch (ServiceResultException error) when (linked.IsCancellationRequested && IsCancellation(error))
            {
                if (operation.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException("The GMS observation was cancelled.", error, operation.Token);
                }
                throw new TimeoutException("The GMS observation window expired during native resolution.", error);
            }
            TimeSpan remaining = selected.Window - m_timeProvider.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException("The GMS observation window expired during native resolution.");
            }
            try
            {
                return await observe(nodes, selected with { Window = remaining }, operation.Token)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException error) when (operation.Token.IsCancellationRequested && IsCancellation(error))
            {
                throw new OperationCanceledException("The GMS observation was cancelled.", error, operation.Token);
            }
        }

        /// <summary>
        /// Recognizes transport cancellation statuses without masking unrelated service errors.
        /// </summary>
        private static bool IsCancellation(ServiceResultException error) =>
            error.StatusCode == StatusCodes.BadRequestInterrupted ||
            error.StatusCode == StatusCodes.BadRequestCancelledByClient;

        /// <summary>
        /// Selects actual variable identifiers obtained through generated native navigation and bounded typed reads.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolveObservationAsync(GMSTypeClient root, CancellationToken ct)
        {
            var nodes = new HashSet<NodeId>();
            GMSMonitoringTypeClient monitoring = await root.GetMonitoringAsync(Telemetry, ct).ConfigureAwait(false) ??
                throw Missing(MachineTool.BrowseNames.Monitoring);
            MachineOperationMonitoringTypeClient machine = await monitoring.GetMachineToolAsync(Telemetry, ct)
                .ConfigureAwait(false) ?? throw Missing(MachineTool.BrowseNames.MachineTool);
            AddNodes(await ResolveFieldsAsync(machine.ObjectId,
                [MachineName(MachineTool.BrowseNames.OperationMode), MachineName(MachineTool.BrowseNames.FeedOverride),
                    MachineName(MachineTool.BrowseNames.IsWarmUp)], ct).ConfigureAwait(false));
            ProductionTypeClient production = await root.GetProductionAsync(Telemetry, ct).ConfigureAwait(false) ??
                throw Missing(MachineTool.BrowseNames.Production);
            ProductionActiveProgramTypeClient program = await production.GetActiveProgramAsync(Telemetry, ct)
                .ConfigureAwait(false) ?? throw Missing(MachineTool.BrowseNames.ActiveProgram);
            AddNodes(await ResolveFieldsAsync(
                program.ObjectId, [MachineName(MachineTool.BrowseNames.Name)], ct).ConfigureAwait(false));
            ProductionProgramStateMachineTypeClient state = await program.GetStateAsync(Telemetry, ct)
                .ConfigureAwait(false) ?? throw Missing(MachineTool.BrowseNames.State);
            AddNodes(await ResolveFieldsAsync(state.ObjectId,
                [new QualifiedName(Opc.Ua.BrowseNames.CurrentState)], ct).ConfigureAwait(false));
            GmsSystemReadSnapshot system = await ReadSnapshotAsync(root.ObjectId, ct).ConfigureAwait(false);
            Add(system.Identification.SubDeviceClass);
            Add(system.Identification.Workspace);
            if (system.Loading is { } loading)
            {
                Add(loading.LoadStatus);
                Add(loading.IsInLoadingPosition);
            }
            if (system.ToolMonitoring is { } tools)
            {
                Add(tools.ActiveTool);
            }
            GmsCalibrationReadSnapshot? calibration = await ReadCalibrationAsync(root.ObjectId, ct)
                .ConfigureAwait(false);
            if (calibration != null)
            {
                Add(calibration.PredictedTime);
                Add(calibration.Calibrated);
                Add(calibration.DateOfCalibration);
                Add(calibration.CalibrationInterval);
                Add(calibration.CalibrationPreptime);
                Add(calibration.CalibrationCertificate);
            }
            GmsEquipmentReadSnapshot equipment = await ReadEquipmentAsync(root.ObjectId, ct).ConfigureAwait(false);
            foreach (GmsSensorReadSnapshot sensor in equipment.Sensors)
            {
                AddSensor(sensor);
            }
            foreach (GmsAdditionalSensorReadSnapshot sensor in equipment.AdditionalSensors)
            {
                nodes.Add(sensor.NodeId);
            }
            foreach (GmsAccessoryReadSnapshot accessory in equipment.Accessories)
            {
                Add(accessory.IsAvailable);
                Add(accessory.IsIntegrated);
                Add(accessory.NumberOfAxes);
            }
            if (nodes.Count > 256)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The selected GMS observation exceeds 256 actual variables.");
            }
            return new List<NodeId>(nodes).ToArrayOf();

            void Add(in GmsVariableReading value)
            {
                if (value.IsPresent)
                {
                    nodes.Add(value.NodeId);
                }
            }

            void AddNodes(ArrayOf<NodeId> values)
            {
                foreach (NodeId value in values)
                {
                    if (!value.IsNull)
                    {
                        nodes.Add(value);
                    }
                }
            }

            void AddSensor(GmsSensorReadSnapshot sensor)
            {
                Add(sensor.Class);
                Add(sensor.IsQualifiedStatus);
                foreach (MachineToolToolLifeReadSnapshot life in sensor.Inherited.Life)
                {
                    if (life.BrowseName == Name(BrowseNames.Qualified))
                    {
                        nodes.Add(life.NodeId);
                    }
                }
                foreach (GmsSensorReadSnapshot child in sensor.Sensors)
                {
                    AddSensor(child);
                }
            }
        }
    }
}
