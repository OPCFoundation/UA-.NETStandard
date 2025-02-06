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
using Opc.Ua;

/// <summary>
/// Fuzzing code for the certificate decoder.
/// </summary>
public static partial class FuzzableCode
{
    /// <summary>
    /// The certificate decoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCertificateDecoder(Stream stream)
    {
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            FuzzCertificateDecoderCore(memoryStream.ToArray());
        }
    }

    /// <summary>
    /// The certificate chain decoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCertificateChainDecoder(Stream stream)
    {
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            FuzzCertificateChainDecoderCore(memoryStream.ToArray());
        }
    }

    /// <summary>
    /// The certificate chain decoder with custom blob fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzCertificateChainDecoderCustom(Stream stream)
    {
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            FuzzCertificateChainDecoderCore(memoryStream.ToArray(), true);
        }
    }

    /// <summary>
    /// The certificate decoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCertificateDecoder(ReadOnlySpan<byte> input)
    {
        _ = FuzzCertificateDecoderCore(input);
    }

    /// <summary>
    /// The certificate encoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCertificateChainDecoder(ReadOnlySpan<byte> input)
    {
        _ = FuzzCertificateChainDecoderCore(input);
    }

    /// <summary>
    /// The certificate encoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzCertificateChainDecoderCustom(ReadOnlySpan<byte> input)
    {
        _ = FuzzCertificateChainDecoderCore(input, true);
    }

    /// <summary>
    /// The fuzz target for the Certificate decoder.
    /// </summary>
    /// <param name="serialized">A byte array with fuzz content.</param>
    internal static X509Certificate2 FuzzCertificateDecoderCore(ReadOnlySpan<byte> serialized, bool throwAll = false)
    {
        try
        {
            return X509CertificateLoader.LoadCertificate(serialized.ToArray());
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
    /// The fuzz target for the Certificate chain decoder.
    /// </summary>
    /// <param name="serialized">A byte array with fuzz content.</param>
    internal static X509Certificate2Collection FuzzCertificateChainDecoderCore(ReadOnlySpan<byte> serialized, bool useAsn1Parser = false, bool throwAll = false)
    {
        try
        {
            return Utils.ParseCertificateChainBlob(serialized.ToArray(), useAsn1Parser);
        }
        catch (ServiceResultException sre)
        {
            switch (sre.StatusCode)
            {
                case StatusCodes.BadCertificateInvalid:
                    if (!throwAll)
                    {
                        return null;
                    }
                    break;

                default:
                    break;
            }   
            throw;
        }
    }
}

