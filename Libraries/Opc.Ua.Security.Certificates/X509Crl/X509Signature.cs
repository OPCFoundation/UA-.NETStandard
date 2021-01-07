/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Describes the three required fields of a X509 Certificate and CRL.
    /// </summary>
    public class X509Signature
    {
        /// <summary>
        /// The field contains the ASN.1 data to be signed.
        /// </summary>
        public byte[] Tbs { get; private set; }
        /// <summary>
        /// The signature of the data.
        /// </summary>
        public byte[] Signature { get; private set; }
        /// <summary>
        /// The encoded signature algorithm that was used for signing.
        /// </summary>
        public byte[] SignatureAlgorithmIdentifier { get; private set; }
        /// <summary>
        /// The signature algorithm as Oid string.
        /// </summary>
        public string SignatureAlgorithm { get; private set; }
        /// <summary>
        /// The hash algorithm used for signing.
        /// </summary>
        public HashAlgorithmName Name { get; private set; }
        /// <summary>
        /// Initialize and decode the sequence with binary ASN.1 encoded CRL or certificate.
        /// </summary>
        /// <param name="signedBlob"></param>
        public X509Signature(byte[] signedBlob)
        {
            Decode(signedBlob);
        }

        /// <summary>
        /// Initialize the X509 signature values.
        /// </summary>
        /// <param name="tbs">The data to be signed.</param>
        /// <param name="signature">The signature of the data.</param>
        /// <param name="signatureAlgorithmIdentifier">The algorithm used to create the signature.</param>
        public X509Signature(byte[] tbs, byte[] signature, byte[] signatureAlgorithmIdentifier)
        {
            Tbs = tbs;
            Signature = signature;
            SignatureAlgorithmIdentifier = signatureAlgorithmIdentifier;
            SignatureAlgorithm = DecodeAlgorithm(signatureAlgorithmIdentifier);
            Name = Oids.GetHashAlgorithmName(SignatureAlgorithm);
        }

        /// <summary>
        /// Encode Tbs with a signature in ASN format.
        /// </summary>
        /// <returns>X509 ASN format of EncodedData+SignatureOID+Signature bytes.</returns>
        public byte[] Encode()
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);

            var tag = Asn1Tag.Sequence;
            writer.PushSequence(tag);

            // write Tbs encoded data
            writer.WriteEncodedValue(Tbs);

            // Signature Algorithm Identifier
            if (SignatureAlgorithmIdentifier != null)
            {
                writer.WriteEncodedValue(SignatureAlgorithmIdentifier);
            }
            else
            {
                writer.PushSequence();
                string signatureAlgorithmIdentifier = Oids.GetRSAOid(Name);
                writer.WriteObjectIdentifier(signatureAlgorithmIdentifier);
                writer.WriteNull();
                writer.PopSequence();
            }

            // Add signature
            writer.WriteBitString(Signature);

            writer.PopSequence(tag);

            return writer.Encode();
        }

        /// <summary>
        /// Decoder for the signature sequence.
        /// </summary>
        /// <param name="crl">The encoded CRL or certificate sequence.</param>
        private void Decode(byte[] crl)
        {
            try
            {
                AsnReader crlReader = new AsnReader(crl, AsnEncodingRules.DER);
                var seqReader = crlReader.ReadSequence(Asn1Tag.Sequence);
                if (seqReader != null)
                {
                    // Tbs encoded data
                    Tbs = seqReader.ReadEncodedValue().ToArray();

                    // Signature Algorithm Identifier
                    var sigOid = seqReader.ReadSequence();
                    SignatureAlgorithm = sigOid.ReadObjectIdentifier();
                    Name = Oids.GetHashAlgorithmName(SignatureAlgorithm);

                    // Signature
                    int unusedBitCount;
                    Signature = seqReader.ReadBitString(out unusedBitCount);
                    if (unusedBitCount != 0)
                    {
                        throw new AsnContentException("Unexpected data in signature.");
                    }
                    seqReader.ThrowIfNotEmpty();
                    return;
                }
                throw new CryptographicException("No valid data in the X509 signature.");
            }
            catch (AsnContentException ace)
            {
                throw new CryptographicException("Failed to decode the X509 signature.", ace);
            }
        }

        /// <summary>
        /// Verify the signature with the public key of the signer.
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns>true if the signature is valid.</returns>
        public bool Verify(X509Certificate2 certificate)
        {
            switch (SignatureAlgorithm)
            {
                case Oids.RsaPkcs1Sha1:
                case Oids.RsaPkcs1Sha256:
                case Oids.RsaPkcs1Sha384:
                case Oids.RsaPkcs1Sha512:
                    return VerifyForRSA(certificate, RSASignaturePadding.Pkcs1);

                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha512:
                    return VerifyForECDsa(certificate);

                default:
                    throw new CryptographicException("Failed to verify signature due to unknown signature algorithm.");
            }
        }

        /// <summary>
        /// Verify the signature with the RSA public key of the signer.
        /// </summary>
        private bool VerifyForRSA(X509Certificate2 certificate, RSASignaturePadding padding)
        {
            RSA rsa = null;
            try
            {
                rsa = certificate.GetRSAPublicKey();
                return rsa.VerifyData(Tbs, Signature, Name, padding);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Verify the signature with the ECC public key of the signer.
        /// </summary>
        private bool VerifyForECDsa(X509Certificate2 certificate)
        {
            using (ECDsa key = certificate.GetECDsaPublicKey())
            {
                var decodedSignature = DecodeECDsa(Signature);
                return key.VerifyData(Tbs, decodedSignature, Name);
            }
        }

        /// <summary>
        /// Decode the algorithm that was used for encoding.
        /// </summary>
        /// <param name="oid">The ASN.1 encoded algorithm oid.</param>
        /// <returns></returns>
        private string DecodeAlgorithm(byte[] oid)
        {
            var seqReader = new AsnReader(oid, AsnEncodingRules.DER);
            var sigOid = seqReader.ReadSequence();
            seqReader.ThrowIfNotEmpty();
            var result = sigOid.ReadObjectIdentifier();
            if (sigOid.HasData)
            {
                sigOid.ReadNull();
            }
            sigOid.ThrowIfNotEmpty();
            return result;
        }

        /// <summary>
        /// Encode a ECDSA signature as ASN.1.
        /// </summary>
        /// <param name="signature">The signature to encode as ASN.1</param>
        private static byte[] EncodeECDsa(byte[] signature)
        {
            // Encode from IEEE signature format to ASN1 DER encoded 
            // signature format for ecdsa certificates.
            // ECDSA-Sig-Value ::= SEQUENCE { r INTEGER, s INTEGER }
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            var tag = Asn1Tag.Sequence;
            writer.PushSequence(tag);

            int segmentLength = signature.Length / 2;
            writer.WriteIntegerUnsigned(new ReadOnlySpan<byte>(signature, 0, segmentLength));
            writer.WriteIntegerUnsigned(new ReadOnlySpan<byte>(signature, segmentLength, segmentLength));

            writer.PopSequence(tag);

            return writer.Encode();
        }

        /// <summary>
        /// Decode a ECDSA signature from ASN.1.
        /// </summary>
        /// <param name="signature">The signature to decode from ASN.1</param>
        private static byte[] DecodeECDsa(ReadOnlyMemory<byte> signature)
        {
            AsnReader reader = new AsnReader(signature, AsnEncodingRules.DER);
            var seqReader = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            var r = seqReader.ReadIntegerBytes();
            var s = seqReader.ReadIntegerBytes();
            seqReader.ThrowIfNotEmpty();
            if (r.Span[0] == 0)
            {
                r = r.Slice(1);
            }
            if (s.Span[0] == 0)
            {
                s = s.Slice(1);
            }
            var result = new byte[r.Length + s.Length];
            r.CopyTo(new Memory<byte>(result, 0, r.Length));
            s.CopyTo(new Memory<byte>(result, r.Length, s.Length));
            return result;
        }
    }
}
