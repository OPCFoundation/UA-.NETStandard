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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Gds.Server;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GlobalDiscoveryServerConfigurationTests
    {
        [Test]
        public void DefaultConstructorCreatesInstance()
        {
            var config = new GlobalDiscoveryServerConfiguration();
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public void DefaultConstructorLeavesStringPropertiesNull()
        {
            var config = new GlobalDiscoveryServerConfiguration();

            Assert.That(config.AuthoritiesStorePath, Is.Null);
            Assert.That(config.ApplicationCertificatesStorePath, Is.Null);
            Assert.That(config.BaseCertificateGroupStorePath, Is.Null);
            Assert.That(config.DefaultSubjectNameContext, Is.Null);
            Assert.That(config.DatabaseStorePath, Is.Null);
            Assert.That(config.UsersDatabaseStorePath, Is.Null);
        }

        [Test]
        public void DefaultConstructorLeavesCollectionPropertiesNull()
        {
            var config = new GlobalDiscoveryServerConfiguration();

            Assert.That(config.CertificateGroups.IsEmpty, Is.True);
            Assert.That(config.KnownHostNames.IsEmpty, Is.True);
        }

        [Test]
        public void PropertiesCanBeSetAndRetrieved()
        {
            var config = new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath = "/auth/store",
                ApplicationCertificatesStorePath = "/app/certs",
                BaseCertificateGroupStorePath = "/base/store",
                DefaultSubjectNameContext = "O=Test",
                DatabaseStorePath = "/db/path.json",
                UsersDatabaseStorePath = "/users/path.json",
                KnownHostNames = ["host1", "host2"]
            };

            Assert.That(config.AuthoritiesStorePath, Is.EqualTo("/auth/store"));
            Assert.That(config.ApplicationCertificatesStorePath, Is.EqualTo("/app/certs"));
            Assert.That(config.BaseCertificateGroupStorePath, Is.EqualTo("/base/store"));
            Assert.That(config.DefaultSubjectNameContext, Is.EqualTo("O=Test"));
            Assert.That(config.DatabaseStorePath, Is.EqualTo("/db/path.json"));
            Assert.That(config.UsersDatabaseStorePath, Is.EqualTo("/users/path.json"));
            Assert.That(config.KnownHostNames.Count, Is.EqualTo(2));
            Assert.That(config.KnownHostNames[0], Is.EqualTo("host1"));
        }

        [Test]
        public void CertificateGroupConfigurationDefaultConstructorSetsDefaults()
        {
            var group = new CertificateGroupConfiguration();

            Assert.That(group.DefaultCertificateLifetime,
                Is.EqualTo(CertificateFactory.DefaultLifeTime));
            Assert.That(group.DefaultCertificateKeySize,
                Is.EqualTo(CertificateFactory.DefaultKeySize));
            Assert.That(group.DefaultCertificateHashSize,
                Is.EqualTo(CertificateFactory.DefaultHashSize));
            Assert.That(group.CACertificateLifetime,
                Is.EqualTo(CertificateFactory.DefaultLifeTime));
            Assert.That(group.CACertificateKeySize,
                Is.EqualTo(CertificateFactory.DefaultKeySize));
            Assert.That(group.CACertificateHashSize,
                Is.EqualTo(CertificateFactory.DefaultHashSize));
            Assert.That(group.CertificateTypes.IsEmpty, Is.True);
        }

        [Test]
        public void CertificateGroupConfigurationDefaultLeavesStringPropertiesNull()
        {
            var group = new CertificateGroupConfiguration();

            Assert.That(group.Id, Is.Null);
            Assert.That(group.SubjectName, Is.Null);
            Assert.That(group.BaseStorePath, Is.Null);
        }

        [Test]
        public void CertificateGroupConfigurationPropertiesCanBeSet()
        {
            var group = new CertificateGroupConfiguration
            {
                Id = "TestGroup",
                SubjectName = "CN=Test CA",
                BaseStorePath = "/test/store",
                DefaultCertificateLifetime = 24,
                DefaultCertificateKeySize = 4096,
                DefaultCertificateHashSize = 512,
                CACertificateLifetime = 120,
                CACertificateKeySize = 4096,
                CACertificateHashSize = 512,
                CertificateTypes = ["RsaSha256ApplicationCertificateType"]
            };

            Assert.That(group.Id, Is.EqualTo("TestGroup"));
            Assert.That(group.SubjectName, Is.EqualTo("CN=Test CA"));
            Assert.That(group.BaseStorePath, Is.EqualTo("/test/store"));
            Assert.That(group.DefaultCertificateLifetime, Is.EqualTo(24));
            Assert.That(group.DefaultCertificateKeySize, Is.EqualTo(4096));
            Assert.That(group.DefaultCertificateHashSize, Is.EqualTo(512));
            Assert.That(group.CACertificateLifetime, Is.EqualTo(120));
            Assert.That(group.CACertificateKeySize, Is.EqualTo(4096));
            Assert.That(group.CACertificateHashSize, Is.EqualTo(512));
            Assert.That(group.CertificateTypes.Count, Is.EqualTo(1));
            Assert.That(group.CertificateTypes[0],
                Is.EqualTo("RsaSha256ApplicationCertificateType"));
        }

        [Test]
        public void TrustedListPathCombinesBaseStorePathWithTrusted()
        {
            var group = new CertificateGroupConfiguration
            {
                BaseStorePath = "/test/store"
            };

            string expected = "/test/store" + Path.DirectorySeparatorChar + "trusted";
            Assert.That(group.TrustedListPath, Is.EqualTo(expected));
        }

        [Test]
        public void IssuerListPathCombinesBaseStorePathWithIssuer()
        {
            var group = new CertificateGroupConfiguration
            {
                BaseStorePath = "/test/store"
            };

            string expected = "/test/store" + Path.DirectorySeparatorChar + "issuer";
            Assert.That(group.IssuerListPath, Is.EqualTo(expected));
        }

        [Test]
        public void CertificateGroupsCanBeAssigned()
        {
            var config = new GlobalDiscoveryServerConfiguration();
            var group = new CertificateGroupConfiguration
            {
                Id = "Default",
                SubjectName = "CN=Test CA",
                BaseStorePath = "/store"
            };

            config.CertificateGroups = [group];

            Assert.That(config.CertificateGroups.IsEmpty, Is.False);
            Assert.That(config.CertificateGroups.Count, Is.EqualTo(1));
            Assert.That(config.CertificateGroups[0].Id, Is.EqualTo("Default"));
        }
    }
}
