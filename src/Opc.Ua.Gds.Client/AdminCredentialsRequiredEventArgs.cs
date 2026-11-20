/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The arguments passed with a AdminCredentialsRequiredEventArgs event.
    /// </summary>
    public class AdminCredentialsRequiredEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AdminCredentialsRequiredEventArgs"/> class.
        /// </summary>
        public AdminCredentialsRequiredEventArgs()
        {
        }

        /// <summary>
        /// Gets or sets the credentials.
        /// </summary>
        public UserIdentity? Credentials { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating the credentials should be cached.
        /// </summary>
        public bool CacheCredentials { get; set; }
    }
}
