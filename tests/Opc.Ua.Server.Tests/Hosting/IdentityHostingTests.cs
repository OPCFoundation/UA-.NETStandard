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
 *
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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Identity")]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class IdentityHostingTests
    {
        [Test]
        public void DefaultAuthenticatorsRespectFlagsAndAvailableDependencies()
        {
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "false"
            });

            using ServiceProvider withoutDependencies = CreateServices(configuration).BuildServiceProvider();
            Assert.That(CreateAuthenticators(withoutDependencies), Has.Exactly(1).TypeOf<AnonymousAuthenticator>());

            ServiceCollection services = CreateServices(configuration);
            services.AddSingleton(Mock.Of<IUserDatabase>());
            services.AddSingleton(Mock.Of<IUserManagement>());
            services.AddSingleton(Mock.Of<ICertificateValidatorEx>());
            using ServiceProvider withDependencies = services.BuildServiceProvider();

            IList<IUserTokenAuthenticator> authenticators = CreateAuthenticators(withDependencies);
            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Exactly(1).TypeOf<UserNamePasswordAuthenticator>());
            Assert.That(authenticators, Has.Exactly(1).TypeOf<X509Authenticator>());

            IConfiguration disabledConfiguration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "false"
            });
            using ServiceProvider disabled = CreateServices(disabledConfiguration).BuildServiceProvider();
            Assert.That(CreateAuthenticators(disabled), Is.Empty);
        }

        [Test]
        public void ConfiguredJwtIssuerRegistersResolverAndAuthenticator()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "true",
                ["OpcUa:Server:Identity:Defaults:ExpectedAudience"] = "urn:opcua:test-server",
                ["OpcUa:Server:Identity:Issuers:0:IssuerUri"] = "https://issuer.example.test",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:Kid"] = "kid-rsa",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:Algorithm"] = "RS256",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:RsaModulus"] = Base64UrlEncode(parameters.Modulus),
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:RsaExponent"] = Base64UrlEncode(parameters.Exponent)
            });
            using ServiceProvider services = CreateServices(configuration).BuildServiceProvider();

            Assert.That(services.GetServices<IIssuerKeyResolver>().Count(), Is.EqualTo(1));
            Assert.That(CreateAuthenticators(services), Has.Exactly(1).TypeOf<JwtAuthenticator>());
        }

        [Test]
        public void DefaultAuthenticatorsHonoredViaActionOptionsIdentity()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(o =>
            {
                o.Identity.Defaults.EnableAnonymous = true;
                o.Identity.Defaults.EnableUserNamePassword = false;
                o.Identity.Defaults.EnableX509 = false;
                o.Identity.Defaults.EnableJwt = false;
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IList<IUserTokenAuthenticator> authenticators = CreateAuthenticators(sp);
            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Count.EqualTo(1));
        }

        [Test]
        public void JwtIssuerHonoredViaActionOptionsIdentityIssuers()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(o =>
            {
                o.Identity.Defaults.EnableAnonymous = false;
                o.Identity.Defaults.EnableUserNamePassword = false;
                o.Identity.Defaults.EnableX509 = false;
                o.Identity.Defaults.EnableJwt = true;
                o.Identity.Defaults.ExpectedAudience = "urn:opcua:test-server";
                o.Identity.Issuers.Add(new JwtIssuerOptions
                {
                    IssuerUri = "https://issuer.example.test",
                    StaticKeys =
                    {
                        new JwtStaticKeyOptions
                        {
                            Kid = "kid-rsa",
                            Algorithm = "RS256",
                            RsaModulus = Base64UrlEncode(parameters.Modulus),
                            RsaExponent = Base64UrlEncode(parameters.Exponent)
                        }
                    }
                });
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(CreateAuthenticators(sp), Has.Exactly(1).TypeOf<JwtAuthenticator>());
        }

        [Test]
        public void JwksJwtIssuerHonoredViaActionOptionsWithoutHttpClientRegistration()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(o =>
            {
                o.Identity.Defaults.EnableAnonymous = false;
                o.Identity.Defaults.EnableUserNamePassword = false;
                o.Identity.Defaults.EnableX509 = false;
                o.Identity.Defaults.EnableJwt = true;
                o.Identity.Defaults.ExpectedAudience = "urn:opcua:test-server";
                o.Identity.Issuers.Add(new JwtIssuerOptions
                {
                    IssuerUri = "https://issuer.example.test",
                    JwksUri = "https://issuer.example.test/.well-known/jwks"
                });
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(CreateAuthenticators(sp), Has.Exactly(1).TypeOf<JwtAuthenticator>());
        }

        [Test]
        public void BareConfigurationServerRegistersAnonymousFallback()
        {
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>());
            using ServiceProvider sp = CreateServices(configuration).BuildServiceProvider();

            IList<IUserTokenAuthenticator> authenticators = CreateAuthenticators(sp);

            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExplicitDefaultAuthenticatorsDoNotDoubleRegisterAnonymousFallback()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.Identity.Defaults.EnableAnonymous = false;
                    o.Identity.Defaults.EnableUserNamePassword = false;
                    o.Identity.Defaults.EnableX509 = false;
                    o.Identity.Defaults.EnableJwt = false;
                })
                .AddDefaultIdentityAuthenticators(o =>
                {
                    o.EnableAnonymous = true;
                    o.EnableUserNamePassword = false;
                    o.EnableX509 = false;
                    o.EnableJwt = false;
                });

            using ServiceProvider sp = services.BuildServiceProvider();
            IList<IUserTokenAuthenticator> authenticators = CreateAuthenticators(sp);

            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Count.EqualTo(1));
        }

        [Test]
        public void CustomAuthenticatorOnConfigurationServerDoesNotRejectAnonymous()
        {
            // No Identity section: the custom authenticator is the only configuration and
            // must not disable anonymous access (the endpoint policy still governs it).
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>());
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(configuration)
                .AddIdentityAuthenticator<UserNameStubAuthenticator>();
            using ServiceProvider sp = services.BuildServiceProvider();

            List<IUserTokenAuthenticator> authenticators = OpcUaServerHostedService.CreateIdentityAuthenticators(
                sp.GetServices<OpcUaServerIdentityAuthenticatorRegistration>(), sp, null);

            Assert.That(authenticators, Has.Exactly(1).TypeOf<UserNameStubAuthenticator>());
            Assert.That(authenticators, Has.None.TypeOf<OpcUaServerHostedService.AnonymousRejectingAuthenticator>());
        }

        [Test]
        public void DisabledAnonymousDefaultsRejectAnonymous()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(o =>
            {
                o.Identity.Defaults.EnableAnonymous = false;
                o.Identity.Defaults.EnableUserNamePassword = true;
                o.Identity.Defaults.EnableX509 = false;
                o.Identity.Defaults.EnableJwt = false;
            }).AddIdentityAuthenticator<UserNameStubAuthenticator>();
            using ServiceProvider sp = services.BuildServiceProvider();

            List<IUserTokenAuthenticator> authenticators = OpcUaServerHostedService.CreateIdentityAuthenticators(
                sp.GetServices<OpcUaServerIdentityAuthenticatorRegistration>(), sp, null);

            Assert.That(authenticators, Has.Exactly(1).TypeOf<OpcUaServerHostedService.AnonymousRejectingAuthenticator>());
            Assert.That(authenticators, Has.None.TypeOf<AnonymousAuthenticator>());
        }

        /// <summary>
        /// With anonymous access disabled and no explicit UserTokenPolicies the implicit
        /// Anonymous policy is not advertised; the endpoint lists the token types the
        /// authenticators accept instead (Part 4 7.14, 7.41).
        /// </summary>
        [Test]
        public void DisabledAnonymousDefaultsReplaceDefaultAnonymousPolicy()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(o =>
            {
                o.Identity.Defaults.EnableAnonymous = false;
                o.Identity.Defaults.EnableUserNamePassword = true;
                o.Identity.Defaults.EnableX509 = false;
                o.Identity.Defaults.EnableJwt = false;
            }).AddIdentityAuthenticator<UserNameStubAuthenticator>();
            using ServiceProvider sp = services.BuildServiceProvider();
            List<IUserTokenAuthenticator> authenticators = OpcUaServerHostedService.CreateIdentityAuthenticators(
                sp.GetServices<OpcUaServerIdentityAuthenticatorRegistration>(), sp, null);
            authenticators.Add(new IssuedTokenStubAuthenticator());

            ArrayOf<UserTokenPolicy> policies = OpcUaServerHostedService.ReplaceDefaultAnonymousUserTokenPolicy(
                new ArrayOf<UserTokenPolicy>(new[] { new UserTokenPolicy(UserTokenType.Anonymous) }),
                authenticators);

            UserTokenPolicy[] result = [.. policies];
            Assert.That(result.Select(p => p.TokenType), Is.EqualTo(new[]
            {
                UserTokenType.UserName,
                UserTokenType.IssuedToken
            }));
            Assert.That(result[1].IssuedTokenType, Is.EqualTo(IssuedTokenStubAuthenticator.ProfileUri));
        }

        [Test]
        public void NoIdentityRegistrationKeepsAnonymousDefault()
        {
            using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();

            List<IUserTokenAuthenticator> authenticators = OpcUaServerHostedService.CreateIdentityAuthenticators(
                [], sp, null);

            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// IssuedToken authenticator stub.
        /// </summary>
        private sealed class IssuedTokenStubAuthenticator : IUserTokenAuthenticator
        {
            public const string ProfileUri = "http://opcfoundation.org/UA/UserToken#JWT";

            public UserTokenType TokenType => UserTokenType.IssuedToken;

            public string IssuedTokenProfileUri => ProfileUri;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }

        /// <summary>
        /// UserName authenticator stub; created by the container through AddIdentityAuthenticator.
        /// </summary>
        public sealed class UserNameStubAuthenticator : IUserTokenAuthenticator
        {
            public UserTokenType TokenType => UserTokenType.UserName;

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }

        private static ServiceCollection CreateServices(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(configuration);
            return services;
        }

        private static List<IUserTokenAuthenticator> CreateAuthenticators(IServiceProvider services)
        {
            var authenticators = new List<IUserTokenAuthenticator>();
            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in
                services.GetServices<OpcUaServerIdentityAuthenticatorRegistration>())
            {
                authenticators.AddRange(registration.CreateAuthenticators(services, null));
            }
            return authenticators;
        }

        private static IConfiguration CreateConfiguration(IDictionary<string, string> values)
        {
            var source = new MemoryConfigurationSource { InitialData = values };
            var builder = new ConfigurationBuilder();
            builder.Sources.Add(source);
            return builder.Build();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
