// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Linq;
using System.Xml;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for the Xml decoder and encoder.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// The Xml decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzXmlDecoder(Stream stream)
        {
            _ = FuzzXmlDecoderCore(stream);
        }

        /// <summary>
        /// The Xml encoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzXmlEncoder(Stream stream)
        {
            IEncodeable? encodeable = FuzzXmlDecoderCore(stream);
            if (encodeable != null)
            {
                using var encoder = new XmlEncoder(MessageContext);
                encoder.EncodeMessage(encodeable, encodeable.TypeId);
                encoder.Close();
            }
        }

        /// <summary>
        /// The Xml encoder idempotent fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzXmlEncoderIndempotent(Stream stream)
        {
            IEncodeable? encodeable = FuzzXmlDecoderCore(stream);
            if (encodeable != null)
            {
                string serialized = EncodeXmlMessage(encodeable);
                FuzzXmlEncoderIndempotentCore(serialized, encodeable);
            }
        }

        /// <summary>
        /// The Xml decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzXmlDecoder(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            _ = FuzzXmlDecoderCore(memoryStream);
        }

        /// <summary>
        /// The Xml encoder fuzz target for afl-fuzz.
        /// </summary>
        public static void LibfuzzXmlEncoder(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            AflfuzzXmlEncoder(memoryStream);
        }

        /// <summary>
        /// The Xml encoder idempotent fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzXmlEncoderIndempotent(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            AflfuzzXmlEncoderIndempotent(memoryStream);
        }

        /// <summary>
        /// The idempotent fuzz target core for the XmlEncoder.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void FuzzXmlEncoderIndempotentCore(
            string serialized,
            IEncodeable encodeable)
        {
            if (serialized == null || encodeable == null)
            {
                return;
            }

            using var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(serialized));
            IEncodeable encodeable2 = FuzzXmlDecoderCore(memoryStream, true)!;
            string serialized2 = EncodeXmlMessage(encodeable2);
            using var memoryStream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(serialized2));
            IEncodeable encodeable3 = FuzzXmlDecoderCore(memoryStream2, true)!;

            string encodeableTypeName = encodeable2?.GetType().Name ?? "unknown type";
            if (serialized2 == null || !serialized.SequenceEqual(serialized2))
            {
                throw new EncodingFidelityException(
                    Utils.Format("Idempotent XML encoding failed. Type={0}.", encodeableTypeName));
            }

            if (!Utils.IsEqual(encodeable2, encodeable3))
            {
                throw new EncodingFidelityException(Utils.Format(
                    "Idempotent XML 3rd gen decoding failed. Type={0}.",
                    encodeableTypeName));
            }
        }

        private static string EncodeXmlMessage(IEncodeable encodeable)
        {
            using var encoder = new XmlEncoder(MessageContext);
            encoder.EncodeMessage(encodeable, encodeable.TypeId);
            return encoder.CloseAndReturnText() ?? string.Empty;
        }

        /// <summary>
        /// The fuzz target for the XmlDecoder.
        /// </summary>
        /// <param name="stream">A stream with fuzz content.</param>
        internal static IEncodeable? FuzzXmlDecoderCore(Stream stream, bool throwAll = false)
        {
            try
            {
                return DecodeWithOracles(nameof(XmlDecoder), GetRemainingLength(stream), () =>
                {
                    using XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                    reader.MoveToContent();
                    using var decoder = new XmlDecoder(reader, MessageContext);
                    return decoder.DecodeMessage<IEncodeable>();
                });
            }
            catch (XmlException exception)
            {
                if (!throwAll)
                {
                    return null;
                }
                throw new ServiceResultException(StatusCodes.BadDecodingError, exception.Message, exception);
            }
            catch (ServiceResultException exception) when (!throwAll && IsExpectedDecodingError(exception))
            {
                return null;
            }
        }
    }
}
