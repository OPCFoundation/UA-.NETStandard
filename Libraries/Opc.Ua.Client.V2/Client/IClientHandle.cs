// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client capabilities
    /// </summary>
    public interface IClientHandle
    {
        /// <summary>
        /// Manage certificates throuhg the certificates
        /// management api
        /// </summary>
        IPkiManagement PkiManagement { get; }

        /// <summary>
        /// Discover endpoints through the discovery api
        /// </summary>
        IDiscovery Discovery { get; }
    }
}
