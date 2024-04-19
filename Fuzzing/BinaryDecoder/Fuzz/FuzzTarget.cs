

using System;
using System.IO;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace BinaryDecoder.Fuzz
{
    public static class FuzzableCode
    {
        const int SegmentSize = 0x40;
        private static ServiceMessageContext messageContext = null;

        public static void FuzzTarget(Stream stream)
        {
            if (messageContext == null)
            {
                messageContext = new ServiceMessageContext();
            }

            try
            {
                // fuzzer uses a non seekable stream, causing false positives
                // use ArraySegmentStream in combination with fuzzed decoder...
                {
                    using (var binaryStream = new BinaryReader(stream))
                    {
                        var bufferCollection = new BufferCollection();
                        byte[] buffer;
                        do
                        {
                            buffer = binaryStream.ReadBytes(SegmentSize);
                            bufferCollection.Add(buffer);
                        } while (buffer.Length == SegmentSize);
                        stream = new ArraySegmentStream(bufferCollection);
                    }
                }

                using (var decoder = new Opc.Ua.BinaryDecoder(stream, messageContext))
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
}

#if mist
    public static class FuzzableCode
    {
        //public static void FuzzTargetMethod(ReadOnlySpan<byte> input)
        public static void FuzzTargetMethod(byte[] input)
        {
            try
            {
                var messageContext = new ServiceMessageContext();
                using (var decoder = new BinaryDecoder(input, messageContext))
                {
                    decoder.DecodeMessage(null);
                }
            }
            catch (Exception ex) when (ex is ServiceResultException)
            {
                // This is an example. You should filter out any
                // *expected* exception(s) from your code here,
                // but it’s an anti-pattern to catch *all* Exceptions,
                // as you might suppress legitimate problems, such as 
                // your code throwing a NullReferenceException.
            }
        }
    }
#endif

