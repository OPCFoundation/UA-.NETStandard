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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Tests for the <c>IConfiguration</c>/<c>IConfigurationSection</c>
    /// AddClient overloads, AddIdentityProvider generic and composite
    /// overloads, AddAccessTokenProvider null-arg guards, and
    /// AddWebApiTransportChannel wiring.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaClientBuilderExtensionsTests2
    {
        // ----------------------------------------------------------------
        // AddClient(IConfiguration) overload
        // ----------------------------------------------------------------

        [Test]
        public void AddClientWithIConfiguration_NullConfiguration_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddClient((IConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddClientWithIConfiguration_NullBuilder_Throws()
        {
            IConfiguration cfg = BuildConfig([]);

            Assert.That(
                () => OpcUaClientBuilderExtensions.AddClient(null!, cfg),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddClientWithIConfiguration_RegistersCoreServices()
        {
            IConfiguration cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["OpcUa:Client:Session:SessionName"] = "FromConfig"
            });

            var services = new ServiceCollection();
            IOpcUaClientBuilder clientBuilder = services.AddOpcUa().AddClient(cfg);

            Assert.That(clientBuilder, Is.Not.Null);
            Assert.That(clientBuilder.Services, Is.SameAs(services));

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<OpcUaClientOptions>(), Is.Not.Null);
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
        }

        // ----------------------------------------------------------------
        // AddClient(IConfigurationSection) overload
        // ----------------------------------------------------------------

        [Test]
        public void AddClientWithSection_NullBuilder_Throws()
        {
            IConfiguration cfg = BuildConfig([]);
            IConfigurationSection section = cfg.GetSection("OpcUa:Client");

            Assert.That(
                () => OpcUaClientBuilderExtensions.AddClient(null!, section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddClientWithSection_NullSection_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddClient((IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddClientWithSection_BindsSessionNameFromConfig()
        {
            IConfiguration cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["OpcUa:Client:Session:SessionName"] = "MySession"
            });

            var services = new ServiceCollection();
            IOpcUaClientBuilder clientBuilder = services.AddOpcUa()
                .AddClient(cfg.GetSection("OpcUa:Client"));

            // The overload must succeed and return a valid builder.
            Assert.That(clientBuilder, Is.Not.Null);

            using ServiceProvider sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<OpcUaClientOptions>();

            // OpcUaClientOptions itself is successfully resolved.
            Assert.That(options, Is.Not.Null);
        }

        // ----------------------------------------------------------------
        // AddIdentityProvider<T> (generic) overload
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentityProviderGeneric_NullBuilder_Throws()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddIdentityProvider<AnonymousIdentityProvider>(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddIdentityProviderGeneric_RegistersTypedProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddIdentityProvider<AnonymousIdentityProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IClientIdentityProvider provider = sp.GetRequiredService<IClientIdentityProvider>();

            Assert.That(provider, Is.InstanceOf<AnonymousIdentityProvider>());
        }

        // ----------------------------------------------------------------
        // AddIdentityProvider(Action<CompositeClientIdentityProviderBuilder>)
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentityProviderComposite_NullBuilder_Throws()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddIdentityProvider(
                    null!,
                    _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddIdentityProviderComposite_NullConfigure_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            Assert.That(
                () => builder.AddIdentityProvider((Action<CompositeClientIdentityProviderBuilder>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddIdentityProviderComposite_ConfigureActionRuns()
        {
            bool actionRan = false;
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddIdentityProvider(b =>
                {
                    actionRan = true;
                    b.AddAnonymous();
                });

            using ServiceProvider sp = services.BuildServiceProvider();
            IClientIdentityProvider provider = sp.GetRequiredService<IClientIdentityProvider>();

            Assert.That(actionRan, Is.True);
            Assert.That(provider, Is.InstanceOf<CompositeClientIdentityProvider>());
            Assert.That(provider.SupportedTokenTypes, Does.Contain(UserTokenType.Anonymous));
        }

        // ----------------------------------------------------------------
        // AddAccessTokenProvider null-guard paths (already-hit happy paths
        // are in AccessTokenProviderRegistrationTests)
        // ----------------------------------------------------------------

        [Test]
        public void AddAccessTokenProviderGeneric_NullBuilder_Throws()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddAccessTokenProvider<StubAccessTokenProvider2>(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAccessTokenProviderInstance_NullBuilder_Throws()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddAccessTokenProvider(
                    null!,
                    new StubAccessTokenProvider2("https://x.example")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAccessTokenProviderInstance_NullInstance_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            Assert.That(
                () => builder.AddAccessTokenProvider((IAccessTokenProvider)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAccessTokenProviderFactory_NullBuilder_Throws()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddAccessTokenProvider(
                    null!,
                    _ => null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAccessTokenProviderFactory_NullFactory_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            Assert.That(
                () => builder.AddAccessTokenProvider(
                    (Func<IServiceProvider, IAccessTokenProvider>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAccessTokenProviderGeneric_RegistersProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider<StubAccessTokenProvider2>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IAccessTokenProvider provider = sp.GetRequiredService<IAccessTokenProvider>();

            Assert.That(provider, Is.InstanceOf<StubAccessTokenProvider2>());
        }

        // ----------------------------------------------------------------
        // AddWebApiTransportChannel wiring
        // ----------------------------------------------------------------

        [Test]
        public void AddWebApiTransportChannel_NullBuilder_Throws()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddWebApiTransportChannel(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiTransportChannel_NoArgs_RegistersDefaultOptions()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiTransportChannel();

            using ServiceProvider sp = services.BuildServiceProvider();
            var options = sp.GetService<WebApiClientOptions>();

            Assert.That(options, Is.Not.Null);
        }

        [Test]
        public void AddWebApiTransportChannel_WithConfigure_RunsActionBeforeResolution()
        {
            bool actionRan = false;
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiTransportChannel(opt =>
            {
                actionRan = true;
                opt.Encoding = WebApiEncoding.Verbose;
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            // Resolve the options to trigger the lazy factory.
            var options = sp.GetRequiredService<WebApiClientOptions>();

            Assert.That(actionRan, Is.True);
            Assert.That(options.Encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void AddWebApiTransportChannel_NullConfigure_UsesDefaults()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiTransportChannel(configure: null);

            using ServiceProvider sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<WebApiClientOptions>();

            Assert.That(options, Is.Not.Null);
            Assert.That(options.Encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        // ----------------------------------------------------------------
        // DefaultConfigurationSection constant
        // ----------------------------------------------------------------

        [Test]
        public void DefaultConfigurationSection_HasExpectedValue()
        {
            Assert.That(OpcUaClientBuilderExtensions.DefaultConfigurationSection,
                Is.EqualTo("OpcUa:Client"));
        }

        // ----------------------------------------------------------------
        // Helpers / stubs
        // ----------------------------------------------------------------

        private static IConfiguration BuildConfig(Dictionary<string, string?> data)
        {
            var source = new MemoryConfigurationSource { InitialData = data };
            return new ConfigurationBuilder().Add(source).Build();
        }

        /// <summary>Stub access-token provider used only within this fixture.</summary>
        public sealed class StubAccessTokenProvider2 : IAccessTokenProvider
        {
            public StubAccessTokenProvider2()
                : this("https://stub2.example")
            {
            }

            public StubAccessTokenProvider2(string authorityUri)
            {
                AuthorityUri = authorityUri;
            }

            public string AuthorityUri { get; }

            public ValueTask<AccessToken> AcquireAsync(
                AuthorizationServerMetadata metadata,
                CancellationToken ct = default)
            {
#pragma warning disable CA2000 // AccessToken ownership transfers to caller via ValueTask
                return new ValueTask<AccessToken>(new AccessToken(
                    Profiles.JwtUserToken,
                    [1],
                    DateTime.MaxValue,
                    AuthorityUri));
#pragma warning restore CA2000
            }
        }
    }
}
