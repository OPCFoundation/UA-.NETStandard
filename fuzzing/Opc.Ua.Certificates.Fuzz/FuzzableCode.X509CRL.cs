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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for signed CRL decoding and canonical TBS encoding.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// The X509 CRL fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzX509CRL(Stream stream)
        {
            _ = FuzzX509CRLCore(ReadAllBytes(stream));
        }

        /// <summary>
        /// The X509 CRL fuzz target for libFuzzer.
        /// </summary>
        public static void LibfuzzX509CRL(ReadOnlySpan<byte> input)
        {
            _ = FuzzX509CRLCore(input.ToArray());
        }

        /// <summary>
        /// Re-encodes a decoded signed CRL as TBS data.
        /// </summary>
        public static void AflfuzzCRLEncoder(Stream stream)
        {
            _ = FuzzCRLEncoderCore(ReadAllBytes(stream), roundTrip: false);
        }

        /// <summary>
        /// Re-encodes a decoded signed CRL as TBS data.
        /// </summary>
        public static void LibfuzzCRLEncoder(ReadOnlySpan<byte> input)
        {
            _ = FuzzCRLEncoderCore(input.ToArray(), roundTrip: false);
        }

        /// <summary>
        /// Requires byte-exact and field-exact canonical CRL TBS round trips.
        /// </summary>
        public static void AflfuzzCRLEncoderIndempotent(Stream stream)
        {
            _ = FuzzCRLEncoderCore(ReadAllBytes(stream), roundTrip: true);
        }

        /// <summary>
        /// Requires byte-exact and field-exact canonical CRL TBS round trips.
        /// </summary>
        public static void LibfuzzCRLEncoderIndempotent(ReadOnlySpan<byte> input)
        {
            _ = FuzzCRLEncoderCore(input.ToArray(), roundTrip: true);
        }

        internal static bool FuzzX509CRLCore(byte[] input)
        {
            return DecodeCrlInput(input) != null;
        }

        internal static bool FuzzCRLEncoderCore(byte[] input, bool roundTrip)
        {
            X509CRL crl = DecodeCrlInput(input);
            if (crl == null)
            {
                return false;
            }

            byte[] algorithmIdentifier = ReadCrlAlgorithmIdentifier(input);
            byte[] canonical = CrlBuilder.Create(crl).Encode(algorithmIdentifier);
            AssertCrlAlgorithmIdentifier(algorithmIdentifier, canonical);
            if (roundTrip)
            {
                var decoded = new X509CRL();
                decoded.DecodeCrl(canonical);
                AssertCrlFieldsEqual(crl, decoded);

                byte[] reencoded = CrlBuilder.Create(decoded).Encode(algorithmIdentifier);
                if (!canonical.AsSpan().SequenceEqual(reencoded))
                {
                    throw new InvalidOperationException("Canonical CRL TBS bytes changed after re-encoding.");
                }

                var decodedAgain = new X509CRL();
                decodedAgain.DecodeCrl(reencoded);
                AssertCrlFieldsEqual(decoded, decodedAgain);
            }
            return true;
        }

        internal static void AssertCrlAlgorithmIdentifier(ReadOnlySpan<byte> expected, byte[] tbs)
        {
            if (!expected.SequenceEqual(ReadCrlTbsAlgorithmIdentifier(tbs)))
            {
                throw new InvalidOperationException("Canonical CRL signature algorithm identifier changed.");
            }
        }

        internal static void AssertCrlFieldsEqual(IX509CRL expected, IX509CRL actual)
        {
            if (!expected.IssuerName.RawData.AsSpan().SequenceEqual(actual.IssuerName.RawData) ||
                expected.Issuer != actual.Issuer ||
                expected.HashAlgorithmName != actual.HashAlgorithmName ||
                expected.ThisUpdate != actual.ThisUpdate ||
                expected.NextUpdate != actual.NextUpdate ||
                expected.RevokedCertificates.Count != actual.RevokedCertificates.Count)
            {
                throw new InvalidOperationException("Canonical CRL fields changed after re-encoding.");
            }

            for (int index = 0; index < expected.RevokedCertificates.Count; index++)
            {
                RevokedCertificate expectedEntry = expected.RevokedCertificates[index];
                RevokedCertificate actualEntry = actual.RevokedCertificates[index];
                if (!expectedEntry.UserCertificate.AsSpan().SequenceEqual(actualEntry.UserCertificate) ||
                    expectedEntry.SerialNumber != actualEntry.SerialNumber ||
                    expectedEntry.RevocationDate != actualEntry.RevocationDate)
                {
                    throw new InvalidOperationException("Canonical CRL revoked entries changed after re-encoding.");
                }
                AssertCrlExtensionsEqual(expectedEntry.CrlEntryExtensions, actualEntry.CrlEntryExtensions);
            }

            AssertCrlExtensionsEqual(expected.CrlExtensions, actual.CrlExtensions);
        }

        private static void AssertCrlExtensionsEqual(
            X509ExtensionCollection expected,
            X509ExtensionCollection actual)
        {
            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException("Canonical CRL extension count changed after re-encoding.");
            }
            for (int index = 0; index < expected.Count; index++)
            {
                if (expected[index].Oid?.Value != actual[index].Oid?.Value ||
                    expected[index].Critical != actual[index].Critical ||
                    !expected[index].RawData.AsSpan().SequenceEqual(actual[index].RawData))
                {
                    throw new InvalidOperationException("Canonical CRL extensions changed after re-encoding.");
                }
            }
        }

        private static byte[] ReadCrlAlgorithmIdentifier(byte[] input)
        {
            var signature = new X509Signature(input);
            return ReadCrlTbsAlgorithmIdentifier(signature.Tbs);
        }

        private static byte[] ReadCrlTbsAlgorithmIdentifier(byte[] input)
        {
            var reader = new AsnReader(input, AsnEncodingRules.DER);
            AsnReader tbs = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            if (tbs.PeekTag() == Asn1Tag.Integer)
            {
                _ = tbs.ReadInteger();
            }
            return tbs.ReadEncodedValue().ToArray();
        }

        private static X509CRL DecodeCrlInput(byte[] input)
        {
            try
            {
                var crl = new X509CRL(input);
                _ = crl.Issuer;
                _ = crl.IssuerName;
                _ = crl.ThisUpdate;
                _ = crl.NextUpdate;
                _ = crl.HashAlgorithmName;
                _ = crl.RawData;
                foreach (RevokedCertificate revokedCertificate in crl.RevokedCertificates)
                {
                    _ = revokedCertificate.SerialNumber;
                    _ = revokedCertificate.RevocationDate;
                    foreach (X509Extension extension in revokedCertificate.CrlEntryExtensions)
                    {
                        _ = extension.Format(false);
                    }
                }
                foreach (X509Extension extension in crl.CrlExtensions)
                {
                    _ = extension.Format(false);
                }
                _ = crl.ToString();
                return crl;
            }
            catch (CryptographicException exception) when (IsExpectedCertificateInputException(exception))
            {
                return null;
            }
        }
    }
}
