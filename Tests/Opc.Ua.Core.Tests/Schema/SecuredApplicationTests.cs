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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Security;
using Opc.Ua.Tests;
using SecurityNs = Opc.Ua.Security;

#pragma warning disable IDE0004 // Remove Unnecessary Cast

namespace Opc.Ua.Core.Tests.Schema
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SecuredApplicationTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void FromApplicationTypeRoundTrip()
        {
            foreach (SecurityNs.ApplicationType appType in
#if NET8_0_OR_GREATER
                Enum.GetValues<SecurityNs.ApplicationType>())
#else
                (SecurityNs.ApplicationType[])Enum.GetValues(typeof(SecurityNs.ApplicationType)))
#endif
            {
                ApplicationType uaType = SecuredApplication.FromApplicationType(appType);
                SecurityNs.ApplicationType roundTripped = SecuredApplication.ToApplicationType(uaType);
                Assert.That(roundTripped, Is.EqualTo(appType));
            }
        }

        [Test]
        public void ToCertificateIdentifierRoundTrip()
        {
            var input = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "%LocalApplicationData%/OPC/certs",
                SubjectName = "CN=TestApp",
                Thumbprint = "AABB"
            };

            var secured =
                SecuredApplication.ToCertificateIdentifier(input);

            Assert.That(secured, Is.Not.Null);
            Assert.That(secured.StoreType, Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(secured.StorePath, Is.EqualTo(input.StorePath));
            Assert.That(secured.SubjectName, Is.EqualTo("CN=TestApp"));

            CertificateIdentifier restored =
                SecuredApplication.FromCertificateIdentifier(secured);

            Assert.That(restored.StoreType, Is.EqualTo(input.StoreType));
            Assert.That(restored.StorePath, Is.EqualTo(input.StorePath));
            Assert.That(restored.SubjectName, Is.EqualTo(input.SubjectName));
        }

        [Test]
        public void ToCertificateIdentifierNullReturnsNull()
        {
            var result =
                SecuredApplication.ToCertificateIdentifier(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ToCertificateIdentifierEmptyStoreReturnsNull()
        {
            var input = new CertificateIdentifier
            {
                StoreType = null,
                StorePath = null
            };
            var result =
                SecuredApplication.ToCertificateIdentifier(input);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FromCertificateIdentifierNullCreatesEmpty()
        {
            CertificateIdentifier result =
                SecuredApplication.FromCertificateIdentifier(null);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StoreType, Is.Null);
        }

        [Test]
        public void ToCertificateStoreIdentifierRoundTrip()
        {
            var input = new CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "%LocalApplicationData%/OPC/trusted"
            };

            var secured =
                SecuredApplication.ToCertificateStoreIdentifier(input);

            Assert.That(secured, Is.Not.Null);
            Assert.That(secured.StorePath, Is.EqualTo(input.StorePath));

            CertificateStoreIdentifier restored =
                SecuredApplication.FromCertificateStoreIdentifier(secured);

            Assert.That(restored.StoreType, Is.EqualTo(input.StoreType));
            Assert.That(restored.StorePath, Is.EqualTo(input.StorePath));
        }

        [Test]
        public void ToCertificateStoreIdentifierNullReturnsNull()
        {
            var result =
                SecuredApplication.ToCertificateStoreIdentifier(
                    (CertificateStoreIdentifier)null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FromCertificateStoreIdentifierNullCreatesDefault()
        {
            CertificateStoreIdentifier result =
                SecuredApplication.FromCertificateStoreIdentifier(null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FromCertificateStoreIdentifierToTrustListRoundTrip()
        {
            var input = new SecurityNs.CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "/trusted/certs"
            };

            CertificateTrustList trustList =
                SecuredApplication.FromCertificateStoreIdentifierToTrustList(input);

            Assert.That(trustList.StoreType, Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(trustList.StorePath, Is.EqualTo("/trusted/certs"));
        }

        [Test]
        public void FromCertificateStoreIdentifierToTrustListNullCreatesDefault()
        {
            CertificateTrustList trustList =
                SecuredApplication.FromCertificateStoreIdentifierToTrustList(null);
            Assert.That(trustList, Is.Not.Null);
        }

        [Test]
        public void ToCertificateTrustListRoundTrip()
        {
            var input = new SecurityNs.CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "/peer/certs",
                ValidationOptions = 1
            };

            var trust =
                SecuredApplication.ToCertificateTrustList(input);

            Assert.That(trust.StoreType, Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(trust.StorePath, Is.EqualTo("/peer/certs"));
        }

        [Test]
        public void ToCertificateListRoundTrip()
        {
            var certs = new List<CertificateIdentifier>
            {
                new() {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/1",
                    SubjectName = "CN=Cert1"
                },
                new() {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/2",
                    SubjectName = "CN=Cert2"
                }
            };

            var secured =
                SecuredApplication.ToCertificateList(certs.ToArrayOf());

            Assert.That(secured.Certificates, Is.Not.Null);
            Assert.That(secured.Certificates, Has.Count.EqualTo(2));

            ArrayOf<CertificateIdentifier> restored =
                SecuredApplication.FromCertificateList(secured);

            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored[0].SubjectName, Is.EqualTo("CN=Cert1"));
            Assert.That(restored[1].SubjectName, Is.EqualTo("CN=Cert2"));
        }

        [Test]
        public void FromCertificateListNullReturnsEmpty()
        {
            ArrayOf<CertificateIdentifier> result =
                SecuredApplication.FromCertificateList(null);
            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void ToListOfBaseAddressesRoundTrip()
        {
            var config = new ServerConfiguration
            {
                BaseAddresses = new List<string>
                {
                    "opc.tcp://host:4840",
                    "https://host:443"
                },
                AlternateBaseAddresses = new List<string>
                {
                    "opc.tcp://althost:4840"
                }
            };

            var addresses =
                SecuredApplication.ToListOfBaseAddresses(config);

            Assert.That(addresses, Has.Count.EqualTo(3));

            var restored = new ServerConfiguration();
            SecuredApplication.FromListOfBaseAddresses(restored, addresses);

            Assert.That(restored.BaseAddresses.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ToListOfBaseAddressesNullReturnsEmpty()
        {
            var result =
                SecuredApplication.ToListOfBaseAddresses(null);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FromListOfBaseAddressesNullDoesNotThrow()
        {
            var config = new ServerConfiguration();
            Assert.DoesNotThrow(
                () => SecuredApplication.FromListOfBaseAddresses(config, null));
        }

        [Test]
        public void ToListOfSecurityProfilesContainsKnownPolicies()
        {
            var policies = new List<ServerSecurityPolicy>
            {
                new() {
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt
                }
            };

            var profiles =
                SecuredApplication.ToListOfSecurityProfiles(policies.ToArrayOf());

            Assert.That(profiles, Is.Not.Empty);

            bool foundEnabled = false;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].ProfileUri == SecurityPolicies.Basic256Sha256 &&
                    profiles[i].Enabled)
                {
                    foundEnabled = true;
                }
            }
            Assert.That(foundEnabled, Is.True);
        }

        [Test]
        public void FromListOfSecurityProfilesReturnsEnabledOnly()
        {
            var profiles = new ListOfSecurityProfiles
            {
                new SecurityProfile
                {
                    ProfileUri = SecurityPolicies.Basic256Sha256,
                    Enabled = true
                },
                new SecurityProfile
                {
                    ProfileUri = SecurityPolicies.Basic128Rsa15,
                    Enabled = false
                }
            };

            ArrayOf<ServerSecurityPolicy> policies =
                SecuredApplication.FromListOfSecurityProfiles(profiles);

            Assert.That(policies.Count, Is.EqualTo(1));
            Assert.That(
                policies[0].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void FromListOfSecurityProfilesEmptyAddsNone()
        {
            var profiles = new ListOfSecurityProfiles();

            ArrayOf<ServerSecurityPolicy> policies =
                SecuredApplication.FromListOfSecurityProfiles(profiles);

            Assert.That(policies.Count, Is.EqualTo(1));
            Assert.That(
                policies[0].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.None));
        }

        [Test]
        public void SecuredApplicationEncodingRoundTrip()
        {
            var app = new SecuredApplication
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = SecurityNs.ApplicationType.Server_0,
                ProductName = "TestProduct",
                ConfigurationFile = "test.config",
                ExecutableFile = "test.exe",
                LastExportTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ApplicationCertificate = new SecurityNs.CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/app",
                    SubjectName = "CN=TestApp"
                },
                TrustedCertificateStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/trusted"
                },
                IssuerCertificateStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/issuers"
                },
                RejectedCertificatesStore = new SecurityNs.CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "/certs/rejected"
                },
                BaseAddresses =
                [
                    "opc.tcp://testhost:4840"
                ],
                SecurityProfiles =
                [
                    new SecurityProfile
                    {
                        ProfileUri = SecurityPolicies.Basic256Sha256,
                        Enabled = true
                    }
                ]
            };

            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);

            using var ms = new MemoryStream();
            using (var writer = XmlWriter.Create(ms, Utils.DefaultXmlWriterSettings()))
            {
                using var encoder = new XmlEncoder(
                    typeof(SecuredApplication), writer, ctx);
                SecuredApplicationEncoding.EncodeContents(encoder, app);
                encoder.Close();
            }

            ms.Position = 0;

            var decoded = new SecuredApplication();
            var parser = new XmlParser(typeof(SecuredApplication), ms, ctx);
            SecuredApplicationEncoding.DecodeContents(parser, decoded);

            Assert.That(decoded.ApplicationName, Is.EqualTo("TestApp"));
            Assert.That(decoded.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(
                decoded.ApplicationType,
                Is.EqualTo(SecurityNs.ApplicationType.Server_0));
            Assert.That(decoded.ProductName, Is.EqualTo("TestProduct"));
            Assert.That(decoded.ApplicationCertificate, Is.Not.Null);
            Assert.That(
                decoded.ApplicationCertificate.SubjectName,
                Is.EqualTo("CN=TestApp"));
            Assert.That(decoded.TrustedCertificateStore, Is.Not.Null);
            Assert.That(decoded.IssuerCertificateStore, Is.Not.Null);
            Assert.That(decoded.RejectedCertificatesStore, Is.Not.Null);
            Assert.That(decoded.BaseAddresses, Is.Not.Null);
            Assert.That(decoded.BaseAddresses, Is.Not.Empty);
            Assert.That(decoded.SecurityProfiles, Is.Not.Null);
            Assert.That(decoded.SecurityProfiles, Is.Not.Empty);
        }

        [Test]
        public void SecuredApplicationEncodingMinimalRoundTrip()
        {
            var app = new SecuredApplication
            {
                ApplicationName = "MinApp",
                ApplicationUri = "urn:min:app",
                ApplicationType = SecurityNs.ApplicationType.Client_1
            };

            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);

            using var ms = new MemoryStream();
            using (var writer = XmlWriter.Create(ms, Utils.DefaultXmlWriterSettings()))
            {
                using var encoder = new XmlEncoder(
                    typeof(SecuredApplication), writer, ctx);
                SecuredApplicationEncoding.EncodeContents(encoder, app);
                encoder.Close();
            }

            ms.Position = 0;

            var decoded = new SecuredApplication();
            var parser = new XmlParser(typeof(SecuredApplication), ms, ctx);
            SecuredApplicationEncoding.DecodeContents(parser, decoded);

            Assert.That(decoded.ApplicationName, Is.EqualTo("MinApp"));
            Assert.That(decoded.ApplicationUri, Is.EqualTo("urn:min:app"));
            Assert.That(
                decoded.ApplicationType,
                Is.EqualTo(SecurityNs.ApplicationType.Client_1));
        }
    }
}
