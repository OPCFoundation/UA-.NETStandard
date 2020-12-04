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
    public class X509Signature
    {
        public byte[] Tbs { get; private set; }
        public byte[] Signature { get; private set; }
        public byte[] SignatureAlgorithmIdentifier { get; private set; }
        public string SignatureAlgorithm { get; private set; }
        public HashAlgorithmName Name { get; private set; }

        public X509Signature(byte[] signedBlob)
        {
            Decode(signedBlob);
        }

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

        private void Decode(byte[] crl)
        {
            try
            {
                AsnReader crlReader = new AsnReader(crl, AsnEncodingRules.DER);
                {
                    var tag = Asn1Tag.Sequence;
                    var seqReader = crlReader?.ReadSequence(tag);
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
                        Debug.Assert(unusedBitCount == 0, "Unexpected unused bit count.");
                        return;
                    }
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
                case Oids.RsaPkcs1Md5: 
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
            using (RSA rsa = certificate.GetRSAPublicKey())
            {
                return rsa.VerifyData(Tbs, Signature, Name, padding);
            }
        }

        /// <summary>
        /// Verify the signature with the ECC public key of the signer.
        /// </summary>
        private bool VerifyForECDsa(X509Certificate2 certificate)
        {
            using (ECDsa key = certificate.GetECDsaPublicKey())
            {
                return key.VerifyData(Tbs, Signature, Name);
            }
        }

        private string DecodeAlgorithm(byte [] oid)
        {
            var seqReader = new AsnReader(oid, AsnEncodingRules.DER);
            var sigOid = seqReader.ReadSequence();
            return sigOid.ReadObjectIdentifier();
        }

        /// <summary>
        /// Encode a ECDSA signature as ASN.1
        /// </summary>
        /// <param name="signature"></param>
        private static byte[] EncodeECDSA(byte[] signature)
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
    }
}
