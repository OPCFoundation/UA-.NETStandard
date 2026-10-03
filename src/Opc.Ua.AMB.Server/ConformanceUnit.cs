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

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// The browse names of the OPC 10000-110 server conformance units the
    /// Asset Management Basics node manager can advertise.
    /// </summary>
    /// <remarks>
    /// The values are the conformance-unit names of OPC 10000-110 1.01.1
    /// Table 54, in table order. They are declared here rather than inline so
    /// the advertised set can be reviewed against the profile database in one
    /// place, and so a typo cannot be introduced independently at each usage
    /// site. A unit whose rule reads "all manageable assets" is advertised
    /// only while at least one asset is registered.
    /// </remarks>
    internal static class ConformanceUnits
    {
        /// <summary>
        /// All assets publish a non-empty <c>ProductInstanceUri</c> (§7).
        /// </summary>
        public const string AssetIdentification = "AMB Asset Identification";

        /// <summary>
        /// All assets publish a writable, persistent <c>AssetId</c> (§7).
        /// </summary>
        public const string ConfigurableAssetIdentification = "AMB Configurable Asset Identification";

        /// <summary>
        /// <c>AssetsByProductInstanceUri</c> lists every asset (§8.2.2).
        /// </summary>
        public const string AssetDiscoveryByProductInstanceUri = "AMB Asset Discovery by ProductInstanceUri";

        /// <summary>
        /// <c>AssetsByAssetId</c> lists every asset (§8.2.3).
        /// </summary>
        public const string AssetDiscoveryByAssetId = "AMB Asset Discovery by AssetId";

        /// <summary>
        /// All assets publish <c>DeviceHealth</c> (§9.2).
        /// </summary>
        public const string AssetHealthStatusBase = "AMB Asset Health Status Base";

        /// <summary>
        /// All assets report health alarms (§9.3).
        /// </summary>
        public const string AssetHealthStatusAlarms = "AMB Asset Health Status Alarms";

        /// <summary>
        /// All health alarms implement <c>IRootCauseIndicationType</c> (§9.4).
        /// </summary>
        public const string AssetHealthStatusRootCauses = "AMB Asset Health Status Root Causes";

        /// <summary>
        /// All health alarms carry an AMB or standard condition class (§9.5).
        /// </summary>
        public const string AssetHealthStatusAlarmCategories = "AMB Asset Health Status Alarm Categories";

        /// <summary>
        /// The history of <c>DeviceHealth</c> is available (§9.6).
        /// </summary>
        public const string AssetHealthTrackingOverallAssetStatus =
            "AMB Asset Health Tracking Overall Asset Status";

        /// <summary>
        /// The event history of the health alarms is available (§9.6).
        /// </summary>
        public const string AssetHealthTrackingEvents = "AMB Asset Health Tracking Events";

        /// <summary>
        /// All assets publish their revisions and revision counter (§10.2).
        /// </summary>
        public const string VersionInformation = "AMB Version Information";

        /// <summary>
        /// An asset publishes operation counters (§10.3).
        /// </summary>
        public const string OperationCounters = "AMB Operation Counters";

        /// <summary>
        /// An asset publishes the <c>DocumentationLinks</c> AddIn (§10.5).
        /// </summary>
        public const string DocumentationLinksBase = "AMB DocumentationLinks Base";

        /// <summary>
        /// All assets publish a writable, persistent documentation link (§10.5).
        /// </summary>
        public const string DocumentationLinksEditBase = "AMB DocumentationLinks Edit Base";

        /// <summary>
        /// All assets offer <c>AddLink</c> and <c>RemoveLink</c> (§10.5).
        /// </summary>
        public const string DocumentationLinksEditAdvanced = "AMB DocumentationLinks Edit Advanced";

        /// <summary>
        /// An asset publishes its requirements (§10.6).
        /// </summary>
        public const string Requirements = "AMB Requirements";

        /// <summary>
        /// An asset publishes its capabilities (§10.7).
        /// </summary>
        public const string Capabilities = "AMB Capabilities";

        /// <summary>
        /// An asset is classified through a dictionary entry (§11).
        /// </summary>
        public const string Classification = "AMB Classification";

        /// <summary>
        /// All assets report maintenance conditions (§12).
        /// </summary>
        public const string CurrentAndFutureMaintenanceActivities =
            "AMB Current and Future Maintenance Activities";

        /// <summary>
        /// The event history of the maintenance conditions is available (§12).
        /// </summary>
        public const string PastMaintenanceActivities = "AMB Past Maintenance Activities";

        /// <summary>
        /// An asset publishes its local time (§13.2).
        /// </summary>
        public const string LocalTime = "AMB Local Time";

        /// <summary>
        /// An asset publishes the <c>HierarchicalLocation</c> Property (§13.3.2).
        /// </summary>
        public const string HierarchicalLocationProperty = "AMB Hierarchical Location Property";

        /// <summary>
        /// Assets are contained in a hierarchical location tree (§13.3.3).
        /// </summary>
        public const string HierarchicalLocationObjects = "AMB Hierarchical Location Objects";

        /// <summary>
        /// An asset publishes the <c>OperationalLocation</c> Property (§13.4.2).
        /// </summary>
        public const string OperationalLocationProperty = "AMB Operational Location Property";

        /// <summary>
        /// Assets are contained in an operational location tree (§13.4.3).
        /// </summary>
        public const string OperationalLocationObjects = "AMB Operational Location Objects";

        /// <summary>
        /// An asset publishes the <c>DigitalLocation</c> Property (§13.5).
        /// </summary>
        public const string DigitalLocation = "AMB Digital Location";

        /// <summary>
        /// An asset exposes another registered asset as its sub-asset (§14).
        /// </summary>
        public const string SubAssets = "AMB Sub-assets";

        /// <summary>
        /// An asset references another registered asset non-hierarchically (§14).
        /// </summary>
        public const string AssetRelations = "AMB Asset relations";
    }

    /// <summary>
    /// The URIs of the OPC 10000-110 server facets the Asset Management Basics
    /// node manager can advertise on
    /// <c>Server/ServerCapabilities/ServerProfileArray</c> (Table 55).
    /// </summary>
    internal static class ServerProfiles
    {
        /// <summary>
        /// AMB Base Asset Management Server Facet (Table 56).
        /// </summary>
        public const string BaseServer = "http://opcfoundation.org/UA-Profile/AMB/Server/BaseServer";
    }
}
