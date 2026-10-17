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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Holds an accepted original outcome independently of later acknowledgement failures or Server payload release.
    /// </summary>
    /// <typeparam name="TResult">The generated response or proxy's typed result.</typeparam>
    public sealed class AsyncServiceResult<TResult>
    {
        /// <summary>
        /// Captures the accepted outcome and any legitimate original or projected method failure.
        /// </summary>
        internal AsyncServiceResult(
            ServiceOperationInfoDataType operation,
            IServiceResponse? originalResponse,
            TResult value,
            ServiceResultException? failure)
        {
            m_operation = CoreUtils.Clone(operation)!;
            OriginalResponse = originalResponse;
            m_value = value;
            m_failure = failure != null ? ExceptionDispatchInfo.Capture(failure) : null;
        }

        /// <summary>
        /// Gets an independent copy of the metadata observed when this outcome was received.
        /// Remaining durations are not renewed by reading this property.
        /// </summary>
        public ServiceOperationInfoDataType Operation => CoreUtils.Clone(m_operation)!;

        /// <summary>
        /// Gets the complete original response, including its header, diagnostics and per-item results.
        /// Null denotes a terminal protocol outcome, not a fabricated original Service response.
        /// </summary>
        public IServiceResponse? OriginalResponse { get; }

        /// <summary>
        /// Gets whether the validated projection produced a value rather than an original/protocol failure.
        /// A response value can still contain ordinary per-item failures.
        /// </summary>
        public bool HasValue => m_failure == null;

        /// <summary>
        /// Returns the typed value, or throws the accepted original/protocol failure.
        /// ACK transport failures are never thrown by this method.
        /// </summary>
        public TResult GetValueOrThrow()
        {
            m_failure?.Throw();
            return m_value;
        }

        /// <summary>
        /// Reads a typed value without converting an accepted failure into a success.
        /// </summary>
        public bool TryGetValue([MaybeNullWhen(false)] out TResult value)
        {
            value = m_value;
            return HasValue;
        }

        private readonly ServiceOperationInfoDataType m_operation;
        private readonly TResult m_value;
        private readonly ExceptionDispatchInfo? m_failure;
    }
}
