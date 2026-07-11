/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
        /// Verbose JSON per Part 6 §5.4.1. Variants emit the
        /// <c>{ "Type", "Body" }</c> envelope so decoders can recover
        /// the originating Built-In type without consulting
        /// DataSetMetaData.
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
        /// without the <c>{ "Type", "Body" }</c> envelope; the decoder
        /// requires DataSetMetaData and cannot recover OPC UA type
        /// fidelity from the body alone.
        /// </summary>
        RawData = 2
    }
}
