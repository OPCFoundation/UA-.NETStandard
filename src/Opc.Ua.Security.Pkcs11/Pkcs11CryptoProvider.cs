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

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Declares that keys for a purpose are served by a PKCS#11 token.
    /// </summary>
    /// <remarks>
    /// The provider does not perform the cryptography itself; the token key is
    /// surfaced as an ordinary <see cref="System.Security.Cryptography.RSA"/> or
    /// <see cref="System.Security.Cryptography.ECDsa"/> by the store. What this
    /// type contributes is the two facts the stack cannot infer: which purposes
    /// the token is meant to serve, and what may honestly be said about the
    /// module behind it.
    /// <para>
    /// The validation level is <see cref="CryptoValidationLevel.Uncertified"/> by
    /// default, and deliberately so. A token may well hold a FIPS 140 certificate,
    /// but nothing in the PKCS#11 interface reports one, so the stack must not
    /// assume it. An operator who knows their token's certificate can say so by
    /// passing an explicit <see cref="CryptoValidationStatus"/>, which is then
    /// what the audit surfaces report.
    /// </para>
    /// </remarks>
    public sealed class Pkcs11CryptoProvider : ICryptoProvider
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="capabilities">
        /// The purposes this token serves. When empty, the token is declared for
        /// <see cref="CryptoPurpose.ApplicationInstanceKey"/> only, which is the
        /// common case.
        /// </param>
        /// <param name="validation">
        /// What may be said about the token's validation. Defaults to
        /// <see cref="CryptoValidationLevel.Uncertified"/>.
        /// </param>
        /// <param name="name">
        /// A stable identifier for configuration, logs and the address space.
        /// </param>
        public Pkcs11CryptoProvider(
            ArrayOf<CryptoCapability> capabilities = default,
            CryptoValidationStatus validation = default,
            string name = "PKCS11")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));

            Capabilities = capabilities.Count > 0 ? capabilities : s_defaultCapabilities;

            Validation = validation.Level == CryptoValidationLevel.Unknown
                ? new CryptoValidationStatus(
                    CryptoValidationLevel.Uncertified,
                    "PKCS#11 token")
                : validation;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public CryptoValidationStatus Validation { get; }

        /// <inheritdoc/>
        public ArrayOf<CryptoCapability> Capabilities { get; }

        private static readonly ArrayOf<CryptoCapability> s_defaultCapabilities = new(
            new CryptoCapability[]
            {
                new(CryptoPurpose.ApplicationInstanceKey)
            });
    }
}
