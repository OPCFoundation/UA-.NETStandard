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
using Opc.Ua;

/// <summary>
/// Fuzzing code for the binary decoder and encoder.
/// </summary>
public static partial class FuzzableCode
{
    /// <summary>
    /// The binary decoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzBinaryDecoder(Stream stream)
    {
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            FuzzBinaryDecoderCore(memoryStream);
        }
    }

    /// <summary>
    /// The binary encoder fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void AflfuzzBinaryEncoder(Stream stream)
    {
        IEncodeable encodeable = null;
        using (var memoryStream = PrepareArraySegmentStream(stream))
        {
            try
            {
                encodeable = FuzzBinaryDecoderCore(memoryStream);
            }
            catch
            {
                return;
            }
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            _ = BinaryEncoder.EncodeMessage(encodeable, messageContext);
        }
    }

    /// <summary>
    /// The binary decoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzBinaryDecoder(ReadOnlySpan<byte> input)
    {
        using (var memoryStream = new MemoryStream(input.ToArray()))
        {
            _ = FuzzBinaryDecoderCore(memoryStream);
        }
    }

    /// <summary>
    /// The binary encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void LibfuzzBinaryEncoder(ReadOnlySpan<byte> input)
    {
        IEncodeable encodeable = null;
        using (var memoryStream = new MemoryStream(input.ToArray()))
        {
            try
            {
                encodeable = FuzzBinaryDecoderCore(memoryStream);
            }
            catch
            {
                return;
            }
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            _ = BinaryEncoder.EncodeMessage(encodeable, messageContext);
        }
    }

    /// <summary>
    /// The fuzz target for the BinaryDecoder.
    /// </summary>
    /// <param name="stream">A memory stream with fuzz content.</param>
    internal static IEncodeable FuzzBinaryDecoderCore(MemoryStream stream, bool throwAll = false)
    {
        try
        {
            using (var decoder = new BinaryDecoder(stream, messageContext))
            {
                return decoder.DecodeMessage(null);
            }
        }
        catch (ServiceResultException sre)
        {
            switch (sre.StatusCode)
            {
                case StatusCodes.BadEncodingLimitsExceeded:
                case StatusCodes.BadDecodingError:
                    if (!throwAll)
                    {
                        return null;
                    }
                    break;
            }

            throw;
        }
    }
}

