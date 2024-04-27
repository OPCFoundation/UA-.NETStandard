
using System;
using System.IO;
using Opc.Ua;

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
    private static IEncodeable FuzzBinaryDecoderCore(MemoryStream stream)
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
                    return null;
            }

            throw;
        }
    }
}

