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

namespace Opc.Ua
{
    /// <summary>
    /// Configures which crypto provider serves which operations.
    /// </summary>
    /// <remarks>
    /// Bindings are stated explicitly rather than discovered. Scanning
    /// assemblies for providers would be convenient but is incompatible with the
    /// trimming and ahead of time posture of this stack, and it would make the
    /// effective security configuration depend on what happens to be loaded.
    /// </remarks>
    public sealed class CryptoProviderBuilder
    {
        /// <summary>
        /// Initializes a builder over a registry.
        /// </summary>
        /// <param name="registry">The registry to configure.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public CryptoProviderBuilder(ICryptoProviderRegistry registry)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// The registry being configured.
        /// </summary>
        public ICryptoProviderRegistry Registry { get; }

        /// <summary>
        /// Sets the provider used when nothing more specific matches.
        /// </summary>
        /// <param name="provider">The provider to use.</param>
        /// <returns>The same builder, for chaining.</returns>
        public CryptoProviderBuilder UseDefault(ICryptoProvider provider)
        {
            Registry.RegisterDefault(provider);
            return this;
        }

        /// <summary>
        /// Begins a binding for one purpose.
        /// </summary>
        /// <param name="purpose">The purpose to bind.</param>
        /// <returns>A binding that selects the provider.</returns>
        public CryptoPurposeBinding For(CryptoPurpose purpose)
        {
            return new CryptoPurposeBinding(this, purpose, null);
        }

        /// <summary>
        /// Begins a binding for one purpose under one security policy.
        /// </summary>
        /// <param name="purpose">The purpose to bind.</param>
        /// <param name="securityPolicyUri">The security policy to bind.</param>
        /// <returns>A binding that selects the provider.</returns>
        public CryptoPurposeBinding For(CryptoPurpose purpose, string securityPolicyUri)
        {
            return new CryptoPurposeBinding(this, purpose, securityPolicyUri);
        }
    }

    /// <summary>
    /// A pending binding of a purpose, and optionally a security policy, to a
    /// provider.
    /// </summary>
    public readonly struct CryptoPurposeBinding : IEquatable<CryptoPurposeBinding>
    {
        internal CryptoPurposeBinding(
            CryptoProviderBuilder builder,
            CryptoPurpose purpose,
            string? securityPolicyUri)
        {
            m_builder = builder;
            m_purpose = purpose;
            m_securityPolicyUri = securityPolicyUri;
        }

        /// <summary>
        /// Completes the binding with a provider instance.
        /// </summary>
        /// <param name="provider">The provider to use.</param>
        /// <returns>The builder, for chaining.</returns>
        public CryptoProviderBuilder Use(ICryptoProvider provider)
        {
            if (m_securityPolicyUri == null)
            {
                m_builder.Registry.RegisterFor(m_purpose, provider);
            }
            else
            {
                m_builder.Registry.RegisterFor(m_purpose, m_securityPolicyUri, provider);
            }

            if (m_securityPolicyUri == null &&
                m_purpose.Equals(CryptoPurpose.RandomNumberGeneration) &&
                provider is ISecureRandomSource randomSource)
            {
                // Nonces are created from many places that have no container in
                // scope, so the source is published to the process here rather
                // than resolved per call site. Only an unscoped binding may do
                // this: a binding made for one security policy must not redirect
                // nonce generation for every other policy as well.
                Nonce.SetRandomSource(randomSource);
            }

            return m_builder;
        }

        /// <inheritdoc/>
        public bool Equals(CryptoPurposeBinding other)
        {
            return ReferenceEquals(m_builder, other.m_builder) &&
                m_purpose.Equals(other.m_purpose) &&
                string.Equals(m_securityPolicyUri, other.m_securityPolicyUri, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is CryptoPurposeBinding other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(m_builder, m_purpose, m_securityPolicyUri);
        }

        /// <summary>
        /// Compares two bindings for equality.
        /// </summary>
        public static bool operator ==(CryptoPurposeBinding left, CryptoPurposeBinding right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two bindings for inequality.
        /// </summary>
        public static bool operator !=(CryptoPurposeBinding left, CryptoPurposeBinding right)
        {
            return !left.Equals(right);
        }

        private readonly CryptoProviderBuilder m_builder;
        private readonly CryptoPurpose m_purpose;
        private readonly string? m_securityPolicyUri;
    }
}
