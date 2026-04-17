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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Security;
using Opc.Ua.Tests;
using SecurityNs = Opc.Ua.Security;

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SecurityConfigurationManagerTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestSecurityConfigMgr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            try
            {
                if (Directory.Exists(m_tempDir))
                {
                    Directory.Delete(m_tempDir, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }

        [Test]
        public void ReadConfigurationNullThrows()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.Throws<ArgumentNullException>(() => manager.ReadConfiguration(null));
        }

        [Test]
        public void ReadConfigurationFileNotFoundThrows()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.Throws<ServiceResultException>(
                () => manager.ReadConfiguration(
                    Path.Combine(m_tempDir, "nonexistent.config")));
        }

        [Test]
        public void WriteConfigurationNullConfigThrows()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            Assert.Throws<ArgumentNullException>(
                () => manager.WriteConfiguration("somefile.config", null));
        }

        [Test]
        public void WriteConfigurationFileNotFoundThrows()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            var app = new SecuredApplication { ApplicationName = "Test" };
            Assert.Throws<ServiceResultException>(
                () => manager.WriteConfiguration(
                    Path.Combine(m_tempDir, "nonexistent.config"), app));
        }

        [Test]
        public void WriteConfigurationEmptyPathThrows()
        {
            var manager = new SecurityConfigurationManager(m_telemetry);
            var app = new SecuredApplication { ApplicationName = "Test" };
            Assert.Throws<ServiceResultException>(
                () => manager.WriteConfiguration(string.Empty, app));
        }

        [Test]
        public void ReadConfigurationSecuredApplicationXml()
        {
            string configPath = Path.Combine(m_tempDir, "secured_app.config");
            WriteSecuredApplicationConfigFile(configPath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication result = manager.ReadConfiguration(configPath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ApplicationName, Is.EqualTo("TestSecuredApp"));
            Assert.That(result.ApplicationUri, Is.EqualTo("urn:test:securedapp"));
            Assert.That(result.ConfigurationFile, Is.EqualTo(configPath));
        }

        [Test]
        public void ReadConfigurationAppConfigXml()
        {
            string configPath = Path.Combine(m_tempDir, "appconfig.config");
            WriteApplicationConfigFile(configPath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication result = manager.ReadConfiguration(configPath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ApplicationName, Is.EqualTo("TestAppConfig"));
            Assert.That(result.ApplicationUri, Is.EqualTo("urn:test:appconfig"));
        }

        [Test]
        public void ReadAndWriteSecuredApplicationRoundTrip()
        {
            string configPath = Path.Combine(m_tempDir, "roundtrip_secured.config");
            WriteSecuredApplicationConfigFile(configPath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication original = manager.ReadConfiguration(configPath);

            original.ApplicationName = "ModifiedName";

            manager.WriteConfiguration(configPath, original);

            SecuredApplication reloaded = manager.ReadConfiguration(configPath);
            Assert.That(reloaded.ApplicationName, Is.EqualTo("ModifiedName"));
        }

        [Test]
        public void ReadAndWriteAppConfigRoundTrip()
        {
            string configPath = Path.Combine(m_tempDir, "roundtrip_appconfig.config");
            WriteApplicationConfigFile(configPath);

            var manager = new SecurityConfigurationManager(m_telemetry);
            SecuredApplication original = manager.ReadConfiguration(configPath);

            original.ApplicationName = "UpdatedAppName";

            manager.WriteConfiguration(configPath, original);

            SecuredApplication reloaded = manager.ReadConfiguration(configPath);
            Assert.That(reloaded.ApplicationName, Is.EqualTo("UpdatedAppName"));
        }

        [Test]
        public void ReadConfigurationExePathLooksForConfig()
        {
            string uniqueName = "testapp_" + Guid.NewGuid().ToString("N")[..8];
            string exePath = Path.Combine(m_tempDir, uniqueName + ".exe");
            string configFileName = uniqueName + ".Config.xml";
            string configInCwd = Path.Combine(Directory.GetCurrentDirectory(), configFileName);

            File.WriteAllText(exePath, "dummy exe content");
            WriteSecuredApplicationConfigFile(configInCwd);

            try
            {
                var manager = new SecurityConfigurationManager(m_telemetry);
                SecuredApplication result = manager.ReadConfiguration(exePath);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.ApplicationName, Is.EqualTo("TestSecuredApp"));
                Assert.That(result.ExecutableFile, Is.EqualTo(exePath));
            }
            finally
            {
                try
                {
                    File.Delete(configInCwd);
                }
                catch
                {
                }
            }
        }

        private void WriteSecuredApplicationConfigFile(string path)
        {
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);

            var app = new SecuredApplication
            {
                ApplicationName = "TestSecuredApp",
                ApplicationUri = "urn:test:securedapp",
                ApplicationType = SecurityNs.ApplicationType.Server_0,
                LastExportTime = DateTime.UtcNow,
                ApplicationCertificate = new SecurityNs.CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/OPC/certs",
                    SubjectName = "CN=TestSecuredApp"
                },
                TrustedCertificateStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/OPC/trusted"
                },
                IssuerCertificateStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/OPC/issuers"
                },
                RejectedCertificatesStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/OPC/rejected"
                }
            };

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

            File.WriteAllBytes(path, ms.ToArray());
        }

        private void WriteApplicationConfigFile(string path)
        {
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);

            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestAppConfig",
                ApplicationUri = "urn:test:appconfig",
                ApplicationType = Opc.Ua.ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "%LocalApplicationData%/OPC/certs",
                        SubjectName = "CN=TestAppConfig"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "%LocalApplicationData%/OPC/trusted"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "%LocalApplicationData%/OPC/issuers"
                    },
                    RejectedCertificateStore = new CertificateStoreIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "%LocalApplicationData%/OPC/rejected"
                    }
                },
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = ["opc.tcp://localhost:4840"],
                    SecurityPolicies = new List<ServerSecurityPolicy>
                    {
                        new() {
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                            SecurityMode = MessageSecurityMode.SignAndEncrypt
                        }
                    }.ToArrayOf()
                }
            };

            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };
            using (var writer = XmlWriter.Create(ms, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(ApplicationConfiguration), writer, ctx);
                config.Encode(encoder);
                encoder.Close();
            }

            File.WriteAllBytes(path, ms.ToArray());
        }
    }
}
