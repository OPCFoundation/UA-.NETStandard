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

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers registration of the crypto provider model through dependency
    /// injection, which is how an integrator supplies an alternative without
    /// recompiling the stack.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoProviderDependencyInjectionTests
    {
        private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();

        [Test]
        public void AddCryptoProviderRegistersARegistry()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddCryptoProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetService<ICryptoProviderRegistry>();

            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.Resolve(CryptoPurpose.ApplicationInstanceKey),
                Is.SameAs(PlatformCryptoProvider.Instance),
                "Registering the model on its own must not change behaviour.");
        }

        [Test]
        public void ConfiguredBindingsAreAppliedToTheRegistry()
        {
            var tpm = new StubProvider("Tpm", CryptoPurpose.ApplicationInstanceKey);
            var keyVault = new StubProvider("KeyVault", CryptoPurpose.UserIdentityKey);

            var services = new ServiceCollection();
            services.AddOpcUa().AddCryptoProvider(crypto => crypto
                .For(CryptoPurpose.ApplicationInstanceKey).Use(tpm)
                .For(CryptoPurpose.UserIdentityKey).Use(keyVault));

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ICryptoProviderRegistry>();

            foreach (CryptoProviderConfiguration configuration in
                provider.GetServices<CryptoProviderConfiguration>())
            {
                configuration.Apply(registry);
            }

            Assert.Multiple(() =>
            {
                Assert.That(registry.Resolve(CryptoPurpose.ApplicationInstanceKey), Is.SameAs(tpm));
                Assert.That(registry.Resolve(CryptoPurpose.UserIdentityKey), Is.SameAs(keyVault));
                Assert.That(
                    registry.Resolve(CryptoPurpose.ChannelSymmetric),
                    Is.SameAs(PlatformCryptoProvider.Instance));
            });
        }

        /// <summary>
        /// Several independent calls must compose rather than overwrite, so that
        /// a library can contribute a binding without knowing what the host did.
        /// </summary>
        [Test]
        public void SeveralConfigurationsCompose()
        {
            var first = new StubProvider("First", CryptoPurpose.ApplicationInstanceKey);
            var second = new StubProvider("Second", CryptoPurpose.KeyAgreement);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddCryptoProvider(crypto => crypto.For(CryptoPurpose.ApplicationInstanceKey).Use(first))
                .AddCryptoProvider(crypto => crypto.For(CryptoPurpose.KeyAgreement).Use(second));

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ICryptoProviderRegistry>();

            foreach (CryptoProviderConfiguration configuration in
                provider.GetServices<CryptoProviderConfiguration>())
            {
                configuration.Apply(registry);
            }

            Assert.Multiple(() =>
            {
                Assert.That(registry.Resolve(CryptoPurpose.ApplicationInstanceKey), Is.SameAs(first));
                Assert.That(registry.Resolve(CryptoPurpose.KeyAgreement), Is.SameAs(second));
            });
        }

        /// <summary>
        /// A consumer that registered its own registry keeps it.
        /// </summary>
        [Test]
        public void PreRegisteredRegistryWins()
        {
            var custom = new CryptoProviderRegistry();

            var services = new ServiceCollection();
            services.AddSingleton<ICryptoProviderRegistry>(custom);
            services.AddOpcUa().AddCryptoProvider();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ICryptoProviderRegistry>(),
                Is.SameAs(custom));
        }

        [Test]
        public void ChannelQuotasCarriesTheRegistry()
        {
            var registry = new CryptoProviderRegistry();
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry))
            {
                CryptoProviders = registry
            };

            Assert.That(quotas.CryptoProviders, Is.SameAs(registry));
            Assert.That(
                new ChannelQuotas(ServiceMessageContext.Create(m_telemetry)).CryptoProviders,
                Is.Null,
                "A channel with no registry uses platform cryptography.");
        }

        private sealed class StubProvider : ICryptoProvider
        {
            public StubProvider(string name, params CryptoPurpose[] purposes)
            {
                Name = name;
                var capabilities = new CryptoCapability[purposes.Length];
                for (int ii = 0; ii < purposes.Length; ii++)
                {
                    capabilities[ii] = new CryptoCapability(purposes[ii]);
                }
                Capabilities = new ArrayOf<CryptoCapability>(capabilities);
            }

            public string Name { get; }

            public CryptoValidationStatus Validation { get; }
                = new(CryptoValidationLevel.Uncertified, "test double");

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }
}
