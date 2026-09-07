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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Exercises the <c>AddServer(configurationFile)</c> and
    /// <c>AddServer(configurationStream)</c> migration paths: a hosted OPC UA
    /// server whose <see cref="ApplicationConfiguration"/> is loaded from an
    /// existing OPC UA XML configuration document instead of being built from
    /// <see cref="OpcUaServerOptions"/>. Covers the new <c>IOpcUaBuilder</c>
    /// overloads, the <see cref="OpcUaServerOptions.ConfigurationFile"/> /
    /// <see cref="OpcUaServerOptions.ConfigurationStream"/> options and the
    /// configuration-section binding, the
    /// <see cref="OpcUaServerOptions.ConfigureLoadedConfiguration"/>
    /// override callback, precedence over <c>ConfigureApplication(...)</c>,
    /// mutual exclusion of file and stream, startup failure for missing
    /// files, and the user-token-policy warning derived from the loaded
    /// document.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class ConfigurationFileHostingTests
    {
        [Test]
        public async Task AddServerAppliesAllSettingsFromConfigurationFileAsync()
        {
            ConfigurationCaptureServer.Reset();
            const string applicationName = "CfgXmlApplyServer";
            string testRoot = CreateTestRoot();
            int port = GetAvailablePort();
            string configurationFile = WriteConfigurationFile(testRoot, applicationName, port);
            var loggerProvider = new CapturingLoggerProvider();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    services.AddOpcUa().AddServer<ConfigurationCaptureServer>(configurationFile);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => ConfigurationCaptureServer.Started,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True,
                "The hosted server did not start from the configuration file.");

            ApplicationConfiguration configuration =
                ConfigurationCaptureServer.StartedConfiguration ??
                throw new InvalidOperationException("No configuration was captured.");

            // Every setting captured at server start must come from the file.
            Assert.That(configuration.SourceFilePath, Is.EqualTo(configurationFile));
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
            Assert.That(
                configuration.ApplicationUri,
                Is.EqualTo("urn:opcfoundation:test:" + applicationName));
            Assert.That(
                configuration.ProductUri,
                Is.EqualTo("uri:opcfoundation.org:test:" + applicationName));
            Assert.That(configuration.TransportQuotas, Is.Not.Null);
            Assert.That(configuration.TransportQuotas!.MaxStringLength, Is.EqualTo(654321));
            Assert.That(configuration.TransportQuotas.OperationTimeout, Is.EqualTo(90000));
            Assert.That(configuration.ServerConfiguration, Is.Not.Null);
            Assert.That(configuration.ServerConfiguration!.MaxSessionCount, Is.EqualTo(77));
            Assert.That(configuration.ServerConfiguration.BaseAddresses.Count, Is.EqualTo(1));
            string expectedBaseAddress = Utils.ReplaceLocalhost(FormatBaseAddress(applicationName, port))!;
            Assert.That(
                configuration.ServerConfiguration.BaseAddresses[0],
                Is.EqualTo(expectedBaseAddress));
            Assert.That(
                configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                Is.True);
            Assert.That(
                configuration.SecurityConfiguration.ApplicationCertificates[0].StorePath,
                Is.EqualTo(GetPkiRoot(testRoot) + "/own"));

            // The hosted service reports the listen endpoints from the loaded
            // file because OpcUaServerOptions.EndpointUrls is empty on this path.
            Assert.That(
                await WaitForAsync(
                    () => loggerProvider.Messages.Any(message =>
                        message.Contains("listening", StringComparison.OrdinalIgnoreCase) &&
                        message.Contains(expectedBaseAddress, StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                Is.True,
                "The endpoint from the configuration file was not reported as listening.");
        }

        [Test]
        public async Task AddServerStartsDefaultServerFromConfigurationFileAsync()
        {
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(
                testRoot,
                "CfgXmlDefaultServer",
                GetAvailablePort());
            var startupTask = new RecordingStartupTask();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IServerStartupTask>(startupTask);
                    services.AddOpcUa().AddServer(configurationFile);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => startupTask.InvocationCount > 0,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True,
                "The default hosted server did not start from the configuration file.");

            OpcUaServerOptions options = fixture.Services
                .GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(options.ConfigurationFile, Is.EqualTo(configurationFile));
        }

        [Test]
        public async Task ConfigureLoadedConfigurationOverridesIndividualSettingsAsync()
        {
            ConfigurationCaptureServer.Reset();
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(
                testRoot,
                "CfgXmlOverrideServer",
                GetAvailablePort());

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging();
                    services.AddOpcUa().AddServer<ConfigurationCaptureServer>(
                        configurationFile,
                        configuration =>
                            configuration.ServerConfiguration!.MaxSessionCount = 4242);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => ConfigurationCaptureServer.Started,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True);

            ApplicationConfiguration configuration =
                ConfigurationCaptureServer.StartedConfiguration ??
                throw new InvalidOperationException("No configuration was captured.");

            // The callback override wins over the file value while every
            // untouched setting keeps its file value.
            Assert.That(configuration.ServerConfiguration!.MaxSessionCount, Is.EqualTo(4242));
            Assert.That(configuration.TransportQuotas!.MaxStringLength, Is.EqualTo(654321));
            Assert.That(configuration.SourceFilePath, Is.EqualTo(configurationFile));
        }

        [Test]
        public async Task ConfigurationFileTakesPrecedenceOverConfigureApplicationAsync()
        {
            ConfigurationCaptureServer.Reset();
            const string applicationName = "CfgXmlPrecedenceServer";
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(
                testRoot,
                applicationName,
                GetAvailablePort());

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging();
                    services.AddOpcUa()
                        .ConfigureApplication(options =>
                        {
                            options.ApplicationName = "SharedHostedApplication";
                            options.ApplicationUri = "urn:localhost:SharedHostedApplication";
                            options.PkiRoot = Path.Combine(testRoot, "sharedpki");
                            options.AutoAcceptUntrustedCertificates = true;
                        })
                        .AddServer<ConfigurationCaptureServer>(configurationFile);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => ConfigurationCaptureServer.Started,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True);

            ApplicationConfiguration configuration =
                ConfigurationCaptureServer.StartedConfiguration ??
                throw new InvalidOperationException("No configuration was captured.");

            Assert.That(configuration.SourceFilePath, Is.EqualTo(configurationFile));
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
        }

        [Test]
        public async Task UnmatchedUserNamePolicyFromConfigurationFileLogsWarningAsync()
        {
            string testRoot = CreateTestRoot();
            string configurationFile = WriteConfigurationFile(
                testRoot,
                "CfgXmlWarnServer",
                GetAvailablePort(),
                AnonymousTokenPolicyXml + UserNameTokenPolicyXml);
            var loggerProvider = new CapturingLoggerProvider();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                    // No user database / user management is registered, so the
                    // UserName policy advertised by the file has no matching
                    // authenticator and the hosted service must warn.
                    services.AddOpcUa().AddServer(configurationFile);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => loggerProvider.Messages.Any(message =>
                        message.Contains("UserName", StringComparison.Ordinal) &&
                        message.Contains(
                            "without a matching identity authenticator",
                            StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True,
                "Expected a warning for the unmatched UserName token policy from the file.");
        }

        [Test]
        public async Task MissingConfigurationFileFailsStartupAsync()
        {
            string missingFile = Path.Combine(CreateTestRoot(), "DoesNotExist.Config.xml");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(missingFile);

            Exception? failure = await StartAndCaptureStartupFailureAsync(services)
                .ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task AddServerAppliesSettingsFromConfigurationStreamAsync()
        {
            ConfigurationCaptureServer.Reset();
            const string applicationName = "CfgXmlStreamServer";
            string testRoot = CreateTestRoot();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(BuildConfigurationXml(
                applicationName,
                GetAvailablePort(),
                GetPkiRoot(testRoot))));

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging();
                    services.AddOpcUa().AddServer<ConfigurationCaptureServer>(
                        stream,
                        configuration =>
                            configuration.ServerConfiguration!.MaxSessionCount = 555);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => ConfigurationCaptureServer.Started,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True,
                "The hosted server did not start from the configuration stream.");

            ApplicationConfiguration configuration =
                ConfigurationCaptureServer.StartedConfiguration ??
                throw new InvalidOperationException("No configuration was captured.");

            // Settings come from the stream document; a stream has no source
            // file path; the override callback wins over the document value.
            Assert.That(configuration.SourceFilePath, Is.Null);
            Assert.That(configuration.ApplicationName, Is.EqualTo(applicationName));
            Assert.That(configuration.TransportQuotas!.MaxStringLength, Is.EqualTo(654321));
            Assert.That(configuration.ServerConfiguration!.MaxSessionCount, Is.EqualTo(555));
            Assert.That(configuration.ServerConfiguration.BaseAddresses.Count, Is.EqualTo(1));

            // The hosted service disposes the stream after reading it.
            Assert.That(stream.CanRead, Is.False);
        }

        [Test]
        public async Task AddServerStartsDefaultServerFromConfigurationStreamAsync()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(BuildConfigurationXml(
                "CfgXmlStreamDefault",
                GetAvailablePort(),
                GetPkiRoot(CreateTestRoot()))));
            var startupTask = new RecordingStartupTask();

            await using HostedServerFixture fixture = await HostedServerFixture.StartAsync(
                services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IServerStartupTask>(startupTask);
                    services.AddOpcUa().AddServer(stream);
                }).ConfigureAwait(false);

            Assert.That(
                await WaitForAsync(
                    () => startupTask.InvocationCount > 0,
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false),
                Is.True,
                "The default hosted server did not start from the configuration stream.");

            OpcUaServerOptions options = fixture.Services
                .GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(options.ConfigurationStream, Is.SameAs(stream));
        }

        [Test]
        public async Task WhiteSpaceConfigurationFileFailsStartupWithClearErrorAsync()
        {
            // A white-space path can reach the options through configuration
            // binding or the options callback, bypassing the AddServer
            // argument validation. Startup must fail with a clear error
            // instead of a confusing file-load failure.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(options => options.ConfigurationFile = "   ");

            Exception? failure = await StartAndCaptureStartupFailureAsync(services)
                .ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(failure!.Message, Does.Contain("white-space"));
        }

        [Test]
        public async Task ConfigurationFileAndStreamTogetherFailStartupAsync()
        {
            using var stream = new MemoryStream([1, 2, 3]);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(options =>
            {
                options.ConfigurationFile = "Some.Config.xml";
                options.ConfigurationStream = stream;
            });

            Exception? failure = await StartAndCaptureStartupFailureAsync(services)
                .ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(failure!.Message, Does.Contain("only one"));
        }

        [Test]
        public void AddServerConfigurationFileValidatesArguments()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => default(IOpcUaBuilder)!.AddServer("Server.Config.xml"),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddServer((string)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationFile"));
            Assert.That(
                () => builder.AddServer("   "),
                Throws.TypeOf<ArgumentException>().With
                    .Property(nameof(ArgumentException.ParamName)).EqualTo("configurationFile"));

            Assert.That(
                () => default(IOpcUaBuilder)!.AddServer<ConfigurationCaptureServer>(
                    "Server.Config.xml"),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddServer<ConfigurationCaptureServer>((string)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationFile"));
            Assert.That(
                () => builder.AddServer<ConfigurationCaptureServer>("   "),
                Throws.TypeOf<ArgumentException>().With
                    .Property(nameof(ArgumentException.ParamName)).EqualTo("configurationFile"));

            using var stream = new MemoryStream();
            Assert.That(
                () => default(IOpcUaBuilder)!.AddServer(stream),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddServer((Stream)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationStream"));
            Assert.That(
                () => default(IOpcUaBuilder)!.AddServer<ConfigurationCaptureServer>(stream),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            Assert.That(
                () => builder.AddServer<ConfigurationCaptureServer>((Stream)null!),
                Throws.ArgumentNullException.With
                    .Property(nameof(ArgumentNullException.ParamName)).EqualTo("configurationStream"));
        }

        /// <summary>
        /// Builds the provider, starts the single hosted service, and returns
        /// the exception the startup failed with (from a synchronous
        /// <c>StartAsync</c> fault or the faulted execute task), or
        /// <c>null</c> when startup did not fail promptly. Disposes the
        /// provider before returning.
        /// </summary>
        private static async Task<Exception?> StartAndCaptureStartupFailureAsync(
            ServiceCollection services)
        {
            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                IHostedService hostedService = provider.GetServices<IHostedService>().Single();
                try
                {
                    await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    Task? executeTask = (hostedService as BackgroundService)?.ExecuteTask;
                    if (executeTask == null)
                    {
                        return null;
                    }

                    Task completed = await Task.WhenAny(
                        executeTask,
                        Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
                    if (!ReferenceEquals(completed, executeTask))
                    {
                        return null;
                    }
                    return executeTask.Exception?.GetBaseException();
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void AddServerConfigurationFileThrowsWhenServerAlreadyRegistered()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddServer("First.Config.xml");

            Assert.That(
                () => builder.AddServer("Second.Config.xml"),
                Throws.InvalidOperationException);
            Assert.That(
                () => builder.AddServer<ConfigurationCaptureServer>("Third.Config.xml"),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddServerBindsConfigurationFileFromConfigurationSection()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Server:ApplicationName"] = "BoundConfigFileServer",
                    ["OpcUa:Server:ConfigurationFile"] = "Legacy/Server.Config.xml"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            using ServiceProvider provider = services.AddOpcUa()
                .AddServer(configuration)
                .Services.BuildServiceProvider();

            OpcUaServerOptions options = provider
                .GetRequiredService<IOptions<OpcUaServerOptions>>().Value;

            Assert.That(options.ConfigurationFile, Is.EqualTo("Legacy/Server.Config.xml"));
            Assert.That(options.ApplicationName, Is.EqualTo("BoundConfigFileServer"));
        }

        private const string AnonymousTokenPolicyXml = """
                  <ua:UserTokenPolicy>
                    <ua:TokenType>Anonymous_0</ua:TokenType>
                  </ua:UserTokenPolicy>
            """;

        private const string UserNameTokenPolicyXml = """
                  <ua:UserTokenPolicy>
                    <ua:TokenType>UserName_1</ua:TokenType>
                  </ua:UserTokenPolicy>
            """;

        private static string CreateTestRoot()
        {
            // Keep the root short: certificate file names would otherwise push
            // the PFX path past MAX_PATH on .NET Framework.
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cfgxml",
                Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(testRoot);
            return testRoot;
        }

        private static string GetPkiRoot(string testRoot)
        {
            return Path.Combine(testRoot, "pki").Replace('\\', '/');
        }

        private static string FormatBaseAddress(string applicationName, int port)
        {
            return "opc.tcp://localhost:" +
                port.ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName;
        }

        /// <summary>
        /// Writes a classic OPC UA server configuration XML file of the shape
        /// existing applications carry (security configuration, transport
        /// quotas, server configuration) with distinctive values the tests
        /// can assert on.
        /// </summary>
        private static string WriteConfigurationFile(
            string testRoot,
            string applicationName,
            int port,
            string? userTokenPoliciesXml = null)
        {
            string configurationFile = Path.Combine(testRoot, applicationName + ".Config.xml");
            File.WriteAllText(
                configurationFile,
                BuildConfigurationXml(applicationName, port, GetPkiRoot(testRoot), userTokenPoliciesXml));
            return configurationFile;
        }

        /// <summary>
        /// Builds the classic OPC UA server configuration XML document used
        /// by both the file-based and the stream-based tests.
        /// </summary>
        private static string BuildConfigurationXml(
            string applicationName,
            int port,
            string pkiRoot,
            string? userTokenPoliciesXml = null)
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
                  <ApplicationType>Server_0</ApplicationType>
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
                  <ServerConfiguration>
                    <BaseAddresses>
                      <ua:String>{FormatBaseAddress(applicationName, port)}</ua:String>
                    </BaseAddresses>
                    <SecurityPolicies>
                      <ServerSecurityPolicy>
                        <SecurityMode>None_1</SecurityMode>
                        <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
                      </ServerSecurityPolicy>
                    </SecurityPolicies>
                    <UserTokenPolicies>
                {userTokenPoliciesXml ?? AnonymousTokenPolicyXml}
                    </UserTokenPolicies>
                    <DiagnosticsEnabled>false</DiagnosticsEnabled>
                    <MaxSessionCount>77</MaxSessionCount>
                    <MinSessionTimeout>10000</MinSessionTimeout>
                    <MaxSessionTimeout>3600000</MaxSessionTimeout>
                  </ServerConfiguration>
                </ApplicationConfiguration>
                """;
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

        /// <summary>
        /// A <see cref="StandardServer"/> that captures the effective
        /// <see cref="ApplicationConfiguration"/> the hosted service starts
        /// it with, so tests can assert that the settings of the loaded
        /// configuration file were applied.
        /// </summary>
        public sealed class ConfigurationCaptureServer : StandardServer
        {
            public ConfigurationCaptureServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            public static ApplicationConfiguration? StartedConfiguration { get; private set; }

            public static bool Started { get; private set; }

            public static void Reset()
            {
                StartedConfiguration = null;
                Started = false;
            }

            protected override void OnServerStarting(ApplicationConfiguration configuration)
            {
                StartedConfiguration = configuration;
                base.OnServerStarting(configuration);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                Started = true;
                base.OnServerStarted(server);
            }
        }

        private sealed class RecordingStartupTask : IServerStartupTask
        {
            public int InvocationCount => Volatile.Read(ref m_invocationCount);

            public ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_invocationCount);
                return default;
            }

            private int m_invocationCount;
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

            public IServiceProvider Services => m_provider;

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
