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
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates
{
    public class X509Signature
    {
        public byte[] Tbs { get; private set; }
        public byte[] Signature { get; private set; }
        public HashAlgorithmName Name { get; private set; }

        public X509Signature(byte[] signedBlob)
        {
            Decode(signedBlob);
        }

        public X509Signature(byte[] tbs, byte[] signature, HashAlgorithmName name)
        {
            Tbs = tbs;
            Signature = signature;
            Name = name;
        }

        /// <summary>
        /// Encode Tbs with a signature in ASN format.
        /// </summary>
        /// <returns>X509 ASN format of EncodedData+SignatureOID+Signature bytes.</returns>
        public byte[] GetEncoded()
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);

            var tag = Asn1Tag.Sequence;
            writer.PushSequence(tag);

            // write Tbs encoded data
            writer.WriteEncodedValue(Tbs);

            // Signature Algorithm Identifier
            writer.PushSequence();
            string signatureAlgorithm = OidConstants.GetRSAOid(Name);
            writer.WriteObjectIdentifier(signatureAlgorithm);
            writer.WriteNull();
            writer.PopSequence();

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
                        var signatureAlgorithm = sigOid.ReadObjectIdentifier();
                        Name = OidConstants.GetHashAlgorithmName(signatureAlgorithm);

                        // Signature
                        int unusedBitCount;
                        Signature = seqReader.ReadBitString(out unusedBitCount);
                    }
                }
            }
            catch (AsnContentException ace)
            {
                throw new CryptographicException("Failed to decode the X509 signature.", ace);
            }
        }

        /// <summary>
        /// Encode a ECDSA signature as ASN.1
        /// </summary>
        /// <param name="signature"></param>
        public static byte[] EncodeECDSA(byte[] signature)
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
