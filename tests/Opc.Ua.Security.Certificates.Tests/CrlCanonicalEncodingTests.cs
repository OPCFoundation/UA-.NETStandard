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
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Regressions for CRL TBS decoding and canonical encoding without signing keys.
    /// Expected DER is written independently of the production CRL encoder.
    /// </summary>
    [TestFixture]
    [Category("CRL")]
    [Parallelizable]
    public sealed class CrlCanonicalEncodingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DecodeCrlAcceptsThisUpdateAtEndOfSequence(bool includeVersion)
        {
            byte[] issuer = CreateIssuer();
            AsnWriter writer = CreateTbsPrefix(issuer, includeVersion: includeVersion);
            writer.WriteUtcTime(s_thisUpdate);
            writer.PopSequence();
            var crl = new X509CRL();

            crl.DecodeCrl(writer.Encode());

            AssertMinimalFields(crl, issuer, s_thisUpdate, DateTime.MinValue);
            Assert.That(crl.CrlExtensions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DecodeCrlRejectsMissingRequiredThisUpdate(bool appendNull)
        {
            AsnWriter writer = CreateTbsPrefix(CreateIssuer());
            if (appendNull)
            {
                writer.WriteNull();
            }
            writer.PopSequence();
            var crl = new X509CRL();

            Assert.That(
                () => crl.DecodeCrl(writer.Encode()),
                Throws.TypeOf<CryptographicException>());
        }

        [TestCase("thisUpdate", false)]
        [TestCase("nextUpdate", false)]
        [TestCase("revocationDate", false)]
        [TestCase("thisUpdate", true)]
        [TestCase("nextUpdate", true)]
        [TestCase("revocationDate", true)]
        public void DecodeCrlRejectsFractionalTimeInsteadOfLosingPrecision(string field, bool belowTickPrecision)
        {
            AsnWriter writer = CreateTbsPrefix(CreateIssuer());
            byte[] content = Encoding.ASCII.GetBytes(
                belowTickPrecision ? "20550102030405.000000001Z" : "20550102030405.123Z");
            byte[] fractionalTime = [0x18, (byte)content.Length, .. content];
            if (field == "thisUpdate")
            {
                writer.WriteEncodedValue(fractionalTime);
            }
            else
            {
                writer.WriteUtcTime(s_thisUpdate);
            }
            if (field == "nextUpdate")
            {
                writer.WriteEncodedValue(fractionalTime);
            }
            if (field == "revocationDate")
            {
                writer.PushSequence();
                writer.PushSequence();
                writer.WriteInteger(1);
                writer.WriteEncodedValue(fractionalTime);
                writer.PopSequence();
                writer.PopSequence();
            }
            writer.PopSequence();
            var crl = new X509CRL();

            Assert.That(
                () => crl.DecodeCrl(writer.Encode()),
                Throws.TypeOf<CryptographicException>());
        }

        [TestCase(UniversalTagNumber.UtcTime, "260050036546Z")]
        [TestCase(UniversalTagNumber.UtcTime, "000361838182Z")]
        [TestCase(UniversalTagNumber.UtcTime, "260101000000+0000")]
        [TestCase(UniversalTagNumber.GeneralizedTime, "20550050036546Z")]
        [TestCase(UniversalTagNumber.GeneralizedTime, "00000102030405Z")]
        [TestCase(UniversalTagNumber.GeneralizedTime, "20550102030405.123Z")]
        public void DecodeCrlRejectsMalformedTimeValuesWithoutBclDateException(
            UniversalTagNumber timeTag,
            string value)
        {
            AsnWriter writer = CreateTbsPrefix(CreateIssuer());
            WriteEncodedTime(writer, timeTag, value);
            writer.PopSequence();
            var crl = new X509CRL();

            Exception exception = Assert.Catch(() => crl.DecodeCrl(writer.Encode()));

            Assert.That(exception, Is.TypeOf<CryptographicException>());
            Assert.That(exception.InnerException, Is.TypeOf<AsnContentException>());
            Assert.That(exception.InnerException!.InnerException, Is.Null);
        }

        [TestCase(UniversalTagNumber.UtcTime, "260102030405Z")]
        [TestCase(UniversalTagNumber.GeneralizedTime, "20550102030405Z")]
        public void DecodeCrlAcceptsValidTimeValuesAndCanonicalRoundTrips(
            UniversalTagNumber timeTag,
            string value)
        {
            DateTime expected = timeTag == UniversalTagNumber.UtcTime
                ? new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
                : new DateTime(2055, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            byte[] issuer = CreateIssuer();
            AsnWriter writer = CreateTbsPrefix(issuer);
            WriteEncodedTime(writer, timeTag, value);
            writer.PopSequence();
            var decoded = new X509CRL();

            decoded.DecodeCrl(writer.Encode());
            byte[] canonical = CrlBuilder.Create(decoded).Encode(CreateAlgorithmIdentifier(kSha256WithRsaOid));
            var roundTripped = new X509CRL();
            roundTripped.DecodeCrl(canonical);

            AssertMinimalFields(decoded, issuer, expected, DateTime.MinValue);
            AssertMinimalFields(roundTripped, issuer, expected, DateTime.MinValue);
            Assert.That(
                CrlBuilder.Create(roundTripped).Encode(CreateAlgorithmIdentifier(kSha256WithRsaOid)),
                Is.EqualTo(canonical));
        }

        [Test]
        public void DecodeCrlAcceptsExtensionsWithoutNextUpdate()
        {
            byte[] issuer = CreateIssuer();
            AsnWriter writer = CreateTbsPrefix(issuer);
            writer.WriteUtcTime(s_thisUpdate);
            WriteCrlExtensions(writer);
            writer.PopSequence();
            var crl = new X509CRL();

            crl.DecodeCrl(writer.Encode());

            AssertMinimalFields(crl, issuer, s_thisUpdate, DateTime.MinValue);
            Assert.That(crl.CrlExtensions, Has.Count.EqualTo(1));
            AssertExtension(crl.CrlExtensions[0], kCrlNumberOid, false, [0x02, 0x02, 0x01, 0x23]);
        }

        [Test]
        public void DecodeCrlRejectsTrailingNullInsideExplicitExtensionsWrapper()
        {
            AsnWriter writer = CreateTbsPrefix(CreateIssuer());
            writer.WriteUtcTime(s_thisUpdate);
            WriteCrlExtensions(writer, appendNull: true);
            writer.PopSequence();
            var crl = new X509CRL();

            Assert.That(
                () => crl.DecodeCrl(writer.Encode()),
                Throws.TypeOf<CryptographicException>());
        }

        [TestCase(kSha256WithRsaOid, "SHA256")]
        [TestCase("1.2.840.113549.1.1.12", "SHA384")]
        [TestCase("1.2.840.113549.1.1.13", "SHA512")]
        public void CanonicalEncodingPreservesRepresentedFieldsAndOrderedExtensions(
            string signatureOid,
            string hashName)
        {
            byte[] issuer = CreateIssuer();
            byte[] input = CreateRichTbs(issuer, signatureOid);
            byte[] algorithmIdentifier = CreateAlgorithmIdentifier(signatureOid);
            var decoded = new X509CRL();
            decoded.DecodeCrl(input);
            AssertRichFields(decoded, issuer, new HashAlgorithmName(hashName));

            CrlBuilder builder = CrlBuilder.Create(decoded);
            Assert.That(builder.HashAlgorithmName, Is.EqualTo(new HashAlgorithmName(hashName)));
            byte[] canonical = builder.Encode(algorithmIdentifier);
            var roundTripped = new X509CRL();
            roundTripped.DecodeCrl(canonical);

            // This compares TBS bytes, not a newly generated signature with an old one.
            Assert.That(canonical, Is.EqualTo(input));
            AssertRichFields(roundTripped, issuer, new HashAlgorithmName(hashName));
            Assert.That(
                CrlBuilder.Create(roundTripped).Encode(algorithmIdentifier),
                Is.EqualTo(canonical));
        }

        [Test]
        public void CanonicalEncodingNormalizesVersionOneInputToVersionTwo()
        {
            byte[] issuer = CreateIssuer();
            AsnWriter inputWriter = CreateTbsPrefix(issuer, includeVersion: false);
            inputWriter.WriteUtcTime(s_thisUpdate);
            inputWriter.WriteUtcTime(s_nextUpdate);
            inputWriter.PopSequence();
            var decoded = new X509CRL();
            decoded.DecodeCrl(inputWriter.Encode());

            byte[] algorithmIdentifier = CreateAlgorithmIdentifier(kSha256WithRsaOid);
            byte[] canonical = CrlBuilder.Create(decoded).Encode(algorithmIdentifier);

            AsnWriter expectedWriter = CreateTbsPrefix(issuer);
            expectedWriter.WriteUtcTime(s_thisUpdate);
            expectedWriter.WriteUtcTime(s_nextUpdate);
            expectedWriter.PopSequence();
            Assert.That(canonical, Is.EqualTo(expectedWriter.Encode()));
            var roundTripped = new X509CRL();
            roundTripped.DecodeCrl(canonical);
            AssertMinimalFields(roundTripped, issuer, s_thisUpdate, s_nextUpdate);
            Assert.That(roundTripped.CrlExtensions, Is.Empty);
            Assert.That(
                CrlBuilder.Create(roundTripped).Encode(algorithmIdentifier),
                Is.EqualTo(canonical));
        }

        [TestCase(1949, UniversalTagNumber.GeneralizedTime)]
        [TestCase(1950, UniversalTagNumber.UtcTime)]
        [TestCase(2049, UniversalTagNumber.UtcTime)]
        [TestCase(2050, UniversalTagNumber.GeneralizedTime)]
        public void EncodePreservesAllTimeFieldsAtUtcTimeBoundaries(
            int year,
            UniversalTagNumber expectedTimeTag)
        {
            byte[] issuer = CreateIssuer();
            var thisUpdate = new DateTime(year, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            DateTime nextUpdate = thisUpdate.AddDays(2);
            DateTime revocationDate = thisUpdate.AddDays(-1);
            CrlBuilder builder = CrlBuilder
                .Create(new X500DistinguishedName(issuer), HashAlgorithmName.SHA256)
                .SetThisUpdate(thisUpdate)
                .SetNextUpdate(nextUpdate)
                .AddRevokedCertificate(new RevokedCertificate([0x34, 0x12])
                {
                    RevocationDate = revocationDate
                });
            byte[] algorithmIdentifier = CreateAlgorithmIdentifier(kSha256WithRsaOid);

            byte[] encoded = builder.Encode(algorithmIdentifier);
            var decoded = new X509CRL();
            decoded.DecodeCrl(encoded);

            var reader = new AsnReader(encoded, AsnEncodingRules.DER);
            AsnReader fields = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            Assert.That(fields.ReadInteger(), Is.EqualTo(BigInteger.One));
            Assert.That(fields.ReadEncodedValue().ToArray(), Is.EqualTo(algorithmIdentifier));
            Assert.That(fields.ReadEncodedValue().ToArray(), Is.EqualTo(issuer));
            Asn1Tag thisUpdateTag = fields.PeekTag();
            fields.ReadEncodedValue();
            Asn1Tag nextUpdateTag = fields.PeekTag();
            fields.ReadEncodedValue();
            AsnReader revoked = fields.ReadSequence();
            AsnReader entry = revoked.ReadSequence();
            Assert.That(entry.ReadInteger(), Is.EqualTo(new BigInteger(0x1234)));
            Asn1Tag revocationTag = entry.PeekTag();
            entry.ReadEncodedValue();
            entry.ThrowIfNotEmpty();
            revoked.ThrowIfNotEmpty();
            fields.ThrowIfNotEmpty();

            Assert.That(decoded.RevokedCertificates, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(thisUpdateTag, Is.EqualTo(new Asn1Tag(expectedTimeTag)));
                Assert.That(nextUpdateTag, Is.EqualTo(new Asn1Tag(expectedTimeTag)));
                Assert.That(revocationTag, Is.EqualTo(new Asn1Tag(expectedTimeTag)));
                Assert.That(decoded.ThisUpdate, Is.EqualTo(thisUpdate));
                Assert.That(decoded.NextUpdate, Is.EqualTo(nextUpdate));
                Assert.That(decoded.RevokedCertificates[0].RevocationDate, Is.EqualTo(revocationDate));
                Assert.That(decoded.RevokedCertificates[0].UserCertificate, Is.EqualTo(new byte[] { 0x34, 0x12 }));
                Assert.That(decoded.IssuerName.RawData, Is.EqualTo(issuer));
                Assert.That(decoded.HashAlgorithmName, Is.EqualTo(HashAlgorithmName.SHA256));
                Assert.That(decoded.CrlExtensions, Is.Empty);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void EncodePreservesExplicitNextUpdateNotLaterThanThisUpdate(int dayOffset)
        {
            byte[] issuer = CreateIssuer();
            DateTime nextUpdate = s_thisUpdate.AddDays(dayOffset);
            CrlBuilder builder = CrlBuilder
                .Create(new X500DistinguishedName(issuer), HashAlgorithmName.SHA256)
                .SetThisUpdate(s_thisUpdate)
                .SetNextUpdate(nextUpdate)
                .AddCRLExtension(new X509Extension(
                    kCrlNumberOid,
                    [0x02, 0x02, 0x01, 0x23],
                    critical: false));

            byte[] encoded = builder.Encode(CreateAlgorithmIdentifier(kSha256WithRsaOid));
            var decoded = new X509CRL();
            decoded.DecodeCrl(encoded);

            // An extension follows the time fields, isolating field loss from an empty-reader bug.
            AssertMinimalFields(decoded, issuer, s_thisUpdate, nextUpdate);
            Assert.That(decoded.CrlExtensions, Has.Count.EqualTo(1));
            AssertExtension(decoded.CrlExtensions[0], kCrlNumberOid, false, [0x02, 0x02, 0x01, 0x23]);
        }

        [Test]
        public void EncodeOmitsUnspecifiedNextUpdate()
        {
            byte[] issuer = CreateIssuer();
            CrlBuilder builder = CrlBuilder
                .Create(new X500DistinguishedName(issuer), HashAlgorithmName.SHA256)
                .SetThisUpdate(s_thisUpdate);
            AsnWriter expectedWriter = CreateTbsPrefix(issuer);
            expectedWriter.WriteUtcTime(s_thisUpdate);
            expectedWriter.PopSequence();

            byte[] encoded = builder.Encode(CreateAlgorithmIdentifier(kSha256WithRsaOid));

            Assert.That(encoded, Is.EqualTo(expectedWriter.Encode()),
                "An unspecified nextUpdate must remain absent, not become an encoded sentinel date.");
        }

        private static void AssertMinimalFields(
            X509CRL crl,
            byte[] issuer,
            DateTime thisUpdate,
            DateTime nextUpdate)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(crl.IssuerName.RawData, Is.EqualTo(issuer));
                Assert.That(crl.HashAlgorithmName, Is.EqualTo(HashAlgorithmName.SHA256));
                Assert.That(crl.ThisUpdate, Is.EqualTo(thisUpdate));
                Assert.That(crl.ThisUpdate.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(crl.NextUpdate, Is.EqualTo(nextUpdate));
                Assert.That(crl.RevokedCertificates, Is.Empty);
            }
        }

        private static void AssertRichFields(X509CRL crl, byte[] issuer, HashAlgorithmName hash)
        {
            Assert.That(crl.RevokedCertificates, Has.Count.EqualTo(2));
            Assert.That(crl.RevokedCertificates[0].CrlEntryExtensions, Has.Count.EqualTo(2));
            Assert.That(crl.RevokedCertificates[1].CrlEntryExtensions, Has.Count.EqualTo(1));
            Assert.That(crl.CrlExtensions, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(crl.IssuerName.RawData, Is.EqualTo(issuer));
                Assert.That(crl.HashAlgorithmName, Is.EqualTo(hash));
                Assert.That(crl.ThisUpdate, Is.EqualTo(s_thisUpdate));
                Assert.That(crl.NextUpdate, Is.EqualTo(s_nextUpdate));
                Assert.That(crl.ThisUpdate.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(crl.NextUpdate.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(
                    crl.RevokedCertificates[0].UserCertificate,
                    Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x80, 0x00 }));
                Assert.That(crl.RevokedCertificates[0].RevocationDate, Is.EqualTo(s_firstRevocation));
                Assert.That(
                    crl.RevokedCertificates[1].UserCertificate,
                    Is.EqualTo(new byte[] { 0x23, 0x01 }));
                Assert.That(crl.RevokedCertificates[1].RevocationDate, Is.EqualTo(s_secondRevocation));
                AssertExtension(
                    crl.RevokedCertificates[0].CrlEntryExtensions[0],
                    "2.5.29.21", false, [0x0A, 0x01, 0x01]);
                AssertExtension(
                    crl.RevokedCertificates[0].CrlEntryExtensions[1],
                    "1.2.3.4.1", true, [0x04, 0x03, 0xA1, 0xB2, 0xC3]);
                AssertExtension(
                    crl.RevokedCertificates[1].CrlEntryExtensions[0],
                    "1.2.3.4.3", false, [0x02, 0x01, 0x2A]);
                AssertExtension(crl.CrlExtensions[0], kCrlNumberOid, false, [0x02, 0x02, 0x01, 0x23]);
                AssertExtension(crl.CrlExtensions[1], "1.2.3.4.2", true, [0x04, 0x02, 0xCA, 0xFE]);
            }
        }

        private static void AssertExtension(
            X509Extension extension,
            string oid,
            bool critical,
            byte[] rawData)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(extension.Oid?.Value, Is.EqualTo(oid));
                Assert.That(extension.Critical, Is.EqualTo(critical));
                Assert.That(extension.RawData, Is.EqualTo(rawData));
            }
        }

        private static byte[] CreateRichTbs(byte[] issuer, string signatureOid)
        {
            AsnWriter writer = CreateTbsPrefix(issuer, signatureOid);
            writer.WriteUtcTime(s_thisUpdate);
            writer.WriteUtcTime(s_nextUpdate);
            writer.PushSequence();
            writer.PushSequence();
            // Descending serial order and a sign-padding byte make sorting/endian loss observable.
            writer.WriteIntegerUnsigned([0x80, 0x00, 0x00, 0x01]);
            writer.WriteUtcTime(s_firstRevocation);
            writer.PushSequence();
            WriteExtension(writer, "2.5.29.21", false, [0x0A, 0x01, 0x01]);
            WriteExtension(writer, "1.2.3.4.1", true, [0x04, 0x03, 0xA1, 0xB2, 0xC3]);
            writer.PopSequence();
            writer.PopSequence();
            writer.PushSequence();
            writer.WriteIntegerUnsigned([0x01, 0x23]);
            writer.WriteUtcTime(s_secondRevocation);
            writer.PushSequence();
            WriteExtension(writer, "1.2.3.4.3", false, [0x02, 0x01, 0x2A]);
            writer.PopSequence();
            writer.PopSequence();
            writer.PopSequence();
            WriteCrlExtensions(writer, includeUnknownExtension: true);
            writer.PopSequence();
            return writer.Encode();
        }

        private static AsnWriter CreateTbsPrefix(
            byte[] issuer,
            string signatureOid = kSha256WithRsaOid,
            bool includeVersion = true)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            if (includeVersion)
            {
                writer.WriteInteger(1);
            }
            writer.WriteEncodedValue(CreateAlgorithmIdentifier(signatureOid));
            writer.WriteEncodedValue(issuer);
            return writer;
        }

        private static void WriteCrlExtensions(
            AsnWriter writer,
            bool includeUnknownExtension = false,
            bool appendNull = false)
        {
            var tag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            writer.PushSequence(tag);
            writer.PushSequence();
            WriteExtension(writer, kCrlNumberOid, false, [0x02, 0x02, 0x01, 0x23]);
            if (includeUnknownExtension)
            {
                WriteExtension(writer, "1.2.3.4.2", true, [0x04, 0x02, 0xCA, 0xFE]);
            }
            writer.PopSequence();
            if (appendNull)
            {
                writer.WriteNull();
            }
            writer.PopSequence(tag);
        }

        private static void WriteExtension(
            AsnWriter writer,
            string oid,
            bool critical,
            byte[] rawData)
        {
            writer.PushSequence();
            writer.WriteObjectIdentifier(oid);
            if (critical)
            {
                writer.WriteBoolean(true);
            }
            writer.WriteOctetString(rawData);
            writer.PopSequence();
        }

        private static void WriteEncodedTime(AsnWriter writer, UniversalTagNumber timeTag, string value)
        {
            byte[] content = Encoding.ASCII.GetBytes(value);
            writer.WriteEncodedValue([(byte)timeTag, (byte)content.Length, .. content]);
        }

        private static byte[] CreateAlgorithmIdentifier(string signatureOid)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier(signatureOid);
            writer.WriteNull();
            writer.PopSequence();
            return writer.Encode();
        }

        private static byte[] CreateIssuer()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.PushSetOf();
            writer.PushSequence();
            writer.WriteObjectIdentifier("2.5.4.10");
            writer.WriteCharacterString(UniversalTagNumber.UTF8String, "OPC Foundation");
            writer.PopSequence();
            writer.PopSetOf();
            writer.PushSetOf();
            writer.PushSequence();
            writer.WriteObjectIdentifier("2.5.4.3");
            writer.WriteCharacterString(UniversalTagNumber.PrintableString, "CRL Parity Issuer");
            writer.PopSequence();
            writer.PopSetOf();
            writer.PopSequence();
            return writer.Encode();
        }

        private const string kSha256WithRsaOid = "1.2.840.113549.1.1.11";
        private const string kCrlNumberOid = "2.5.29.20";
        private static readonly DateTime s_thisUpdate = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        private static readonly DateTime s_nextUpdate = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        private static readonly DateTime s_firstRevocation = new(2025, 12, 30, 1, 2, 3, DateTimeKind.Utc);
        private static readonly DateTime s_secondRevocation = new(2025, 12, 31, 2, 3, 4, DateTimeKind.Utc);
    }
}
