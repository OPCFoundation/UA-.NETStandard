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
