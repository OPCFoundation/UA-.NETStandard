

using System;
using System.IO;
using Opc.Ua;
using Opc.Ua.Bindings;

public static partial class FuzzableCode
{
    private static ServiceMessageContext messageContext = ServiceMessageContext.GlobalContext;

    /// <summary>
    /// Print information about the fuzzer target.
    /// </summary>
    public static void FuzzInfo()
    {
        Console.WriteLine("OPC UA Core Encoder Fuzzer for afl-fuzz and libfuzzer.");
        Console.WriteLine("Fuzzing targets for various aspects of the Binary, Json and Xml encoders.");
    }

    /// <summary>
    /// Prepare a seekable memory stream from the input stream.
    /// </summary>
    private static MemoryStream PrepareArraySegmentStream(Stream stream)
    {
        const int segmentSize = 0x40;

        // afl-fuzz uses a non seekable stream, causing false positives
        // use ArraySegmentStream in combination with fuzz target...
        MemoryStream memoryStream;
        using (var binaryStream = new BinaryReader(stream))
        {
            var bufferCollection = new BufferCollection();
            byte[] buffer;
            do
            {
                buffer = binaryStream.ReadBytes(segmentSize);
                bufferCollection.Add(buffer);
            } while (buffer.Length == segmentSize);
            memoryStream = new ArraySegmentStream(bufferCollection);
        }

        return memoryStream;
    }
}

