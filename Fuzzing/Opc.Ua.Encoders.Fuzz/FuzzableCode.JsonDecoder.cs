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
using System.Text;

namespace Opc.Ua.Fuzzing
{
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
            IEncodeable encodeable;
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
                using var encoder = new JsonEncoder(MessageContext, JsonEncoderOptions.Verbose);
                encoder.EncodeMessage(encodeable, encodeable.TypeId);
                encoder.Close();
            }
        }

        /// <summary>
        /// The compact Json encoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonEncoderCompact(string input)
        {
            IEncodeable encodeable;
            try
            {
                encodeable = FuzzJsonDecoderCore(input);
            }
            catch
            {
                return;
            }

            if (encodeable != null)
            {
                using var encoder = new JsonEncoder(MessageContext, JsonEncoderOptions.Compact);
                encoder.EncodeMessage(encodeable, encodeable.TypeId);
                encoder.Close();
            }
        }

        /// <summary>
        /// The Json encoder idempotent fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonEncoderIndempotent(string input)
        {
            IEncodeable encodeable;
            string serialized;
            try
            {
                encodeable = FuzzJsonDecoderCore(input, true);
                serialized = EncodeJsonMessage(encodeable, JsonEncoderOptions.Verbose);
            }
            catch
            {
                return;
            }

            FuzzJsonEncoderIndempotentCore(serialized, encodeable);
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
                using var encoder = new JsonEncoder(MessageContext, JsonEncoderOptions.Verbose);
                encoder.EncodeMessage(encodeable, encodeable.TypeId);
                encoder.Close();
            }
        }

        /// <summary>
        /// The compact Json encoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonEncoderCompact(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            AflfuzzJsonEncoderCompact(json);
        }

        /// <summary>
        /// The Json encoder idempotent fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonEncoderIndempotent(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            AflfuzzJsonEncoderIndempotent(json);
        }

        /// <summary>
        /// The fuzz target for the JsonDecoder.
        /// </summary>
        /// <param name="json">A string with fuzz content.</param>
        internal static IEncodeable FuzzJsonDecoderCore(string json, bool throwAll = false)
        {
            try
            {
                using var decoder = new JsonDecoder(json, MessageContext);
                return decoder.DecodeMessage<IEncodeable>();
            }
            catch (ServiceResultException sre)
            {
                if (!throwAll &&
                    (sre.StatusCode == StatusCodes.BadDecodingError ||
                        sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded))
                {
                    return null;
                }
                throw;
            }
        }

        /// <summary>
        /// The idempotent fuzz target core for the JsonEncoder.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void FuzzJsonEncoderIndempotentCore(
            string serialized,
            IEncodeable encodeable)
        {
            if (serialized == null || encodeable == null)
            {
                return;
            }

            IEncodeable encodeable2 = FuzzJsonDecoderCore(serialized, true);
            string serialized2 = EncodeJsonMessage(encodeable2, JsonEncoderOptions.Verbose);
            IEncodeable encodeable3 = FuzzJsonDecoderCore(serialized2, true);

            string encodeableTypeName = encodeable2?.GetType().Name ?? "unknown type";
            if (serialized2 == null || !serialized.SequenceEqual(serialized2))
            {
                throw new InvalidOperationException(
                    Utils.Format("Idempotent JSON encoding failed. Type={0}.", encodeableTypeName));
            }

            if (!Utils.IsEqual(encodeable2, encodeable3))
            {
                throw new InvalidOperationException(Utils.Format(
                    "Idempotent JSON 3rd gen decoding failed. Type={0}.",
                    encodeableTypeName));
            }
        }

        private static string EncodeJsonMessage(IEncodeable encodeable, JsonEncoderOptions options)
        {
            using var memoryStream = new MemoryStream(0x1000);
            using var encoder = new JsonEncoder(memoryStream, MessageContext, options);
            encoder.EncodeMessage(encodeable, encodeable.TypeId);
            encoder.Close();
            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }
    }
}
