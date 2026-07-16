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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for public ASN.1 helpers and low-level ASN.1 readers.
    /// </summary>
    public static partial class FuzzableCode
    {
        public static void AflfuzzAsnUtilsX509Blob(Stream stream)
        {
            FuzzAsnUtilsX509BlobCore(ReadAllBytes(stream));
        }

        public static void LibfuzzAsnUtilsX509Blob(ReadOnlySpan<byte> input)
        {
            FuzzAsnUtilsX509BlobCore(input.ToArray());
        }

        public static void AflfuzzAsnReaderSequence(Stream stream)
        {
            FuzzAsnReaderSequenceCore(ReadAllBytes(stream));
        }

        public static void LibfuzzAsnReaderSequence(ReadOnlySpan<byte> input)
        {
            FuzzAsnReaderSequenceCore(input.ToArray());
        }

        public static void AflfuzzAsnReaderInteger(Stream stream)
        {
            FuzzAsnReaderIntegerCore(ReadAllBytes(stream));
        }

        public static void LibfuzzAsnReaderInteger(ReadOnlySpan<byte> input)
        {
            FuzzAsnReaderIntegerCore(input.ToArray());
        }

        internal static void FuzzAsnUtilsX509BlobCore(byte[] input)
        {
            try
            {
                _ = AsnUtils.ParseX509Blob(input);
            }
            catch (CryptographicException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
            catch (AsnContentException)
            {
            }
        }

        internal static void FuzzAsnReaderSequenceCore(byte[] input)
        {
            try
            {
                var reader = new AsnReader(input, AsnEncodingRules.DER);
                AsnReader sequence = reader.ReadSequence();
                while (sequence.HasData)
                {
                    _ = sequence.ReadEncodedValue();
                }
                reader.ThrowIfNotEmpty();
            }
            catch (CryptographicException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
            catch (AsnContentException)
            {
            }
        }

        internal static void FuzzAsnReaderIntegerCore(byte[] input)
        {
            try
            {
                var reader = new AsnReader(input, AsnEncodingRules.DER);
                _ = reader.ReadIntegerBytes();
                reader.ThrowIfNotEmpty();
            }
            catch (CryptographicException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
            catch (AsnContentException)
            {
            }
        }
    }
}
