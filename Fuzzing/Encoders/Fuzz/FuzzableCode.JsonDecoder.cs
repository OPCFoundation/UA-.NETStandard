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
using System.Text;
using Opc.Ua;

/// <summary>
/// Fuzzing code for the JSON decoder and encoder.
/// </summary>
public static partial class FuzzableCode
{
    /// <summary>
    /// The Json decoder fuzz target for afl-fuzz.
    /// </summary>
    public static void AflfuzzJsonDecoder(string input)
    {
        _ = FuzzJsonDecoderCore(input);
    }

    /// <summary>
    /// The Json encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void AflfuzzJsonEncoder(string input)
    {
        IEncodeable encodeable = null;
        try
        {
            encodeable = FuzzJsonDecoderCore(input);
        }
        catch
        {
            return;
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            using (var encoder = new JsonEncoder(messageContext, true))
            {
                encoder.EncodeMessage(encodeable);
                encoder.Close();
            }
        }
    }

    /// <summary>
    /// The json decoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzJsonDecoder(ReadOnlySpan<byte> input)
    {
#if NETFRAMEWORK
        string json = Encoding.UTF8.GetString(input.ToArray());
#else
        string json = Encoding.UTF8.GetString(input);
#endif
        _ = FuzzJsonDecoderCore(json);
    }

    /// <summary>
    /// The binary encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void LibfuzzJsonEncoder(ReadOnlySpan<byte> input)
    {
        IEncodeable encodeable = null;
#if NETFRAMEWORK
        string json = Encoding.UTF8.GetString(input.ToArray());
#else
        string json = Encoding.UTF8.GetString(input);
#endif
        try
        {
            encodeable = FuzzJsonDecoderCore(json);
        }
        catch
        {
            return;
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            using (var encoder = new JsonEncoder(messageContext, true))
            {
                encoder.EncodeMessage(encodeable);
                encoder.Close();
            }
        }
    }

    /// <summary>
    /// The fuzz target for the JsonDecoder.
    /// </summary>
    /// <param name="json">A string with fuzz content.</param>
    internal static IEncodeable FuzzJsonDecoderCore(string json, bool throwAll = false)
    {
        try
        {
            using (var decoder = new JsonDecoder(json, messageContext))
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

