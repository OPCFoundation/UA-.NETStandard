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

namespace Opc.Ua
{
    /// <summary>
    /// Selects the crypto provider that serves a given operation.
    /// </summary>
    /// <remarks>
    /// Resolution runs from the most specific registration to the least: a
    /// provider bound to one purpose and one security policy wins over one bound
    /// to the purpose alone, which wins over the registered default, which wins
    /// over the platform. This is the same precedence the historian provider
    /// registry uses for nodes, namespaces and its default.
    /// <para>
    /// Resolve at the point where something is bound, not per operation. A
    /// channel resolves once when it opens and holds the result for its lifetime,
    /// the same way it caches the security policy on its token. Calling this on a
    /// per message path would put a lookup where none is needed.
    /// </para>
    /// </remarks>
    public interface ICryptoProviderRegistry
    {
        /// <summary>
        /// Binds a provider to one purpose under one security policy.
        /// </summary>
        /// <param name="purpose">The purpose to bind.</param>
        /// <param name="securityPolicyUri">The security policy to bind.</param>
        /// <param name="provider">The provider to use.</param>
        void RegisterFor(CryptoPurpose purpose, string securityPolicyUri, ICryptoProvider provider);

        /// <summary>
        /// Binds a provider to one purpose, for every security policy.
        /// </summary>
        /// <param name="purpose">The purpose to bind.</param>
        /// <param name="provider">The provider to use.</param>
        void RegisterFor(CryptoPurpose purpose, ICryptoProvider provider);

        /// <summary>
        /// Sets the provider used when nothing more specific matches.
        /// </summary>
        /// <param name="provider">The provider to use.</param>
        void RegisterDefault(ICryptoProvider provider);

        /// <summary>
        /// Selects the provider for an operation.
        /// </summary>
        /// <param name="purpose">What the operation is for.</param>
        /// <param name="securityPolicyUri">
        /// The security policy in play, or <c>null</c> when it does not matter.
        /// </param>
        /// <param name="certificateType">
        /// The certificate type in play, or <see cref="NodeId.Null"/> when it
        /// does not matter.
        /// </param>
        /// <returns>
        /// The provider to use. Never <c>null</c>: the platform provider is the
        /// final fallback, so a caller that has registered nothing still gets
        /// today's behaviour.
        /// </returns>
        ICryptoProvider Resolve(
            CryptoPurpose purpose,
            string? securityPolicyUri = null,
            NodeId certificateType = default);

        /// <summary>
        /// Every provider known to the registry, including the default.
        /// </summary>
        /// <remarks>
        /// Used to report the effective configuration and to compute the set of
        /// security policies that can be advertised.
        /// </remarks>
        ArrayOf<ICryptoProvider> Providers { get; }
    }
}
