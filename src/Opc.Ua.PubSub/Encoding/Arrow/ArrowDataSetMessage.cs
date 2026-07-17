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
using Opc.Ua;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// PubSub DataSetMessage row in an Arrow IPC RecordBatch. The first
    /// adapter version supports RawData field columns for the built-in
    /// scalar and one-dimensional array types listed by the Part 14 Arrow
    /// draft; unsupported field built-in types fail with NotSupportedException.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public sealed record ArrowDataSetMessage : PubSubDataSetMessage
    {
        /// <summary>
        /// Gets the DataSet field-content bits emitted as Arrow RawData columns.
        /// </summary>
        public DataSetFieldContentMask FieldContentMask { get; init; }
            = DataSetFieldContentMask.RawData;
    }
}
#endif
