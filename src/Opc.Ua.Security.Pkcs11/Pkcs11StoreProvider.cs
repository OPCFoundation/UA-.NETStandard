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
    /// Creates <see cref="Pkcs11CertificateStore"/> instances for store paths
    /// that use the RFC 7512 <c>pkcs11:</c> scheme.
    /// </summary>
    /// <remarks>
    /// Register with <c>CertificateManagerOptions.AddStoreProvider</c>, or
    /// through <c>IOpcUaBuilder.AddPkcs11CertificateStore</c>.
    /// Once registered, an application configuration can point any certificate
    /// store at a token by changing only its store path.
    /// </remarks>
    public sealed class Pkcs11StoreProvider : ICertificateStoreProvider
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="options">
        /// Token options applied to every store this provider creates, or
        /// <c>null</c> to take them from each store path.
        /// </param>
        /// <remarks>
        /// Supplying options is how a PIN is kept out of the configuration file:
        /// the store path names the token, and the PIN arrives from a secret
        /// store through <see cref="Pkcs11TokenOptions.PinProvider"/>.
        /// </remarks>
        public Pkcs11StoreProvider(Pkcs11TokenOptions? options = null)
        {
            m_options = options;
        }

        /// <inheritdoc/>
        public string StoreTypeName => Pkcs11CertificateStore.StoreTypeName;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return Pkcs11TokenOptions.IsPkcs11Uri(storePath);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new Pkcs11CertificateStore(telemetry, m_options);
        }

        private readonly Pkcs11TokenOptions? m_options;
    }
}
