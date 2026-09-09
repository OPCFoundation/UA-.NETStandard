/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryEncoderCore(memoryStream, false, true);
        }

        /// <summary>
        /// The binary encoder idempotent fuzz target for afl-fuzz.
        /// </summary>
        /// <param name="stream">The stdin stream from the afl-fuzz process.</param>
        public static void AflfuzzBinaryEncoderIndempotent(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryEncoderCore(memoryStream, true, true);
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
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryEncoderCore(memoryStream, false, false);
        }

        /// <summary>
        /// The binary encoder idempotent fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzBinaryEncoderIndempotent(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryEncoderCore(memoryStream, true, false);
        }

        /// <summary>
        /// Decodes binary input across non-contiguous buffer boundaries.
        /// </summary>
        public static void LibfuzzBinaryDecoderSegmented(ReadOnlySpan<byte> input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            _ = FuzzBinaryDecoderCore(stream);
        }

        /// <summary>
        /// Re-encodes binary input decoded from non-contiguous buffers.
        /// </summary>
        public static void LibfuzzBinaryEncoderSegmented(ReadOnlySpan<byte> input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryEncoderCore(stream, false, true);
        }

        /// <summary>
        /// Checks canonical binary bytes and values using segmented decodes throughout.
        /// </summary>
        public static void LibfuzzBinaryEncoderIndempotentSegmented(ReadOnlySpan<byte> input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryEncoderCore(stream, true, true);
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
                return decoder.DecodeMessage<IEncodeable>();
            }
            catch (ServiceResultException sre) when (!throwAll && IsExpectedDecodingError(sre))
            {
                return null;
            }
        }

        /// <summary>
        /// The idempotent fuzz target core for the BinaryEncoder.
        /// </summary>
        /// <param name="serialized">The idempotent UA binary encoded data.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void FuzzBinaryEncoderIndempotentCore(
            byte[] serialized,
            IEncodeable encodeable,
            bool segmented = false)
        {
            if (serialized == null || encodeable == null)
            {
                return;
            }

            using MemoryStream memoryStream = segmented
                ? PrepareArraySegmentStream(serialized)
                : new MemoryStream(serialized);
            IEncodeable encodeable2 = FuzzBinaryDecoderCore(memoryStream, true);
            byte[] serialized2 = BinaryEncoder.EncodeMessage(encodeable2, MessageContext);

            using MemoryStream memoryStream2 = segmented
                ? PrepareArraySegmentStream(serialized2)
                : new MemoryStream(serialized2);
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

        private static void FuzzBinaryEncoderCore(MemoryStream stream, bool idempotent, bool segmented)
        {
            IEncodeable encodeable = FuzzBinaryDecoderCore(stream);
            if (encodeable != null)
            {
                byte[] serialized = BinaryEncoder.EncodeMessage(encodeable, MessageContext);
                if (idempotent)
                {
                    FuzzBinaryEncoderIndempotentCore(serialized, encodeable, segmented);
                }
            }
        }
    }
}
