/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Covers registering PKCS#11 support through dependency injection.
    /// </summary>
    /// <remarks>
    /// Registration is what makes the <c>pkcs11:</c> store scheme resolvable at
    /// all, so it is worth asserting on without a token present.
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [Parallelizable(ParallelScope.All)]
    public class OpcUaPkcs11BuilderExtensionsTests
    {
        [Test]
        public void AddPkcs11CertificateStoreRegistersTheStoreProvider()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddPkcs11CertificateStore();

            using ServiceProvider provider = services.BuildServiceProvider();

            ICertificateStoreProvider[] registered = [.. provider.GetServices<ICertificateStoreProvider>()];

            Assert.That(
                registered.Any(p => p.StoreTypeName == Pkcs11CertificateStore.StoreTypeName),
                Is.True,
                "the pkcs11: scheme is only resolvable once the provider is registered");
        }

        [Test]
        public void AddPkcs11CertificateStoreCarriesTheSuppliedOptions()
        {
            var options = new Pkcs11TokenOptions
            {
                ModulePath = "/tmp/module.so",
                TokenLabel = "di-token"
            };

            var services = new ServiceCollection();
            services.AddOpcUa().AddPkcs11CertificateStore(options);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            ICertificateStoreProvider registered = serviceProvider
                .GetServices<ICertificateStoreProvider>()
                .First(p => p.StoreTypeName == Pkcs11CertificateStore.StoreTypeName);

            using ICertificateStore store = registered.CreateStore(NUnitTelemetryContext.Create());

            // Options supplied at registration mean the store does not need the
            // module or the PIN in its path.
            store.Open("pkcs11:token=di-token", noPrivateKeys: false);

            Assert.That(store.StoreType, Is.EqualTo(Pkcs11CertificateStore.StoreTypeName));
        }

        [Test]
        public void AddPkcs11CryptoProviderBindsThePurpose()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddPkcs11CryptoProvider(CryptoPurpose.ApplicationInstanceKey);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            ICryptoProviderRegistry registry = ApplyConfiguration(serviceProvider);
            ICryptoProvider resolved = registry.Resolve(CryptoPurpose.ApplicationInstanceKey);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.Name, Is.EqualTo("PKCS11"));
                Assert.That(
                    resolved.Validation.Level,
                    Is.EqualTo(CryptoValidationLevel.Uncertified),
                    "a token cannot report a validation certificate, so none may be assumed");
            });
        }

        [Test]
        public void AddPkcs11CryptoProviderCarriesAnAssertedValidation()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddPkcs11CryptoProvider(
                    CryptoPurpose.UserIdentityKey,
                    new CryptoValidationStatus(
                        CryptoValidationLevel.FipsValidated, "Vendor HSM", "CMVP #1234"),
                    "vendor-hsm");

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            ICryptoProviderRegistry registry = ApplyConfiguration(serviceProvider);
            ICryptoProvider resolved = registry.Resolve(CryptoPurpose.UserIdentityKey);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.Name, Is.EqualTo("vendor-hsm"));
                Assert.That(resolved.Validation.CertificateReference, Is.EqualTo("CMVP #1234"));
                Assert.That(resolved.Validation.IsAcceptableForFips, Is.True);
            });
        }

        /// <summary>
        /// A purpose the token was not bound to must still fall back to the
        /// platform, not to the token.
        /// </summary>
        [Test]
        public void AnUnboundPurposeStillResolvesToThePlatform()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddPkcs11CryptoProvider(CryptoPurpose.ApplicationInstanceKey);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            ICryptoProviderRegistry registry = ApplyConfiguration(serviceProvider);

            Assert.That(
                registry.Resolve(CryptoPurpose.ChannelSymmetric).Name,
                Is.EqualTo("Platform"));
        }

        /// <summary>
        /// Applies the pending bindings, which is what a host does once the
        /// registry has been resolved.
        /// </summary>
        /// <param name="serviceProvider">The built container.</param>
        /// <returns>The configured registry.</returns>
        private static ICryptoProviderRegistry ApplyConfiguration(ServiceProvider serviceProvider)
        {
            var registry = serviceProvider.GetRequiredService<ICryptoProviderRegistry>();

            foreach (CryptoProviderConfiguration configuration in
                serviceProvider.GetServices<CryptoProviderConfiguration>())
            {
                configuration.Apply(registry);
            }

            return registry;
        }

        [Test]
        public void ExtensionsRejectANullBuilder()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaPkcs11BuilderExtensions.AddPkcs11CertificateStore(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaPkcs11BuilderExtensions.AddPkcs11CryptoProvider(
                        null!, CryptoPurpose.ApplicationInstanceKey));
            });
        }
    }
}
