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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Directly exercises <see cref="OpcUaServerHostedService"/> paths that
    /// are not reached by the broader fluent-API hosting tests: the
    /// constructor's null-argument guards, the post-start historian/alias
    /// registry wiring (<c>RegisterPostStartRegistries</c>) on a plain
    /// (non dependency-injection-aware) <see cref="StandardServer"/>, and
    /// the matched-authenticator branches of <c>HasMatchingAuthenticator</c>
    /// for <see cref="UserTokenType.Certificate"/> and
    /// <see cref="UserTokenType.IssuedToken"/>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class OpcUaServerHostedServiceCoverageTests
    {
        [Test]
        public void ConstructorThrowsForNullOptions()
        {
            Assert.That(
                () => new OpcUaServerHostedService(
                    null!,
                    NUnitTelemetryContext.Create(isServer: true),
                    Mock.Of<IApplicationInstanceFactory>(),
                    [],
                    [],
                    [],
                    [],
                    [],
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IOpcUaServerFactory>(),
                    NullLogger<OpcUaServerHostedService>.Instance),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("options"));
        }

        [Test]
        public void ConstructorThrowsForNullConfigurationProviders()
        {
            Assert.That(
                () => new OpcUaServerHostedService(
                    Options.Create(new OpcUaServerOptions()),
                    NUnitTelemetryContext.Create(isServer: true),
                    Mock.Of<IApplicationInstanceFactory>(),
                    null!,
                    [],
                    [],
                    [],
                    [],
                    Mock.Of<IServiceProvider>(),
                    Mock.Of<IOpcUaServerFactory>(),
                    NullLogger<OpcUaServerHostedService>.Instance),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("configurationProviders"));
        }

        [Test]
        public async Task RegisterPostStartRegistriesWiresHistorianAndAliasStoresOnPlainServerAsync()
        {
            RegistryCaptureServer.Reset();
            var historian = new Mock<IHistorianProvider>();
            var directStore = new Mock<IAliasNameStore>();
            directStore.SetupGet(s => s.RootCategories).Returns([]);
            var registrySourcedStore = new Mock<IAliasNameStore>();
            registrySourcedStore.SetupGet(s => s.RootCategories).Returns([]);
            var sourceRegistry = new Mock<IAliasNameStoreRegistry>();
            sourceRegistry.SetupGet(r => r.Stores).Returns([registrySourcedStore.Object]);

            var loggerProvider = new CapturingLoggerProvider();
            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    services.AddOpcUa()
                        .AddServer<RegistryCaptureServer>(o => ConfigureHostedOptions(o, "PostStartRegistries"))
                        .AddHistorian(historian.Object)
                        .AddAliasNameStore(directStore.Object)
                        .AddAliasNameStoreRegistry(sourceRegistry.Object);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => RegistryCaptureServer.StartedServer != null,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True);

            IServerInternal server = RegistryCaptureServer.StartedServer ??
                throw new InvalidOperationException("The server did not start.");

            var historianRegistryProvider = (IHistorianRegistryProvider)server;
            Assert.That(
                historianRegistryProvider.HistorianRegistry.Providers,
                Has.Member(historian.Object));

            var aliasRegistryProvider = (IAliasNameStoreRegistryProvider)server;
            Assert.That(
                aliasRegistryProvider.AliasNameStoreRegistry.Stores,
                Has.Member(directStore.Object));
            Assert.That(
                aliasRegistryProvider.AliasNameStoreRegistry.Stores,
                Has.Member(registrySourcedStore.Object));
        }

        [Test]
        public async Task MatchingCertificateAndIssuedTokenPoliciesDoNotLogUnmatchedWarningAsync()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            var loggerProvider = new CapturingLoggerProvider();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    services.AddSingleton(Mock.Of<ICertificateValidatorEx>());
                    services.AddOpcUa().AddServer(o =>
                    {
                        ConfigureHostedOptions(o, "MatchedAuthenticators");
                        o.UserTokenPolicies.Clear();
                        o.UserTokenPolicies.Add(
                            new OpcUaUserTokenPolicy { TokenType = UserTokenType.Certificate });
                        o.UserTokenPolicies.Add(
                            new OpcUaUserTokenPolicy { TokenType = UserTokenType.IssuedToken });
                        o.Identity.Defaults.EnableAnonymous = false;
                        o.Identity.Defaults.EnableUserNamePassword = false;
                        o.Identity.Defaults.EnableX509 = true;
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
                                    RsaModulus = Base64UrlEncode(parameters.Modulus!),
                                    RsaExponent = Base64UrlEncode(parameters.Exponent!)
                                }
                            }
                        });
                    });
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => loggerProvider.Messages.Any(
                        message => message.Contains("OPC UA server listening", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True);

            Assert.That(
                loggerProvider.Messages.Any(
                    message => message.Contains(
                        "without a matching identity authenticator", StringComparison.Ordinal)),
                Is.False);
        }

        [Test]
        public async Task HostedServiceWiresOptionalFeaturesAndMatchesUserNamePolicyAsync()
        {
            RegistryCaptureServer.Reset();
            var augmenter = new Mock<IIdentityAugmenter>();
            var loggerProvider = new CapturingLoggerProvider();
            bool rateLimitsConfigured = false;

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    services.AddSingleton<ITransportBindingRegistry>(TestTransportBindings.WithAllSchemes());
                    services.AddSingleton(new ServerComplexTypeOptions { Enabled = false });
                    services.AddSingleton(Mock.Of<IUserDatabase>());
                    services.AddSingleton(Mock.Of<IUserManagement>());
                    services.AddOpcUa()
                        .AddServer<RegistryCaptureServer>(o =>
                        {
                            ConfigureHostedOptions(o, "OptionalFeatures");
                            o.UserTokenPolicies.Clear();
                            o.UserTokenPolicies.Add(
                                new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                            o.Identity.Defaults.EnableAnonymous = false;
                            o.Identity.Defaults.EnableUserNamePassword = true;
                            o.ConfigureRateLimits = _ => rateLimitsConfigured = true;
                        })
                        .AddIdentityAugmenter(_ => augmenter.Object);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => RegistryCaptureServer.StartedServer != null,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True);

            IServerInternal server = RegistryCaptureServer.StartedServer ??
                throw new InvalidOperationException("The server did not start.");

            Assert.That(RegistryCaptureServer.StartedInstance, Is.Not.Null);
            Assert.That(RegistryCaptureServer.StartedInstance!.LoadComplexTypes, Is.False);
            Assert.That(RegistryCaptureServer.StartedInstance.TransportBindings, Is.Not.Null);
            Assert.That(rateLimitsConfigured, Is.True);
            Assert.That(
                loggerProvider.Messages.Any(
                    message => message.Contains(
                        "without a matching identity authenticator", StringComparison.Ordinal)),
                Is.False);
            Assert.That(server.IdentityRegistry.UnregisterAugmenter(augmenter.Object), Is.True);
        }

        private static void ConfigureHostedOptions(OpcUaServerOptions options, string applicationName)
        {
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(OpcUaServerHostedServiceCoverageTests),
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// A plain <see cref="StandardServer"/> (i.e. not a
        /// <see cref="DependencyInjectionStandardServer"/>) whose
        /// <c>CurrentInstance</c> is the stock <c>ServerInternalData</c>,
        /// which implements <see cref="IHistorianRegistryProvider"/> and
        /// <see cref="IAliasNameStoreRegistryProvider"/> -- required to
        /// reach the <c>RegisterPostStartRegistries</c> loops, which
        /// explicitly skip <see cref="DependencyInjectionStandardServer"/>.
        /// </summary>
        [SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by the DI container through AddServer<TServer>.")]
        private sealed class RegistryCaptureServer : StandardServer
        {
            public RegistryCaptureServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
                StartedInstance = this;
            }

            public static IServerInternal? StartedServer { get; private set; }

            public static RegistryCaptureServer? StartedInstance { get; private set; }

            public static void Reset()
            {
                StartedServer = null;
                StartedInstance = null;
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                StartedServer = server;
                base.OnServerStarted(server);
            }
        }

        private sealed class HostedServerFixture : IAsyncDisposable
        {
            private HostedServerFixture(ServiceProvider provider, IHostedService hostedService)
            {
                m_provider = provider;
                m_hostedService = hostedService;
            }

            public static async ValueTask<HostedServerFixture> StartAsync(
                Action<IServiceCollection> configureServices)
            {
                var services = new ServiceCollection();
                configureServices(services);
                ServiceProvider provider = services.BuildServiceProvider();
                IHostedService hostedService = provider.GetServices<IHostedService>().Single();
                try
                {
                    await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    return new HostedServerFixture(provider, hostedService);
                }
                catch
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await m_hostedService.StopAsync(cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    await m_provider.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly ServiceProvider m_provider;
            private readonly IHostedService m_hostedService;
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public ConcurrentBag<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Messages);
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(ConcurrentBag<string> messages)
            {
                m_messages = messages;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                m_messages.Add(formatter(state, exception));
            }

            private readonly ConcurrentBag<string> m_messages;
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
