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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    [Category("ClientBuilder")]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class IdentityProviderConfigurationTests
    {
        [Test]
        public async Task AnonymousOnlyBuildsAnonymousProvider()
        {
            IConfiguration configuration = BuildConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Client:Identity:EnableAnonymous"] = "true"
            });
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddIdentityProvider(configuration.GetSection("OpcUa:Client:Identity"));

            using ServiceProvider sp = services.BuildServiceProvider();
            IClientIdentityProvider provider = sp.GetRequiredService<IClientIdentityProvider>();

            Assert.That(provider.SupportedTokenTypes, Is.EqualTo([UserTokenType.Anonymous]));
            CanSatisfyResult anon = await provider
                .CanSatisfyAsync(CreatePolicy(UserTokenType.Anonymous), CreateContext())
                .ConfigureAwait(false);
            CanSatisfyResult user = await provider
                .CanSatisfyAsync(CreatePolicy(UserTokenType.UserName), CreateContext())
                .ConfigureAwait(false);
            CanSatisfyResult cert = await provider
                .CanSatisfyAsync(CreatePolicy(UserTokenType.Certificate), CreateContext())
                .ConfigureAwait(false);
            CanSatisfyResult issued = await provider
                .CanSatisfyAsync(CreateIssuedTokenPolicy(), CreateContext())
                .ConfigureAwait(false);
            Assert.That(anon.CanSatisfy, Is.True);
            Assert.That(user.CanSatisfy, Is.False);
            Assert.That(cert.CanSatisfy, Is.False);
            Assert.That(issued.CanSatisfy, Is.False);
        }

        [Test]
        public void ConfiguredOrderControlsCompositePreference()
        {
            IConfiguration configuration = BuildConfiguration(CreateFullIdentityConfiguration(order: true));
            var services = new ServiceCollection();
            AddIdentityDependencies(services);
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddIdentityProvider(configuration.GetSection("OpcUa:Client:Identity"));

            using ServiceProvider sp = services.BuildServiceProvider();
            IClientIdentityProvider provider = sp.GetRequiredService<IClientIdentityProvider>();

            Assert.That(provider.SupportedTokenTypes, Is.EqualTo(
            [
                UserTokenType.IssuedToken,
                UserTokenType.Certificate,
                UserTokenType.UserName
            ]));
        }

        [Test]
        public void EmptyOrderPreservesRegistrationOrder()
        {
            IConfiguration configuration = BuildConfiguration(CreateFullIdentityConfiguration(order: false));
            var services = new ServiceCollection();
            AddIdentityDependencies(services);
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddIdentityProvider(configuration.GetSection("OpcUa:Client:Identity"));

            using ServiceProvider sp = services.BuildServiceProvider();
            IClientIdentityProvider provider = sp.GetRequiredService<IClientIdentityProvider>();

            Assert.That(provider.SupportedTokenTypes, Is.EqualTo(
            [
                UserTokenType.UserName,
                UserTokenType.Certificate,
                UserTokenType.IssuedToken
            ]));
        }

        [Test]
        public void MissingIssuedTokenAuthorityThrowsDuringProviderConstruction()
        {
            IConfiguration configuration = BuildConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Client:Identity:EnableAnonymous"] = "false",
                ["OpcUa:Client:Identity:IssuedToken:AuthorityUri"] = "https://missing.example",
                ["OpcUa:Client:Identity:IssuedToken:ProfileUri"] = Profiles.JwtUserToken
            });
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider(new StubAccessTokenProvider("https://issuer.example"))
                .AddIdentityProvider(configuration.GetSection("OpcUa:Client:Identity"));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                using ServiceProvider sp = services.BuildServiceProvider();
                _ = sp.GetRequiredService<IClientIdentityProvider>();
            });

            Assert.That(ex.Message, Does.Contain("https://missing.example"));
            Assert.That(ex.Message, Does.Contain("IAccessTokenProvider"));
        }

        private static Dictionary<string, string> CreateFullIdentityConfiguration(bool order)
        {
            var data = new Dictionary<string, string>
            {
                ["OpcUa:Client:Identity:EnableAnonymous"] = "false",
                ["OpcUa:Client:Identity:UserName:UserName"] = "operator",
                ["OpcUa:Client:Identity:UserName:SecretName"] = "operator-password",
                ["OpcUa:Client:Identity:UserName:SecretStoreType"] = "Fake",
                ["OpcUa:Client:Identity:X509:StoreType"] = CertificateStoreType.Directory,
                ["OpcUa:Client:Identity:X509:StorePath"] = "pki/user",
                ["OpcUa:Client:Identity:X509:SubjectName"] = "CN=Operator",
                ["OpcUa:Client:Identity:IssuedToken:AuthorityUri"] = "https://issuer.example",
                ["OpcUa:Client:Identity:IssuedToken:ProfileUri"] = Profiles.JwtUserToken
            };
            if (order)
            {
                data["OpcUa:Client:Identity:Order:0"] = "IssuedToken";
                data["OpcUa:Client:Identity:Order:1"] = "X509";
                data["OpcUa:Client:Identity:Order:2"] = "UserName";
            }
            return data;
        }

        private static IConfiguration BuildConfiguration(Dictionary<string, string> data)
        {
            var source = new MemoryConfigurationSource
            {
                InitialData = data
            };
            return new ConfigurationBuilder()
                .Add(source)
                .Build();
        }

        private static void AddIdentityDependencies(IServiceCollection services)
        {
            services.AddSingleton<ISecretRegistry>(new FakeSecretRegistry());
            services.AddSingleton<ICertificateProvider>(new FakeCertificateProvider());
            services.AddSingleton<ICertificatePasswordProvider>(new FakeCertificatePasswordProvider());
            services.AddSingleton<IAccessTokenProvider>(
                new StubAccessTokenProvider("https://issuer.example"));
        }

        private static UserTokenPolicy CreatePolicy(UserTokenType tokenType)
        {
            return new UserTokenPolicy
            {
                PolicyId = tokenType.ToString(),
                TokenType = tokenType,
                SecurityPolicyUri = SecurityPolicies.None
            };
        }

        private static UserTokenPolicy CreateIssuedTokenPolicy()
        {
            UserTokenPolicy policy = CreatePolicy(UserTokenType.IssuedToken);
            policy.IssuedTokenType = Profiles.JwtUserToken;
            return policy;
        }

        private static IdentitySelectionContext CreateContext()
        {
            var endpoint = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = []
            };
            return new IdentitySelectionContext(
                endpoint,
                ArrayOf<UserTokenPolicy>.Empty,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private sealed class FakeSecretRegistry : ISecretRegistry
        {
            public void RegisterStore(ISecretStore store)
            {
            }

            public ISecret TryGet(SecretIdentifier id)
            {
                return null;
            }

            public ValueTask<ISecret> GetAsync(
                SecretIdentifier id,
                CancellationToken ct = default)
            {
                return new ValueTask<ISecret>((ISecret)null);
            }
        }

        private sealed class FakeCertificateProvider : ICertificateProvider
        {
            public Certificate TryGetPrivateKeyCertificate(string thumbprint)
            {
                return null;
            }

            public ValueTask<Certificate> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider passwordProvider = null,
                string applicationUri = null,
                CancellationToken ct = default)
            {
                return new ValueTask<Certificate>((Certificate)null);
            }
        }

        private sealed class FakeCertificatePasswordProvider : ICertificatePasswordProvider
        {
            public char[] GetPassword(CertificateIdentifier certificateIdentifier)
            {
                return [];
            }
        }

        private sealed class StubAccessTokenProvider : IAccessTokenProvider
        {
            public StubAccessTokenProvider(string authorityUri)
            {
                AuthorityUri = authorityUri;
            }

            public string AuthorityUri { get; }

            public ValueTask<AccessToken> AcquireAsync(
                AuthorizationServerMetadata metadata,
                CancellationToken ct = default)
            {
#pragma warning disable CA2000 // ownership of the AccessToken transfers to the caller via the returned ValueTask
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
