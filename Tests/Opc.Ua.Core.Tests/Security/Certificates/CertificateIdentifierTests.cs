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
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateIdentifier")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateIdentifierTests
    {
        private Certificate m_selfSignedCert;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_selfSignedCert = CertificateBuilder.Create("CN=CertIdTest")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(365)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_selfSignedCert?.Dispose();
        }

        [Test]
        public void DefaultConstructorCreatesEmptyIdentifier()
        {
            var id = new CertificateIdentifier();
            Assert.That(id.Certificate, Is.Null);
            Assert.That(id.StoreType, Is.Null);
            Assert.That(id.StorePath, Is.Null);
            Assert.That(id.SubjectName, Is.Null);
            Assert.That(id.Thumbprint, Is.Null);
        }

        [Test]
        public void ConstructorWithCertificateSetsCertificate()
        {
            var id = new CertificateIdentifier(m_selfSignedCert);
            Assert.That(id.Certificate, Is.Not.Null);
            Assert.That(id.Certificate.Subject, Is.EqualTo("CN=CertIdTest"));
        }

        [Test]
        public void ConstructorWithCertificateAndValidationOptions()
        {
            var id = new CertificateIdentifier(
                m_selfSignedCert,
                CertificateValidationOptions.SuppressCertificateExpired);
            Assert.That(id.Certificate, Is.Not.Null);
            Assert.That(
                id.ValidationOptions,
                Is.EqualTo(CertificateValidationOptions.SuppressCertificateExpired));
        }

        [Test]
        public void ConstructorWithRawDataSetsCertificate()
        {
            byte[] rawData = m_selfSignedCert.RawData;
            var id = new CertificateIdentifier(rawData);
            Assert.That(id.Certificate, Is.Not.Null);
            Assert.That(id.Certificate.Subject, Is.EqualTo("CN=CertIdTest"));
        }

        [Test]
        public void ValidationOptionsDefaultsToZero()
        {
            var id = new CertificateIdentifier();
            Assert.That(id.ValidationOptions, Is.EqualTo(CertificateValidationOptions.Default));
        }

        [Test]
        public void ValidationOptionsCanBeSet()
        {
            var id = new CertificateIdentifier
            {
                ValidationOptions = CertificateValidationOptions.SuppressCertificateExpired
            };
            Assert.That(
                id.ValidationOptions,
                Is.EqualTo(CertificateValidationOptions.SuppressCertificateExpired));
        }

        [Test]
        public void DisposeCertificateNullsCertificateProperty()
        {
            // Use a separate certificate to avoid disposing the shared one
            using Certificate cert = CertificateBuilder.Create("CN=DisposeTest")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(365)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var id = new CertificateIdentifier(cert);
            Assert.That(id.Certificate, Is.Not.Null);
            id.DisposeCertificate();
            Assert.That(id.Certificate, Is.Null);
        }

        [Test]
        public void DisposeCertificateOnEmptyIdentifierDoesNotThrow()
        {
            var id = new CertificateIdentifier();
            Assert.DoesNotThrow(() => id.DisposeCertificate());
        }

        [Test]
        public void GetCertificateTypeReturnsRsaSha256ForSha256Cert()
        {
            NodeId certType = CertificateIdentifier.GetCertificateType(m_selfSignedCert);
            Assert.That(certType, Is.EqualTo(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void ValidateCertificateTypeWithNullTypeReturnsTrue()
        {
            bool result = CertificateIdentifier.ValidateCertificateType(
                m_selfSignedCert, NodeId.Null);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCertificateTypeRsaSha256Matches()
        {
            bool result = CertificateIdentifier.ValidateCertificateType(
                m_selfSignedCert,
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCertificateTypeRsaMinMatches()
        {
            bool result = CertificateIdentifier.ValidateCertificateType(
                m_selfSignedCert,
                ObjectTypeIds.RsaMinApplicationCertificateType);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCertificateTypeApplicationCertMatches()
        {
            bool result = CertificateIdentifier.ValidateCertificateType(
                m_selfSignedCert,
                ObjectTypeIds.ApplicationCertificateType);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCertificateTypeEccDoesNotMatchRsa()
        {
            bool result = CertificateIdentifier.ValidateCertificateType(
                m_selfSignedCert,
                ObjectTypeIds.EccNistP256ApplicationCertificateType);
            Assert.That(result, Is.False);
        }

        [Test]
        public void MapSecurityPolicyBasic256Sha256ReturnsRsaSha256()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.Basic256Sha256);
            Assert.That(types, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(types, Does.Contain(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyAes128ReturnsRsaSha256()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.Aes128_Sha256_RsaOaep);
            Assert.That(types, Does.Contain(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyAes256ReturnsRsaSha256()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.Aes256_Sha256_RsaPss);
            Assert.That(types, Does.Contain(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyBasic256ReturnsRsaMinAndRsaSha256()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.Basic256);
            Assert.That(types, Does.Contain(ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(types, Does.Contain(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyEccNistP256ReturnsNistTypes()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.ECC_nistP256);
            Assert.That(types, Does.Contain(ObjectTypeIds.EccNistP256ApplicationCertificateType));
            Assert.That(types, Does.Contain(ObjectTypeIds.EccNistP384ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyEccNistP384ReturnsP384Only()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.ECC_nistP384);
            Assert.That(types, Has.Count.EqualTo(1));
            Assert.That(types, Does.Contain(ObjectTypeIds.EccNistP384ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyEccBrainpoolP256r1ReturnsBrainpoolTypes()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.ECC_brainpoolP256r1);
            Assert.That(
                types,
                Does.Contain(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType));
            Assert.That(
                types,
                Does.Contain(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType));
        }

        [Test]
        public void MapSecurityPolicyHttpsReturnsHttpsType()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.Https);
            Assert.That(types, Does.Contain(ObjectTypeIds.HttpsCertificateType));
        }

        [Test]
        public void MapSecurityPolicyNoneReturnsEmpty()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(SecurityPolicies.None);
            Assert.That(types, Is.Empty);
        }

        [Test]
        public void MapSecurityPolicyUnknownReturnsEmpty()
        {
            IList<NodeId> types = CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes("http://unknown/policy");
            Assert.That(types, Is.Empty);
        }

        [Test]
        public void OpenStoreWithDirectoryStoreType()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "%LocalApplicationData%/OPC/CertIdTests/certs"
            };
            using ICertificateStore store = id.OpenStore(NUnitTelemetryContext.Create());
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void GetMinKeySizeForRsaMinReturnsConfiguredValue()
        {
            var secConfig = new SecurityConfiguration
            {
                MinimumCertificateKeySize = 2048
            };
            var id = new CertificateIdentifier
            {
                CertificateType = ObjectTypeIds.RsaMinApplicationCertificateType
            };
            ushort keySize = id.GetMinKeySize(secConfig);
            Assert.That(keySize, Is.EqualTo(2048));
        }

        [Test]
        public void GetMinKeySizeForRsaSha256ReturnsConfiguredValue()
        {
            var secConfig = new SecurityConfiguration
            {
                MinimumCertificateKeySize = 4096
            };
            var id = new CertificateIdentifier
            {
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            ushort keySize = id.GetMinKeySize(secConfig);
            Assert.That(keySize, Is.EqualTo(4096));
        }

        [Test]
        public void GetMinKeySizeForEccReturnsZero()
        {
            var secConfig = new SecurityConfiguration
            {
                MinimumCertificateKeySize = 2048
            };
            var id = new CertificateIdentifier
            {
                CertificateType = ObjectTypeIds.EccNistP256ApplicationCertificateType
            };
            ushort keySize = id.GetMinKeySize(secConfig);
            Assert.That(keySize, Is.Zero);
        }
    }

    [TestFixture]
    [Category("CertificateStoreIdentifier")]
    [Parallelizable]
    public class CertificateStoreIdentifierTests
    {
        [Test]
        public void DefaultConstructorCreatesDirectoryStoreType()
        {
            var id = new CertificateStoreIdentifier();
            Assert.That(id.StoreType, Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(id.StorePath, Is.Null.Or.Empty);
        }

        [Test]
        public void ConstructorWithDirectoryPathSetsDirectoryType()
        {
            var id = new CertificateStoreIdentifier(
                "%LocalApplicationData%/OPC/teststore");
            Assert.That(id.StoreType, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void ConstructorWithExplicitStoreType()
        {
            var id = new CertificateStoreIdentifier(
                "LocalMachine\\My", CertificateStoreType.X509Store);
            Assert.That(id.StoreType, Is.EqualTo(CertificateStoreType.X509Store));
        }

        [Test]
        public void DetermineStoreTypeNullReturnsDirectory()
        {
            string result = CertificateStoreIdentifier.DetermineStoreType(null);
            Assert.That(result, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void DetermineStoreTypeEmptyReturnsDirectory()
        {
            string result = CertificateStoreIdentifier.DetermineStoreType(string.Empty);
            Assert.That(result, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void DetermineStoreTypeLocalMachineReturnsX509()
        {
            string result = CertificateStoreIdentifier
                .DetermineStoreType("LocalMachine\\My");
            Assert.That(result, Is.EqualTo(CertificateStoreType.X509Store));
        }

        [Test]
        public void DetermineStoreTypeCurrentUserReturnsX509()
        {
            string result = CertificateStoreIdentifier
                .DetermineStoreType("CurrentUser\\My");
            Assert.That(result, Is.EqualTo(CertificateStoreType.X509Store));
        }

        [Test]
        public void DetermineStoreTypeArbitraryPathReturnsDirectory()
        {
            string result = CertificateStoreIdentifier
                .DetermineStoreType("/some/path/to/certs");
            Assert.That(result, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void CreateStoreNullTypeReturnsCertificateIdentifierCollectionStore()
        {
            using ICertificateStore store = CertificateStoreIdentifier
                .CreateStore(null, NUnitTelemetryContext.Create());
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void CreateStoreDirectoryTypeReturnsDirectoryStore()
        {
            using ICertificateStore store = CertificateStoreIdentifier
                .CreateStore(CertificateStoreType.Directory, NUnitTelemetryContext.Create());
            Assert.That(store, Is.Not.Null);
            Assert.That(store, Is.InstanceOf<DirectoryCertificateStore>());
        }

        [Test]
        public void CreateStoreX509TypeReturnsX509Store()
        {
            using ICertificateStore store = CertificateStoreIdentifier
                .CreateStore(CertificateStoreType.X509Store, NUnitTelemetryContext.Create());
            Assert.That(store, Is.Not.Null);
            Assert.That(store, Is.InstanceOf<X509CertificateStore>());
        }

        [Test]
        public void CreateStoreInvalidTypeThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                CertificateStoreIdentifier.CreateStore(
                    "InvalidStoreType", NUnitTelemetryContext.Create()));
        }

        [Test]
        public void OpenStoreReturnsStore()
        {
            var id = new CertificateStoreIdentifier(
                "%LocalApplicationData%/OPC/CertStoreIdTests/certs");
            using ICertificateStore store = id.OpenStore(NUnitTelemetryContext.Create());
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void ToStringReturnsStorePath()
        {
            const string path = "%LocalApplicationData%/OPC/test";
            var id = new CertificateStoreIdentifier(path);
            string result = id.ToString();
            Assert.That(result, Does.Contain(path));
        }

        [Test]
        public void ToStringFormattableReturnsValue()
        {
            const string path = "%LocalApplicationData%/OPC/test";
            CertificateStoreIdentifier formattable = new CertificateStoreIdentifier(path);
            string result = formattable.ToString(null, null);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }
    }
}
