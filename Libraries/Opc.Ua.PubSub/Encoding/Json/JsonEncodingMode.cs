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
    /// message family. Each value mirrors a Part 6 §5.4.1 JSON encoding
    /// profile mapped onto the Part 14 §7.2.5 wire shapes.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see> mode selector. Wraps the four Part 6 JSON
    /// profiles in the names used by the v1.5 publisher / subscriber
    /// API so existing call sites keep working.
    /// </remarks>
    public enum JsonEncodingMode
    {
        /// <summary>
        /// Reversible JSON per Part 6 §5.4.1. Every Variant is wrapped in
        /// the <c>{ "Type", "Body" }</c> envelope so the decoder can
        /// recover the originating Built-In type without metadata.
        /// </summary>
        Reversible = 0,

        /// <summary>
        /// Non-reversible JSON per Part 6 §5.4.1. Variants emit bare
        /// values; the decoder requires DataSetMetaData to rehydrate
        /// each field.
        /// </summary>
        NonReversible = 1,

        /// <summary>
        /// Compact JSON per Part 6 §5.4.1. Suppresses default values,
        /// optional fields and pretty-printing artifacts. Behaves like
        /// <see cref="NonReversible"/> for value bodies.
        /// </summary>
        Compact = 2,

        /// <summary>
        /// Verbose JSON per Part 6 §5.4.1. Emits every property
        /// (including defaults) plus the reversible Variant envelope to
        /// produce the most diagnosable wire form.
        /// </summary>
        Verbose = 3
    }
}
