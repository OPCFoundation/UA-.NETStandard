/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Generates key pairs in software, which is what the stack has always done.
    /// </summary>
    public sealed class DefaultKeyPairGenerator : IKeyPairGenerator
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static DefaultKeyPairGenerator Instance { get; } = new();

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The certificate type names an elliptic curve the platform does not
        /// support.
        /// </exception>
        public Certificate CreateCertificate(
            ICertificateBuilder builder,
            NodeId certificateType,
            ushort keySizeInBits)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (CertificateIdentifier.IsRsaCertificateType(certificateType))
            {
                ushort keySize = keySizeInBits == 0
                    ? CertificateFactory.DefaultKeySize
                    : keySizeInBits;

                return builder.SetRSAKeySize(keySize).CreateForRSA();
            }

            ECCurve curve =
                CryptoUtils.GetCurveFromCertificateTypeId(certificateType)
                ?? throw ServiceResultException.ConfigurationError(
                    "The Ecc certificate type is not supported.");

            return builder.SetECCurve(curve).CreateForECDsa();
        }

        private DefaultKeyPairGenerator()
        {
        }
    }
}
