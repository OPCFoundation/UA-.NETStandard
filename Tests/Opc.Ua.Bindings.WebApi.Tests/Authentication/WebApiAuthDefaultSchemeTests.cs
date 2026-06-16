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

using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Authentication;

namespace Opc.Ua.Bindings.WebApi.Tests.Authentication
{
    /// <summary>
    /// Regression tests for the WebApi auth-scheme default + policy
    /// scheme installed by every AddWebApi*Auth() extension. Pins the
    /// behaviour fixed by alert <c>sec-5-default-scheme</c>: previously
    /// the extensions called <c>AddAuthentication()</c> with no default
    /// scheme, so when more than one scheme was registered
    /// <c>UseAuthentication()</c> became a no-op (it requires a default
    /// scheme to know which handler to run) and <c>HttpContext.User</c>
    /// stayed anonymous for every request — silently bypassing all
    /// configured auth.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiAuthDefaultSchemeTests
    {
        [Test]
        public void AddWebApiBearerAuthRegistersPolicySchemeAsDefault()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiBearerAuth(_ => { });

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationOptions options = serviceProvider
                .GetRequiredService<IOptions<AuthenticationOptions>>().Value;

            Assert.That(options.DefaultScheme, Is.EqualTo(WebApiAuthSchemes.Default),
                "AddWebApiBearerAuth must install the WebApi policy scheme as the " +
                "default — without it UseAuthentication() is a no-op.");
            Assert.That(options.DefaultAuthenticateScheme, Is.EqualTo(WebApiAuthSchemes.Default));
            Assert.That(options.DefaultChallengeScheme, Is.EqualTo(WebApiAuthSchemes.Default));
            Assert.That(options.DefaultForbidScheme, Is.EqualTo(WebApiAuthSchemes.Default));
        }

        [Test]
        public async Task AddWebApiBearerAuthRegistersPolicySchemeOnSchemeProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiBearerAuth(_ => { });

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IAuthenticationSchemeProvider schemeProvider = serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>();
            AuthenticationScheme? policyScheme = await schemeProvider
                .GetSchemeAsync(WebApiAuthSchemes.Default)
                .ConfigureAwait(false);

            Assert.That(policyScheme, Is.Not.Null,
                "The OpcUaWebApi.Default policy scheme must be registered.");
        }

        [Test]
        public async Task AddWebApiBasicAuthAloneRegistersPolicySchemeAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiBasicAuth((_, _) => Task.FromResult<ClaimsPrincipal?>(null));

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationOptions options = serviceProvider
                .GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            Assert.That(options.DefaultScheme, Is.EqualTo(WebApiAuthSchemes.Default));

            AuthenticationScheme? policyScheme = await serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.Default).ConfigureAwait(false);
            Assert.That(policyScheme, Is.Not.Null);
        }

        [Test]
        public async Task AddWebApiMutualTlsAuthAloneRegistersPolicySchemeAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiMutualTlsAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            AuthenticationOptions options = serviceProvider
                .GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            Assert.That(options.DefaultScheme, Is.EqualTo(WebApiAuthSchemes.Default));

            AuthenticationScheme? policyScheme = await serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(WebApiAuthSchemes.Default).ConfigureAwait(false);
            Assert.That(policyScheme, Is.Not.Null);
        }

        [Test]
        public async Task PolicySchemeIsRegisteredExactlyOnceAcrossMultipleAuthOptInsAsync()
        {
            // Calling all three AddWebApi*Auth() extensions must not
            // produce duplicate registrations of the policy scheme.
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .AddWebApiBasicAuth((_, _) => Task.FromResult<ClaimsPrincipal?>(null))
                .AddWebApiMutualTlsAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IAuthenticationSchemeProvider schemeProvider = serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>();
            System.Collections.Generic.IEnumerable<AuthenticationScheme> allSchemes = await schemeProvider
                .GetAllSchemesAsync()
                .ConfigureAwait(false);
            int policyCount = allSchemes.Count(s => s.Name == WebApiAuthSchemes.Default);

            Assert.That(policyCount, Is.EqualTo(1),
                "The WebApi policy scheme must be registered exactly once even " +
                "across multiple AddWebApi*Auth() calls.");
        }

        [Test]
        public async Task AllConfiguredSchemesAreRegisteredAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddWebApiBearerAuth(_ => { })
                .AddWebApiBasicAuth((_, _) => Task.FromResult<ClaimsPrincipal?>(null))
                .AddWebApiMutualTlsAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IAuthenticationSchemeProvider schemeProvider = serviceProvider
                .GetRequiredService<IAuthenticationSchemeProvider>();

            AuthenticationScheme? bearer = await schemeProvider
                .GetSchemeAsync(WebApiAuthSchemes.Bearer).ConfigureAwait(false);
            AuthenticationScheme? basic = await schemeProvider
                .GetSchemeAsync(WebApiAuthSchemes.Basic).ConfigureAwait(false);
            AuthenticationScheme? mtls = await schemeProvider
                .GetSchemeAsync(WebApiAuthSchemes.MutualTls).ConfigureAwait(false);
            AuthenticationScheme? policy = await schemeProvider
                .GetSchemeAsync(WebApiAuthSchemes.Default).ConfigureAwait(false);

            Assert.That(bearer, Is.Not.Null, "Bearer scheme must be registered.");
            Assert.That(basic, Is.Not.Null, "Basic scheme must be registered.");
            Assert.That(mtls, Is.Not.Null, "Mutual TLS scheme must be registered.");
            Assert.That(policy, Is.Not.Null, "Policy scheme must be registered.");
        }

        [Test]
        public void AnonymousAuthDoesNotInstallPolicyScheme()
        {
            // AddWebApiAnonymousAuth() does not register any auth handler,
            // so it must not change the default scheme (preserves the
            // historical anonymous request flow).
            var services = new ServiceCollection();
            services.AddOpcUa().AddWebApiAnonymousAuth();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IOptions<AuthenticationOptions>? options = serviceProvider
                .GetService<IOptions<AuthenticationOptions>>();

            // Either no AuthenticationOptions at all, or DefaultScheme is null.
            string? defaultScheme = options?.Value.DefaultScheme;
            Assert.That(defaultScheme, Is.Null,
                "Anonymous-only registration must not install a default auth scheme " +
                "(no behaviour change for bindings that don't opt into auth).");
        }
    }
}
