// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Directly exercises <see cref="OpcUaClientApplicationConfigurationFeature.ApplyDefaults"/>,
    /// covering both outcomes of each <c>??=</c> assignment: the
    /// already-set (no-op) branch and the unset (assign-from-client-options)
    /// branch, for every <see cref="OpcUaApplicationOptions"/> property.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaClientApplicationConfigurationFeatureTests
    {
        [Test]
        public void ApplyDefaultsFillsUnsetPropertiesFromClientOptions()
        {
            var feature = CreateFeature(new OpcUaClientOptions
            {
                ApplicationName = "ClientName",
                ApplicationUri = "urn:localhost:ClientName",
                ProductUri = "uri:opcfoundation.org:ClientName",
                SubjectName = "CN=ClientName",
                PkiRoot = "pki-root",
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 3072
            });

            var options = new OpcUaApplicationOptions();

            feature.ApplyDefaults(options);

            Assert.That(options.ApplicationName, Is.EqualTo("ClientName"));
            Assert.That(options.ApplicationUri, Is.EqualTo("urn:localhost:ClientName"));
            Assert.That(options.ProductUri, Is.EqualTo("uri:opcfoundation.org:ClientName"));
            Assert.That(options.SubjectName, Is.EqualTo("CN=ClientName"));
            Assert.That(options.PkiRoot, Is.EqualTo("pki-root"));
            Assert.That(options.AutoAcceptUntrustedCertificates, Is.True);
            Assert.That(options.RejectSHA1SignedCertificates, Is.False);
            Assert.That(options.MinimumCertificateKeySize, Is.EqualTo((ushort)3072));
        }

        [Test]
        public void ApplyDefaultsLeavesAlreadySetPropertiesUnchanged()
        {
            var feature = CreateFeature(new OpcUaClientOptions
            {
                ApplicationName = "ClientName",
                ApplicationUri = "urn:localhost:ClientName",
                ProductUri = "uri:opcfoundation.org:ClientName",
                SubjectName = "CN=ClientName",
                PkiRoot = "client-pki-root",
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 3072
            });

            var options = new OpcUaApplicationOptions
            {
                ApplicationName = "AlreadySetName",
                ApplicationUri = "urn:localhost:AlreadySetName",
                ProductUri = "uri:opcfoundation.org:AlreadySetName",
                SubjectName = "CN=AlreadySetName",
                PkiRoot = "already-set-pki-root",
                AutoAcceptUntrustedCertificates = false,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 2048
            };

            feature.ApplyDefaults(options);

            Assert.That(options.ApplicationName, Is.EqualTo("AlreadySetName"));
            Assert.That(options.ApplicationUri, Is.EqualTo("urn:localhost:AlreadySetName"));
            Assert.That(options.ProductUri, Is.EqualTo("uri:opcfoundation.org:AlreadySetName"));
            Assert.That(options.SubjectName, Is.EqualTo("CN=AlreadySetName"));
            Assert.That(options.PkiRoot, Is.EqualTo("already-set-pki-root"));
            Assert.That(options.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(options.RejectSHA1SignedCertificates, Is.False);
            Assert.That(options.MinimumCertificateKeySize, Is.EqualTo((ushort)2048));
        }

        [Test]
        public void ConfigureReturnsClientSecurityBuilder()
        {
            var feature = CreateFeature(new OpcUaClientOptions());
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry);
            IApplicationConfigurationBuilderTypes builder = appInstance.Build(
                "urn:localhost:OpcUaClientApplicationConfigurationFeatureTests",
                "uri:opcfoundation.org:OpcUaClientApplicationConfigurationFeatureTests");

            IApplicationConfigurationBuilderSecurity securityBuilder = feature.Configure(builder);

            Assert.That(securityBuilder, Is.Not.Null);
            Assert.That(appInstance.ApplicationConfiguration!.ApplicationType, Is.EqualTo(ApplicationType.Client));
        }

        private static OpcUaClientApplicationConfigurationFeature CreateFeature(OpcUaClientOptions options)
        {
            return new OpcUaClientApplicationConfigurationFeature(Options.Create(options));
        }
    }
}
