// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Security.Pkcs11;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers PKCS#11 token support.
    /// </summary>
    public static class OpcUaPkcs11BuilderExtensions
    {
        /// <summary>
        /// Registers the PKCS#11 certificate store provider.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="options">
        /// Token options applied to every PKCS#11 store, or <c>null</c> to take
        /// them from each store path.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        /// <example>
        /// <code>
        /// services.AddOpcUa()
        ///     .AddPkcs11CertificateStore(new Pkcs11TokenOptions
        ///     {
        ///         ModulePath = "/usr/lib/softhsm/libsofthsm2.so",
        ///         TokenLabel = "opcua",
        ///         PinProvider = () =&gt; secretStore.Read("token-pin")
        ///     });
        /// </code>
        /// </example>
        /// <remarks>
        /// Registering the provider makes the <c>pkcs11:</c> store scheme
        /// resolvable. Without dependency injection, pass a
        /// <see cref="Pkcs11StoreProvider"/> to
        /// <c>CertificateManagerOptions.AddStoreProvider</c> instead.
        /// </remarks>
        public static IOpcUaBuilder AddPkcs11CertificateStore(
            this IOpcUaBuilder builder,
            Pkcs11TokenOptions? options = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<ICertificateStoreProvider>(
                _ => new Pkcs11StoreProvider(options));

            return builder;
        }

        /// <summary>
        /// Registers a PKCS#11 token as the provider for a purpose.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="purpose">
        /// The purpose the token serves, for example
        /// <see cref="CryptoPurpose.ApplicationInstanceKey"/>.
        /// </param>
        /// <param name="validation">
        /// What may be said about the token's validation. Defaults to
        /// <see cref="CryptoValidationLevel.Uncertified"/>, which is what makes
        /// the use of an unvalidated module visible in the audit surfaces.
        /// </param>
        /// <param name="name">A stable identifier for logs and diagnostics.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        /// <remarks>
        /// This records provenance and drives selection. It does not by itself
        /// route key operations: those follow the certificate, which comes from
        /// the store registered by
        /// <see cref="AddPkcs11CertificateStore"/>.
        /// </remarks>
        public static IOpcUaBuilder AddPkcs11CryptoProvider(
            this IOpcUaBuilder builder,
            CryptoPurpose purpose,
            CryptoValidationStatus validation = default,
            string name = "PKCS11")
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var provider = new Pkcs11CryptoProvider(
                new ArrayOf<CryptoCapability>(new CryptoCapability[] { new(purpose) }),
                validation,
                name);

            return builder.AddCryptoProvider(crypto => crypto.For(purpose).Use(provider));
        }
    }
}
