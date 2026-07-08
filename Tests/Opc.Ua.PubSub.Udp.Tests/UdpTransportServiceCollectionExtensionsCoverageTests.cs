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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Coverage-gap tests for
    /// <see cref="UdpTransportServiceCollectionExtensions"/> —
    /// exercises the newly-added overloads and null-guard branches
    /// not reached by
    /// <see cref="UdpTransportServiceCollectionExtensionsTests"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class UdpTransportServiceCollectionExtensionsCoverageTests
    {
        // ---- WithDtls(IConfigurationSection) ----

        [Test]
        public async Task WithDtlsIConfigurationSectionBindsPreferredProfileAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dtls:PreferredProfileName"] = "ECC_nistP256"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport().WithDtls(configuration.GetSection("Dtls")));

            await using ServiceProvider sp = services.BuildServiceProvider();
            DtlsTransportOptions options =
                sp.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.That(options.PreferredProfileName, Is.EqualTo("ECC_nistP256"));
        }

        [Test]
        public async Task WithDtlsIConfigurationSectionBindsAllScalarOptionsAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dtls:PreferredProfileName"] = "ECC_nistP384",
                    ["Dtls:MaxHandshakeDatagramSize"] = "2048",
                    ["Dtls:InitialRetransmissionTimeout"] = "00:00:00.500",
                    ["Dtls:MaxRetransmissionTimeout"] = "00:00:04",
                    ["Dtls:RequireHelloRetryRequestCookie"] = "true",
                    ["Dtls:RequireClientCertificate"] = "true"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport().WithDtls(configuration.GetSection("Dtls")));

            await using ServiceProvider sp = services.BuildServiceProvider();
            DtlsTransportOptions options =
                sp.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.PreferredProfileName, Is.EqualTo("ECC_nistP384"));
                Assert.That(options.MaxHandshakeDatagramSize, Is.EqualTo(2048));
                Assert.That(options.InitialRetransmissionTimeout,
                    Is.EqualTo(TimeSpan.FromMilliseconds(500)));
                Assert.That(options.MaxRetransmissionTimeout,
                    Is.EqualTo(TimeSpan.FromSeconds(4)));
                Assert.That(options.RequireHelloRetryRequestCookie, Is.True);
                Assert.That(options.RequireClientCertificate, Is.True);
            });
        }

        [Test]
        public async Task WithDtlsIConfigurationSectionBindsDisabledProfilesAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dtls:DisabledProfiles:0"] = "ECC_nistP256",
                    ["Dtls:DisabledProfiles:1"] = "ECC_nistP384"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport().WithDtls(configuration.GetSection("Dtls")));

            await using ServiceProvider sp = services.BuildServiceProvider();
            DtlsTransportOptions options =
                sp.GetRequiredService<IOptions<DtlsTransportOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.DisabledProfiles, Has.Count.EqualTo(2));
                Assert.That(options.DisabledProfiles, Contains.Item("ECC_nistP256"));
                Assert.That(options.DisabledProfiles, Contains.Item("ECC_nistP384"));
            });
        }

        [Test]
        public async Task WithDtlsIConfigurationSectionRegistersProfileRegistryAndFactoryAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddUdpTransport().WithDtls(configuration.GetSection("Dtls")));

            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetRequiredService<DtlsProfileRegistry>(), Is.Not.Null);
                Assert.That(sp.GetRequiredService<IDtlsContextFactory>(),
                    Is.InstanceOf<DefaultDtlsContextFactory>());
            });
        }

        [Test]
        public void WithDtlsIConfigurationSectionNullBuilderThrows()
        {
            IUdpTransportBuilder? builder = null;
            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build()
                .GetSection("Dtls");

            Assert.That(
                () => builder!.WithDtls(section),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void WithDtlsIConfigurationSectionNullSectionThrows()
        {
            var services = new ServiceCollection();
            IUdpTransportBuilder? captured = null;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub.AddUdpTransport());
            using ServiceProvider sp = services.BuildServiceProvider();
            IConfigurationSection? section = null;

            Assert.That(
                () => captured!.WithDtls(section!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
        }

        [Test]
        public void WithDtlsIConfigurationNullConfigurationThrows()
        {
            var services = new ServiceCollection();
            IUdpTransportBuilder? captured = null;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub.AddUdpTransport());
            using ServiceProvider sp = services.BuildServiceProvider();
            IConfiguration? configuration = null;

            Assert.That(
                () => captured!.WithDtls(configuration!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        // ---- AddUdpPubSub(IServiceCollection) null guard ----

        [Test]
        public void AddUdpPubSubNullServiceCollectionThrows()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddUdpPubSub(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
        }

        // ---- AddUdpPubSub(IOpcUaBuilder) null guard ----

        [Test]
        public void AddUdpPubSubNullOpcUaBuilderThrows()
        {
            IOpcUaBuilder? builder = null;

            Assert.That(
                () => builder!.AddUdpPubSub(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        // ---- AddSecureUdpPubSub(IServiceCollection) null guard ----

        [Test]
        public void AddSecureUdpPubSubNullServiceCollectionThrows()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddSecureUdpPubSub(
                    "group-1",
                    PubSubSecurityPolicyUri.PubSubAes256Ctr,
                    _ => new NoOpSecurityKeyService()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
        }

        // ---- AddSecureUdpPubSub(IOpcUaBuilder) null guard and registration ----

        [Test]
        public void AddSecureUdpPubSubNullOpcUaBuilderThrows()
        {
            IOpcUaBuilder? builder = null;

            Assert.That(
                () => builder!.AddSecureUdpPubSub(
                    "group-1",
                    PubSubSecurityPolicyUri.PubSubAes256Ctr,
                    _ => new NoOpSecurityKeyService()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public async Task AddSecureUdpPubSubOnOpcUaBuilderRegistersApplicationAndProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());

            services.AddOpcUa().AddSecureUdpPubSub(
                "group-2",
                PubSubSecurityPolicyUri.PubSubAes256Ctr,
                _ => new NoOpSecurityKeyService());

            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetService<IPubSubApplication>(), Is.Not.Null);
                Assert.That(
                    sp.GetServices<IPubSubSecurityKeyProvider>()
                        .Single().SecurityGroupId,
                    Is.EqualTo("group-2"));
                Assert.That(
                    sp.GetServices<IPubSubTransportFactory>().OfType<UdpPubSubTransportFactory>(),
                    Is.Not.Empty);
            });
        }

        [Test]
        public async Task AddSecureUdpPubSubOnOpcUaBuilderWithTransportConfigureIsInvokedAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            bool transportConfigured = false;

            services.AddOpcUa().AddSecureUdpPubSub(
                "group-3",
                PubSubSecurityPolicyUri.PubSubAes256Ctr,
                _ => new NoOpSecurityKeyService(),
                udp =>
                {
                    transportConfigured = true;
                    _ = udp;
                });

            await using ServiceProvider sp = services.BuildServiceProvider();
            _ = sp.GetService<IPubSubApplication>();

            Assert.That(transportConfigured, Is.True);
        }

        private sealed class NoOpSecurityKeyService : ISecurityKeyService
        {
            public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged
            {
                add { }
                remove { }
            }

            public ValueTask<SksKeyResponse> GetSecurityKeysAsync(
                SksKeyRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException(
                    "Coverage test does not start the SKS pull loop.");
            }
        }
    }
}
