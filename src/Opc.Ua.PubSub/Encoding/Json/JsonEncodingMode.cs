// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Encoding-mode selector for the JSON NetworkMessage / DataSet
    /// message family. Each value names a JSON encoding profile defined
    /// in OPC UA Part 6 §5.4.1 and used by the PubSub JSON mapping in
    /// Part 14 §7.2.5 (v1.05.06).
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see>. The three values correspond 1:1 to
    /// <see cref="JsonEncoderOptions.Verbose"/>,
    /// <see cref="JsonEncoderOptions.Compact"/>, and
    /// <see cref="JsonEncoderOptions.RawData"/> from the Stack.
    /// The 1.04-era <c>Reversible</c> / <c>NonReversible</c> names are
    /// removed; <c>Verbose</c> replaces the former <c>Reversible</c> and
    /// <c>Compact</c> replaces the former <c>NonReversible</c>.
    /// </remarks>
    public enum JsonEncodingMode
    {
        /// <summary>
        /// Verbose JSON per Part 6 §5.4.1. Variants emit the Part 6
        /// §5.4.2.17 <c>{ "UaType", "Value" }</c> envelope so decoders can
        /// recover the originating Built-In type without consulting
        /// DataSetMetaData; top-level Variants whose FieldMetaData supplies
        /// a concrete DataType are collapsed to the bare value (Part 14
        /// §7.2.5.4.2).
        /// </summary>
        Verbose = 0,

        /// <summary>
        /// Compact JSON per Part 6 §5.4.1. Suppresses default values
        /// and optional fields; the decoder requires DataSetMetaData
        /// to rehydrate field types.
        /// </summary>
        Compact = 1,

        /// <summary>
        /// RawData JSON per Part 6 §5.4.1. Variants emit the bare body
        /// without the <c>{ "UaType", "Value" }</c> envelope; the decoder
        /// requires DataSetMetaData and cannot recover OPC UA type
        /// fidelity from the body alone.
        /// </summary>
        RawData = 2
    }
}
