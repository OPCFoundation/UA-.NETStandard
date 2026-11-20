// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Text.Json.Nodes;

namespace Opc.Ua.Mcp.Serialization
{
    /// <summary>
    /// Projects UA values through their existing encodeable contracts, not reflection or stringification.
    /// </summary>
    public static class McpCompanionJson
    {
        /// <summary>
        /// Encodes a structured value using the stack's generated encoder implementation.
        /// </summary>
        /// <typeparam name="T">The generated encodeable type.</typeparam>
        public static JsonNode? Encode<T>(T value, IServiceMessageContext context)
            where T : IEncodeable
        {
            ArgumentNullException.ThrowIfNull(context);
            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            encoder.WriteEncodeable("value", value, default);
            return ReadValue(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Preserves a Variant's type and value, including false, zero, arrays and structured bodies.
        /// </summary>
        public static JsonNode? Variant(in Variant value, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            encoder.WriteVariant("value", value);
            return ReadValue(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Preserves data quality and timestamps beside a value.
        /// </summary>
        public static JsonNode? DataValue(in DataValue value, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            encoder.WriteDataValue("value", value);
            return ReadValue(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Represents non-finite measurements explicitly as the UA JSON strings NaN or signed Infinity.
        /// </summary>
        public static JsonNode Number(double value)
        {
            if (double.IsNaN(value))
            {
                return JsonValue.Create("NaN");
            }
            if (double.IsPositiveInfinity(value))
            {
                return JsonValue.Create("Infinity");
            }
            if (double.IsNegativeInfinity(value))
            {
                return JsonValue.Create("-Infinity");
            }
            return JsonValue.Create(value);
        }

        /// <summary>
        /// Detaches the known value field from the encoder's object envelope.
        /// </summary>
        private static JsonNode? ReadValue(string json)
        {
            if (JsonNode.Parse(json) is not JsonObject document ||
                !document.TryGetPropertyValue("value", out JsonNode? value))
            {
                throw new InvalidOperationException("The UA JSON encoder did not emit the requested value.");
            }
            return value?.DeepClone();
        }
    }
}
