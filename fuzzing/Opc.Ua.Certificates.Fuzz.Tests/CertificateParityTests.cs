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

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Positive-path, rejection, ownership and oracle tests using temporary generated fixtures.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class CertificateParityTests
    {
        [OneTimeSetUp]
        public void GenerateRuntimeFixtures()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "OpcUaCertificateFuzz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
            m_prefix = Path.Combine(m_directory, "Testcases");
            AssertBalancedCertificates(() => Testcases.Run(m_prefix, NUnitTelemetryContext.Create()));
        }

        [OneTimeTearDown]
        public void DeleteRuntimeFixtures()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        [TestCase("certificate.der", "Fuzzing Test Application")]
        [TestCase("certificate-expired.der", "Fuzzing Test Expired")]
        [TestCase("certificate-selfsigned.der", "Fuzzing Test SelfSigned")]
        [TestCase("certificate-ecc.der", "Fuzzing Test ECC")]
        [TestCase("certificate-long-length.der", "Fuzzing Test Long DER Length")]
        public void CertificateSeedsReachBothCallbacks(string fileName, string subject)
        {
            byte[] input = ReadSeed("X509Cert", fileName);
            AssertBalancedCertificates(() =>
            {
                using Certificate certificate = Certificate.FromRawData(input);
                Assert.That(certificate.RawData, Is.EqualTo(input));
                Assert.That(certificate.Subject, Does.Contain(subject));
                Assert.That(certificate.HashAlgorithmName, Is.EqualTo(HashAlgorithmName.SHA256));
                Assert.That(certificate.HasPrivateKey, Is.False);
                Assert.That(FuzzableCode.FuzzCertificateDecoderCore(input), Is.True);
                long created = Certificate.InstancesCreated;
                FuzzableCode.LibfuzzCertificateDecoder(input);
                Assert.That(Certificate.InstancesCreated - created, Is.EqualTo(2));
                using var stream = new MemoryStream(input, writable: false);
                created = Certificate.InstancesCreated;
                FuzzableCode.AflfuzzCertificateDecoder(stream);
                Assert.That(Certificate.InstancesCreated - created, Is.EqualTo(2));
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.Position, Is.EqualTo(input.Length));
            });
        }

        [TestCase("chain-single.der", 1, "Fuzzing Test Application")]
        [TestCase("chain-rsa.der", 2, "Fuzzing Test Application")]
        [TestCase("chain-ecc.der", 2, "Fuzzing Test ECC")]
        [TestCase("chain-three.der", 3, "Fuzzing Test Chain Leaf")]
        [TestCase("chain-long-length.der", 2, "Fuzzing Test Long DER Length")]
        public void ChainSeedsReachBothParserPathsAndCallbacks(string fileName, int count, string firstSubject)
        {
            byte[] input = ReadSeed("X509Chain", fileName);
            foreach (bool useAsnParser in new[] { false, true })
            {
                AssertBalancedCertificates(() =>
                {
                    using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                        input, telemetry: null, useAsnParser: useAsnParser);
                    Assert.That(chain, Has.Count.EqualTo(count));
                    Assert.That(chain[0].Subject, Does.Contain(firstSubject));
                    Assert.That(Utils.CreateCertificateChainBlob(chain), Is.EqualTo(input));
                    for (int index = 0; index < count - 1; index++)
                    {
                        Assert.That(chain[index].IssuerName.RawData, Is.EqualTo(chain[index + 1].SubjectName.RawData));
                    }

                    Assert.That(FuzzableCode.FuzzCertificateChainDecoderCore(input, useAsnParser), Is.True);
                    InvokeChainCallbacks(input, useAsnParser);
                });
            }
        }

        [TestCase("crl.der", 1, 1, "SHA256", true)]
        [TestCase("crl-multi-revoked.der", 3, 2, "SHA256", true)]
        [TestCase("crl-empty.der", 0, 1, "SHA256", true)]
        [TestCase("crl-minimal.der", 0, 0, "SHA256", false)]
        [TestCase("crl-ecc.der", 1, 1, "SHA256", true)]
        [TestCase("crl-no-next-update.der", 2, 2, "SHA256", false)]
        [TestCase("crl-time-boundaries.der", 2, 0, "SHA256", true)]
        [TestCase("crl-sha384.der", 0, 0, "SHA384", true)]
        [TestCase("crl-sha512.der", 0, 0, "SHA512", true)]
        [TestCase("crl-version-one.der", 0, 0, "SHA256", false)]
        public void CrlSeedsReachDecodeEncodeAndCanonicalCallbacks(
            string fileName,
            int revokedCount,
            int extensionCount,
            string hash,
            bool hasNextUpdate)
        {
            byte[] input = ReadSeed("X509CRL", fileName);
            var crl = new X509CRL(input);
            Assert.That(crl.RevokedCertificates, Has.Count.EqualTo(revokedCount));
            Assert.That(crl.CrlExtensions, Has.Count.EqualTo(extensionCount));
            Assert.That(crl.HashAlgorithmName, Is.EqualTo(new HashAlgorithmName(hash)));
            Assert.That(crl.NextUpdate != DateTime.MinValue, Is.EqualTo(hasNextUpdate));
            Assert.That(crl.ThisUpdate.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(FuzzableCode.FuzzX509CRLCore(input), Is.True);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: false), Is.True);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: true), Is.True);
            InvokeCrlCallbacks(input);
        }

        [Test]
        public void RichCrlSeedContainsOrderedSerialsAndCriticalExtensions()
        {
            var crl = new X509CRL(ReadSeed("X509CRL", "crl-no-next-update.der"));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(crl.ThisUpdate, Is.EqualTo(s_fixtureTime));
                Assert.That(crl.NextUpdate, Is.EqualTo(DateTime.MinValue));
                Assert.That(crl.RevokedCertificates[0].UserCertificate, Is.EqualTo(new byte[] { 1, 0, 128, 0 }));
                Assert.That(crl.RevokedCertificates[1].UserCertificate, Is.EqualTo(new byte[] { 2 }));
                Assert.That(crl.RevokedCertificates[0].RevocationDate, Is.EqualTo(s_fixtureTime.AddDays(-1)));
                Assert.That(crl.RevokedCertificates[1].RevocationDate, Is.EqualTo(s_fixtureTime.AddDays(-2)));
                Assert.That(crl.RevokedCertificates[0].CrlEntryExtensions, Has.Count.EqualTo(2));
                Assert.That(crl.RevokedCertificates[1].CrlEntryExtensions, Is.Empty);
                Assert.That(crl.RevokedCertificates[0].CrlEntryExtensions[1].Oid.Value, Is.EqualTo("1.2.3.4"));
                Assert.That(crl.RevokedCertificates[0].CrlEntryExtensions[1].Critical, Is.True);
                Assert.That(
                    crl.RevokedCertificates[0].CrlEntryExtensions[1].RawData,
                    Is.EqualTo(new byte[] { 4, 1, 42 }));
                Assert.That(crl.CrlExtensions[1].Oid.Value, Is.EqualTo("1.2.3.5"));
                Assert.That(crl.CrlExtensions[1].Critical, Is.True);
                Assert.That(crl.CrlExtensions[1].RawData, Is.EqualTo(new byte[] { 2, 1, 7 }));
            }
        }

        [TestCase("issuer")]
        [TestCase("hash")]
        [TestCase("thisUpdate")]
        [TestCase("nextUpdate")]
        [TestCase("revokedCount")]
        [TestCase("revokedOrder")]
        [TestCase("serial")]
        [TestCase("revocationDate")]
        [TestCase("entryExtensionCount")]
        [TestCase("entryExtensionOrder")]
        [TestCase("entryExtensionOid")]
        [TestCase("entryExtensionCritical")]
        [TestCase("entryExtensionBytes")]
        [TestCase("crlExtensionCount")]
        [TestCase("crlExtensionOrder")]
        [TestCase("crlExtensionOid")]
        [TestCase("crlExtensionCritical")]
        [TestCase("crlExtensionBytes")]
        public void CanonicalOracleRejectsChangedFields(string mutation)
        {
            byte[] input = ReadSeed("X509CRL", "crl-no-next-update.der");
            var expected = new X509CRL(input);
            CrlBuilder actual = CrlBuilder.Create(new X509CRL(input));
            switch (mutation)
            {
                case "issuer":
                    actual = CrlBuilder.Create(new X500DistinguishedName("CN=Changed issuer"))
                        .SetThisUpdate(expected.ThisUpdate)
                        .SetNextUpdate(expected.NextUpdate)
                        .SetHashAlgorithm(expected.HashAlgorithmName)
                        .AddRevokedCertificates(actual.RevokedCertificates);
                    foreach (X509Extension extension in expected.CrlExtensions)
                    {
                        actual.AddCRLExtension(extension);
                    }
                    break;
                case "hash":
                    actual.SetHashAlgorithm(HashAlgorithmName.SHA512);
                    break;
                case "thisUpdate":
                    actual.SetThisUpdate(expected.ThisUpdate.AddSeconds(1));
                    break;
                case "nextUpdate":
                    actual.SetNextUpdate(s_fixtureTime.AddDays(1));
                    break;
                case "revokedCount":
                    actual.RevokedCertificates.RemoveAt(0);
                    break;
                case "revokedOrder":
                    (actual.RevokedCertificates[0], actual.RevokedCertificates[1]) =
                        (actual.RevokedCertificates[1], actual.RevokedCertificates[0]);
                    break;
                case "serial":
                    actual.RevokedCertificates[0].UserCertificate[0] ^= 1;
                    break;
                case "revocationDate":
                    actual.RevokedCertificates[0].RevocationDate = s_fixtureTime;
                    break;
                case "entryExtensionCount":
                    actual.RevokedCertificates[0].CrlEntryExtensions.Add(
                        new X509Extension("1.2.3.9", [0x05, 0x00], false));
                    break;
                case "entryExtensionOid":
                    actual.RevokedCertificates[0].CrlEntryExtensions[1].Oid = new Oid("1.2.3.9");
                    break;
                case "entryExtensionOrder":
                    SwapExtensions(actual.RevokedCertificates[0].CrlEntryExtensions);
                    break;
                case "entryExtensionCritical":
                    actual.RevokedCertificates[0].CrlEntryExtensions[1].Critical = false;
                    break;
                case "entryExtensionBytes":
                    actual.RevokedCertificates[0].CrlEntryExtensions[1].RawData = [0x05, 0x00];
                    break;
                case "crlExtensionCount":
                    actual.CrlExtensions.Add(new X509Extension("1.2.3.9", [0x05, 0x00], false));
                    break;
                case "crlExtensionOid":
                    actual.CrlExtensions[1].Oid = new Oid("1.2.3.9");
                    break;
                case "crlExtensionOrder":
                    SwapExtensions(actual.CrlExtensions);
                    break;
                case "crlExtensionCritical":
                    actual.CrlExtensions[1].Critical = false;
                    break;
                case "crlExtensionBytes":
                    actual.CrlExtensions[1].RawData = [0x05, 0x00];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.That(
                () => FuzzableCode.AssertCrlFieldsEqual(expected, actual),
                Throws.TypeOf<InvalidOperationException>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanonicalOracleRejectsChangedAlgorithmIdentifierEvenWithTheSameHash(bool includeNull)
        {
            var crl = new X509CRL(ReadSeed("X509CRL", "crl-ecc.der"));
            var algorithmWriter = new AsnWriter(AsnEncodingRules.DER);
            algorithmWriter.PushSequence();
            algorithmWriter.WriteObjectIdentifier("1.2.840.10045.4.3.2");
            algorithmWriter.PopSequence();
            byte[] expectedAlgorithm = algorithmWriter.Encode();
            byte[] canonical = CrlBuilder.Create(crl).Encode(expectedAlgorithm);
            FuzzableCode.AssertCrlAlgorithmIdentifier(expectedAlgorithm, canonical);

            algorithmWriter.Reset();
            algorithmWriter.PushSequence();
            algorithmWriter.WriteObjectIdentifier(includeNull ? "1.2.840.10045.4.3.2" : "1.2.840.113549.1.1.11");
            algorithmWriter.WriteNull();
            algorithmWriter.PopSequence();
            byte[] changed = CrlBuilder.Create(crl).Encode(algorithmWriter.Encode());
            var decoded = new X509CRL();
            decoded.DecodeCrl(changed);
            Assert.That(decoded.HashAlgorithmName, Is.EqualTo(crl.HashAlgorithmName));
            Assert.That(
                () => FuzzableCode.AssertCrlAlgorithmIdentifier(expectedAlgorithm, changed),
                Throws.TypeOf<InvalidOperationException>());
        }

        [TestCaseSource(nameof(MalformedInputs))]
        public void MalformedCertificateAndCrlInputsAreRejected(byte[] input)
        {
            AssertBalancedCertificates(() =>
            {
                Assert.That(FuzzableCode.FuzzCertificateDecoderCore(input), Is.False);
                FuzzableCode.LibfuzzCertificateDecoder(input);
                using var stream = new MemoryStream(input, writable: false);
                FuzzableCode.AflfuzzCertificateDecoder(stream);
            });
            Assert.That(FuzzableCode.FuzzX509CRLCore(input), Is.False);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: false), Is.False);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: true), Is.False);
            InvokeCrlCallbacks(input);
        }

        [Test]
        public void TruncatedValidCertificateAndCrlAreRejected()
        {
            byte[] certificate = ReadSeed("X509Cert", "certificate.der");
            byte[] crl = ReadSeed("X509CRL", "crl.der");
            AssertBalancedCertificates(() =>
                Assert.That(
                    FuzzableCode.FuzzCertificateDecoderCore(certificate.AsSpan(0, certificate.Length - 1).ToArray()),
                    Is.False));
            Assert.That(
                FuzzableCode.FuzzCRLEncoderCore(crl.AsSpan(0, crl.Length - 1).ToArray(), roundTrip: true),
                Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FractionalSignedCrlTimeIsRejectedBeforeCanonicalEncoding(bool belowTickPrecision)
        {
            byte[] time = Encoding.ASCII.GetBytes(
                belowTickPrecision ? "20550102030405.000000001Z" : "20550102030405.123Z");
            byte[] input = ReplaceSignedCrlThisUpdate([0x18, (byte)time.Length, .. time]);

            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: true), Is.False);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: false), Is.False);
            Assert.That(FuzzableCode.FuzzX509CRLCore(input), Is.False);
            InvokeCrlCallbacks(input);
        }

        [TestCase(UniversalTagNumber.UtcTime, "260050036546Z")]
        [TestCase(UniversalTagNumber.UtcTime, "000361838182Z")]
        [TestCase(UniversalTagNumber.GeneralizedTime, "20550050036546Z")]
        public void MalformedSignedCrlTimeIsRejectedByAllCrlTargets(
            UniversalTagNumber timeTag,
            string value)
        {
            byte[] content = Encoding.ASCII.GetBytes(value);
            byte[] input = ReplaceSignedCrlThisUpdate([(byte)timeTag, (byte)content.Length, .. content]);

            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: true), Is.False);
            Assert.That(FuzzableCode.FuzzCRLEncoderCore(input, roundTrip: false), Is.False);
            Assert.That(FuzzableCode.FuzzX509CRLCore(input), Is.False);
            InvokeCrlCallbacks(input);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ChainCallbacksDisposePartialResultsOnInvalidSuffix(bool useAsnParser, bool truncateCertificate)
        {
            byte[] prefix = ReadSeed("X509Chain", "chain-rsa.der");
            byte[] certificate = ReadSeed("X509Cert", "certificate.der");
            byte[] suffix = truncateCertificate
                ? certificate.AsSpan(0, certificate.Length - 1).ToArray()
                : [0x30, 0x00];
            byte[] input = [.. prefix, .. suffix];
            AssertBalancedCertificates(() =>
            {
                Assert.That(FuzzableCode.FuzzCertificateChainDecoderCore(input, useAsnParser), Is.False);
                InvokeChainCallbacks(input, useAsnParser);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyChainRemainsAnEmptySuccessfulParse(bool useAsnParser)
        {
            AssertBalancedCertificates(() =>
            {
                Assert.That(FuzzableCode.FuzzCertificateChainDecoderCore([], useAsnParser), Is.True);
                InvokeChainCallbacks([], useAsnParser);
            });
        }

        [TestCaseSource(nameof(InputExceptions))]
        public void InputRejectionPolicyInspectsEveryInnerException(Exception exception, bool allowed)
        {
            Assert.That(FuzzableCode.IsExpectedCertificateInputException(exception), Is.EqualTo(allowed));
        }

        [Test]
        public void AflCallbacksRejectNullStreams()
        {
            Assert.That(() => FuzzableCode.AflfuzzCertificateDecoder(null), Throws.ArgumentNullException);
            Assert.That(() => FuzzableCode.AflfuzzCertificateChainDecoder(null), Throws.ArgumentNullException);
            Assert.That(() => FuzzableCode.AflfuzzCertificateChainDecoderCustom(null), Throws.ArgumentNullException);
            Assert.That(() => FuzzableCode.AflfuzzX509CRL(null), Throws.ArgumentNullException);
            Assert.That(() => FuzzableCode.AflfuzzCRLEncoder(null), Throws.ArgumentNullException);
            Assert.That(() => FuzzableCode.AflfuzzCRLEncoderIndempotent(null), Throws.ArgumentNullException);
        }

        [Test]
        public void PublicCallbacksHaveOnlySupportedNamesAndShapes()
        {
            MethodInfo[] callbacks = typeof(FuzzableCode)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.GetParameters().Length == 1)
                .ToArray();
            Assert.That(callbacks, Has.Length.EqualTo(30));
            foreach (MethodInfo callback in callbacks)
            {
                Assert.That(callback.ReturnType, Is.EqualTo(typeof(void)));
                Type expectedInput;
                if (callback.Name.StartsWith("Libfuzz", StringComparison.Ordinal))
                {
                    expectedInput = typeof(ReadOnlySpan<byte>);
                }
                else if (callback.Name is nameof(FuzzableCode.AflfuzzPemImportCertificate) or
                    nameof(FuzzableCode.AflfuzzPemImportPrivateKey))
                {
                    expectedInput = typeof(string);
                }
                else
                {
                    Assert.That(callback.Name, Does.StartWith("Aflfuzz"));
                    expectedInput = typeof(Stream);
                }
                Assert.That(callback.GetParameters()[0].ParameterType, Is.EqualTo(expectedInput));
            }
            Assert.That(
                callbacks.Where(method => method.Name.StartsWith("Libfuzz", StringComparison.Ordinal))
                    .Select(method => method.Name),
                Does.Contain(nameof(FuzzableCode.LibfuzzCertificateDecoder))
                    .And.Contain(nameof(FuzzableCode.LibfuzzCertificateChainDecoder))
                    .And.Contain(nameof(FuzzableCode.LibfuzzCertificateChainDecoderCustom))
                    .And.Contain(nameof(FuzzableCode.LibfuzzCRLEncoder))
                    .And.Contain(nameof(FuzzableCode.LibfuzzCRLEncoderIndempotent)));
        }

        public static IEnumerable<TestCaseData> MalformedInputs()
        {
            yield return new TestCaseData(Array.Empty<byte>()).SetName("RejectsEmptyCertificateAndCrl");
            yield return new TestCaseData(new byte[] { 0x30, 0x00 }).SetName("RejectsEmptyDerSequence");
            yield return new TestCaseData(new byte[] { 0x30, 0x82, 0xFF }).SetName("RejectsTruncatedDerLength");
            yield return new TestCaseData(new byte[] { 0xFF, 0x00, 0x80 }).SetName("RejectsInvalidDerTag");
        }

        public static IEnumerable<TestCaseData> InputExceptions()
        {
            yield return new TestCaseData(new CryptographicException("Invalid certificate."), true);
            yield return new TestCaseData(new AsnContentException("Invalid DER."), true);
            yield return new TestCaseData(
                new AsnContentException("Invalid DER.", new ArgumentOutOfRangeException()), true);
            yield return new TestCaseData(
                InvalidCertificate(new CryptographicException("Invalid DER.", new AsnContentException())), true);
            yield return new TestCaseData(
                InvalidCertificate(
                    new CryptographicException(
                        "Invalid DER.",
                        new AsnContentException("Invalid DER.", new ArgumentOutOfRangeException()))),
                true);
            yield return new TestCaseData(new ServiceResultException(StatusCodes.BadCertificateInvalid), false);
            yield return new TestCaseData(
                new ServiceResultException(StatusCodes.BadUnexpectedError, "Unexpected.", new CryptographicException()),
                false);
            foreach (Exception programmerError in new Exception[]
            {
                new ArgumentException(),
                new ArgumentOutOfRangeException(),
                CaptureNullReferenceFailure(),
                CaptureIndexFailure(),
                new InvalidOperationException(),
                new FormatException()
            })
            {
                yield return new TestCaseData(programmerError, false);
                yield return new TestCaseData(
                    InvalidCertificate(new CryptographicException("Wrapped failure.", programmerError)), false);
            }
        }

        private static ServiceResultException InvalidCertificate(Exception inner)
        {
            return new ServiceResultException(StatusCodes.BadCertificateInvalid, "Invalid certificate.", inner);
        }

        private static NullReferenceException CaptureNullReferenceFailure()
        {
            byte[] input = null;
            return Assert.Throws<NullReferenceException>(() => _ = input.Length);
        }

        private static IndexOutOfRangeException CaptureIndexFailure()
        {
            byte[] input = [];
            return Assert.Throws<IndexOutOfRangeException>(() => _ = input[0]);
        }

        private static void AssertBalancedCertificates(Action action)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            action();
            Assert.That(Certificate.InstancesCreated - created, Is.EqualTo(Certificate.InstancesDisposed - disposed),
                "Every certificate core created by the callback must be disposed before it returns.");
        }

        private static void SwapExtensions(X509ExtensionCollection extensions)
        {
            var first = new X509Extension(extensions[0], extensions[0].Critical);
            extensions[0].CopyFrom(extensions[1]);
            extensions[1].CopyFrom(first);
        }

        private static void InvokeChainCallbacks(byte[] input, bool useAsnParser)
        {
            using var stream = new MemoryStream(input, writable: false);
            if (useAsnParser)
            {
                FuzzableCode.LibfuzzCertificateChainDecoderCustom(input);
                FuzzableCode.AflfuzzCertificateChainDecoderCustom(stream);
            }
            else
            {
                FuzzableCode.LibfuzzCertificateChainDecoder(input);
                FuzzableCode.AflfuzzCertificateChainDecoder(stream);
            }
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.Position, Is.EqualTo(input.Length));
        }

        private static void InvokeCrlCallbacks(byte[] input)
        {
            FuzzableCode.LibfuzzX509CRL(input);
            FuzzableCode.LibfuzzCRLEncoder(input);
            FuzzableCode.LibfuzzCRLEncoderIndempotent(input);
            using var decoder = new MemoryStream(input, writable: false);
            using var encoder = new MemoryStream(input, writable: false);
            using var canonical = new MemoryStream(input, writable: false);
            FuzzableCode.AflfuzzX509CRL(decoder);
            FuzzableCode.AflfuzzCRLEncoder(encoder);
            FuzzableCode.AflfuzzCRLEncoderIndempotent(canonical);
            Assert.That(decoder.CanRead && encoder.CanRead && canonical.CanRead, Is.True);
        }

        private byte[] ReplaceSignedCrlThisUpdate(ReadOnlySpan<byte> encodedTime)
        {
            var reader = new AsnReader(ReadSeed("X509CRL", "crl.der"), AsnEncodingRules.DER);
            AsnReader signed = reader.ReadSequence();
            var tbsReader = new AsnReader(signed.ReadEncodedValue(), AsnEncodingRules.DER);
            AsnReader tbs = tbsReader.ReadSequence();
            byte[] algorithm = signed.ReadEncodedValue().ToArray();
            byte[] signature = signed.ReadBitString(out _);
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteEncodedValue(tbs.ReadEncodedValue().Span);
            writer.WriteEncodedValue(tbs.ReadEncodedValue().Span);
            writer.WriteEncodedValue(tbs.ReadEncodedValue().Span);
            _ = tbs.ReadEncodedValue();
            writer.WriteEncodedValue(encodedTime);
            while (tbs.HasData)
            {
                writer.WriteEncodedValue(tbs.ReadEncodedValue().Span);
            }
            writer.PopSequence();
            return new X509Signature(writer.Encode(), signature, algorithm).Encode();
        }

        private byte[] ReadSeed(string bucket, string fileName)
        {
            return File.ReadAllBytes(Path.Combine(m_prefix + "." + bucket, fileName));
        }

        private static readonly DateTime s_fixtureTime = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        private string m_directory = string.Empty;
        private string m_prefix = string.Empty;
    }
}
