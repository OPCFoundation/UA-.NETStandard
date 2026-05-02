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
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the <see cref="Certificate"/> wrapper class.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateTests
    {
        private const string TestSubject = "CN=CertificateWrapperTest";

        #region Constructor Tests
        [Test]
        public void ConstructorFromByteArrayCreatesCertificate()
        {
            using Certificate built = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] rawData = built.RawData;
            using var cert = new Certificate(rawData);

            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.Subject, Is.EqualTo(built.Subject));
            Assert.That(cert.Thumbprint, Is.EqualTo(built.Thumbprint));
            Assert.That(cert.HasPrivateKey, Is.False);
        }
        #endregion

        #region Factory Method Tests
        [Test]
        public void FromWrapsX509Certificate2AndPreservesInstance()
        {
            using Certificate built = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            X509Certificate2 x509Copy = built.AsX509Certificate2();
            using var cert = Certificate.From(x509Copy);

            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.Subject, Is.EqualTo(built.Subject));
            Assert.That(cert.Thumbprint, Is.EqualTo(built.Thumbprint));
            Assert.That(cert.X509, Is.SameAs(x509Copy));
        }

        [Test]
        public void FromRawDataCreatesCertificateFromBytes()
        {
            using Certificate built = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] rawData = built.RawData;
            using var cert = Certificate.FromRawData(rawData);

            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.Thumbprint, Is.EqualTo(built.Thumbprint));
            Assert.That(cert.HasPrivateKey, Is.False);
        }
        #endregion

        #region AsX509Certificate2 Tests
        [Test]
        public void AsX509Certificate2ReturnsNewInstance()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using X509Certificate2 copy = cert.AsX509Certificate2();

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy, Is.Not.SameAs(cert.X509));
            Assert.That(copy.Thumbprint, Is.EqualTo(cert.Thumbprint));
            Assert.That(copy.Subject, Is.EqualTo(cert.Subject));
        }

        [Test]
        public void AsX509Certificate2PreservesPrivateKey()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(cert.HasPrivateKey, Is.True);

            using X509Certificate2 copy = cert.AsX509Certificate2();

            Assert.That(copy.HasPrivateKey, Is.True);
            Assert.That(copy.Thumbprint, Is.EqualTo(cert.Thumbprint));
        }
        #endregion

        #region Property Tests
        [Test]
        public void PropertiesMatchUnderlyingX509Certificate2()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetLifeTime(12)
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            X509Certificate2 x509 = cert.X509;

            Assert.That(cert.Subject, Is.EqualTo(x509.Subject));
            Assert.That(cert.Thumbprint, Is.EqualTo(x509.Thumbprint));
            Assert.That(cert.NotBefore, Is.EqualTo(x509.NotBefore));
            Assert.That(cert.NotAfter, Is.EqualTo(x509.NotAfter));
            Assert.That(cert.SubjectName.Name, Is.EqualTo(x509.SubjectName.Name));
            Assert.That(cert.IssuerName.Name, Is.EqualTo(x509.IssuerName.Name));
            Assert.That(cert.RawData, Is.EqualTo(x509.RawData));
            Assert.That(cert.HasPrivateKey, Is.EqualTo(x509.HasPrivateKey));
            Assert.That(cert.PublicKey.Oid.Value, Is.EqualTo(x509.PublicKey.Oid.Value));
            Assert.That(cert.Issuer, Is.EqualTo(x509.Issuer));
            Assert.That(cert.SignatureAlgorithm.Value, Is.EqualTo(x509.SignatureAlgorithm.Value));
            Assert.That(cert.SerialNumber, Is.EqualTo(x509.SerialNumber));
            Assert.That(cert.Extensions.Count, Is.EqualTo(x509.Extensions.Count));
        }

        [Test]
        public void HashAlgorithmNameIsCorrect()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(cert.HashAlgorithmName, Is.EqualTo(HashAlgorithmName.SHA256));
        }
        #endregion

        #region Method Tests
        [Test]
        public void GetRSAPrivateKeyReturnsKeyWhenPresent()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using RSA privateKey = cert.GetRSAPrivateKey();

            Assert.That(privateKey, Is.Not.Null);
        }

        [Test]
        public void GetRSAPublicKeyReturnsKey()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using RSA publicKey = cert.GetRSAPublicKey();

            Assert.That(publicKey, Is.Not.Null);
            Assert.That(publicKey.KeySize, Is.EqualTo(2048));
        }

        [Test]
        public void GetRSAPrivateKeyReturnsNullForPublicOnlyCert()
        {
            using Certificate built = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var cert = Certificate.FromRawData(built.RawData);
            using RSA privateKey = cert.GetRSAPrivateKey();

            Assert.That(privateKey, Is.Null);
        }

        [Test]
        public void GetKeyAlgorithmReturnsOid()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string algorithm = cert.GetKeyAlgorithm();

            Assert.That(algorithm, Is.Not.Null.And.Not.Empty);
            Assert.That(algorithm, Is.EqualTo(cert.X509.GetKeyAlgorithm()));
        }

        [Test]
        public void GetNameInfoReturnsSubjectSimpleName()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string subjectName = cert.GetNameInfo(X509NameType.SimpleName, false);
            string issuerName = cert.GetNameInfo(X509NameType.SimpleName, true);

            Assert.That(subjectName, Is.Not.Null.And.Not.Empty);
            Assert.That(
                subjectName,
                Is.EqualTo(cert.X509.GetNameInfo(X509NameType.SimpleName, false)));
            Assert.That(issuerName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetSerialNumberReturnsBytes()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] serialNumber = cert.GetSerialNumber();

            Assert.That(serialNumber, Is.Not.Null);
            Assert.That(serialNumber.Length, Is.GreaterThan(0));
            Assert.That(serialNumber, Is.EqualTo(cert.X509.GetSerialNumber()));
        }
        #endregion

        #region CopyWithPrivateKey Tests
        [Test]
        public void CopyWithPrivateKeyAttachesRSAKey()
        {
            using Certificate built = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var publicOnly = Certificate.FromRawData(built.RawData);

            Assert.That(publicOnly.HasPrivateKey, Is.False);

            using RSA rsa = built.GetRSAPrivateKey();
            using Certificate withKey = publicOnly.CopyWithPrivateKey(rsa);

            Assert.That(withKey.HasPrivateKey, Is.True);
            Assert.That(withKey.Thumbprint, Is.EqualTo(publicOnly.Thumbprint));
        }
        #endregion

        #region IEquatable Tests
        [Test]
        public void EqualsCertificatesFromSameDataAreEqual()
        {
            using Certificate cert1 = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var cert2 = Certificate.FromRawData(cert1.RawData);

            Assert.That(cert1.Equals(cert2), Is.True);
            Assert.That(cert1.GetHashCode(), Is.EqualTo(cert2.GetHashCode()));
        }

        [Test]
        public void EqualsDifferentCertificatesAreNotEqual()
        {
            using Certificate certA = CertificateBuilder
                .Create("CN=CertA")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using Certificate certB = CertificateBuilder
                .Create("CN=CertB")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(certA.Equals(certB), Is.False);
        }

        [Test]
        public void EqualsNullReturnsFalse()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(cert.Equals((Certificate)null), Is.False);
            Assert.That(cert.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsSameReferenceReturnsTrue()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(cert.Equals(cert), Is.True);
        }

        [Test]
        public void EqualsObjectOverloadWorks()
        {
            using Certificate cert1 = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var cert2 = Certificate.FromRawData(cert1.RawData);

            Assert.That(cert1.Equals((object)cert2), Is.True);
        }
        #endregion

        #region ToString Tests
        [Test]
        public void ToStringReturnsNonEmptyString()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string result = cert.ToString();

            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }
        #endregion

        #region Dispose Tests
        [Test]
        public void DisposeDoesNotThrow()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.DoesNotThrow(() => cert.Dispose());
        }

        [Test]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            using Certificate cert = CertificateBuilder
                .Create(TestSubject)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.DoesNotThrow(() =>
            {
                cert.Dispose();
                cert.Dispose();
                cert.Dispose();
            });
        }
        #endregion
    }

    /// <summary>
    /// Tests for the <see cref="CertificateCollection"/> class.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateCollectionTests
    {
        private const string TestSubject = "CN=CollectionTest";

        /// <summary>
        /// Helper to create a <see cref="Certificate"/> wrapping
        /// a fresh self-signed RSA cert.
        /// </summary>
        private static Certificate CreateTestCertificate(string cn = TestSubject)
        {
            return CertificateBuilder
                .Create(cn)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        #region Constructor Tests
        [Test]
        public void EmptyConstructorCreatesEmptyCollection()
        {
            using var collection = new CertificateCollection();

            Assert.That(collection.Count, Is.EqualTo(0));
        }

        [Test]
        public void CapacityConstructorCreatesEmptyCollection()
        {
            using var collection = new CertificateCollection(10);

            Assert.That(collection.Count, Is.EqualTo(0));
        }

        [Test]
        public void EnumerableConstructorPopulatesCollection()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Enum1");
            using Certificate cert2 = CreateTestCertificate("CN=Enum2");
            var list = new List<Certificate> { cert1, cert2 };

            using var collection = new CertificateCollection(list);

            Assert.That(collection.Count, Is.EqualTo(2));
        }
        #endregion

        #region Add and Count Tests
        [Test]
        public void AddIncreasesCount()
        {
            using var collection = new CertificateCollection();
            using Certificate cert1 = CreateTestCertificate("CN=Add1");
            using Certificate cert2 = CreateTestCertificate("CN=Add2");

            collection.Add(cert1);

            Assert.That(collection.Count, Is.EqualTo(1));

            collection.Add(cert2);

            Assert.That(collection.Count, Is.EqualTo(2));
        }
        #endregion

        #region From Factory Tests
        [Test]
        public void FromCreatesCollectionFromX509Certificate2Collection()
        {
            using Certificate builtA = CertificateBuilder
                .Create("CN=FromA")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate builtB = CertificateBuilder
                .Create("CN=FromB")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var x509Collection = new X509Certificate2Collection
            {
                X509CertificateLoader.LoadCertificate(builtA.RawData),
                X509CertificateLoader.LoadCertificate(builtB.RawData)
            };

            using var collection = CertificateCollection.From(x509Collection);

            Assert.That(collection.Count, Is.EqualTo(2));
            Assert.That(collection[0].Thumbprint, Is.EqualTo(builtA.Thumbprint));
            Assert.That(collection[1].Thumbprint, Is.EqualTo(builtB.Thumbprint));
        }

        [Test]
        public void FromThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(
                () => CertificateCollection.From(null));
        }
        #endregion

        #region AsX509Certificate2Collection Tests
        [Test]
        public void AsX509Certificate2CollectionReturnsCopies()
        {
            using Certificate cert = CreateTestCertificate();
            using var collection = new CertificateCollection();
            collection.Add(cert);

            X509Certificate2Collection x509Col =
                collection.AsX509Certificate2Collection();

            Assert.That(x509Col.Count, Is.EqualTo(1));
            Assert.That(x509Col[0].Thumbprint, Is.EqualTo(cert.Thumbprint));

            Assert.That(x509Col[0], Is.Not.SameAs(cert.X509));
        }
        #endregion

        #region Find Tests
        [Test]
        public void FindByThumbprintReturnsMatchingCert()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Find1");
            using Certificate cert2 = CreateTestCertificate("CN=Find2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            using CertificateCollection found = collection.Find(
                X509FindType.FindByThumbprint,
                cert1.Thumbprint,
                false);

            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].Thumbprint, Is.EqualTo(cert1.Thumbprint));
        }

        [Test]
        public void FindByThumbprintReturnsEmptyWhenNoMatch()
        {
            using Certificate cert = CreateTestCertificate();
            using var collection = new CertificateCollection();
            collection.Add(cert);

            using CertificateCollection found = collection.Find(
                X509FindType.FindByThumbprint,
                "0000000000000000000000000000000000000000",
                false);

            Assert.That(found.Count, Is.EqualTo(0));
        }

        [Test]
        public void FindBySubjectDistinguishedNameReturnsMatch()
        {
            using Certificate cert1 = CreateTestCertificate("CN=SubjectA");
            using Certificate cert2 = CreateTestCertificate("CN=SubjectB");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            using CertificateCollection found = collection.Find(
                X509FindType.FindBySubjectDistinguishedName,
                cert2.Subject,
                false);

            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].Subject, Is.EqualTo(cert2.Subject));
        }

        [Test]
        public void FindBySerialNumberReturnsMatch()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Serial1");
            using Certificate cert2 = CreateTestCertificate("CN=Serial2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            using CertificateCollection found = collection.Find(
                X509FindType.FindBySerialNumber,
                cert1.SerialNumber,
                false);

            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].SerialNumber, Is.EqualTo(cert1.SerialNumber));
        }
        #endregion

        #region Contains and IndexOf Tests
        [Test]
        public void ContainsReturnsTrueForAddedCert()
        {
            using Certificate cert = CreateTestCertificate();
            using var collection = new CertificateCollection();
            collection.Add(cert);

            Assert.That(collection.Contains(cert), Is.True);
        }

        [Test]
        public void ContainsReturnsFalseForMissingCert()
        {
            using Certificate cert = CreateTestCertificate("CN=InCollection");
            using Certificate other = CreateTestCertificate("CN=NotInCollection");
            using var collection = new CertificateCollection();
            collection.Add(cert);

            bool result = collection.Contains(other);

            Assert.That(result, Is.False);

            other.Dispose();
        }

        [Test]
        public void IndexOfReturnsCorrectIndex()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Idx0");
            using Certificate cert2 = CreateTestCertificate("CN=Idx1");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            Assert.That(collection.IndexOf(cert1), Is.EqualTo(0));
            Assert.That(collection.IndexOf(cert2), Is.EqualTo(1));
        }
        #endregion

        #region Remove Tests
        [Test]
        public void RemoveDeletesEntry()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Rem1");
            using Certificate cert2 = CreateTestCertificate("CN=Rem2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            bool removed = collection.Remove(cert1);

            Assert.That(removed, Is.True);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0].Subject, Is.EqualTo(cert2.Subject));

            cert1.Dispose();
        }

        [Test]
        public void RemoveAtDeletesByIndex()
        {
            using Certificate cert1 = CreateTestCertificate("CN=RemAt0");
            using Certificate cert2 = CreateTestCertificate("CN=RemAt1");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            collection.RemoveAt(0);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0].Thumbprint, Is.EqualTo(cert2.Thumbprint));

            cert1.Dispose();
        }
        #endregion

        #region Clear Tests
        [Test]
        public void ClearRemovesAllEntries()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Clr1");
            using Certificate cert2 = CreateTestCertificate("CN=Clr2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            collection.Clear();

            Assert.That(collection.Count, Is.EqualTo(0));

            cert1.Dispose();
            cert2.Dispose();
        }
        #endregion

        #region Indexer Tests
        [Test]
        public void IndexerGetReturnsCorrectCertificate()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Get0");
            using Certificate cert2 = CreateTestCertificate("CN=Get1");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            Assert.That(collection[0], Is.SameAs(cert1));
            Assert.That(collection[1], Is.SameAs(cert2));
        }

        [Test]
        public void IndexerSetReplacesCertificate()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Set0");
            using Certificate replacement = CreateTestCertificate("CN=Replaced");
            using var collection = new CertificateCollection();
            collection.Add(cert1);

            collection[0] = replacement;

            Assert.That(collection[0], Is.SameAs(replacement));
            Assert.That(collection.Count, Is.EqualTo(1));

            cert1.Dispose();
        }
        #endregion

        #region Insert Tests
        [Test]
        public void InsertAddsAtCorrectPosition()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Ins0");
            using Certificate cert2 = CreateTestCertificate("CN=Ins2");
            using Certificate inserted = CreateTestCertificate("CN=Ins1");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            collection.Insert(1, inserted);

            Assert.That(collection.Count, Is.EqualTo(3));
            Assert.That(collection[1], Is.SameAs(inserted));
        }
        #endregion

        #region CopyTo Tests
        [Test]
        public void CopyToCopiesElements()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Cpy1");
            using Certificate cert2 = CreateTestCertificate("CN=Cpy2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            var array = new Certificate[2];
            collection.CopyTo(array, 0);

            Assert.That(array[0], Is.SameAs(cert1));
            Assert.That(array[1], Is.SameAs(cert2));
        }
        #endregion

        #region Enumeration Tests
        [Test]
        public void ForeachEnumeratesAllCertificates()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Each1");
            using Certificate cert2 = CreateTestCertificate("CN=Each2");
            using Certificate cert3 = CreateTestCertificate("CN=Each3");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);
            collection.Add(cert3);

            var enumerated = new List<Certificate>();

            foreach (Certificate c in collection)
            {
                enumerated.Add(c);
            }

            Assert.That(enumerated.Count, Is.EqualTo(3));
            Assert.That(enumerated[0], Is.SameAs(cert1));
            Assert.That(enumerated[1], Is.SameAs(cert2));
            Assert.That(enumerated[2], Is.SameAs(cert3));
        }

        [Test]
        public void LinqWorksOnCollection()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Linq1");
            using Certificate cert2 = CreateTestCertificate("CN=Linq2");
            using var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            List<string> subjects = collection.Select(c => c.Subject).ToList();

            Assert.That(subjects.Count, Is.EqualTo(2));
            Assert.That(subjects, Does.Contain(cert1.Subject));
            Assert.That(subjects, Does.Contain(cert2.Subject));
        }
        #endregion

        #region IsReadOnly Tests
        [Test]
        public void IsReadOnlyReturnsFalse()
        {
            using var collection = new CertificateCollection();

            Assert.That(collection.IsReadOnly, Is.False);
        }
        #endregion

        #region Dispose Tests
        [Test]
        public void DisposeDisposesAllContainedCertificates()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Disp1");
            using Certificate cert2 = CreateTestCertificate("CN=Disp2");
            X509Certificate2 inner1 = cert1.X509;
            X509Certificate2 inner2 = cert2.X509;

            var collection = new CertificateCollection();
            collection.Add(cert1);
            collection.Add(cert2);

            // Dispose the caller's refs (Add already AddRef'd for the collection)
            cert1.Dispose();
            cert2.Dispose();

            // Now the collection is the sole owner
            collection.Dispose();

            // After disposal, accessing inner cert properties throws
            // (CryptographicException on .NET 10+, ObjectDisposedException on older runtimes)
            Exception caughtException1 = Assert.Catch(() => _ = inner1.RawData);
            Assert.That(caughtException1, Is.InstanceOf<ObjectDisposedException>()
                .Or.InstanceOf<System.Security.Cryptography.CryptographicException>());

            Exception caughtException2 = Assert.Catch(() => _ = inner2.RawData);
            Assert.That(caughtException2, Is.InstanceOf<ObjectDisposedException>()
                .Or.InstanceOf<System.Security.Cryptography.CryptographicException>());
        }

        [Test]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            var collection = new CertificateCollection();
            using Certificate cert = CreateTestCertificate();
            collection.Add(cert);

            Assert.DoesNotThrow(() =>
            {
                collection.Dispose();
                collection.Dispose();
                collection.Dispose();
            });
        }

        [Test]
        public void AccessAfterDisposeThrowsObjectDisposedException()
        {
            var collection = new CertificateCollection();
            using Certificate certX = CreateTestCertificate("CN=X");
            collection.Add(certX);
            collection.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = collection.Count);
            using Certificate certY = CreateTestCertificate("CN=Y");
            Assert.Throws<ObjectDisposedException>(
                () => collection.Add(certY));
        }
        #endregion

        #region Ownership Transfer Tests

        [Test]
        public void CertificateEntryOwnsIndependentRefs()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=EntryTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var chain = new CertificateCollection();

            var entry = new CertificateEntry(
                cert, chain, new NodeId(0));

            // Caller disposes their references — entry still holds AddRef'd refs
            cert.Dispose();
            chain.Dispose();

            // Entry's certificate should still be alive
            Assert.That(entry.Certificate.Thumbprint, Is.Not.Null);
            Assert.That(entry.Certificate.Subject, Is.Not.Null);

            // Entry dispose cleans up
            entry.Dispose();
        }

        [Test]
        public void FindReturnsIndependentlyDisposableCollection()
        {
            using var source = new CertificateCollection();
            using Certificate cert = CreateTestCertificate("CN=FindTest");
            source.Add(cert);

            CertificateCollection found = source.Find(
                X509FindType.FindByThumbprint,
                cert.Thumbprint,
                false);

            // Dispose the found collection (contains AddRef'd copy)
            found.Dispose();

            // Source's cert should still be alive
            Assert.That(source[0].Thumbprint, Is.Not.Null);
        }

        [Test]
        public void AddRefIncreasesAllMemberRefCounts()
        {
            using Certificate cert1 = CreateTestCertificate("CN=Ref1");
            using Certificate cert2 = CreateTestCertificate("CN=Ref2");
            var collection = new CertificateCollection { cert1, cert2 };

            collection.AddRef();

            // Dispose the collection — decrements each member once
            collection.Dispose();

            // Members should still be alive (extra ref from AddRef)
            Assert.That(cert1.Thumbprint, Is.Not.Null);
            Assert.That(cert2.Thumbprint, Is.Not.Null);

            // Final cleanup
            cert1.Dispose();
            cert2.Dispose();
        }

        #endregion

#if DEBUG
        #region Leak Detection Finalizer Tests

        [Test]
        public void VerifyLeakDetectionTracksAllocation()
        {
            WeakReference weakRef = CreateLeakedCertificateRef("CN=LeakTest");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(weakRef.IsAlive, Is.False);
        }

        [Test]
        public void VerifyNoLeakWhenProperlyDisposed()
        {
            WeakReference weakRef = CreateDisposedCertificateRef(
                "CN=NoLeakTest");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(weakRef.IsAlive, Is.False);
        }

        [Test]
        public void VerifyAddRefDisposeNoLeak()
        {
            WeakReference weakRef = CreateAddRefDisposedCertificateRef(
                "CN=RefCountTest");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(weakRef.IsAlive, Is.False);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference CreateLeakedCertificateRef(string cn)
        {
            Certificate cert = CertificateBuilder.Create(cn)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            return new WeakReference(cert);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference CreateDisposedCertificateRef(string cn)
        {
            Certificate cert = CertificateBuilder.Create(cn)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            cert.Dispose();
            return new WeakReference(cert);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference CreateAddRefDisposedCertificateRef(
            string cn)
        {
            Certificate cert = CertificateBuilder.Create(cn)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            cert.AddRef();
            cert.Dispose();
            cert.Dispose();
            return new WeakReference(cert);
        }

        #endregion
#endif
    }
}
