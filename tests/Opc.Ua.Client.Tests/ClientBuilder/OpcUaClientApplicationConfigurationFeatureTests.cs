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
