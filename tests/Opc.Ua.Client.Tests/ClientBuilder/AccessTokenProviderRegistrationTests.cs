/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    [Category("ClientBuilder")]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AccessTokenProviderRegistrationTests
    {
        private static readonly string[] s_expectedAuthorityOrder =
        [
            "https://one.example",
            "https://two.example"
        ];

        [Test]
        public void AddAccessTokenProviderTypedRegistersProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider<TypedAccessTokenProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();
            List<IAccessTokenProvider> providers = [.. sp.GetServices<IAccessTokenProvider>()];

            Assert.That(providers, Has.Count.EqualTo(1));
            Assert.That(providers[0], Is.InstanceOf<TypedAccessTokenProvider>());
            Assert.That(providers[0].AuthorityUri, Is.EqualTo(TypedAccessTokenProvider.Uri));
        }

        [Test]
        public void AddAccessTokenProviderInstanceRegistersProvider()
        {
            var services = new ServiceCollection();
            var instance = new StubAccessTokenProvider("https://instance.example");
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider(instance);

            using ServiceProvider sp = services.BuildServiceProvider();
            List<IAccessTokenProvider> providers = [.. sp.GetServices<IAccessTokenProvider>()];

            Assert.That(providers, Has.Count.EqualTo(1));
            Assert.That(providers[0], Is.SameAs(instance));
        }

        [Test]
        public void AddAccessTokenProviderFactoryRegistersProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider(_ => new StubAccessTokenProvider("https://factory.example"));

            using ServiceProvider sp = services.BuildServiceProvider();
            List<IAccessTokenProvider> providers = [.. sp.GetServices<IAccessTokenProvider>()];

            Assert.That(providers, Has.Count.EqualTo(1));
            Assert.That(providers[0].AuthorityUri, Is.EqualTo("https://factory.example"));
        }

        [Test]
        public void AddAccessTokenProviderAllowsMultipleAuthorities()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(_ => { })
                .AddAccessTokenProvider(new StubAccessTokenProvider("https://one.example"))
                .AddAccessTokenProvider(_ => new StubAccessTokenProvider("https://two.example"));

            using ServiceProvider sp = services.BuildServiceProvider();
            List<string> authorities = [.. sp.GetServices<IAccessTokenProvider>().Select(provider => provider.AuthorityUri)];

            Assert.That(authorities, Is.EqualTo(s_expectedAuthorityOrder));
        }

        public sealed class TypedAccessTokenProvider : StubAccessTokenProvider
        {
            public const string Uri = "https://typed.example";

            public TypedAccessTokenProvider()
                : base(Uri)
            {
            }
        }

        public class StubAccessTokenProvider : IAccessTokenProvider
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
