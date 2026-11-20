// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
