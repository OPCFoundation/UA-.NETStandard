// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Bindings.WebApi.Authentication;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// DI registration tests for the REST authentication-mode extensions.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiAuthExtensionsTests
    {
        [Test]
        public void AddWebApiAnonymousAuthRegistersDefaultIdentityProvider()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiAnonymousAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            ISessionlessIdentityProvider identityProvider =
                serviceProvider.GetRequiredService<ISessionlessIdentityProvider>();

            Assert.That(identityProvider, Is.InstanceOf<DefaultSessionlessIdentityProvider>());
        }

        [Test]
        public async Task AddWebApiBearerAuthRegistersJwtBearerScheme()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiBearerAuth(_ => { });

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationScheme? scheme = await serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.Bearer)
                .ConfigureAwait(false);

            Assert.That(scheme, Is.Not.Null);
            Assert.That(scheme!.HandlerType, Is.EqualTo(typeof(JwtBearerHandler)));
        }

        [Test]
        public async Task AddWebApiBasicAuthRegistersBasicScheme()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiBasicAuth(
                (_, _) => Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal()));

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationScheme? scheme = await serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.Basic)
                .ConfigureAwait(false);
            BasicAuthenticationOptions options = serviceProvider
                .GetRequiredService<IOptionsMonitor<BasicAuthenticationOptions>>()
                .Get(WebApiAuthSchemes.Basic);

            Assert.That(scheme, Is.Not.Null);
            Assert.That(scheme!.HandlerType, Is.EqualTo(typeof(BasicAuthenticationHandler)));
            Assert.That(options.ValidateCredentials, Is.Not.Null);
        }

        [Test]
        public async Task AddWebApiMutualTlsAuthRegistersCertificateScheme()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddWebApiMutualTlsAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationScheme? scheme = await serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.MutualTls)
                .ConfigureAwait(false);

            Assert.That(scheme, Is.Not.Null);
            Assert.That(
                scheme!.HandlerType.FullName,
                Is.EqualTo("Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationHandler"));
        }

        [Test]
        public async Task MultipleAuthModesComposeWithoutCollision()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .AddWebApiBasicAuth((_, _) => Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal()))
                .AddWebApiMutualTlsAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IAuthenticationSchemeProvider schemeProvider =
                serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

            AuthenticationScheme? bearer =
                await schemeProvider.GetSchemeAsync(WebApiAuthSchemes.Bearer).ConfigureAwait(false);
            AuthenticationScheme? basic =
                await schemeProvider.GetSchemeAsync(WebApiAuthSchemes.Basic).ConfigureAwait(false);
            AuthenticationScheme? mutualTls =
                await schemeProvider.GetSchemeAsync(WebApiAuthSchemes.MutualTls).ConfigureAwait(false);

            Assert.That(bearer, Is.Not.Null);
            Assert.That(basic, Is.Not.Null);
            Assert.That(mutualTls, Is.Not.Null);
        }
    }
}
