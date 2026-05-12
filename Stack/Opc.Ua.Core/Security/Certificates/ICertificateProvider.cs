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
 *
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

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Resolves <see cref="Certificate"/> instances on demand from a
    /// centralised cache + store pipeline. Designed for the
    /// <c>TryGet</c> &#8594; <c>GetAsync</c> pattern where the sync
    /// fast-path serves cache hits with no allocation, and the async
    /// cold-path falls through to the underlying
    /// <see cref="ICertificateStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumers (for example <see cref="X509IdentityTokenHandler"/>)
    /// hold a <see cref="CertificateIdentifier"/> instead of caching a
    /// live <see cref="Certificate"/> reference, and resolve only when
    /// they need to sign / verify / encrypt. This eliminates the
    /// <see cref="System.IDisposable"/> coupling that would otherwise
    /// flow up to <see cref="UserIdentity"/>.
    /// </para>
    /// <para>
    /// Returned <see cref="Certificate"/> instances are
    /// <see cref="Certificate.AddRef"/>'d for the caller. The caller
    /// MUST dispose them when done.
    /// </para>
    /// </remarks>
    public interface ICertificateProvider
    {
        /// <summary>
        /// Synchronous fast-path lookup by thumbprint. Returns an
        /// AddRef'd certificate if a matching entry is in the cache,
        /// otherwise <see langword="null"/>.
        /// </summary>
        /// <param name="thumbprint">
        /// The certificate thumbprint (case-insensitive hex).
        /// </param>
        /// <returns>
        /// An <see cref="Certificate.AddRef"/>'d certificate the caller
        /// must dispose, or <see langword="null"/> on cache miss.
        /// </returns>
        Certificate? TryGetPrivateKeyCertificate(string thumbprint);

        /// <summary>
        /// Resolves a private-key-bearing certificate for the supplied
        /// identifier. Cache hits complete synchronously with zero
        /// allocations; cache misses fall through to
        /// <see cref="CertificateIdentifierResolver.LoadPrivateKeyAsync"/>
        /// and write through to the cache on success.
        /// </summary>
        /// <param name="identifier">
        /// The certificate identifier (store type, store path,
        /// thumbprint / subject name).
        /// </param>
        /// <param name="passwordProvider">
        /// Optional provider used to unlock PFX private keys.
        /// </param>
        /// <param name="applicationUri">
        /// Optional fallback application URI used to find the cert
        /// after rotation.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// An AddRef'd certificate the caller must dispose, or
        /// <see langword="null"/> when the identifier could not be
        /// resolved.
        /// </returns>
        ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider? passwordProvider = null,
            string? applicationUri = null,
            CancellationToken ct = default);
    }
}
