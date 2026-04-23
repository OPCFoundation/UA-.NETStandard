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
using Opc.Ua.Security;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SecurityConfigManagerAdditionalTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestSecConfigAdd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            try
            {
                Directory.Delete(m_tempDir, true);
            }
            catch
            {
            }
        }

        private string CreateAppConfigXml(
            string appName,
            string appUri,
            string appType = "Server_0")
        {
            return
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<ApplicationConfiguration xmlns=""http://opcfoundation.org/UA/SDK/Configuration.xsd""
    xmlns:ua=""http://opcfoundation.org/UA/2008/02/Types.xsd"">
  <ApplicationName>" +
                appName +
                @"</ApplicationName>
  <ApplicationUri>" +
                appUri +
                @"</ApplicationUri>
  <ApplicationType>" +
                appType +
                @"</ApplicationType>
  <SecurityConfiguration>
    <ApplicationCertificates />
    <TrustedIssuerCertificates>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/issuers</StorePath>
    </TrustedIssuerCertificates>
    <TrustedPeerCertificates>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/trusted</StorePath>
    </TrustedPeerCertificates>
    <RejectedCertificateStore>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/rejected</StorePath>
    </RejectedCertificateStore>
  </SecurityConfiguration>
  <ServerConfiguration>
    <BaseAddresses>
      <ua:String>opc.tcp://localhost:4840</ua:String>
    </BaseAddresses>
    <SecurityPolicies>
      <ServerSecurityPolicy>
        <SecurityMode>SignAndEncrypt_3</SecurityMode>
        <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
      </ServerSecurityPolicy>
    </SecurityPolicies>
  </ServerConfiguration>
</ApplicationConfiguration>";
        }

        private void WriteSecuredApplicationFile(string filePath)
        {
            var app = new SecuredApplication
            {
                ApplicationName = "SecuredApp",
                ApplicationUri = "urn:secured:app",
                ApplicationType = Opc.Ua.Security.ApplicationType.Server_0,
                ProductName = "TestProduct",
                LastExportTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);

            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };
            using (var writer = XmlWriter.Create(ms, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(SecuredApplication), writer, ctx);
                SecuredApplicationEncoding.EncodeContents(encoder, app);
                encoder.Close();
            }

            File.WriteAllBytes(filePath, ms.ToArray());
        }

        [Test]
        public void ReadConfigurationThrowsOnNull()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.That(
                () => manager.ReadConfiguration(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ReadConfigurationThrowsOnMissingFile()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.That(
                () => manager.ReadConfiguration(
                    Path.Combine(m_tempDir, "does_not_exist.xml")),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ReadConfigurationFromAppConfigXml()
        {
            string filePath = Path.Combine(m_tempDir, "read_appconfig.xml");
            File.WriteAllText(filePath, CreateAppConfigXml("TestApp", "urn:test:app"), Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app, Is.Not.Null);
            Assert.That(app.ApplicationName, Is.EqualTo("TestApp"));
            Assert.That(app.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(app.ConfigurationFile, Is.EqualTo(filePath));
        }

        [Test]
        public void ReadConfigurationFromSecuredApplicationXml()
        {
            string filePath = Path.Combine(m_tempDir, "read_secured.xml");
            WriteSecuredApplicationFile(filePath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app, Is.Not.Null);
            Assert.That(app.ApplicationName, Is.EqualTo("SecuredApp"));
            Assert.That(app.ApplicationUri, Is.EqualTo("urn:secured:app"));
        }

        [Test]
        public void ReadConfigurationSetsSecurityInfo()
        {
            string filePath = Path.Combine(m_tempDir, "read_security_info.xml");
            File.WriteAllText(filePath, CreateAppConfigXml("SecurityApp", "urn:test:security"), Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app.TrustedCertificateStore, Is.Not.Null);
            Assert.That(app.IssuerCertificateStore, Is.Not.Null);
            Assert.That(app.RejectedCertificatesStore, Is.Not.Null);
        }

        [Test]
        public void ReadConfigurationSetsBaseAddressesAndPolicies()
        {
            string filePath = Path.Combine(m_tempDir, "read_base_addresses.xml");
            File.WriteAllText(filePath, CreateAppConfigXml("AddrApp", "urn:test:addr"), Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app.BaseAddresses, Is.Not.Null);
            Assert.That(app.BaseAddresses, Is.Not.Empty);
            Assert.That(app.SecurityProfiles, Is.Not.Null);
            Assert.That(app.SecurityProfiles, Is.Not.Empty);
        }

        [Test]
        public void WriteConfigurationThrowsOnNull()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.That(
                () => manager.WriteConfiguration("somefile.xml", null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void WriteConfigurationThrowsOnMissingFile()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            var config = new SecuredApplication
            {
                ApplicationName = "Test",
                ConfigurationFile = Path.Combine(m_tempDir, "missing_write.xml")
            };
            Assert.That(
                () => manager.WriteConfiguration(
                    Path.Combine(m_tempDir, "missing_write.xml"), config),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void WriteConfigurationThrowsOnEmptyPath()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            var config = new SecuredApplication();
            Assert.That(
                () => manager.WriteConfiguration(string.Empty, config),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void WriteConfigurationUpdatesSecuredApplicationXml()
        {
            string filePath = Path.Combine(m_tempDir, "write_secured.xml");
            WriteSecuredApplicationFile(filePath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            var config = new SecuredApplication
            {
                ApplicationName = "UpdatedApp",
                ApplicationUri = "urn:updated:app",
                ConfigurationFile = filePath
            };

            manager.WriteConfiguration(filePath, config);

            string content = File.ReadAllText(filePath);
            Assert.That(content, Does.Contain("UpdatedApp"));
        }

        [Test]
        public void WriteConfigurationUpdatesAppConfigXml()
        {
            string filePath = Path.Combine(m_tempDir, "write_appconfig.xml");
            File.WriteAllText(filePath, CreateAppConfigXml("OrigApp", "urn:orig:app"), Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication readApp = manager.ReadConfiguration(filePath);
            readApp.ApplicationName = "UpdatedAppConfig";

            manager.WriteConfiguration(filePath, readApp);

            string content = File.ReadAllText(filePath);
            Assert.That(content, Does.Contain("UpdatedAppConfig"));
        }

        [Test]
        public void ReadAndWriteRoundTripPreservesData()
        {
            string filePath = Path.Combine(m_tempDir, "roundtrip.xml");
            File.WriteAllText(filePath, CreateAppConfigXml("RoundTrip", "urn:test:roundtrip"), Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app.ApplicationName, Is.EqualTo("RoundTrip"));

            app.ApplicationName = "RoundTripUpdated";
            manager.WriteConfiguration(filePath, app);

            string content = File.ReadAllText(filePath);
            Assert.That(content, Does.Contain("RoundTripUpdated"));
        }

        [Test]
        public void ReadConfigurationWithDiscoveryServerConfig()
        {
            const string xml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<ApplicationConfiguration xmlns=""http://opcfoundation.org/UA/SDK/Configuration.xsd""
    xmlns:ua=""http://opcfoundation.org/UA/2008/02/Types.xsd"">
  <ApplicationName>DiscoveryApp</ApplicationName>
  <ApplicationUri>urn:test:discovery</ApplicationUri>
  <ApplicationType>DiscoveryServer_3</ApplicationType>
  <SecurityConfiguration>
    <ApplicationCertificates />
  </SecurityConfiguration>
  <DiscoveryServerConfiguration>
    <BaseAddresses>
      <ua:String>opc.tcp://localhost:4840/discovery</ua:String>
    </BaseAddresses>
    <SecurityPolicies>
      <ServerSecurityPolicy>
        <SecurityMode>None_1</SecurityMode>
        <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
      </ServerSecurityPolicy>
    </SecurityPolicies>
  </DiscoveryServerConfiguration>
</ApplicationConfiguration>";

            string filePath = Path.Combine(m_tempDir, "read_discovery.xml");
            File.WriteAllText(filePath, xml, Encoding.UTF8);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication app = manager.ReadConfiguration(filePath);

            Assert.That(app, Is.Not.Null);
            Assert.That(app.ApplicationName, Is.EqualTo("DiscoveryApp"));
            Assert.That(app.BaseAddresses, Is.Not.Null);
        }
    }
}
