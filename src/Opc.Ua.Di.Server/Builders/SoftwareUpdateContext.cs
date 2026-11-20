// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Internal context object materialised by
    /// <see cref="SoftwareUpdateFacetWiring"/> and passed to user
    /// callbacks via <see cref="ISoftwareUpdateContext"/>.
    /// </summary>
    internal sealed class SoftwareUpdateContext : ISoftwareUpdateContext
    {
        public SoftwareUpdateContext(
            NodeId deviceId,
            ISystemContext systemContext,
            ISoftwarePackageStore packageStore,
            ISoftwareFolder softwareFolder)
        {
            DeviceId = deviceId;
            SystemContext = systemContext;
            PackageStore = packageStore;
            SoftwareFolder = softwareFolder;
        }

        public NodeId DeviceId { get; }
        public ISystemContext SystemContext { get; }
        public ISoftwarePackageStore PackageStore { get; }
        public ISoftwareFolder SoftwareFolder { get; }
    }
}
