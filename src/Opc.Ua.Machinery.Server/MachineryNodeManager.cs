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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using ConformanceUnitNames = Opc.Ua.Machinery.Server.ConformanceUnits;
using ServerProfileUris = Opc.Ua.Machinery.Server.ServerProfiles;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Stock DI-based node manager for the compiled OPC 40001 models and
    /// application-owned machine instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One manager serves every configured part of the series: the parts share
    /// one instance tree — a machine's process values, jobs, energy carriers
    /// and results all hang off the same object — so splitting them across
    /// managers would only fragment it. A server that publishes results and
    /// nothing else uses the stand-alone
    /// <see cref="MachineryResultNodeManager"/> instead, which carries no
    /// Device Integration at all.
    /// </para>
    /// <para>
    /// The manager derives from <see cref="DiNodeManager"/>, whose constructor
    /// always registers the DI namespace itself. The provider namespace list is
    /// therefore split in two: the manager list has DI removed and the factory
    /// list adds it back for the announcement.
    /// </para>
    /// </remarks>
    public sealed class MachineryNodeManager :
        DiNodeManager,
        IMachineryNodeIdFactory,
        IMachineryFacetSink
    {
        private readonly MachineryServerOptions m_options;
        private readonly ArrayOf<IMachineryModelProvider> m_providers;
        private readonly MachineryBuildCoordinator m_buildCoordinator;
        private readonly HashSet<MachineryFacet> m_facets = [];
        private readonly Lock m_facetLock = new();
        private bool m_addressSpaceReady;

        /// <summary>
        /// Creates a stock manager with default options and the built-in
        /// provider.
        /// </summary>
        public MachineryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, new MachineryServerOptions())
        {
        }

        /// <summary>
        /// Creates a stock manager with explicit options and the built-in
        /// provider for the configured parts.
        /// </summary>
        public MachineryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            MachineryServerOptions options)
            : this(
                server,
                configuration,
                new IMachineryModelProvider[]
                {
                    new MachineryModelProvider(
                        (options ?? throw new ArgumentNullException(nameof(options))).Parts)
                },
                options)
        {
        }

        /// <summary>
        /// Creates a stock manager with explicitly supplied providers and
        /// options.
        /// </summary>
        public MachineryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ArrayOf<IMachineryModelProvider> providers,
            MachineryServerOptions options,
            IDiPostSetupRunner? postSetupRunner = null)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  MachineryModelProviderUtilities.GetManagerNamespaceUris(providers, options))
        {
            m_options = MachineryModelProviderUtilities.ValidateOptions(options);
            m_providers = MachineryModelProviderUtilities.Normalize(providers, m_options.Parts);
            m_options = MachineryModelProviderUtilities.ValidateOptions(m_options, m_providers);
            m_buildCoordinator = MachineryBuildCoordinator.Get(this);
            RegisterEncodeables(server.Factory, m_options.Parts);
        }

        /// <summary>
        /// Teaches the server the structured types the configured parts put on
        /// the wire. Without this a client's
        /// <c>ResultTransferOptionsDataType</c> arrives as an opaque
        /// <see cref="ExtensionObject"/> and the OPC 40001-101 download refuses
        /// it as an invalid argument.
        /// </summary>
        private static void RegisterEncodeables(
            IEncodeableFactory factory,
            MachineryParts parts)
        {
            IEncodeableFactoryBuilder builder = factory.Builder;
            bool commit = false;
            if (parts.HasFlag(MachineryParts.Result) &&
                !factory.ContainsEncodeableType(
                    Opc.Ua.Machinery.Result.DataTypeIds.ResultDataType))
            {
                builder = builder.AddOpcUaMachineryResult();
                commit = true;
            }
            if (parts.HasFlag(MachineryParts.Jobs) &&
                !factory.ContainsEncodeableType(
                    Opc.Ua.ISA95.JobControl.V2.DataTypeIds.ISA95JobOrderDataType))
            {
                builder = Opc.Ua.ISA95.JobControl.V2.OpcUaISA95JobControlV2Extensions
                    .AddOpcUaISA95JobControlV2(builder);
                commit = true;
            }
            if (commit)
            {
                builder.Commit();
            }
        }

        /// <summary>
        /// Gets the OPC 40001 parts this manager serves.
        /// </summary>
        public MachineryParts Parts => m_options.Parts;

        /// <summary>
        /// Gets the application-owned instance namespace index that Machinery
        /// instances are created in.
        /// </summary>
        /// <remarks>
        /// This is not the same value as
        /// <see cref="DiNodeManager.InstanceNamespaceIndex"/>, which the base
        /// derives from the server configuration. Machinery lets the
        /// application pick its own instance namespace through
        /// <see cref="MachineryServerOptions.InstanceNamespaceUri"/>, and
        /// requires it to be registered.
        /// </remarks>
        public ushort MachineryInstanceNamespaceIndex
        {
            get
            {
                int namespaceIndex = Server.NamespaceUris.GetIndex(
                    m_options.InstanceNamespaceUri);
                if (namespaceIndex < 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The Machinery instance namespace '{0}' is not registered.",
                        m_options.InstanceNamespaceUri);
                }
                return (ushort)namespaceIndex;
            }
        }

        internal int ReservedNodeIdCount =>
            m_buildCoordinator.GetReservedNodeIdCount(MachineryInstanceNamespaceIndex);

        /// <inheritdoc/>
        public override ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new List<QualifiedName>();
                ArrayOf<QualifiedName> inherited = base.ConformanceUnits;
                for (int ii = 0; ii < inherited.Count; ii++)
                {
                    units.Add(inherited[ii]);
                }
                foreach (string unit in BuildMachineryConformanceUnits())
                {
                    units.Add(new QualifiedName(unit));
                }
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public override ArrayOf<string> ServerProfiles
        {
            get
            {
                var profiles = new List<string>();
                ArrayOf<string> inherited = base.ServerProfiles;
                for (int ii = 0; ii < inherited.Count; ii++)
                {
                    profiles.Add(inherited[ii]);
                }
                profiles.AddRange(BuildMachineryServerProfiles());
                return profiles.ToArrayOf();
            }
        }

        /// <summary>
        /// Creates a build context for direct, non-hosted configuration.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The address space or required models are not available.
        /// </exception>
        public IMachineryBuildContext CreateMachineryBuildContext(
            CancellationToken cancellationToken = default)
        {
            if (!m_addressSpaceReady)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Machinery address space is not available yet.");
            }
            return new MachineryBuildContext(this, m_options, cancellationToken);
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                return m_buildCoordinator.ReserveNodeId(
                    this,
                    MachineryInstanceNamespaceIndex,
                    node);
            }
            return node.NodeId;
        }

        /// <inheritdoc/>
        protected override async ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await base.AddPredefinedNodeAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (!node.NodeId.IsNull &&
                    ReferenceEquals(FindPredefinedNode(node.NodeId), node))
                {
                    m_buildCoordinator.ReleaseNodeId(node.NodeId, node);
                }
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            for (int ii = 0; ii < m_providers.Count; ii++)
            {
                m_providers[ii].AddPredefinedNodes(nodes, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            m_addressSpaceReady = true;
            if (m_options.Parts.HasFlag(MachineryParts.BuildingBlocks) && HasMachinesFolder())
            {
                RecordFacet(MachineryFacet.Machines);
            }
            return default;
        }

        void IMachineryFacetSink.RecordFacet(MachineryFacet facet)
        {
            RecordFacet(facet);
        }

        internal void RecordFacet(MachineryFacet facet)
        {
            lock (m_facetLock)
            {
                m_facets.Add(facet);
            }
        }

        internal bool HasFacet(MachineryFacet facet)
        {
            lock (m_facetLock)
            {
                return m_facets.Contains(facet);
            }
        }

        private bool HasMachinesFolder()
        {
            var machinesId = NodeId.Create(
                Opc.Ua.Machinery.Objects.Machines,
                Opc.Ua.Machinery.Namespaces.Machinery,
                Server.NamespaceUris);
            return FindPredefinedNode(machinesId) != null;
        }

        /// <summary>
        /// Reports the OPC 40001 conformance units this server satisfies.
        /// </summary>
        /// <remarks>
        /// The series has two kinds of unit and they are answered
        /// differently. A <em>type-exposure</em> unit — "the server exposes
        /// this type and all its supertypes" — is satisfied by loading the
        /// model, so it follows <see cref="Parts"/>. Every other unit talks
        /// about instances, methods or references, and is reported only once
        /// the build actually materialised and wired the structure behind it.
        /// </remarks>
        private List<string> BuildMachineryConformanceUnits()
        {
            var units = new List<string>();

            if (m_options.Parts.HasFlag(MachineryParts.ProcessValues))
            {
                AddUnit(ConformanceUnitNames.ProcessValuesBaseTypes);
                AddUnit(ConformanceUnitNames.ProcessValuesBaseSetpointType);
                AddUnit(ConformanceUnitNames.ProcessValuesBaseEventTypes);
            }
            if (m_options.Parts.HasFlag(MachineryParts.Result))
            {
                AddUnit(ConformanceUnitNames.ResultTypes);
            }

            Add(MachineryFacet.Machines, ConformanceUnitNames.FindMachines);
            Add(MachineryFacet.MachineIdentification, ConformanceUnitNames.MachineIdentification);
            Add(
                MachineryFacet.ComponentIdentification,
                ConformanceUnitNames.ComponentIdentification);
            Add(
                MachineryFacet.MachineIdentificationWritable,
                ConformanceUnitNames.MachineIdentificationWritable);
            Add(
                MachineryFacet.ComponentIdentificationMandatory,
                ConformanceUnitNames.ComponentIdentificationMandatory);
            Add(
                MachineryFacet.ComponentIdentificationWritable,
                ConformanceUnitNames.ComponentIdentificationWritable);
            Add(MachineryFacet.Components, ConformanceUnitNames.FindComponentsOfMachines);
            Add(
                MachineryFacet.BuildingBlockOrganization,
                ConformanceUnitNames.BuildingBlockOrganization);
            Add(MachineryFacet.Monitoring, ConformanceUnitNames.Monitoring);
            Add(MachineryFacet.MachineryItemState, ConformanceUnitNames.MachineryItemState);
            Add(MachineryFacet.OperationMode, ConformanceUnitNames.OperationMode);
            Add(MachineryFacet.OperationCounters, ConformanceUnitNames.OperationCounter);
            Add(MachineryFacet.LifetimeCounters, ConformanceUnitNames.LifetimeCounter);
            Add(MachineryFacet.MachineryEquipment, ConformanceUnitNames.MachineryEquipment);
            Add(MachineryFacet.Notifications, ConformanceUnitNames.Notifications);

            Add(
                MachineryFacet.ProcessValues,
                ConformanceUnitNames.ProcessValuesAnalogObjectInstances);
            Add(
                MachineryFacet.ProcessValueSetpoint,
                ConformanceUnitNames.ProcessValuesBaseProcessValueSetpoint);
            Add(
                MachineryFacet.ProcessValuePercentage,
                ConformanceUnitNames.ProcessValuesPercentageValue);
            Add(MachineryFacet.ProcessValueLimits, ConformanceUnitNames.ProcessValuesLimitsBase);
            Add(
                MachineryFacet.ProcessValueLimitAlarm,
                ConformanceUnitNames.ProcessValuesLimitsAlarm);
            Add(
                MachineryFacet.ProcessValueLimitAlarm,
                ConformanceUnitNames.ProcessValuesLimitsAlarmObject);
            Add(
                MachineryFacet.ProcessValueDeviationBase,
                ConformanceUnitNames.ProcessValuesDeviationBase);
            Add(
                MachineryFacet.ProcessValueDeviationAlarm,
                ConformanceUnitNames.ProcessValuesDeviationAlarm);
            Add(
                MachineryFacet.ProcessValueDeviationAlarm,
                ConformanceUnitNames.ProcessValuesDeviationAlarmObject);
            Add(
                MachineryFacet.ProcessValueDeviationAutoAdjustment,
                ConformanceUnitNames.ProcessValuesDeviationAutoAdjustment);
            Add(
                MachineryFacet.ProcessValueDeviationSensitivity,
                ConformanceUnitNames.ProcessValuesDeviationSensitivity);
            Add(MachineryFacet.ProcessValueStatus, ConformanceUnitNames.ProcessValuesMonitoring);
            Add(
                MachineryFacet.ProcessValueStatus,
                ConformanceUnitNames.ProcessValuesAlarmSuppression);
            Add(
                MachineryFacet.ProcessValuesSimulation,
                ConformanceUnitNames.ProcessValuesSimulation);
            Add(
                MachineryFacet.ProcessValuesDeviceObject,
                ConformanceUnitNames.ProcessValuesDeviceObject);
            Add(
                MachineryFacet.ProcessValuesSimpleDeviceInfo,
                ConformanceUnitNames.ProcessValuesSimpleDeviceInfo);
            Add(
                MachineryFacet.ZeroPointAdjustment,
                ConformanceUnitNames.ProcessValuesZeroPointAdjustmentEvents);

            Add(MachineryFacet.JobManagement, ConformanceUnitNames.JobManagementBase);
            Add(
                MachineryFacet.JobManagement,
                ConformanceUnitNames.JobManagementMinimumStringLength);
            Add(MachineryFacet.JobResults, ConformanceUnitNames.JobManagementResultBase);
            for (int ii = 0;
                ii < ConformanceUnitNames.JobPredefinedParameters.Length;
                ii++)
            {
                Add(
                    MachineryFacet.JobPredefinedParameters,
                    ConformanceUnitNames.JobPredefinedParameters[ii]);
            }

            Add(MachineryFacet.EnergyBaseStructure, ConformanceUnitNames.EnergyBaseStructure);
            Add(MachineryFacet.EnergyMainGrouping, ConformanceUnitNames.EnergyMainGrouping);
            Add(
                MachineryFacet.EnergyNonElectrical,
                ConformanceUnitNames.EnergyNonElectricalBase);
            Add(
                MachineryFacet.EnergyMassFlow,
                ConformanceUnitNames.EnergyNonElectricalMassFlow);
            Add(
                MachineryFacet.EnergyVolumeFlow,
                ConformanceUnitNames.EnergyNonElectricalVolumeFlow);
            Add(MachineryFacet.EnergyContains, ConformanceUnitNames.EnergyContains);

            // The binder publishes all five ResultManagementType methods
            // together as soon as a store is bound, so the four method units
            // stand or fall with the object.
            Add(MachineryFacet.ResultManagement, ConformanceUnitNames.ResultGetLatestResult);
            Add(MachineryFacet.ResultManagement, ConformanceUnitNames.ResultGetResultById);
            Add(MachineryFacet.ResultManagement, ConformanceUnitNames.ResultGetResultsFiltered);
            Add(MachineryFacet.ResultManagement, ConformanceUnitNames.ResultAcknowledgeResults);
            Add(MachineryFacet.ResultVariables, ConformanceUnitNames.ResultVariables);
            Add(MachineryFacet.ResultFiles, ConformanceUnitNames.ResultFiles);
            Add(MachineryFacet.ResultEvents, ConformanceUnitNames.ResultEvents);
            Add(
                MachineryFacet.ResultPredefinedMetaData,
                ConformanceUnitNames.ResultPredefinedResultMetaData);
            return units;

            void Add(MachineryFacet facet, string unit)
            {
                if (HasFacet(facet))
                {
                    AddUnit(unit);
                }
            }

            void AddUnit(string unit)
            {
                if (!units.Contains(unit))
                {
                    units.Add(unit);
                }
            }
        }

        /// <summary>
        /// Reports the OPC 40001 server facets this server satisfies.
        /// </summary>
        /// <remarks>
        /// A facet is advertised only when every conformance unit the
        /// specification marks mandatory for it — and that this library is
        /// responsible for — was materialised and wired. The base-server
        /// units each facet also inherits (address space, view, attribute,
        /// method and event-subscription) are the stack's to report and are
        /// already in <c>base.ServerProfiles</c>.
        /// </remarks>
        private List<string> BuildMachineryServerProfiles()
        {
            var profiles = new List<string>();

            if (HasFacet(MachineryFacet.MachineIdentification) &&
                HasFacet(MachineryFacet.Machines))
            {
                profiles.Add(ServerProfileUris.MachineIdentification);
            }
            if (HasFacet(MachineryFacet.ComponentIdentification) &&
                HasFacet(MachineryFacet.Components))
            {
                profiles.Add(ServerProfileUris.ComponentIdentification);
            }

            bool organized = HasFacet(MachineryFacet.BuildingBlockOrganization);
            if (organized && HasFacet(MachineryFacet.MachineryItemState))
            {
                profiles.Add(ServerProfileUris.State);
            }
            if (organized && HasFacet(MachineryFacet.OperationCounters))
            {
                profiles.Add(ServerProfileUris.OperationCounter);
            }
            if (organized && HasFacet(MachineryFacet.LifetimeCounters))
            {
                profiles.Add(ServerProfileUris.LifetimeCounter);
            }
            bool monitoring = organized && HasFacet(MachineryFacet.Monitoring);
            if (monitoring)
            {
                profiles.Add(ServerProfileUris.Monitoring);
            }
            if (organized && HasFacet(MachineryFacet.MachineryEquipment))
            {
                profiles.Add(ServerProfileUris.MachineryEquipment);
            }
            if (organized && HasFacet(MachineryFacet.Notifications))
            {
                profiles.Add(ServerProfileUris.Notifications);
            }

            AddProcessValueProfiles(profiles);

            if (HasFacet(MachineryFacet.JobManagement))
            {
                profiles.Add(ServerProfileUris.JobManagementBase);
            }

            // OPC 40001-4 composes the Machinery Monitoring Server Facet, so
            // the energy facet cannot stand on its own.
            if (monitoring && HasFacet(MachineryFacet.EnergyBaseStructure))
            {
                profiles.Add(ServerProfileUris.EnergyBase);
            }

            if (HasFacet(MachineryFacet.ResultManagement))
            {
                profiles.Add(ServerProfileUris.ResultSimpleTransfer);
                if (HasFacet(MachineryFacet.ResultEvents))
                {
                    profiles.Add(ServerProfileUris.ResultTransfer);
                }
            }
            if (HasFacet(MachineryFacet.ResultVariables))
            {
                profiles.Add(ServerProfileUris.ResultTransferVariables);
            }
            return profiles;
        }

        private void AddProcessValueProfiles(List<string> profiles)
        {
            if (!m_options.Parts.HasFlag(MachineryParts.ProcessValues) ||
                !HasFacet(MachineryFacet.ProcessValues))
            {
                return;
            }
            profiles.Add(ServerProfileUris.ProcessValuesBase);

            if (HasFacet(MachineryFacet.ProcessValueSetpoint))
            {
                profiles.Add(ServerProfileUris.ProcessValuesSetpoint);
            }
            if (HasFacet(MachineryFacet.ProcessValuePercentage))
            {
                profiles.Add(ServerProfileUris.ProcessValuesPercentageValue);
            }
            if (HasFacet(MachineryFacet.ZeroPointAdjustment))
            {
                profiles.Add(ServerProfileUris.ProcessValuesZeroPointAdjustmentBase);

                // Every instance that publishes the method also reports the
                // event, so the two facets are inseparable here.
                profiles.Add(ServerProfileUris.ProcessValuesZeroPointAdjustmentEvents);
            }

            bool status = HasFacet(MachineryFacet.ProcessValueStatus);
            if (HasFacet(MachineryFacet.ProcessValueLimits))
            {
                profiles.Add(ServerProfileUris.ProcessValuesLimitsBase);
                if (HasFacet(MachineryFacet.ProcessValueLimitAlarm))
                {
                    profiles.Add(ServerProfileUris.ProcessValuesLimitsAlarm);
                }
                if (status)
                {
                    profiles.Add(ServerProfileUris.ProcessValuesLimitsMonitoring);
                    profiles.Add(ServerProfileUris.ProcessValuesLimitsAlarmSuppression);
                }
            }
            if (HasFacet(MachineryFacet.ProcessValueDeviationBase))
            {
                profiles.Add(ServerProfileUris.ProcessValuesDeviationBase);
                if (HasFacet(MachineryFacet.ProcessValueDeviationAlarm))
                {
                    profiles.Add(ServerProfileUris.ProcessValuesDeviationAlarm);
                }
                if (HasFacet(MachineryFacet.ProcessValueDeviationAutoAdjustment))
                {
                    profiles.Add(ServerProfileUris.ProcessValuesDeviationAutoAdjustment);
                }
                if (status)
                {
                    profiles.Add(ServerProfileUris.ProcessValuesDeviationMonitoring);
                    profiles.Add(ServerProfileUris.ProcessValuesDeviationAlarmSuppression);
                }
            }
        }
    }
}
