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
#if NET8_0_OR_GREATER
namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Selects how the Arrow encoder frames a NetworkMessage payload. The column and value
    /// layout (the canonical Part 6 Arrow mapping) is identical for both members; only the
    /// transport framing differs, so this is not an encoding variant.
    /// </summary>
    public enum ArrowIpcFraming
    {
        /// <summary>
        /// A self-contained Arrow IPC stream that embeds the Arrow Schema message before the
        /// RecordBatch. Every payload can be decoded on its own but repeats the schema.
        /// </summary>
        Stream,

        /// <summary>
        /// A bare Arrow RecordBatch message without an embedded Schema message. The schema is
        /// announced once out-of-band and resolved by SchemaId, which removes the per-message
        /// schema overhead. Best for small or single-sample messages on schema-governed channels.
        /// </summary>
        Batch
    }
}
#endif
