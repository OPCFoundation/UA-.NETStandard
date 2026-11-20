/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Migration shim &#8212; restores the <c>CertificateIdentifier.Certificate</c>
    /// instance property that was removed in 1.6 because resolution now
    /// requires a registry/telemetry context. Accessing this shim member
    /// throws <see cref="NotSupportedException"/> at runtime; the
    /// <c>[Obsolete]</c> attribute and matching analyzer rule guide
    /// consumers to the async resolver replacement.
    /// </summary>
    public static class CertificateIdentifierShim
    {
        extension(CertificateIdentifier id)
        {
            /// <summary>
            /// Returns the resolved X.509 certificate. Removed in 1.6 because
            /// resolution is now async and requires a registry.
            /// </summary>
            /// <exception cref="NotSupportedException"></exception>
            [Obsolete("CertificateIdentifier.Certificate was removed in 1.6 because " +
                "resolution requires a registry/telemetry context. " +
                "Use CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey, applicationUri, telemetry, ct). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0018")]
            [OpcUaShim("UA0018")]
            public X509Certificate2? Certificate
                => throw new NotSupportedException(
                    "CertificateIdentifier.Certificate was removed in 1.6 because resolution " +
                    "requires a registry/telemetry context. Use " +
                    "CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey, applicationUri, telemetry, ct).");
        }
    }
}
