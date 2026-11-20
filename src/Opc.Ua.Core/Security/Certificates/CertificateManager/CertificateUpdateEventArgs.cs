/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The event arguments provided when an application certificate update occurs.
    /// </summary>
    public class CertificateUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public CertificateUpdateEventArgs(
            SecurityConfiguration configuration,
            ICertificateValidatorEx validator)
        {
            SecurityConfiguration = configuration;
            CertificateValidator = validator;
        }

        /// <summary>
        /// The new security configuration.
        /// </summary>
        public SecurityConfiguration SecurityConfiguration { get; }

        /// <summary>
        /// The certificate validator (modern <see cref="ICertificateValidatorEx"/>).
        /// </summary>
        public ICertificateValidatorEx CertificateValidator { get; }
    }
}
