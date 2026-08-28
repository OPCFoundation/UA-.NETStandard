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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Configuration;

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
    /// ambiguous-combination validation, and load failures.
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

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions resolvedOptions =
                provider.GetRequiredService<OpcUaClientOptions>();

            Assert.That(
                () => resolvedOptions.ConfigurationProvider!.GetAsync(),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(
                () => resolvedOptions.ConfigurationProvider!.Configuration,
                Throws.InvalidOperationException);
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
