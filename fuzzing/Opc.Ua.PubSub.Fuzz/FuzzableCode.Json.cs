// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Opc.Ua.PubSub.Encoding;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzPubSubJsonDecode(Stream stream)
        {
            _ = DecodePubSubJson(ReadCapped(stream), NewContext());
        }

        public static void LibfuzzPubSubJsonDecode(ReadOnlySpan<byte> input)
        {
            _ = DecodePubSubJson(CopyCapped(input), NewContext());
        }

        internal static PubSubNetworkMessage DecodePubSubJson(
            ReadOnlyMemory<byte> input,
            PubSubNetworkMessageContext context)
        {
            return FuzzOracles.MeasureAllocation(
                "PubSubJsonDecoder",
                input.Length,
                () => PubSubJsonDecoder.DecodeCore(input, context))!;
        }
    }
}
