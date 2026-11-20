// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Provides access to an X509 CRL object.
    /// </summary>
    public interface IX509CRL
    {
        /// <summary>
        /// The name of the issuer for the CRL.
        /// </summary>
        X500DistinguishedName IssuerName { get; }

        /// <summary>
        /// The name of the issuer for the CRL.
        /// </summary>
        string Issuer { get; }

        /// <summary>
        /// When the CRL was last updated.
        /// </summary>
        DateTime ThisUpdate { get; }

        /// <summary>
        /// When the CRL is due for its next update.
        /// </summary>
        DateTime NextUpdate { get; }

        /// <summary>
        /// The hash algorithm used to sign the CRL.
        /// </summary>
        HashAlgorithmName HashAlgorithmName { get; }

        /// <summary>
        /// The revoked user certificates
        /// </summary>
        IList<RevokedCertificate> RevokedCertificates { get; }

        /// <summary>
        /// The X509Extensions of the CRL.
        /// </summary>
        X509ExtensionCollection CrlExtensions { get; }

        /// <summary>
        /// The raw data for the CRL.
        /// </summary>
        byte[] RawData { get; }
    }
}
