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
using Opc.Ua.Redundancy;

namespace Opc.Ua
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ICertificateStoreProvider"/> that creates
    /// <see cref="SharedKeyValueCertificateStore"/> instances over a shared <see cref="ISharedKeyValueStore"/>,
    /// so trusted, issuer and rejected certificate lists are shared across a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// Register this provider (typically through dependency injection) so a
    /// <see cref="CertificateStoreIdentifier"/> with store type
    /// <see cref="CertificateStoreType.SharedKeyValue"/> (or a store path using the
    /// <see cref="CertificateStoreType.SharedKeyValueScheme"/> prefix) resolves to a distributed store.
    /// The injected <see cref="IRecordProtector"/> protects every stored record; an external,
    /// network-reachable shared store must use an authenticating protector so a forged record fails closed.
    /// </remarks>
    public sealed class SharedKeyValueCertificateStoreProvider : ICertificateStoreProvider
    {
        /// <summary>
        /// Creates a provider bound to a shared key/value backend and record protector.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="protector">
        /// The record protector applied to every stored record. Defaults to the no-op
        /// <see cref="NullRecordProtector"/> (safe only for an in-memory, single-process store).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
        public SharedKeyValueCertificateStoreProvider(
            ISharedKeyValueStore store,
            IRecordProtector? protector = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? NullRecordProtector.Instance;
        }

        /// <inheritdoc/>
        public string StoreTypeName => CertificateStoreType.SharedKeyValue;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return !string.IsNullOrEmpty(storePath) &&
                storePath.StartsWith(
                    CertificateStoreType.SharedKeyValueScheme,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new SharedKeyValueCertificateStore(m_store, m_protector, telemetry);
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
    }
}
