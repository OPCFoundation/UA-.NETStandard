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
using System.Security.Cryptography;

namespace Opc.Ua
{
    /// <summary>
    /// Computes stable schema identifiers used by the experimental Avro, Arrow, JSON, and Protobuf encodings.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_1")]
    public static class SchemaId
    {
        private const ulong RabinAvroEmpty = 0xC15D213AA4D7A795UL;
        private static readonly ulong[] s_rabinAvroTable = CreateRabinAvroTable();

        /// <summary>
        /// Computes the CRC-64-AVRO Rabin fingerprint for canonical Avro schema bytes.
        /// </summary>
        /// <param name = "canonical">The canonical schema bytes to fingerprint.</param>
        /// <returns>The CRC-64-AVRO fingerprint.</returns>
        public static ulong RabinCrc64Avro(ReadOnlySpan<byte> canonical)
        {
            ulong result = RabinAvroEmpty;
            for (int ii = 0; ii < canonical.Length; ii++)
            {
                result = (result >> 8) ^ s_rabinAvroTable[(byte)(result ^ canonical[ii])];
            }

            return result;
        }

        /// <summary>
        /// Builds the Avro single-object encoding prefix for the supplied schema fingerprint.
        /// </summary>
        /// <param name = "fp">The Avro Rabin fingerprint to place in the prefix.</param>
        /// <returns>The ten-byte Avro single-object prefix.</returns>
        public static byte[] AvroSingleObjectPrefix(ulong fp)
        {
            byte[] prefix = new byte[10];
            prefix[0] = 0xC3;
            prefix[1] = 0x01;
            for (int ii = 0; ii < sizeof(ulong); ii++)
            {
                prefix[ii + 2] = (byte)(fp >> (8 * ii));
            }

            return prefix;
        }

        /// <summary>
        /// Computes the leading bytes of the SHA-256 digest used as Arrow and Protobuf schema identifiers.
        /// </summary>
        /// <param name = "canonical">The canonical schema bytes to fingerprint.</param>
        /// <param name = "nbytes">The number of leading SHA-256 digest bytes to return.</param>
        /// <returns>The requested leading bytes of the SHA-256 digest.</returns>
        public static byte[] Sha256Id(ReadOnlySpan<byte> canonical, int nbytes = 8)
        {
            if (nbytes < 0 || nbytes > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nbytes),
                    nbytes,
                    "The identifier length must be between 0 and 32 bytes."
                );
            }

#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(canonical);
#else
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(canonical.ToArray());
#endif
            if (nbytes == hash.Length)
            {
                return hash;
            }

            byte[] id = new byte[nbytes];
            Array.Copy(hash, id, nbytes);
            return id;
        }

        /// <summary>
        /// Computes the leading bytes of the SHA-256 digest used as JSON Schema identifiers.
        /// </summary>
        /// <param name = "schemaJson">The UTF-8 encoded JSON Schema document to fingerprint.</param>
        /// <param name = "nbytes">The number of leading SHA-256 digest bytes to return.</param>
        /// <returns>The requested leading bytes of the SHA-256 digest.</returns>
        public static byte[] JsonSchemaId(ReadOnlySpan<byte> schemaJson, int nbytes = 8)
        {
            return Sha256Id(schemaJson, nbytes);
        }

        private static ulong[] CreateRabinAvroTable()
        {
            ulong[] table = new ulong[256];
            for (int ii = 0; ii < table.Length; ii++)
            {
                ulong fp = (ulong)ii;
                for (int jj = 0; jj < 8; jj++)
                {
                    fp = (fp >> 1) ^ (RabinAvroEmpty & (0UL - (fp & 1UL)));
                }

                table[ii] = fp;
            }

            return table;
        }
    }
}
