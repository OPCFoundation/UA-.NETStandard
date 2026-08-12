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
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Decides which security policies a compliance posture permits.
    /// </summary>
    /// <remarks>
    /// Several of the policies the stack supports use algorithms that are not
    /// approved for validated cryptography. They are enabled by default, because
    /// withholding them would break deployments that use them today, so a
    /// deployment that needs validated cryptography states so and this filter
    /// withholds them.
    /// </remarks>
    public static class CryptoCompliance
    {
        /// <summary>
        /// Whether a security policy may be used under a compliance posture.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy to test.</param>
        /// <param name="policy">The compliance posture.</param>
        /// <returns>
        /// <c>false</c> when the posture forbids the policy, or when the policy
        /// cannot be resolved and therefore cannot be shown to be approved.
        /// </returns>
        /// <remarks>
        /// Under <see cref="CryptoCompliancePolicy.FipsOnly"/> this fails closed:
        /// a policy whose classification cannot be established is withheld rather
        /// than allowed through. A deployment that asked for validated
        /// cryptography is better served by losing an endpoint it cannot vouch
        /// for than by advertising one it cannot.
        /// </remarks>
        public static bool IsPolicyPermitted(
            string? securityPolicyUri,
            CryptoCompliancePolicy policy)
        {
            if (policy != CryptoCompliancePolicy.FipsOnly)
            {
                return true;
            }

            // The classification lives on the policy itself, so adding a policy
            // states it next to the algorithms it follows from rather than in a
            // list here that would silently go stale. Platform support is not
            // consulted: whether a policy is approved is a property of its
            // algorithms, not of whether this platform happens to implement them.
            SecurityPolicyInfo? info = SecurityPolicyRegistry.Default.GetInfoIgnoringPlatformSupport(
                securityPolicyUri ?? string.Empty);

            return info != null && info.IsFipsApproved;
        }

        /// <summary>
        /// Filters a set of security policies down to those the posture permits.
        /// </summary>
        /// <param name="securityPolicyUris">The policies to filter.</param>
        /// <param name="policy">The compliance posture.</param>
        /// <returns>The permitted policies, in the order supplied.</returns>
        public static ArrayOf<string> FilterPolicies(
            ArrayOf<string> securityPolicyUris,
            CryptoCompliancePolicy policy)
        {
            if (policy != CryptoCompliancePolicy.FipsOnly)
            {
                return securityPolicyUris;
            }

            var permitted = new List<string>();
            foreach (string uri in securityPolicyUris)
            {
                if (IsPolicyPermitted(uri, policy))
                {
                    permitted.Add(uri);
                }
            }

            return new ArrayOf<string>(permitted.ToArray());
        }

        /// <summary>
        /// The purposes whose registered provider cannot actually perform the
        /// operations they cover.
        /// </summary>
        /// <param name="registry">The registry to inspect.</param>
        /// <returns>
        /// The purposes that resolve to a provider carrying no matching
        /// operation facet, in a stable order. Empty when everything a provider
        /// was bound to it can also perform.
        /// </returns>
        /// <remarks>
        /// The asymmetric purposes are served through
        /// <see cref="System.Security.Cryptography.RSA"/> and
        /// <see cref="System.Security.Cryptography.ECDsa"/>, which arrive with the
        /// key rather than with the provider, so there is nothing to check for
        /// them. The symmetric, key derivation and random purposes are different:
        /// they are served through <see cref="ISymmetricCryptoProvider"/>,
        /// <see cref="IKeyDerivationProvider"/> and <see cref="ISecureRandomSource"/>,
        /// and a provider bound to one of them without the matching facet is
        /// silently bypassed in favour of the platform.
        /// <para>
        /// That silence is the failure worth catching. A deployment that put a
        /// validated module behind every operation would otherwise believe the
        /// module performed the per message cryptography while the platform did.
        /// </para>
        /// </remarks>
        public static ArrayOf<CryptoPurpose> GetUnservedOperationPurposes(
            ICryptoProviderRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var unserved = new List<CryptoPurpose>();

            AddIfUnserved<ISymmetricCryptoProvider>(
                registry, CryptoPurpose.ChannelSymmetric, unserved);
            AddIfUnserved<IKeyDerivationProvider>(
                registry, CryptoPurpose.KeyDerivation, unserved);
            AddIfUnserved<ISecureRandomSource>(
                registry, CryptoPurpose.RandomNumberGeneration, unserved);

            return new ArrayOf<CryptoPurpose>(unserved.ToArray());
        }

        /// <summary>
        /// The operations a registered provider would not actually perform for
        /// the policies an application offers.
        /// </summary>
        /// <param name="registry">The registry to inspect.</param>
        /// <param name="policies">The security policies the application offers.</param>
        /// <returns>
        /// One entry per operation that would fall through to the platform, in a
        /// stable order. Empty when every provider can perform everything the
        /// policies it was bound to require.
        /// </returns>
        /// <remarks>
        /// Carrying the facet is necessary but not sufficient.
        /// <see cref="ISymmetricCryptoProvider.Supports(SymmetricEncryptionAlgorithm)"/>
        /// and its siblings are consulted again at the point of use, and a
        /// provider that answers <c>false</c> is bypassed in favour of the
        /// platform for that algorithm alone. A provider implementing the facet
        /// but not the algorithm the negotiated policy needs would therefore pass
        /// a facet-only check while the platform performed every message, which
        /// is the silence this exists to break.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public static ArrayOf<UnservedCryptoOperation> GetUnservedOperations(
            ICryptoProviderRegistry registry,
            ISecurityPolicyRegistry policies)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (policies is null)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            var unserved = new List<UnservedCryptoOperation>();

            ICryptoProvider symmetricProvider = registry.Resolve(CryptoPurpose.ChannelSymmetric);
            ICryptoProvider derivationProvider = registry.Resolve(CryptoPurpose.KeyDerivation);
            ICryptoProvider randomProvider = registry.Resolve(
                CryptoPurpose.RandomNumberGeneration);

            // The platform performing the platform's work is not a shortfall.
            // Only a provider that was put in front of the platform and then
            // hands part of the work back to it is worth reporting.
            var symmetric = IsPlatform(symmetricProvider)
                ? null
                : symmetricProvider as ISymmetricCryptoProvider;
            var derivation = IsPlatform(derivationProvider)
                ? null
                : derivationProvider as IKeyDerivationProvider;

            if (!IsPlatform(randomProvider) && randomProvider is not ISecureRandomSource)
            {
                unserved.Add(new UnservedCryptoOperation(
                    CryptoPurpose.RandomNumberGeneration, null, "random number generation"));
            }

            if (symmetric == null && derivation == null)
            {
                return new ArrayOf<UnservedCryptoOperation>(unserved.ToArray());
            }

            foreach (SecurityPolicyInfo policy in policies.Policies)
            {
                // A policy that secures nothing needs none of these, and one the
                // platform cannot offer is never negotiated.
                if (policy.Uri == SecurityPolicies.None || !IsPlatformSupported(policy))
                {
                    continue;
                }

                if (!IsPlatform(symmetricProvider) &&
                    (symmetric == null ||
                        !symmetric.Supports(policy.SymmetricEncryptionAlgorithm)))
                {
                    unserved.Add(new UnservedCryptoOperation(
                        CryptoPurpose.ChannelSymmetric,
                        policy.Uri,
                        policy.SymmetricEncryptionAlgorithm.ToString()));
                }

                if (!IsPlatform(symmetricProvider) &&
                    (symmetric == null ||
                        !symmetric.Supports(policy.SymmetricSignatureAlgorithm)))
                {
                    unserved.Add(new UnservedCryptoOperation(
                        CryptoPurpose.ChannelSymmetric,
                        policy.Uri,
                        policy.SymmetricSignatureAlgorithm.ToString()));
                }

                if (!IsPlatform(derivationProvider) &&
                    (derivation == null ||
                        !derivation.Supports(policy.KeyDerivationAlgorithm)))
                {
                    unserved.Add(new UnservedCryptoOperation(
                        CryptoPurpose.KeyDerivation,
                        policy.Uri,
                        policy.KeyDerivationAlgorithm.ToString()));
                }
            }

            return new ArrayOf<UnservedCryptoOperation>(unserved.ToArray());
        }

        private static bool IsPlatform(ICryptoProvider provider)
        {
            return ReferenceEquals(provider, PlatformCryptoProvider.Instance);
        }

        private static bool IsPlatformSupported(SecurityPolicyInfo policy)
        {
            return policy.PlatformSupport?.Invoke() ?? true;
        }

        private static void AddIfUnserved<TFacet>(
            ICryptoProviderRegistry registry,
            CryptoPurpose purpose,
            List<CryptoPurpose> unserved)
            where TFacet : class
        {
            ICryptoProvider provider = registry.Resolve(purpose);

            if (provider is not TFacet)
            {
                unserved.Add(purpose);
            }
        }
    }

    /// <summary>
    /// An operation a registered provider would not perform, so that the
    /// platform would perform it instead.
    /// </summary>
    /// <param name="Purpose">The purpose the provider was bound to.</param>
    /// <param name="SecurityPolicyUri">
    /// The security policy that needs the operation, or <see langword="null"/>
    /// when the operation is not policy specific.
    /// </param>
    /// <param name="Algorithm">The algorithm the provider does not perform.</param>
    public sealed record UnservedCryptoOperation(
        CryptoPurpose Purpose,
        string? SecurityPolicyUri,
        string Algorithm)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            return SecurityPolicyUri == null
                ? $"{Purpose.Name} ({Algorithm})"
                : $"{Purpose.Name} ({Algorithm}) for {SecurityPolicyUri}";
        }
    }
}
