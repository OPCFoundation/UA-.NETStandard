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

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Authentication;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// Targeted tests for the argument-null guards and less-exercised
    /// overloads of <see cref="OpcUaWebApiAuthenticationBuilderExtensions"/>:
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiAnonymousAuth"/>,
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiBearerAuth"/>,
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiBasicAuth"/>,
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiMutualTlsAuth"/>
    /// (with configure callback), and
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.UseJwtClaimIdentityProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthExtensionsNullGuards")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaWebApiAuthenticationBuilderExtensionsTests
    {
        // ─────────────────── AddWebApiAnonymousAuth ──────────────────────────

        [Test]
        public void AddWebApiAnonymousAuthThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiAnonymousAuth(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiAnonymousAuthReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiAnonymousAuth();

            Assert.That(returned, Is.SameAs(builder),
                "Extension must return the same builder for fluent chaining.");
        }

        // ─────────────────── AddWebApiBearerAuth ─────────────────────────────

        [Test]
        public void AddWebApiBearerAuthThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiBearerAuth(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiBearerAuthThrowsForNullConfigure()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWebApiBearerAuth(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiBearerAuthReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiBearerAuth(_ => { });

            Assert.That(returned, Is.SameAs(builder));
        }

        // ─────────────────── AddWebApiBasicAuth ──────────────────────────────

        [Test]
        public void AddWebApiBasicAuthThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiBasicAuth(
                    null!,
                    (_, _) => Task.FromResult<ClaimsPrincipal?>(null)),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiBasicAuthThrowsForNullValidate()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddWebApiBasicAuth(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddWebApiBasicAuthWithConfigureCallbackAppliesOptions()
        {
            var services = new ServiceCollection();
            bool configureCalled = false;

            services.AddOpcUa().AddWebApiBasicAuth(
                (_, _) => Task.FromResult<ClaimsPrincipal?>(null),
                options =>
                {
                    configureCalled = true;
                    options.Realm = "test-realm";
                });

            using ServiceProvider provider = services.BuildServiceProvider();

            // Options are lazily configured when first resolved — trigger resolution.
            _ = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<BasicAuthenticationOptions>>()
                .Get(WebApiAuthSchemes.Basic);

            Assert.That(configureCalled, Is.True,
                "The configure callback must be invoked when the options are first resolved.");
        }

        [Test]
        public void AddWebApiBasicAuthReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiBasicAuth(
                (_, _) => Task.FromResult<ClaimsPrincipal?>(null));

            Assert.That(returned, Is.SameAs(builder));
        }

        // ─────────────────── AddWebApiMutualTlsAuth ──────────────────────────

        [Test]
        public void AddWebApiMutualTlsAuthThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiMutualTlsAuth(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddWebApiMutualTlsAuthWithConfigureCallbackRegistersSchemeAsync()
        {
            var services = new ServiceCollection();
            bool configureCalled = false;

            services.AddOpcUa().AddWebApiMutualTlsAuth(options =>
            {
                configureCalled = true;
                options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            AuthenticationScheme? scheme = await provider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.MutualTls)
                .ConfigureAwait(false);

            Assert.That(scheme, Is.Not.Null,
                "MutualTls scheme must be registered when configure callback is provided.");

            // Options are lazily configured when first resolved — trigger resolution.
            _ = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CertificateAuthenticationOptions>>()
                .Get(WebApiAuthSchemes.MutualTls);

            Assert.That(configureCalled, Is.True,
                "The configure callback must be invoked when the options are first resolved.");
        }

        [Test]
        public void AddWebApiMutualTlsAuthReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWebApiMutualTlsAuth();

            Assert.That(returned, Is.SameAs(builder));
        }

        // ─────────────────── UseJwtClaimIdentityProvider ─────────────────────

        [Test]
        public void UseJwtClaimIdentityProviderThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaWebApiAuthenticationBuilderExtensions.UseJwtClaimIdentityProvider(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseJwtClaimIdentityProviderRegistersJwtProvider()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .UseJwtClaimIdentityProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            ISessionlessIdentityProvider identityProvider =
                provider.GetRequiredService<ISessionlessIdentityProvider>();

            Assert.That(identityProvider, Is.InstanceOf<JwtClaimSessionlessIdentityProvider>(),
                "UseJwtClaimIdentityProvider must replace the default identity provider " +
                "with JwtClaimSessionlessIdentityProvider.");
        }

        [Test]
        public void UseJwtClaimIdentityProviderWithConfigureAppliesOptions()
        {
            var services = new ServiceCollection();
            bool configureCalled = false;

            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .UseJwtClaimIdentityProvider(options =>
                {
                    configureCalled = true;
                    options.SubjectClaim = "custom-sub";
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<ISessionlessIdentityProvider>();

            Assert.That(configureCalled, Is.True,
                "The configure callback must be invoked when provided.");
        }

        [Test]
        public void UseJwtClaimIdentityProviderReplacesDefaultProvider()
        {
            var services = new ServiceCollection();

            // AddWebApiAnonymousAuth installs DefaultSessionlessIdentityProvider first.
            services.AddOpcUa()
                .AddWebApiAnonymousAuth()
                .AddWebApiBearerAuth(_ => { })
                .UseJwtClaimIdentityProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            ISessionlessIdentityProvider identityProvider =
                provider.GetRequiredService<ISessionlessIdentityProvider>();

            Assert.That(identityProvider, Is.InstanceOf<JwtClaimSessionlessIdentityProvider>(),
                "UseJwtClaimIdentityProvider must replace DefaultSessionlessIdentityProvider " +
                "when called after AddWebApiAnonymousAuth.");
        }

        [Test]
        public void UseJwtClaimIdentityProviderWithoutConfigureCallbackUsesDefaults()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .UseJwtClaimIdentityProvider(configure: null);

            using ServiceProvider provider = services.BuildServiceProvider();
            ISessionlessIdentityProvider identityProvider =
                provider.GetRequiredService<ISessionlessIdentityProvider>();

            Assert.That(identityProvider, Is.InstanceOf<JwtClaimSessionlessIdentityProvider>(),
                "Passing null configure must still register the provider with default options.");
        }

        [Test]
        public void UseJwtClaimIdentityProviderReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa().AddWebApiBearerAuth(_ => { });

            IOpcUaBuilder returned = builder.UseJwtClaimIdentityProvider();

            Assert.That(returned, Is.SameAs(builder));
        }
    }
}
