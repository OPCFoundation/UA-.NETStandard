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
using Opc.Ua.Client;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Binds typed common-capability clients to an existing or managed session.
    /// </summary>
    /// <remarks>
    /// OPC 40083 has no universal machine root. Obtain capability NodeIds through the
    /// concrete companion client's generated child accessors or the server's browse paths.
    /// The model assembly also exposes generated proxies for every remaining common type.
    /// </remarks>
    public sealed partial class PlasticsRubberClient
    {
        /// <summary>
        /// Creates a facade and registers the common structured encodings with its session.
        /// </summary>
        public PlasticsRubberClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (!session.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.PIDParametersDataType))
            {
                session.MessageContext.Factory.Builder.AddOpcUaPlasticsRubber().Commit();
            }
        }

        /// <summary>
        /// Gets the caller's session, without acquiring another identity or connection.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the shared telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the connected namespace table contains the common model.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.PlasticsRubber) >= 0;

        /// <summary>
        /// Binds the typed start/stop capability.
        /// </summary>
        public StartDeviceTypeClient StartDevice(NodeId nodeId)
        {
            return new StartDeviceTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds a monitored parameter and its typed values, optional settings and reset method.
        /// </summary>
        public MonitoredParameterTypeClient MonitoredParameter(NodeId nodeId)
        {
            return new MonitoredParameterTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds a controlled parameter, including its generated closed-loop child accessor.
        /// </summary>
        public ControlledParameterTypeClient ControlledParameter(NodeId nodeId)
        {
            return new ControlledParameterTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds PID settings and self-optimization methods.
        /// </summary>
        public ClosedLoopControlTypeClient ClosedLoopControl(NodeId nodeId)
        {
            return new ClosedLoopControlTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds maintenance information and reset.
        /// </summary>
        public MaintenanceTypeClient Maintenance(NodeId nodeId)
        {
            return new MaintenanceTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds diagnostics status and start/stop methods.
        /// </summary>
        public DiagnosticsTypeClient Diagnostics(NodeId nodeId)
        {
            return new DiagnosticsTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds device identification through the model's DI-derived type.
        /// </summary>
        public IdentificationTypeClient Identification(NodeId nodeId)
        {
            return new IdentificationTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds machine information and supported logbook events.
        /// </summary>
        public MachineInformationTypeClient MachineInformation(NodeId nodeId)
        {
            return new MachineInformationTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds naming, location, device-clock and page-retrieval operations.
        /// </summary>
        public MachineConfigurationTypeClient MachineConfiguration(NodeId nodeId)
        {
            return new MachineConfigurationTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds the machine state, users and sleep-mode requests.
        /// </summary>
        public MachineStatusTypeClient MachineStatus(NodeId nodeId)
        {
            return new MachineStatusTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds MES configuration and its standstill-reason catalogue.
        /// </summary>
        public MachineMESConfigurationTypeClient MesConfiguration(NodeId nodeId)
        {
            return new MachineMESConfigurationTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds MES messages, standstill status and the production-control child.
        /// </summary>
        public MachineMESStatusTypeClient MesStatus(NodeId nodeId)
        {
            return new MachineMESStatusTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds production release, automatic-run and watchdog operations.
        /// </summary>
        public ProductionControlTypeClient ProductionControl(NodeId nodeId)
        {
            return new ProductionControlTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds common job lists and their active/preparation children.
        /// </summary>
        public JobsTypeClient Jobs(NodeId nodeId)
        {
            return new JobsTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds active job commands without substituting an ISA-95 job protocol.
        /// </summary>
        public ActiveJobValuesTypeClient ActiveJob(NodeId nodeId)
        {
            return new ActiveJobValuesTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds cyclic job counters and resets.
        /// </summary>
        public ActiveCyclicJobValuesTypeClient ActiveCyclicJob(NodeId nodeId)
        {
            return new ActiveCyclicJobValuesTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds common dataset metadata, status and generated transfer-child accessors.
        /// </summary>
        public ProductionDatasetManagementTypeClient ProductionDatasets(NodeId nodeId)
        {
            return new ProductionDatasetManagementTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds the material catalogue's typed add/remove operations.
        /// </summary>
        public MaterialListTypeClient Materials(NodeId nodeId)
        {
            return new MaterialListTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds a drive and its energy, speed, torque, maintenance and start capabilities.
        /// </summary>
        public DriveTypeClient Drive(NodeId nodeId)
        {
            return new DriveTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds a measuring device and its inherited parameter capabilities.
        /// </summary>
        public MeasuringDeviceTypeClient MeasuringDevice(NodeId nodeId)
        {
            return new MeasuringDeviceTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds common energy measurements without imposing a particular engineering unit.
        /// </summary>
        public EnergyTypeClient Energy(NodeId nodeId)
        {
            return new EnergyTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Binds a temperature zone and its signed deviation limits.
        /// </summary>
        public TemperatureZoneTypeClient TemperatureZone(NodeId nodeId)
        {
            return new TemperatureZoneTypeClient(Session, RequireInstance(nodeId), Telemetry);
        }

        /// <summary>
        /// Rejects empty identities and a server that does not publish the common model.
        /// </summary>
        private NodeId RequireInstance(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("An instance NodeId is required.", nameof(nodeId));
            }
            if (!IsSupported)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The session does not publish the OPC 40083 namespace.");
            }
            return nodeId;
        }
    }
}
