/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
    /// Resolves the optional operation facets a crypto provider may carry.
    /// </summary>
    /// <remarks>
    /// The symmetric, key derivation and random facets are declared separately
    /// from <see cref="ICryptoProvider"/> rather than on it. A provider opts in by
    /// implementing one, which keeps the shipped interface unbroken and lets a
    /// provider serve only the operations it can.
    /// <para>
    /// Every method here returns <c>null</c> when the resolved provider is the
    /// platform. That is not a special case for its own sake: the platform facets
    /// perform exactly the operations the caller would otherwise perform inline,
    /// so returning <c>null</c> tells the caller to take its existing path and
    /// keeps the default configuration free of any interface dispatch on the per
    /// message path. Registering a provider is what makes the seam cost anything,
    /// and only then for the deployment that asked for it.
    /// </para>
    /// <para>
    /// Resolve where something is bound — when a channel token is computed, when a
    /// key provider starts — and hold the result. Never call this per message.
    /// </para>
    /// </remarks>
    public static class CryptoProviderFacets
    {
        /// <summary>
        /// Resolves the symmetric facet for a security policy.
        /// </summary>
        /// <param name="registry">
        /// The registry to resolve through, or <c>null</c> when none is
        /// configured.
        /// </param>
        /// <param name="securityPolicyUri">
        /// The security policy in play, or <c>null</c> when it does not matter.
        /// </param>
        /// <returns>
        /// The facet to use, or <c>null</c> when the caller should perform the
        /// operation itself.
        /// </returns>
        public static ISymmetricCryptoProvider? ResolveSymmetric(
            ICryptoProviderRegistry? registry,
            string? securityPolicyUri = null)
        {
            return Resolve<ISymmetricCryptoProvider>(
                registry, CryptoPurpose.ChannelSymmetric, securityPolicyUri);
        }

        /// <summary>
        /// Resolves the key derivation facet for a security policy.
        /// </summary>
        /// <param name="registry">
        /// The registry to resolve through, or <c>null</c> when none is
        /// configured.
        /// </param>
        /// <param name="securityPolicyUri">
        /// The security policy in play, or <c>null</c> when it does not matter.
        /// </param>
        /// <returns>
        /// The facet to use, or <c>null</c> when the caller should perform the
        /// derivation itself.
        /// </returns>
        public static IKeyDerivationProvider? ResolveKeyDerivation(
            ICryptoProviderRegistry? registry,
            string? securityPolicyUri = null)
        {
            return Resolve<IKeyDerivationProvider>(
                registry, CryptoPurpose.KeyDerivation, securityPolicyUri);
        }

        /// <summary>
        /// Resolves the random facet.
        /// </summary>
        /// <param name="registry">
        /// The registry to resolve through, or <c>null</c> when none is
        /// configured.
        /// </param>
        /// <param name="securityPolicyUri">
        /// The security policy in play, or <c>null</c> when it does not matter.
        /// </param>
        /// <returns>
        /// The facet to use, or <c>null</c> when the caller should use the
        /// platform generator.
        /// </returns>
        public static ISecureRandomSource? ResolveRandom(
            ICryptoProviderRegistry? registry,
            string? securityPolicyUri = null)
        {
            return Resolve<ISecureRandomSource>(
                registry, CryptoPurpose.RandomNumberGeneration, securityPolicyUri);
        }

        private static TFacet? Resolve<TFacet>(
            ICryptoProviderRegistry? registry,
            CryptoPurpose purpose,
            string? securityPolicyUri)
            where TFacet : class
        {
            if (registry is null)
            {
                return null;
            }

            ICryptoProvider provider = registry.Resolve(purpose, securityPolicyUri);

            if (ReferenceEquals(provider, PlatformCryptoProvider.Instance))
            {
                return null;
            }

            return provider as TFacet;
        }
    }
}
