/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Options for registering the GDS default identity authenticators and augmenters.
    /// </summary>
    public sealed class GdsDefaultIdentityAuthenticatorOptions : DefaultAuthenticatorOptions
    {
        /// <summary>
        /// Register <see cref="Identity.GdsApplicationSelfAdminProvider"/> with the default authenticators.
        /// </summary>
        public bool EnableGdsApplicationSelfAdminProvider { get; set; } = true;
    }
}
