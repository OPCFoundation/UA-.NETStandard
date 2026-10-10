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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// A property read with a range and data encoding in the caller's context.
    /// </summary>
    public sealed class WotReadRequest
    {
        /// <summary>
        /// Initializes a contextual property read.
        /// </summary>
        public WotReadRequest(
            IServiceMessageContext context,
            NumericRange indexRange = default,
            QualifiedName dataEncoding = default)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            IndexRange = indexRange;
            DataEncoding = dataEncoding;
        }

        /// <summary>
        /// Gets the caller's namespace and encoding context.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Gets the requested range, or a null range for the complete value.
        /// </summary>
        public NumericRange IndexRange { get; }

        /// <summary>
        /// Gets the requested data encoding in the caller's namespace table.
        /// </summary>
        public QualifiedName DataEncoding { get; }
    }

    /// <summary>
    /// A property write with its value context and native index range.
    /// </summary>
    public sealed class WotWriteRequest
    {
        /// <summary>
        /// Initializes a contextual property write.
        /// </summary>
        public WotWriteRequest(
            in DataValue value,
            IServiceMessageContext context,
            NumericRange indexRange = default)
        {
            Value = value;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            IndexRange = indexRange;
        }

        /// <summary>
        /// Gets the value to write, or the replacement slice for an indexed write.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// Gets the namespace and encoding context of the value.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Gets the range to update without replacing the complete upstream value.
        /// </summary>
        public NumericRange IndexRange { get; }
    }

    /// <summary>
    /// Optional channel capability for contextual property reads and writes.
    /// Implementations honor the requested range and data encoding, or reject
    /// unsupported requests before performing a write. Indexed writes must not
    /// be emulated with an unprotected read-modify-write.
    /// </summary>
    public interface IWotPropertyBindingChannel : IWotBindingChannel
    {
        /// <summary>
        /// Reads the requested value or slice with the requested data encoding.
        /// </summary>
        ValueTask<WotReadResult> ReadAsync(
            WotReadRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Translates and writes the value with the requested native index range.
        /// </summary>
        ValueTask<WotWriteResult> WriteAsync(
            WotWriteRequest request, CancellationToken cancellationToken = default);
    }
}
