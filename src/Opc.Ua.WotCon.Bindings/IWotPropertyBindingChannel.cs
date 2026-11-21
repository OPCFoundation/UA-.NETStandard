// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// <param name="context">The service message context used to initialize the instance.</param>
        /// <param name="indexRange">The index range used to initialize the instance.</param>
        /// <param name="dataEncoding">The data encoding used to initialize the instance.</param>
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
        /// <param name="value">The data value used to initialize the instance.</param>
        /// <param name="context">The service message context used to initialize the instance.</param>
        /// <param name="indexRange">The index range used to initialize the instance.</param>
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
        /// <param name="request">The WoT read request.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotReadResult> ReadAsync(
            WotReadRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Translates and writes the value with the requested native index range.
        /// </summary>
        /// <param name="request">The WoT write request.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotWriteResult> WriteAsync(
            WotWriteRequest request, CancellationToken cancellationToken = default);
    }
}
