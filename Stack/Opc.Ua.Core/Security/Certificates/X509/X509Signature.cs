/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates.X509
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signature"></param>
        public static byte[] EncodeECDSASignatureToASNFormat(byte[] signature)
        {
            // Encode from ieee signature format to ASN1 DER encoded 
            // signature format for ecdsa certificates.
            // ECDSA-Sig-Value ::= SEQUENCE { r INTEGER, s INTEGER }
            // https://www.ietf.org/rfc/rfc5480.txt
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
