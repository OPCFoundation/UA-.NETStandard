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

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateStore")]
    public sealed class CertificateStoreSnapshotRegressionTests
    {
        [TestCase("findHit")]
        [TestCase("findMiss")]
        [TestCase("deleteMiss")]
        [TestCase("deleteHit")]
        [TestCase("addDuplicate")]
        [TestCase("enumerate")]
        public async Task X509OperationsReleaseEveryUnreturnedNativeSnapshotCertificateAsync(string operation)
        {
            using Certificate first = CertificateBuilder.Create("CN=Snapshot First").CreateForRSA();
            using Certificate second = CertificateBuilder.Create("CN=Snapshot Second").CreateForRSA();
            using X509Certificate2 firstNative = X509CertificateLoader.LoadCertificate(first.RawData);
            using X509Certificate2 secondNative = X509CertificateLoader.LoadCertificate(second.RawData);
            var snapshot = new X509Certificate2Collection { firstNative, secondNative };
            using var store = new X509CertificateStore(NUnitTelemetryContext.Create(), _ => snapshot);
            store.Open(CertificateStoreIdentifier.CurrentUser + "My", noPrivateKeys: true);
            using var returned = new CertificateCollection();
            switch (operation)
            {
                case "findHit":
                case "findMiss":
                    using (CertificateCollection found = await store.FindByThumbprintAsync(
                        operation == "findHit" ? first.Thumbprint : new string('0', 40)).ConfigureAwait(false))
                    {
                        foreach (Certificate certificate in found)
                        {
                            returned.Add(certificate);
                        }
                    }
                    break;
                case "deleteMiss":
                case "deleteHit":
                    Assert.That(await store.DeleteAsync(operation == "deleteHit" ? first.Thumbprint : new string('0', 40))
                        .ConfigureAwait(false), Is.True);
                    break;
                case "addDuplicate":
                    await store.AddAsync(first).ConfigureAwait(false);
                    break;
                default:
                    using (CertificateCollection found = await store.EnumerateAsync().ConfigureAwait(false))
                    {
                        foreach (Certificate certificate in found)
                        {
                            returned.Add(certificate);
                        }
                    }
                    break;
            }
            store.Close();
            bool firstReturned = operation is "findHit" or "enumerate";
            bool secondReturned = operation == "enumerate";
            Assert.That(firstNative.Handle != IntPtr.Zero, Is.EqualTo(firstReturned));
            Assert.That(secondNative.Handle != IntPtr.Zero, Is.EqualTo(secondReturned));
            foreach (Certificate certificate in returned)
            {
                using RSA key = certificate.GetRSAPublicKey()!;
                Assert.That(key.KeySize, Is.EqualTo(2048));
            }
            returned.Clear();
            Assert.That(firstNative.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(secondNative.Handle, Is.EqualTo(IntPtr.Zero));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RemovingOnePemCertificateLeavesExactRemainingContentAndNoOldTailAsync(bool privateKey)
        {
            string path = Path.Combine(Path.GetTempPath(), "PemTail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            string fileName = Path.Combine(path, "bundle.pem");
            using Certificate removed = CertificateBuilder.Create("CN=Removed PEM").CreateForRSA();
            using Certificate retained = CertificateBuilder.Create("CN=Retained PEM").CreateForRSA();
            byte[] key = privateKey ? PEMWriter.ExportPrivateKeyAsPEM(retained) : [];
            byte[] original = PEMWriter.ExportCertificateAsPEM(removed)
                .Concat(PEMWriter.ExportCertificateAsPEM(retained)).Concat(key).ToArray();
            byte[]? expected = null;
            byte[]? actual = null;
            try
            {
                File.WriteAllBytes(fileName, original);
                Assert.That(PEMWriter.TryRemovePublicKeyFromPEM(removed.Thumbprint, original, out expected), Is.True);
                using (var store = new DirectoryCertificateStore(true, NUnitTelemetryContext.Create()))
                {
                    store.Open(path);
                    Assert.That(await store.DeleteAsync(removed.Thumbprint).ConfigureAwait(false), Is.True);
                }
                actual = File.ReadAllBytes(fileName);
                Assert.That(actual, Has.Length.EqualTo(expected!.Length));
                Assert.That(actual.SequenceEqual(expected), Is.True);
                using (var reopened = new DirectoryCertificateStore(true, NUnitTelemetryContext.Create()))
                {
                    reopened.Open(path);
                    using CertificateCollection certificates = await reopened.EnumerateAsync().ConfigureAwait(false);
                    Assert.That(certificates, Has.Count.EqualTo(1));
                    Assert.That(certificates[0].Thumbprint, Is.EqualTo(retained.Thumbprint));
                }
                if (privateKey)
                {
                    using Certificate publicCertificate = Certificate.FromRawData(retained.RawData);
                    using Certificate withKey = DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(
                        publicCertificate, actual);
                    using RSA signingKey = withKey.GetRSAPrivateKey()!;
                    using RSA publicKey = retained.GetRSAPublicKey()!;
                    byte[] data = [1, 2, 3, 4];
                    byte[] signature = signingKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    Assert.That(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                        Is.True);
                }
                Assert.That(Directory.GetFiles(path), Has.Length.EqualTo(1));
            }
            finally
            {
                CryptoUtils.ZeroMemory(key);
                CryptoUtils.ZeroMemory(original);
                if (expected != null)
                {
                    CryptoUtils.ZeroMemory(expected);
                }
                if (actual != null)
                {
                    CryptoUtils.ZeroMemory(actual);
                }
                Directory.Delete(path, recursive: true);
            }
        }

        [Test]
        [Platform("Win")]
        public async Task FailedPemReplacementUnderWindowsFileSharingPreservesTheOriginalFileAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), "PemFailure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            string fileName = Path.Combine(path, "bundle.pem");
            using Certificate first = CertificateBuilder.Create("CN=Locked PEM First").CreateForRSA();
            using Certificate second = CertificateBuilder.Create("CN=Locked PEM Second").CreateForRSA();
            byte[] original = PEMWriter.ExportCertificateAsPEM(first).Concat(PEMWriter.ExportCertificateAsPEM(second))
                .ToArray();
            try
            {
                File.WriteAllBytes(fileName, original);
                using var store = new DirectoryCertificateStore(true, NUnitTelemetryContext.Create());
                store.Open(path);
                using (var readLock = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.That(await store.DeleteAsync(first.Thumbprint).ConfigureAwait(false), Is.False);
                }
                Assert.That(File.ReadAllBytes(fileName).SequenceEqual(original), Is.True);
                Assert.That(Directory.GetFiles(path), Has.Length.EqualTo(1));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
