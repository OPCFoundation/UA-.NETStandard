

using System;
using System.IO;
using Opc.Ua;
using Opc.Ua.Bindings;

public static class FuzzableCode
{
    const int SegmentSize = 0x40;
    private static ServiceMessageContext messageContext = null;

    /// <summary>
    /// The fuzz target for afl-fuzz.
    /// </summary>
    /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
    public static void FuzzTarget(Stream stream)
    {
        // fuzzer uses a non seekable stream, causing false positives
        // use ArraySegmentStream in combination with fuzzed decoder...
        MemoryStream memoryStream;
        using (var binaryStream = new BinaryReader(stream))
        {
            var bufferCollection = new BufferCollection();
            byte[] buffer;
            do
            {
                buffer = binaryStream.ReadBytes(SegmentSize);
                bufferCollection.Add(buffer);
            } while (buffer.Length == SegmentSize);
            memoryStream = new ArraySegmentStream(bufferCollection);
        }

        FuzzTargetCore(memoryStream);
    }

    /// <summary>
    /// The fuzz target for libfuzzer.
    /// </summary>
    public static void FuzzTargetLibfuzzer(ReadOnlySpan<byte> input)
    {
        var memoryStream = new MemoryStream(input.ToArray());
        FuzzTargetCore(memoryStream);
    }

    /// <summary>
    /// The fuzz target for the BinaryDecoder.
    /// </summary>
    /// <param name="stream">A memory stream with fuzz content.</param>
    private static void FuzzTargetCore(MemoryStream stream)
    {
        if (messageContext == null)
        {
            messageContext = new ServiceMessageContext();
        }

        try
        {
            using (var decoder = new BinaryDecoder(stream, messageContext))
            {
                _ = decoder.DecodeMessage(null);
            }
        }
        catch (Exception ex)
        {
            if (ex is ServiceResultException sre)
            {
                switch (sre.StatusCode)
                {
                    case StatusCodes.BadEncodingLimitsExceeded:
                    case StatusCodes.BadDecodingError:
                        return;
                }
            }

            throw;
        }
    }
}

