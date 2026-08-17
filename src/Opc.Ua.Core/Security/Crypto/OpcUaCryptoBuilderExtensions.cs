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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the crypto provider model.
    /// </summary>
    public static class OpcUaCryptoBuilderExtensions
    {
        /// <summary>
        /// Registers the crypto provider registry without changing behaviour.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <remarks>
        /// The registry resolves to platform cryptography until something is
        /// bound, so registering it on its own is inert.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddCryptoProvider(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<ICryptoProviderRegistry>(sp =>
            {
                var registry = new CryptoProviderRegistry();

                // Bindings are registered as separate services so that several
                // independent AddCryptoProvider calls compose. They are applied
                // here, when the registry is first resolved, because that is the
                // only point at which every one of them is known.
                foreach (CryptoProviderConfiguration configuration in
                    sp.GetServices<CryptoProviderConfiguration>())
                {
                    configuration.Apply(registry);
                }

                return registry;
            });

            return builder;
        }

        /// <summary>
        /// Registers the crypto provider registry and binds providers to
        /// purposes and security policies.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Configures the bindings.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <example>
        /// <code>
        /// services.AddOpcUa()
        ///     .AddCryptoProvider(crypto => crypto
        ///         .For(CryptoPurpose.ApplicationInstanceKey).Use(tpmProvider)
        ///         .For(CryptoPurpose.KeyAgreement).Use(tpmProvider)
        ///         .For(CryptoPurpose.UserIdentityKey).Use(keyVaultProvider));
        /// </code>
        /// </example>
        /// <remarks>
        /// A consumer that registered its own <see cref="ICryptoProviderRegistry"/>
        /// before this call keeps it, and the bindings are applied to that
        /// instance.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddCryptoProvider(
            this IOpcUaBuilder builder,
            Action<CryptoProviderBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.AddCryptoProvider();

            builder.Services.AddSingleton(new CryptoProviderConfiguration(configure));

            return builder;
        }

        /// <summary>
        /// Registers a security policy contributed by a provider.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="securityPolicy">The security policy to register.</param>
        /// <param name="replaceExisting">
        /// When <c>true</c>, an existing policy with the same URI or name is deliberately replaced.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddSecurityPolicy(
            this IOpcUaBuilder builder,
            SecurityPolicyInfo securityPolicy,
            bool replaceExisting = false)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (securityPolicy is null)
            {
                throw new ArgumentNullException(nameof(securityPolicy));
            }

            builder.AddSecurityPolicyRegistry();

            builder.Services.AddSingleton(new SecurityPolicyConfiguration(securityPolicy, replaceExisting));

            return builder;
        }

        /// <summary>
        /// Registers the security policy registry, so that a consumer can resolve
        /// <see cref="ISecurityPolicyRegistry"/> whether or not the application
        /// contributed any policy of its own.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The registry carries the built-in policies, and any policy registered
        /// through <see cref="AddSecurityPolicy"/> is applied to it. It is a
        /// registry of its own rather than
        /// <see cref="SecurityPolicies.Default"/>, so what one application
        /// registers does not reach another in the same process.
        /// </remarks>
        public static IOpcUaBuilder AddSecurityPolicyRegistry(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<ISecurityPolicyRegistry>(sp =>
            {
                var registry = new SecurityPolicies(
                    sp.GetService<ITelemetryContext>());

                foreach (SecurityPolicyConfiguration configuration in
                    sp.GetServices<SecurityPolicyConfiguration>())
                {
                    configuration.Apply(registry);
                }

                return registry;
            });

            return builder;
        }
    }
}

namespace Opc.Ua
{
    /// <summary>
    /// Carries a pending crypto provider configuration through the container.
    /// </summary>
    /// <remarks>
    /// Bindings are applied by <see cref="Apply"/> once the registry has been
    /// resolved, so that several independent calls to <c>AddCryptoProvider</c>
    /// compose rather than overwrite one another.
    /// </remarks>
    public sealed class CryptoProviderConfiguration
    {
        /// <summary>
        /// Initializes a pending configuration.
        /// </summary>
        /// <param name="configure">The configuration action.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public CryptoProviderConfiguration(Action<CryptoProviderBuilder> configure)
        {
            m_configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        /// <summary>
        /// Applies the configuration to a registry.
        /// </summary>
        /// <param name="registry">The registry to configure.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public void Apply(ICryptoProviderRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            m_configure(new CryptoProviderBuilder(registry));
        }

        private readonly Action<CryptoProviderBuilder> m_configure;
    }
}
