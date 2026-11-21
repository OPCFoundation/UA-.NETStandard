// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Explicit common-capability authorities used by an extrusion device.
    /// Every request retains the actual target NodeId, so one adapter may route several real capabilities.
    /// </summary>
    public sealed class ExtrusionCommonProviders
    {
        /// <summary>
        /// Gets or sets the original-caller authorization policy; there is no implicit allow-all policy.
        /// </summary>
        public IPlasticsRubberAccessPolicy? AccessPolicy { get; set; }

        /// <summary>
        /// Gets or sets the common start/stop and start-interlock authority.
        /// </summary>
        public IStartDeviceProvider? StartDevices { get; set; }

        /// <summary>
        /// Gets or sets common maintenance-reset authority.
        /// </summary>
        public IMaintenanceProvider? Maintenance { get; set; }

        /// <summary>
        /// Gets or sets common monitored/controlled parameter and calibration authority.
        /// </summary>
        public IMonitoredParameterProvider? Parameters { get; set; }

        /// <summary>
        /// Gets or sets common PID/self-optimization authority.
        /// </summary>
        public IClosedLoopControlProvider? ClosedLoopControl { get; set; }

        /// <summary>
        /// Gets or sets device naming, clock and page-rendering authority.
        /// </summary>
        public IMachineConfigurationProvider? MachineConfiguration { get; set; }

        /// <summary>
        /// Gets or sets MES message presentation authority on an extrusion line.
        /// </summary>
        public IMesMessageProvider? MesMessages { get; set; }

        /// <summary>
        /// Gets or sets material-catalogue authority. A read-only list does not require a provider.
        /// Selected AddMaterial or RemoveMaterialById methods require this explicit authority.
        /// </summary>
        public IMaterialListProvider? Materials { get; set; }

        /// <summary>
        /// Gets or sets the engineering unit the MaterialList publishes as its DensityUnit and in which
        /// every material density is given. OPC 40083 declares DensityUnit without a value, so a list
        /// bound to <see cref="Materials"/> or with selected mutation methods requires this unit.
        /// </summary>
        public EUInformation? MaterialDensityUnit { get; set; }

        /// <summary>
        /// Gets or sets the instance-specific production-dataset persistence/activation authority.
        /// </summary>
        public IProductionDatasetProvider? ProductionDatasets { get; set; }

        /// <summary>
        /// Gets or sets explicit dataset resource bounds and grouped capability options.
        /// When absent, options are derived from the already selected real management node.
        /// </summary>
        public ProductionDatasetOptions? DatasetOptions { get; set; }
    }
}
