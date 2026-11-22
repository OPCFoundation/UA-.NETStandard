// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// The browse names of the OPC 10000-100 conformance units that the
    /// Device Integration node manager can advertise.
    /// </summary>
    /// <remarks>
    /// The values are the conformance-unit names published by the OPC
    /// Foundation profile database for the DI 1.05 profile group. They are
    /// declared here rather than inline so the advertised set can be reviewed
    /// against the profile database in one place, and so a typo cannot be
    /// introduced independently at each usage site.
    /// </remarks>
    internal static class ConformanceUnits
    {
        /// <summary>
        /// The device topology is exposed through <c>DeviceSet</c>.
        /// </summary>
        public const string DeviceTopology = "DI DeviceTopology";

        /// <summary>
        /// Offline device information is available.
        /// </summary>
        public const string Offline = "DI Offline";

        /// <summary>
        /// The locking services are wired.
        /// </summary>
        public const string Locking = "DI Locking";

        /// <summary>
        /// The <c>BreakLock</c> method of the locking services is wired.
        /// </summary>
        public const string BreakLocking = "DI BreakLocking";

        /// <summary>
        /// The software update model is exposed.
        /// </summary>
        public const string SoftwareUpdate = "DI SU Software Update";

        /// <summary>
        /// The software update model supports <c>PrepareForUpdate</c>.
        /// </summary>
        public const string SoftwareUpdatePrepareForUpdate = "DI SU PrepareForUpdate";

        /// <summary>
        /// The software update model supports resuming an update.
        /// </summary>
        public const string SoftwareUpdateResumeUpdate = "DI SU Resume Update";

        /// <summary>
        /// Software packages are loaded through the file-system loading model.
        /// </summary>
        public const string SoftwareUpdateFileSystemLoading = "DI SU FileSystem Loading";

        /// <summary>
        /// Installation is driven by the file-system loading model.
        /// </summary>
        public const string SoftwareUpdateInstallationForFileSystem =
            "DI SU Installation for File System";

        /// <summary>
        /// Software packages are loaded through the direct loading model.
        /// </summary>
        public const string SoftwareUpdateDirectLoading = "DI SU DirectLoading";

        /// <summary>
        /// The software update model reports an update status.
        /// </summary>
        public const string SoftwareUpdateUpdateStatus = "DI SU UpdateStatus";

        /// <summary>
        /// Software packages are loaded through the cached loading model.
        /// </summary>
        public const string SoftwareUpdateCachedLoading = "DI SU CachedLoading";

        /// <summary>
        /// Installation is driven by the cached loading model.
        /// </summary>
        public const string SoftwareUpdateInstallationForCachedLoading =
            "DI SU Installation for Cached Loading";
    }

    /// <summary>
    /// The URIs of the OPC 10000-100 server facets that the Device
    /// Integration node manager can advertise on
    /// <c>Server/ServerCapabilities/ServerProfileArray</c>.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-7 for how facets aggregate conformance units. Only
    /// facets whose units are genuinely satisfied at runtime are advertised.
    /// </remarks>
    internal static class ServerProfiles
    {
        /// <summary>
        /// The server hosts devices under <c>DeviceSet</c>.
        /// </summary>
        public const string DeviceIntegrationHost =
            "http://opcfoundation.org/UA-Profile/DI/Server/DeviceIntegrationHost";

        /// <summary>
        /// The server exposes the DI locking services.
        /// </summary>
        public const string Locking =
            "http://opcfoundation.org/UA-Profile/DI/Server/Locking";

        /// <summary>
        /// The server exposes the base software update model.
        /// </summary>
        public const string SoftwareUpdateBase =
            "http://opcfoundation.org/UA-Profile/DI/Server/SoftwareUpdateBase";

        /// <summary>
        /// The server supports file-system software loading.
        /// </summary>
        public const string FileSystemLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/FileSystemLoading";

        /// <summary>
        /// The server supports direct software loading.
        /// </summary>
        public const string DirectLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/DirectLoading";

        /// <summary>
        /// The server supports cached software loading.
        /// </summary>
        public const string CachedLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/CachedLoading";
    }
}
