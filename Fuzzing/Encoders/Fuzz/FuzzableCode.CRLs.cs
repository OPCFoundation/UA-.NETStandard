/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;
using Opc.Ua;

/// <summary>
/// Fuzzing code for the CRL decoder and encoder.
/// </summary>
public static partial class FuzzableCode
{
    /// <summary>
    /// The CRL decoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCRLDecoder(Stream stream)
    {
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            FuzzCRLDecoderCore(memoryStream.ToArray(), false, false);
        }
    }

    /// <summary>
    /// The CRL encoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCRLEncoder(Stream stream)
    {
        X509CRL crl = null;
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            try
            {
                crl = FuzzCRLDecoderCore(memoryStream.ToArray(), false, false);
            }
            catch
            {
                return;
            }
        }

        // encode the fuzzed object and see if it crashes
        if (crl != null)
        {
            _ = CrlBuilder.Create(crl).Encode();
        }
    }

    /// <summary>
    /// The CRL encoder indempotent fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCRLEncoderIndempotent(Stream stream)
    {
        X509CRL crl = null;
        byte[] serialized = null;
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            try
            {
                crl = FuzzCRLDecoderCore(memoryStream.ToArray(), false, true);
                serialized = CrlBuilder.Create(crl).Encode();
            }
            catch
            {
                return;
            }
        }

        // reencode the fuzzed input and see if they are indempotent
        FuzzCRLEncoderIndempotentCore(serialized, crl);
    }

    /// <summary>
    /// The CRL decoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCRLDecoder(ReadOnlySpan<byte> input)
    {
        _ = FuzzCRLDecoderCore(input, false, false);
    }

    /// <summary>
    /// The CRL encoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCRLEncoder(ReadOnlySpan<byte> input)
    {
        X509CRL crl = null;
        try
        {
            crl = FuzzCRLDecoderCore(input, false, true);
        }
        catch
        {
            return;
        }

        // encode the fuzzed object and see if it crashes
        if (crl != null)
        {
            _ = CrlBuilder.Create(crl).Encode();
        }
    }

    /// <summary>
    /// The CRL encoder indempotent fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCRLEncoderIndempotent(ReadOnlySpan<byte> input)
    {
        X509CRL crl = null;
        byte[] serialized = null;
        try
        {
            crl = FuzzCRLDecoderCore(input, false, true);
            serialized = CrlBuilder.Create(crl).Encode();
        }
        catch
        {
            return;
        }

        // reencode the fuzzed input and see if they are indempotent
        FuzzCRLEncoderIndempotentCore(serialized, crl);
    }

    /// <summary>
    /// The fuzz target for the CRL decoder.
    /// </summary>
    /// <param name="serialized">A byte array with fuzz content.</param>
    internal static X509CRL FuzzCRLDecoderCore(ReadOnlySpan<byte> serialized, bool ignoreSignature, bool throwAll)
    {
        try
        {
            var result = new X509CRL(serialized.ToArray(), ignoreSignature);
            _ = result.Issuer;
            return result;
        }
        catch (CryptographicException)
        {
            if (!throwAll)
            {
                return null;
            }
            throw;
        }
    }

    /// <summary>
    /// The indempotent fuzz target core for the CRL encoder.
    /// </summary>
    /// <param name="serialized">The indempotent ASN.1 encoded data.</param>
    /// <exception cref="Exception"></exception>
    internal static void FuzzCRLEncoderIndempotentCore(byte[] serialized, X509CRL encodeable)
    {
        if (serialized == null || encodeable == null) return;

        X509CRL crl2 = FuzzCRLDecoderCore(serialized, true, true);

        byte[] serialized2 = CrlBuilder.Create(crl2).Encode();
        using (var memoryStream2 = new MemoryStream(serialized2))
        {
            X509CRL crl3 = FuzzCRLDecoderCore(serialized2, true, true);

            string encodeableTypeName = crl3?.GetType().Name ?? "unknown type";
            if (serialized2 == null || !serialized.SequenceEqual(serialized2))
            {
                throw new Exception(Utils.Format("Indempotent encoding failed. Type={0}.", encodeableTypeName));
            }

            if (!Utils.IsEqual(crl2.Issuer, crl3.Issuer) ||
                !Utils.IsEqual(crl2.ThisUpdate, crl3.ThisUpdate) ||
                !Utils.IsEqual(crl2.NextUpdate, crl3.NextUpdate))
            {
                throw new Exception(Utils.Format("Indempotent 3rd gen decoding failed. Type={0}.", encodeableTypeName));
            }
        }
    }
}

