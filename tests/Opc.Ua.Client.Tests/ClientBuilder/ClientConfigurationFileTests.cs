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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Exercises the <c>AddClient(configurationFile)</c> and
    /// <c>AddClient(configurationStream)</c> migration paths: OPC UA client
    /// services whose <see cref="ApplicationConfiguration"/> is loaded from
    /// an existing OPC UA XML configuration document instead of being built
    /// from options or the shared application. Covers the new
    /// <c>IOpcUaBuilder</c> overloads, the
    /// <see cref="OpcUaClientOptions.ConfigurationFile"/> /
    /// <see cref="OpcUaClientOptions.ConfigurationStream"/> options and the
    /// configuration-section binding, the lazily loading configuration
    /// provider (including the application-instance certificate bootstrap
    /// and the <see cref="OpcUaClientOptions.ConfigureLoadedConfiguration"/>
    /// override callback), precedence over <c>ConfigureApplication(...)</c>,
    /// the <see cref="OpcUaClientOptions.LoadConfigurationOnStart"/> eager
    /// load of a supplied document, ambiguous-combination validation, and
    /// load failures.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientConfigurationFileTests
    {
        [Test]
        public async Task AddClientLoadsConfigurationFromXmlFileAsync()
        {
            const string applicationName = "CfgXmlClient";
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(testRoot, applicationName);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(
                configurationFile,
                options => options.ConfigureLoadedConfiguration = configuration =>
                    configuration.ClientConfiguration!.MinSubscriptionLifetime = 4242);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            // The supplied document loads lazily: the provider is wired but
            // the configuration is not materialized until first use.
            Assert.That(resolvedOptions.Configuration, Is.Null);
            Assert.That(resolvedOptions.ConfigurationProvider, Is.Not.Null);

            ApplicationConfiguration configuration = await resolvedOptions
                .ConfigurationProvider!.GetAsync().ConfigureAwait(false);

            // Every setting comes from the file; the override callback wins
            // over the file value for the setting it touches.
            Assert.That(configuration.SourceFilePath, Is.EqualTo(configurationFile));
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
            Assert.That(
                configuration.ApplicationUri,
                Is.EqualTo("urn:opcfoundation:test:" + applicationName));
            Assert.That(configuration.TransportQuotas!.MaxStringLength, Is.EqualTo(654321));
            Assert.That(configuration.ClientConfiguration, Is.Not.Null);
            Assert.That(
                configuration.ClientConfiguration!.DefaultSessionTimeout,
                Is.EqualTo(123456));
            Assert.That(
                configuration.ClientConfiguration.MinSubscriptionLifetime,
                Is.EqualTo(4242));

            // The load ran the client application-instance certificate
            // bootstrap: the own store now holds the application certificate.
            Assert.That(
                Directory.Exists(Path.Combine(testRoot, "pki", "own")) &&
                Directory.GetFiles(
                    Path.Combine(testRoot, "pki", "own"),
                    "*",
                    SearchOption.AllDirectories).Length > 0,
                Is.True,
                "The application instance certificate was not created.");

            // The provider caches the loaded document.
            ApplicationConfiguration second = await resolvedOptions
                .ConfigurationProvider.GetAsync().ConfigureAwait(false);
            Assert.That(second, Is.SameAs(configuration));
            Assert.That(
                resolvedOptions.ConfigurationProvider.Configuration,
                Is.SameAs(configuration));
        }

        [Test]
        public async Task AddClientLoadsConfigurationFromStreamAsync()
        {
            const string applicationName = "CfgXmlStreamClient";
            string testRoot = CreateTestRoot();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                BuildConfigurationXml(applicationName, GetPkiRoot(testRoot))));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(stream);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            ApplicationConfiguration configuration = await resolvedOptions
                .ConfigurationProvider!.GetAsync().ConfigureAwait(false);

            // A stream has no source file path; the settings come from the
            // document and the stream is disposed after the single read.
            Assert.That(configuration.SourceFilePath, Is.Null);
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
            Assert.That(configuration.TransportQuotas!.MaxStringLength, Is.EqualTo(654321));
            Assert.That(stream.CanRead, Is.False);
        }

        [Test]
        public async Task AddClientKeepsInjectedCertificateManagerOpenAfterConfigurationDisposalAsync()
        {
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(testRoot, "CfgXmlInjectedClientManager");
            await using var certificateManager = new CertificateManager(Opc.Ua.Tests.NUnitTelemetryContext.Create());
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICertificateManager>(certificateManager);
            services.AddOpcUa().AddClient(configurationFile);

            await using ServiceProvider provider = services.BuildServiceProvider();
            IOpcUaApplicationConfigurationProvider configurationProvider =
                provider.GetRequiredService<OpcUaClientOptions>().ConfigurationProvider!;
            ApplicationConfiguration configuration = await configurationProvider.GetAsync().ConfigureAwait(false);
            Assert.That(configuration.CertificateManager, Is.SameAs(certificateManager));

            await configurationProvider.DisposeAsync().ConfigureAwait(false);
            using ICertificateStore store = certificateManager.OpenCertificateStore(
                Path.Combine(GetPkiRoot(testRoot), "own"),
                CertificateStoreType.Directory);
        }

        /// <summary>
        /// Verifies borrowing through a forwarding factory and ownership of loaded and callback-created managers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SuppliedProviderKeepsInjectedManagerWhenFactoryDecoratesApplicationAsync(bool replaceManager)
        {
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(testRoot, "DecoratedXmlClient");
            ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();
            await using var certificateManager = new CertificateManager(telemetry);
            await using var inner = new ApplicationInstance(telemetry);
            var application = new Mock<IApplicationInstance>(MockBehavior.Strict);
            application.SetupSet(instance => instance.ApplicationType = It.IsAny<ApplicationType>())
                .Callback<ApplicationType>(type => inner.ApplicationType = type);
            application.SetupSet(instance => instance.ApplicationName = It.IsAny<string?>())
                .Callback<string?>(name => inner.ApplicationName = name);
            application.SetupSet(instance =>
                instance.CertificatePasswordProvider = It.IsAny<ICertificatePasswordProvider?>())
                .Callback<ICertificatePasswordProvider?>(passwords => inner.CertificatePasswordProvider = passwords);
            application.Setup(instance => instance.LoadApplicationConfigurationAsync(
                configurationFile, false, It.IsAny<CancellationToken>()))
                .Returns((string file, bool silent, CancellationToken ct) =>
                    inner.LoadApplicationConfigurationAsync(file, silent, ct));
            application.Setup(instance => instance.CheckApplicationInstanceCertificatesAsync(
                true, CertificateFactory.DefaultLifeTime, It.IsAny<CancellationToken>()))
                .Returns((bool silent, ushort? lifetime, CancellationToken ct) =>
                    inner.CheckApplicationInstanceCertificatesAsync(silent, lifetime, ct));
            application.Setup(instance => instance.DisposeAsync()).Returns(() => inner.DisposeAsync());
            var factory = new Mock<IApplicationInstanceFactory>(MockBehavior.Strict);
            factory.Setup(instanceFactory => instanceFactory.Create(telemetry)).Returns(application.Object);
            CertificateManager? loadedManager = null;
            CertificateManager? replacementManager = null;
            await using var configurationProvider = new ClientSuppliedConfigurationProvider(
                configurationFile,
                null,
                configuration =>
                {
                    loadedManager = inner.CertificateManager;
                    Assert.That(configuration.CertificateManager, Is.SameAs(certificateManager));
                    if (replaceManager)
                    {
                        replacementManager = new CertificateManager(telemetry);
                        configuration.CertificateManager = replacementManager;
                    }
                },
                factory.Object,
                telemetry,
                certificateManager);

            ApplicationConfiguration configuration = await configurationProvider.GetAsync().ConfigureAwait(false);
            Assert.That(configurationProvider.Application, Is.SameAs(application.Object));
            Assert.That(loadedManager, Is.Not.Null.And.Not.SameAs(certificateManager));
            await configurationProvider.DisposeAsync().ConfigureAwait(false);
            application.Verify(instance => instance.DisposeAsync(), Times.Once);
            using ICertificateStore store = certificateManager.OpenCertificateStore(
                GetPkiRoot(testRoot), CertificateStoreType.Directory, false);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => loadedManager!.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration))
                .ConfigureAwait(false);
            if (replaceManager)
            {
                Assert.That(configuration.CertificateManager, Is.SameAs(replacementManager));
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => replacementManager!.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that every disposal caller waits for initialization even when its original waiter cancels.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SuppliedProviderDisposalWaitsForInFlightCertificateCheckAsync(bool cancelWaiter)
        {
            string testRoot = CreateTestRoot();
            ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();
            await using var borrowedManager = new CertificateManager(telemetry);
            var loadedManager = new CertificateManager(telemetry);
            var configuration = new ApplicationConfiguration(telemetry) { CertificateManager = loadedManager };
            await using var inner = new ApplicationInstance(configuration, telemetry);
            var checkEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var checkFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var application = new Mock<IApplicationInstance>(MockBehavior.Strict);
            application.SetupSet(instance => instance.ApplicationType = It.IsAny<ApplicationType>());
            application.SetupSet(instance => instance.ApplicationName = It.IsAny<string?>());
            application.SetupSet(instance =>
                instance.CertificatePasswordProvider = It.IsAny<ICertificatePasswordProvider?>());
            application.Setup(instance => instance.LoadApplicationConfigurationAsync(
                "unused.Config.xml", false, CancellationToken.None)).ReturnsAsync(configuration);
            application.Setup(instance => instance.CheckApplicationInstanceCertificatesAsync(
                true, CertificateFactory.DefaultLifeTime, CancellationToken.None))
                .Returns(() =>
                {
                    checkEntered.TrySetResult(true);
                    return new ValueTask<bool>(checkFinished.Task);
                });
            application.Setup(instance => instance.DisposeAsync()).Returns(() => inner.DisposeAsync());
            var factory = new Mock<IApplicationInstanceFactory>(MockBehavior.Strict);
            factory.Setup(instanceFactory => instanceFactory.Create(telemetry)).Returns(application.Object);
            await using var configurationProvider = new ClientSuppliedConfigurationProvider(
                "unused.Config.xml", null, null, factory.Object, telemetry, borrowedManager);
            using var cancellation = new CancellationTokenSource();
            Task<ApplicationConfiguration> initialization = configurationProvider.GetAsync(cancellation.Token);
            await checkEntered.Task.ConfigureAwait(false);
            if (cancelWaiter)
            {
                cancellation.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await initialization.ConfigureAwait(false)).ConfigureAwait(false);
            }

            Task disposal = configurationProvider.DisposeAsync().AsTask();
            Task repeatedDisposal = configurationProvider.DisposeAsync().AsTask();
            try
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(repeatedDisposal, Is.SameAs(disposal));
                application.Verify(instance => instance.DisposeAsync(), Times.Never);
                Assert.That(configuration.CertificateManager, Is.SameAs(borrowedManager));
                using ICertificateStore store = loadedManager.OpenCertificateStore(
                    GetPkiRoot(testRoot), CertificateStoreType.Directory, false);
                Assert.That(store, Is.Not.Null);
            }
            finally
            {
                checkFinished.TrySetResult(true);
                await disposal.ConfigureAwait(false);
                if (!cancelWaiter)
                {
                    Assert.That(await initialization.ConfigureAwait(false), Is.SameAs(configuration));
                }
            }

            application.Verify(instance => instance.DisposeAsync(), Times.Once);
            using ICertificateStore borrowedStore = borrowedManager.OpenCertificateStore(
                GetPkiRoot(testRoot), CertificateStoreType.Directory, false);
            Assert.That(borrowedStore, Is.Not.Null);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => loadedManager.LoadApplicationCertificatesAsync(configuration.SecurityConfiguration))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that failed initialization and cancellation remain observable if application cleanup also fails.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task SuppliedProviderDisposalPreservesInitializationAndCleanupFailuresAsync(
            bool cancel,
            bool failCleanup)
        {
            ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();
            Exception failure = cancel
                ? new OperationCanceledException(new CancellationToken(canceled: true))
                : new InvalidOperationException("Initialization failed.");
            var cleanupFailure = new InvalidOperationException("Cleanup failed.");
            var application = new Mock<IApplicationInstance>(MockBehavior.Strict);
            application.SetupSet(instance => instance.ApplicationType = It.IsAny<ApplicationType>());
            application.SetupSet(instance =>
                instance.CertificatePasswordProvider = It.IsAny<ICertificatePasswordProvider?>());
            application.Setup(instance => instance.LoadApplicationConfigurationAsync(
                "unused.Config.xml", false, CancellationToken.None))
                .Returns(new ValueTask<ApplicationConfiguration>(
                    Task.FromException<ApplicationConfiguration>(failure)));
            application.Setup(instance => instance.DisposeAsync())
                .Returns(() => failCleanup ? new ValueTask(Task.FromException(cleanupFailure)) : default);
            var factory = new Mock<IApplicationInstanceFactory>(MockBehavior.Strict);
            factory.Setup(instanceFactory => instanceFactory.Create(telemetry)).Returns(application.Object);
            var configurationProvider = new ClientSuppliedConfigurationProvider(
                "unused.Config.xml", null, null, factory.Object, telemetry);

            Exception? initializationFailure = await Assert.CatchAsync<Exception>(
                async () => await configurationProvider.GetAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(initializationFailure, Is.SameAs(failure));
            Task disposal = configurationProvider.DisposeAsync().AsTask();
            Exception? disposalFailure = await Assert.CatchAsync<Exception>(
                async () => await disposal.ConfigureAwait(false)).ConfigureAwait(false);
            if (failCleanup)
            {
                Assert.That(disposalFailure, Is.TypeOf<AggregateException>());
                Assert.That(((AggregateException)disposalFailure!).InnerExceptions,
                    Is.EqualTo(new[] { failure, cleanupFailure }));
            }
            else
            {
                Assert.That(disposalFailure, Is.SameAs(failure));
            }
            Assert.That(configurationProvider.DisposeAsync().AsTask(), Is.SameAs(disposal));
            application.Verify(instance => instance.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task SuppliedDocumentTakesPrecedenceOverConfigureApplicationAsync()
        {
            const string applicationName = "CfgXmlPrecedenceClient";
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(testRoot, applicationName);

            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaBuilder builder = services.AddOpcUa()
                .ConfigureApplication(options =>
                {
                    options.ApplicationName = "SharedClientApplication";
                    options.ApplicationUri = "urn:localhost:SharedClientApplication";
                    options.PkiRoot = Path.Combine(testRoot, "sharedpki");
                    options.AutoAcceptUntrustedCertificates = true;
                });
            builder.AddClient(configurationFile);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            ApplicationConfiguration configuration = await resolvedOptions
                .ConfigurationProvider!.GetAsync().ConfigureAwait(false);

            Assert.That(configuration.SourceFilePath, Is.EqualTo(configurationFile));
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
        }

        [Test]
        public async Task MissingConfigurationFileFailsGetAsyncAsync()
        {
            string missingFile = Path.Combine(CreateTestRoot(), "DoesNotExist.Config.xml");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(missingFile);

            ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            Assert.That(
                () => resolvedOptions.ConfigurationProvider!.GetAsync(),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(
                () => resolvedOptions.ConfigurationProvider!.Configuration,
                Throws.InvalidOperationException);
            await Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public void AddClientSuppliedDocumentValidatesArguments()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => default(IOpcUaBuilder)!.AddClient("Client.Config.xml"),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddClient((string)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationFile"));
            Assert.That(
                () => builder.AddClient("   "),
                Throws.TypeOf<ArgumentException>().With
                    .Property(nameof(ArgumentException.ParamName)).EqualTo("configurationFile"));

            using var stream = new MemoryStream();
            Assert.That(
                () => default(IOpcUaBuilder)!.AddClient(stream),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddClient((Stream)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationStream"));
        }

        [Test]
        public void AddClientSuppliedDocumentRejectsAmbiguousCombinations()
        {
            using var stream = new MemoryStream();

            // File and stream together.
            Assert.That(
                () => new ServiceCollection().AddOpcUa().AddClient(options =>
                {
                    options.ConfigurationFile = "Client.Config.xml";
                    options.ConfigurationStream = stream;
                }),
                Throws.InvalidOperationException.With.Message.Contains("only one"));

            // Supplied document combined with an explicit Configuration.
            Assert.That(
                () => new ServiceCollection().AddOpcUa().AddClient(options =>
                {
                    options.ConfigurationFile = "Client.Config.xml";
                    options.Configuration = CreateConfiguration();
                }),
                Throws.InvalidOperationException.With.Message.Contains("Configuration"));

            // Supplied document combined with application identity options.
            Assert.That(
                () => new ServiceCollection().AddOpcUa().AddClient(options =>
                {
                    options.ConfigurationFile = "Client.Config.xml";
                    options.ApplicationName = "SomeClient";
                }),
                Throws.InvalidOperationException.With.Message.Contains("identity options"));

            // A white-space path that bypasses the AddClient(string) argument
            // validation (e.g. from configuration binding).
            Assert.That(
                () => new ServiceCollection().AddOpcUa().AddClient(options =>
                    options.ConfigurationFile = "   "),
                Throws.InvalidOperationException.With.Message.Contains("white-space"));
        }

        [Test]
        public async Task AddClientBindsConfigurationFileFromConfigurationSectionAsync()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .Add(new MemoryConfigurationSource
                {
                    InitialData = new Dictionary<string, string?>
                    {
                        ["OpcUa:Client:ConfigurationFile"] = "Legacy/Client.Config.xml"
                    }
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(configuration);

            // The supplied-document provider is IAsyncDisposable-only (like
            // the shared application provider), so the container must be
            // disposed asynchronously once the provider was instantiated.
            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions options = provider.GetRequiredService<OpcUaClientOptions>();

            Assert.That(options.ConfigurationFile, Is.EqualTo("Legacy/Client.Config.xml"));
            Assert.That(options.ConfigurationProvider, Is.Not.Null);
        }

        [Test]
        public async Task LoadConfigurationOnStartLoadsSuppliedDocumentOnHostStartAsync()
        {
            const string applicationName = "CfgXmlEagerClient";
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(testRoot, applicationName);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(
                configurationFile,
                options => options.LoadConfigurationOnStart = true);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            // Still lazy before the host starts.
            Assert.That(resolvedOptions.Configuration, Is.Null);

            IHostedService loader = provider.GetServices<IHostedService>()
                .Single(service => service is ClientConfigurationLoaderHostedService);
            await loader.StartAsync(CancellationToken.None).ConfigureAwait(false);

            // The document is loaded, validated and published on the options,
            // so a user interface can consume it without a session.
            Assert.That(resolvedOptions.Configuration, Is.Not.Null);
            Assert.That(
                resolvedOptions.Configuration!.ApplicationName,
                Is.EqualTo(applicationName));
            Assert.That(
                resolvedOptions.ConfigurationProvider!.Configuration,
                Is.SameAs(resolvedOptions.Configuration));
            Assert.That(
                resolvedOptions.ConfigurationProvider.Application.ApplicationName,
                Is.EqualTo(applicationName));

            // The application-instance certificate was ensured during start.
            Assert.That(
                Directory.Exists(Path.Combine(testRoot, "pki", "own")) &&
                Directory.GetFiles(
                    Path.Combine(testRoot, "pki", "own"),
                    "*",
                    SearchOption.AllDirectories).Length > 0,
                Is.True,
                "The application instance certificate was not created.");

            await loader.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task LoadConfigurationOnStartSurfacesLoadFailureOnHostStartAsync()
        {
            string missingFile = Path.Combine(
                CreateTestRoot(),
                "DoesNotExist.Config.xml");

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(
                missingFile,
                options => options.LoadConfigurationOnStart = true);

            ServiceProvider provider = services.BuildServiceProvider();
            IHostedService loader = provider.GetServices<IHostedService>()
                .Single(service => service is ClientConfigurationLoaderHostedService);

            // The failure fails the host start instead of surfacing on the
            // first session connect.
            Assert.That(
                () => loader.StartAsync(CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>());
            await Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task WithoutLoadConfigurationOnStartNoLoaderIsRegisteredAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient("Client.Config.xml");

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetServices<IHostedService>()
                    .Any(service => service is ClientConfigurationLoaderHostedService),
                Is.False);
        }

        [Test]
        public void OptionsValidationAcceptsSuppliedDocument()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient("Client.Config.xml");

            using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions boundOptions =
                provider.GetRequiredService<IOptions<OpcUaClientOptions>>().Value;

            // The start-time validator must accept a supplied document as a
            // configuration source even though no shared application
            // provider is registered and Configuration is still null.
            foreach (IValidateOptions<OpcUaClientOptions> validator in
                provider.GetServices<IValidateOptions<OpcUaClientOptions>>())
            {
                ValidateOptionsResult result =
                    validator.Validate(Options.DefaultName, boundOptions);
                Assert.That(result.Failed, Is.False, result.FailureMessage);
            }
        }

        private static string CreateTestRoot()
        {
            // Keep the root short: certificate file names would otherwise
            // push the PFX path past MAX_PATH on .NET Framework.
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "ccfg",
                Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(testRoot);
            return testRoot;
        }

        private static string GetPkiRoot(string testRoot)
        {
            return Path.Combine(testRoot, "pki").Replace('\\', '/');
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration(
                Opc.Ua.Tests.NUnitTelemetryContext.Create());
        }

        private static string WriteConfigurationFile(string testRoot, string applicationName)
        {
            string configurationFile = Path.Combine(testRoot, applicationName + ".Config.xml");
            File.WriteAllText(
                configurationFile,
                BuildConfigurationXml(applicationName, GetPkiRoot(testRoot)));
            return configurationFile;
        }

        /// <summary>
        /// Builds a classic OPC UA client configuration XML document of the
        /// shape existing applications carry (security configuration,
        /// transport quotas, client configuration) with distinctive values
        /// the tests can assert on.
        /// </summary>
        private static string BuildConfigurationXml(string applicationName, string pkiRoot)
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <ApplicationConfiguration
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xmlns:ua="http://opcfoundation.org/UA/2008/02/Types.xsd"
                  xmlns="http://opcfoundation.org/UA/SDK/Configuration.xsd">
                  <ApplicationName>{applicationName}</ApplicationName>
                  <ApplicationUri>urn:opcfoundation:test:{applicationName}</ApplicationUri>
                  <ProductUri>uri:opcfoundation.org:test:{applicationName}</ProductUri>
                  <ApplicationType>Client_1</ApplicationType>
                  <SecurityConfiguration>
                    <ApplicationCertificates>
                      <CertificateIdentifier>
                        <StoreType>Directory</StoreType>
                        <StorePath>{pkiRoot}/own</StorePath>
                        <SubjectName>CN={applicationName}, O=OPC Foundation, DC=localhost</SubjectName>
                        <CertificateTypeString>RsaSha256</CertificateTypeString>
                      </CertificateIdentifier>
                    </ApplicationCertificates>
                    <TrustedIssuerCertificates>
                      <StoreType>Directory</StoreType>
                      <StorePath>{pkiRoot}/issuer</StorePath>
                    </TrustedIssuerCertificates>
                    <TrustedPeerCertificates>
                      <StoreType>Directory</StoreType>
                      <StorePath>{pkiRoot}/trusted</StorePath>
                    </TrustedPeerCertificates>
                    <RejectedCertificateStore>
                      <StoreType>Directory</StoreType>
                      <StorePath>{pkiRoot}/rejected</StorePath>
                    </RejectedCertificateStore>
                    <AutoAcceptUntrustedCertificates>true</AutoAcceptUntrustedCertificates>
                  </SecurityConfiguration>
                  <TransportConfigurations></TransportConfigurations>
                  <TransportQuotas>
                    <OperationTimeout>90000</OperationTimeout>
                    <MaxStringLength>654321</MaxStringLength>
                    <MaxByteStringLength>1048576</MaxByteStringLength>
                    <MaxArrayLength>65535</MaxArrayLength>
                    <MaxMessageSize>4194304</MaxMessageSize>
                    <MaxBufferSize>65535</MaxBufferSize>
                    <ChannelLifetime>300000</ChannelLifetime>
                    <SecurityTokenLifetime>3600000</SecurityTokenLifetime>
                  </TransportQuotas>
                  <ClientConfiguration>
                    <DefaultSessionTimeout>123456</DefaultSessionTimeout>
                    <MinSubscriptionLifetime>10000</MinSubscriptionLifetime>
                  </ClientConfiguration>
                </ApplicationConfiguration>
                """;
        }
    }
}
