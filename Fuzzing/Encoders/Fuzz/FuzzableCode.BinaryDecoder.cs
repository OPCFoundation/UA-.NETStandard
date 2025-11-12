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

namespace Opc.Ua.Fuzzing
{
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
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryDecoderCore(memoryStream);
        }

        /// <summary>
        /// The binary encoder fuzz target for afl-fuzz.
        /// </summary>
        /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
        public static void AflfuzzBinaryEncoder(Stream stream)
        {
            IEncodeable encodeable = null;
            using (MemoryStream memoryStream = PrepareArraySegmentStream(stream))
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
                _ = BinaryEncoder.EncodeMessage(encodeable, MessageContext);
            }
        }

        /// <summary>
        /// The binary encoder idempotent fuzz target for afl-fuzz.
        /// </summary>
        /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
        public static void AflfuzzBinaryEncoderIndempotent(Stream stream)
        {
            IEncodeable encodeable = null;
            byte[] serialized = null;
            using (MemoryStream memoryStream = PrepareArraySegmentStream(stream))
            {
                try
                {
                    encodeable = FuzzBinaryDecoderCore(memoryStream, true);
                    serialized = BinaryEncoder.EncodeMessage(encodeable, MessageContext);
                }
                catch
                {
                    return;
                }
            }

            // reencode the fuzzed input and see if they are idempotent
            FuzzBinaryEncoderIndempotentCore(serialized, encodeable);
        }

        /// <summary>
        /// The binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzBinaryDecoder(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            _ = FuzzBinaryDecoderCore(memoryStream);
        }

        /// <summary>
        /// The binary encoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzBinaryEncoder(ReadOnlySpan<byte> input)
        {
            IEncodeable encodeable = null;
            using (var memoryStream = new MemoryStream(input.ToArray()))
            {
                try
                {
                    encodeable = FuzzBinaryDecoderCore(memoryStream, true);
                }
                catch
                {
                    return;
                }
            }

            // encode the fuzzed object and see if it crashes
            if (encodeable != null)
            {
                _ = BinaryEncoder.EncodeMessage(encodeable, MessageContext);
            }
        }

        /// <summary>
        /// The binary encoder idempotent fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzBinaryEncoderIndempotent(ReadOnlySpan<byte> input)
        {
            IEncodeable encodeable = null;
            byte[] serialized = null;
            using (var memoryStream = new MemoryStream(input.ToArray()))
            {
                try
                {
                    encodeable = FuzzBinaryDecoderCore(memoryStream, true);
                    serialized = BinaryEncoder.EncodeMessage(encodeable, MessageContext);
                }
                catch
                {
                    return;
                }
            }

            // reencode the fuzzed input and see if they are idempotent
            FuzzBinaryEncoderIndempotentCore(serialized, encodeable);
        }

        /// <summary>
        /// The fuzz target for the BinaryDecoder.
        /// </summary>
        /// <param name="stream">A memory stream with fuzz content.</param>
        internal static IEncodeable FuzzBinaryDecoderCore(
            MemoryStream stream,
            bool throwAll = false)
        {
            try
            {
                using var decoder = new BinaryDecoder(stream, MessageContext);
                return decoder.DecodeMessage(null);
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
                        goto default;
                    default:
                        throw;
                }
            }
        }

        /// <summary>
        /// The idempotent fuzz target core for the BinaryEncoder.
        /// </summary>
        /// <param name="serialized">The idempotent UA binary encoded data.</param>
        /// <exception cref="Exception"></exception>
        internal static void FuzzBinaryEncoderIndempotentCore(
            byte[] serialized,
            IEncodeable encodeable)
        {
            if (serialized == null || encodeable == null)
            {
                return;
            }

            using var memoryStream = new MemoryStream(serialized);
            IEncodeable encodeable2 = FuzzBinaryDecoderCore(memoryStream, true);
            byte[] serialized2 = BinaryEncoder.EncodeMessage(encodeable2, MessageContext);

            using var memoryStream2 = new MemoryStream(serialized2);
            IEncodeable encodeable3 = FuzzBinaryDecoderCore(memoryStream2, true);

            string encodeableTypeName = encodeable2?.GetType().Name ?? "unknown type";
            if (serialized2 == null || !serialized.SequenceEqual(serialized2))
            {
                throw new InvalidOperationException(
                    Utils.Format("Idempotent encoding failed. Type={0}.", encodeableTypeName));
            }

            if (!Utils.IsEqual(encodeable2, encodeable3))
            {
                throw new InvalidOperationException(Utils.Format(
                    "Idempotent 3rd gen decoding failed. Type={0}.",
                    encodeableTypeName));
            }
        }
    }
}
