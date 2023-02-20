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
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Utils for ASN.1 encoding and decoding.
    /// </summary>
    public static class AsnUtils
    {
        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
        internal static string ToHexString(this byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(buffer.Length * 2);

            if (invertEndian)
            {
                for (int ii = buffer.Length - 1; ii >= 0; ii--)
                {
                    builder.AppendFormat("{0:X2}", buffer[ii]);
                }
            }
            else
            {
                for (int ii = 0; ii < buffer.Length; ii++)
                {
                    builder.AppendFormat("{0:X2}", buffer[ii]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Converts a hexadecimal string to an array of bytes.
        /// </summary>
        internal static byte[] FromHexString(this string buffer)
        {
            if (buffer == null)
            {
                return null;
            }

            if (buffer.Length == 0)
            {
                return Array.Empty<byte>();
            }

            const string digits = "0123456789ABCDEF";

            byte[] bytes = new byte[(buffer.Length / 2) + (buffer.Length % 2)];

            int ii = 0;

            while (ii < bytes.Length * 2)
            {
                int index = digits.IndexOf(buffer[ii]);

                if (index == -1)
                {
                    break;
                }

                byte b = (byte)index;
                b <<= 4;

                if (ii < buffer.Length - 1)
                {
                    index = digits.IndexOf(buffer[ii + 1]);

                    if (index == -1)
                    {
                        break;
                    }

                    b += (byte)index;
                }

                bytes[ii / 2] = b;
                ii += 2;
            }

            return bytes;
        }

        /// <summary>
        /// Writer for Public Key parameters.
        /// </summary>
        /// <remarks>
        /// https://www.itu.int/rec/T-REC-X.690-201508-I/en
        /// section 8.3 (Encoding of an integer value).
        /// </remarks>
        /// <param name="writer">The writer</param>
        /// <param name="integer">The key parameter</param>
        internal static void WriteKeyParameterInteger(this AsnWriter writer, ReadOnlySpan<byte> integer)
        {
            if (integer[0] == 0)
            {
                int newStart = 1;

                while (newStart < integer.Length)
                {
                    if (integer[newStart] >= 0x80)
                    {
                        newStart--;
                        break;
                    }

                    if (integer[newStart] != 0)
                    {
                        break;
                    }

                    newStart++;
                }

                if (newStart == integer.Length)
                {
                    newStart--;
                }

                integer = integer.Slice(newStart);
            }

            writer.WriteIntegerUnsigned(integer);
        }

        /// <summary>
        /// Parse a X509 Tbs and signature from a byte blob with validation,
        /// return the byte array which contains the X509 blob.
        /// </summary>
        /// <param name="blob">The encoded CRL or certificate sequence.</param>
        public static byte[] ParseX509Blob(byte[] blob)
        {
            try
            {
                AsnReader x509Reader = new AsnReader(blob, AsnEncodingRules.DER);
                var peekBlob = blob.AsSpan(0, x509Reader.PeekContentBytes().Length + 4).ToArray();
                var seqReader = x509Reader.ReadSequence(Asn1Tag.Sequence);
                if (seqReader != null)
                {
                    // Tbs encoded data
                    var tbs = seqReader.ReadEncodedValue();

                    // Signature Algorithm Identifier
                    var sigOid = seqReader.ReadSequence();
                    var signatureAlgorithm = sigOid.ReadObjectIdentifier();
                    var name = Oids.GetHashAlgorithmName(signatureAlgorithm);

                    // Signature
                    int unusedBitCount;
                    var signature = seqReader.ReadBitString(out unusedBitCount);
                    if (unusedBitCount != 0)
                    {
                        throw new AsnContentException("Unexpected data in signature.");
                    }
                    seqReader.ThrowIfNotEmpty();
                    return peekBlob;
                }
            }
            catch (AsnContentException ace)
            {
                throw new CryptographicException("Failed to decode the X509 sequence.", ace);
            }
            throw new CryptographicException("Invalid ASN encoding for the X509 sequence.");
        }
    }
}
