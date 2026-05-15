/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// XML round-trip tests for <see cref="GlobalDiscoveryServerConfiguration"/>
    /// and <see cref="CertificateGroupConfiguration"/>.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GdsConfigurationRoundTripTests
    {
        [Test]
        public void GlobalDiscoveryServerConfigurationRoundTrip()
        {
            string filePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "test-gds-config.xml");

            // Step 1: Decode from XML fixture
            GlobalDiscoveryServerConfiguration original;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                original = DecodeConfiguration(stream);
            }

            // Step 2: Verify decoded fields
            VerifyConfiguration(original);

            // Step 3: Encode back to XML
            string xml = EncodeConfiguration(original);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Step 4: Re-parse encoded XML
            GlobalDiscoveryServerConfiguration roundTripped;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                roundTripped = DecodeConfiguration(stream);
            }

            // Step 5: Verify re-parsed matches original
            VerifyConfiguration(roundTripped);
        }

        private static void VerifyConfiguration(
            GlobalDiscoveryServerConfiguration config)
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config.AuthoritiesStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/authorities"));
            Assert.That(config.ApplicationCertificatesStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/applications"));
            Assert.That(config.BaseCertificateGroupStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/CA"));
            Assert.That(config.DefaultSubjectNameContext,
                Is.EqualTo("O=OPC Foundation"));
            Assert.That(config.DatabaseStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/gdsdb.json"));
            Assert.That(config.UsersDatabaseStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/gdsusersdb.json"));

            // KnownHostNames
            Assert.That(config.KnownHostNames.Count, Is.EqualTo(2));
            Assert.That(config.KnownHostNames[0], Is.EqualTo("localhost"));
            Assert.That(config.KnownHostNames[1], Is.EqualTo("gds.opcfoundation.org"));

            // CertificateGroups
            Assert.That(config.CertificateGroups.Count, Is.EqualTo(2));

            // First group: Default
            CertificateGroupConfiguration defaultGroup = config.CertificateGroups[0];
            Assert.That(defaultGroup.Id, Is.EqualTo("Default"));
            Assert.That(defaultGroup.SubjectName,
                Is.EqualTo("CN=GDS Test CA, O=OPC Foundation"));
            Assert.That(defaultGroup.BaseStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/CA/default"));
            Assert.That(defaultGroup.DefaultCertificateLifetime, Is.EqualTo(12));
            Assert.That(defaultGroup.DefaultCertificateKeySize, Is.EqualTo(2048));
            Assert.That(defaultGroup.DefaultCertificateHashSize, Is.EqualTo(256));
            Assert.That(defaultGroup.CACertificateLifetime, Is.EqualTo(60));
            Assert.That(defaultGroup.CACertificateKeySize, Is.EqualTo(4096));
            Assert.That(defaultGroup.CACertificateHashSize, Is.EqualTo(512));
            Assert.That(defaultGroup.CertificateTypes.Count, Is.EqualTo(2));
            Assert.That(defaultGroup.CertificateTypes[0],
                Is.EqualTo("RsaSha256ApplicationCertificateType"));
            Assert.That(defaultGroup.CertificateTypes[1],
                Is.EqualTo("EccNistP256ApplicationCertificateType"));

            // Second group: EccGroup
            CertificateGroupConfiguration eccGroup = config.CertificateGroups[1];
            Assert.That(eccGroup.Id, Is.EqualTo("EccGroup"));
            Assert.That(eccGroup.SubjectName,
                Is.EqualTo("CN=ECC Test CA, O=OPC Foundation"));
            Assert.That(eccGroup.BaseStorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/GDS/CA/ecc"));
            Assert.That(eccGroup.DefaultCertificateLifetime, Is.EqualTo(24));
            Assert.That(eccGroup.DefaultCertificateKeySize, Is.EqualTo(256));
            Assert.That(eccGroup.DefaultCertificateHashSize, Is.EqualTo(256));
            Assert.That(eccGroup.CACertificateLifetime, Is.EqualTo(120));
            Assert.That(eccGroup.CACertificateKeySize, Is.EqualTo(384));
            Assert.That(eccGroup.CACertificateHashSize, Is.EqualTo(384));
            Assert.That(eccGroup.CertificateTypes.Count, Is.EqualTo(2));
            Assert.That(eccGroup.CertificateTypes[0],
                Is.EqualTo("EccBrainpoolP256r1ApplicationCertificateType"));
            Assert.That(eccGroup.CertificateTypes[1],
                Is.EqualTo("EccBrainpoolP384r1ApplicationCertificateType"));
        }

        private static IServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            return AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static GlobalDiscoveryServerConfiguration DecodeConfiguration(
            Stream stream)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var parser = new XmlParser(
                typeof(GlobalDiscoveryServerConfiguration), stream, ctx);
            var config = new GlobalDiscoveryServerConfiguration();
            config.Decode(parser);
            return config;
        }

        private static string EncodeConfiguration(
            GlobalDiscoveryServerConfiguration configuration)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(GlobalDiscoveryServerConfiguration), writer, ctx);
                configuration.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
