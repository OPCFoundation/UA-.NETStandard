// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        /// <summary>
        /// Converts segmented binary input to verbose JSON.
        /// </summary>
        public static void AflfuzzBinaryJsonEncoder(Stream input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.Verbose);
        }

        /// <summary>
        /// Converts segmented binary input to compact JSON.
        /// </summary>
        public static void AflfuzzBinaryJsonEncoderCompact(Stream input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.Compact);
        }

        /// <summary>
        /// Converts segmented binary input to metadata-backed RawData JSON.
        /// </summary>
        public static void AflfuzzBinaryJsonEncoderRawData(Stream input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.RawData);
        }

        /// <summary>
        /// Converts binary input using the current mapping of legacy Reversible options.
        /// </summary>
        public static void AflfuzzBinaryJsonEncoderLegacyReversible(Stream input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryJsonEncoderCore(stream, LegacyReversibleOptions);
        }

        /// <summary>
        /// Converts binary input using the current mapping of legacy NonReversible options.
        /// </summary>
        public static void AflfuzzBinaryJsonEncoderLegacyNonReversible(Stream input)
        {
            using MemoryStream stream = PrepareArraySegmentStream(input);
            FuzzBinaryJsonEncoderCore(stream, LegacyNonReversibleOptions);
        }

        /// <summary>
        /// Converts contiguous binary input to verbose JSON.
        /// </summary>
        public static void LibfuzzBinaryJsonEncoder(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.Verbose);
        }

        /// <summary>
        /// Converts contiguous binary input to compact JSON.
        /// </summary>
        public static void LibfuzzBinaryJsonEncoderCompact(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.Compact);
        }

        /// <summary>
        /// Converts contiguous binary input to metadata-backed RawData JSON.
        /// </summary>
        public static void LibfuzzBinaryJsonEncoderRawData(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            FuzzBinaryJsonEncoderCore(stream, JsonEncoderOptions.RawData);
        }

        /// <summary>
        /// Converts binary input using the current mapping of legacy Reversible options.
        /// </summary>
        public static void LibfuzzBinaryJsonEncoderLegacyReversible(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            FuzzBinaryJsonEncoderCore(stream, LegacyReversibleOptions);
        }

        /// <summary>
        /// Converts binary input using the current mapping of legacy NonReversible options.
        /// </summary>
        public static void LibfuzzBinaryJsonEncoderLegacyNonReversible(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            FuzzBinaryJsonEncoderCore(stream, LegacyNonReversibleOptions);
        }

        /// <summary>
        /// Converts XML input to verbose JSON, independently of XML re-encoding.
        /// </summary>
        public static void AflfuzzXmlJsonEncoder(Stream input)
        {
            FuzzXmlJsonEncoderCore(input, JsonEncoderOptions.Verbose);
        }

        /// <summary>
        /// Converts XML input to compact JSON.
        /// </summary>
        public static void AflfuzzXmlJsonEncoderCompact(Stream input)
        {
            FuzzXmlJsonEncoderCore(input, JsonEncoderOptions.Compact);
        }

        /// <summary>
        /// Converts XML input to metadata-backed RawData JSON.
        /// </summary>
        public static void AflfuzzXmlJsonEncoderRawData(Stream input)
        {
            FuzzXmlJsonEncoderCore(input, JsonEncoderOptions.RawData);
        }

        /// <summary>
        /// Converts XML input using the current mapping of legacy Reversible options.
        /// </summary>
        public static void AflfuzzXmlJsonEncoderLegacyReversible(Stream input)
        {
            FuzzXmlJsonEncoderCore(input, LegacyReversibleOptions);
        }

        /// <summary>
        /// Converts XML input using the current mapping of legacy NonReversible options.
        /// </summary>
        public static void AflfuzzXmlJsonEncoderLegacyNonReversible(Stream input)
        {
            FuzzXmlJsonEncoderCore(input, LegacyNonReversibleOptions);
        }

        /// <summary>
        /// Converts XML input to verbose JSON.
        /// </summary>
        public static void LibfuzzXmlJsonEncoder(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            AflfuzzXmlJsonEncoder(stream);
        }

        /// <summary>
        /// Converts XML input to compact JSON.
        /// </summary>
        public static void LibfuzzXmlJsonEncoderCompact(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            AflfuzzXmlJsonEncoderCompact(stream);
        }

        /// <summary>
        /// Converts XML input to metadata-backed RawData JSON.
        /// </summary>
        public static void LibfuzzXmlJsonEncoderRawData(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            AflfuzzXmlJsonEncoderRawData(stream);
        }

        /// <summary>
        /// Converts XML input using the current mapping of legacy Reversible options.
        /// </summary>
        public static void LibfuzzXmlJsonEncoderLegacyReversible(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            AflfuzzXmlJsonEncoderLegacyReversible(stream);
        }

        /// <summary>
        /// Converts XML input using the current mapping of legacy NonReversible options.
        /// </summary>
        public static void LibfuzzXmlJsonEncoderLegacyNonReversible(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            AflfuzzXmlJsonEncoderLegacyNonReversible(stream);
        }

        private static void FuzzBinaryJsonEncoderCore(MemoryStream stream, JsonEncoderOptions options)
        {
            IEncodeable? encodeable = FuzzBinaryDecoderCore(stream);
            if (encodeable != null)
            {
                FuzzJsonRoundTripCore(encodeable, options);
            }
        }

        private static void FuzzXmlJsonEncoderCore(Stream stream, JsonEncoderOptions options)
        {
            IEncodeable? encodeable = FuzzXmlDecoderCore(stream);
            if (encodeable != null)
            {
                FuzzJsonRoundTripCore(encodeable, options);
            }
        }
    }
}
