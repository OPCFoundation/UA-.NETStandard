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
#if NET8_0_OR_GREATER
using System.Net;
#endif
using System.Net.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// Reports a failed xRegistry HTTP inspection and optionally retains the backend rejection.
    /// </summary>
    /// <remarks>
    /// A rejected inspection must not be interpreted as an empty registry or an unextended server.
    /// When supplied, <see cref="Response"/> preserves the complete rejection, including Problem Details
    /// or opaque error bytes.
    /// </remarks>
    public sealed class XRegistryHttpException : HttpRequestException
    {
        /// <summary>
        /// Initializes an inspection failure with the default message and no retained backend response.
        /// </summary>
        public XRegistryHttpException()
        {
        }

        /// <summary>
        /// Initializes an inspection failure with a message and no retained backend response.
        /// </summary>
        /// <param name="message">The description of the inspection failure.</param>
        public XRegistryHttpException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes an inspection failure with a message and underlying cause, but no retained backend response.
        /// </summary>
        /// <param name="message">The description of the inspection failure.</param>
        /// <param name="innerException">The exception that caused inspection to fail.</param>
        public XRegistryHttpException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes an inspection failure while retaining the complete backend rejection.
        /// </summary>
        /// <param name="message">The description of the inspection failure.</param>
        /// <param name="response">
        /// The backend response, including its status and any Problem Details or opaque error body.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="response"/> is <see langword="null"/>.
        /// </exception>
        public XRegistryHttpException(string message, XRegistryResponse response)
            : base(message)
        {
            response.ThrowIfNull(nameof(response));
            Response = response;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Initializes an inspection transport failure with an HTTP error classification
        /// and optional status, but no retained registry response.
        /// </summary>
        /// <param name="httpRequestError">The underlying HTTP transport error classification.</param>
        /// <param name="message">An optional description of the failure.</param>
        /// <param name="inner">The optional exception that caused the failure.</param>
        /// <param name="statusCode">The optional HTTP status associated with the failure.</param>
        public XRegistryHttpException(
            HttpRequestError httpRequestError,
            string? message = null,
            Exception? inner = null,
            HttpStatusCode? statusCode = null)
            : base(httpRequestError, message, inner, statusCode)
        {
        }

        /// <summary>
        /// Initializes an inspection transport failure with an optional HTTP status
        /// and underlying cause, but no retained registry response.
        /// </summary>
        /// <param name="message">An optional description of the failure.</param>
        /// <param name="inner">The optional exception that caused the failure.</param>
        /// <param name="statusCode">The optional HTTP status associated with the failure.</param>
        public XRegistryHttpException(string? message, Exception? inner, HttpStatusCode? statusCode)
            : base(message, inner, statusCode)
        {
        }
#endif

        /// <summary>
        /// Gets the complete backend rejection supplied to the response-taking constructor,
        /// or <see langword="null"/> when the exception was constructed without a response.
        /// </summary>
        public XRegistryResponse? Response { get; }
    }
}
