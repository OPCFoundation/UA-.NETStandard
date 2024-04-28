

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
    public static void AflfuzzBinaryEncoder(string input)
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
        string json = Encoding.UTF8.GetString(input);
        _ = FuzzJsonDecoderCore(json);
    }

    /// <summary>
    /// The binary encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void LibfuzzJsonEncoder(ReadOnlySpan<byte> input)
    {
        IEncodeable encodeable = null;
        string json = Encoding.UTF8.GetString(input);
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
    private static IEncodeable FuzzJsonDecoderCore(string json)
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
                    return null;
            }

            throw;
        }
    }
}

