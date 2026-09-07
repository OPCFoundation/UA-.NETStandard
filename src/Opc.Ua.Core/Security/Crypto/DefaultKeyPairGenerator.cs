/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

            if (certificateType.IsNull ||
                certificateType == ObjectTypeIds.ApplicationCertificateType ||
                certificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
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
