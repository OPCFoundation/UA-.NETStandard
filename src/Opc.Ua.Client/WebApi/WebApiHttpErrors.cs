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
using System.Net;
using System.Net.Http;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// Translates the HTTP failures of a REST call into the
    /// <see cref="ServiceResultException"/> an OPC UA client handles, so
    /// callers of <see cref="WebApiClient"/> see StatusCodes rather than
    /// the exceptions of <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// OPC UA errors of a processed request travel in the response body
    /// (<c>ResponseHeader.ServiceResult</c>, HTTP 200). An HTTP error status
    /// means the request did not reach the service: the server refused it,
    /// the route does not exist, the body was rejected, or the server is
    /// unavailable. The HTTP status and reason stay in the message, and an
    /// <see cref="HttpRequestException"/> naming the status is the inner
    /// exception.
    /// </remarks>
    internal static class WebApiHttpErrors
    {
        /// <summary>
        /// The StatusCode for an HTTP error status. Throttling (429/503) is
        /// handled before, with the Retry-After hint.
        /// </summary>
        /// <param name="status">The HTTP status of the response.</param>
        /// <param name="credentialsSent">
        /// Whether the request carried credentials (a bearer token or basic
        /// credentials); a 401 then means they were refused rather than
        /// missing.
        /// </param>
        public static StatusCode ToStatusCode(HttpStatusCode status, bool credentialsSent)
        {
            return (int)status switch
            {
                400 => StatusCodes.BadDecodingError,
                401 => credentialsSent ? StatusCodes.BadUserAccessDenied : StatusCodes.BadIdentityTokenInvalid,
                403 => StatusCodes.BadUserAccessDenied,
                404 or 405 or 501 => StatusCodes.BadServiceUnsupported,
                408 or 504 => StatusCodes.BadTimeout,
                413 => StatusCodes.BadRequestTooLarge,
                415 => StatusCodes.BadDataEncodingUnsupported,
                500 => StatusCodes.BadInternalError,
                _ => StatusCodes.BadCommunicationError
            };
        }

        /// <summary>
        /// The exception for a response with an HTTP error status.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="path">The route that was called.</param>
        /// <param name="credentialsSent">Whether the request carried credentials.</param>
        public static ServiceResultException FromResponse(
            HttpResponseMessage response,
            string path,
            bool credentialsSent)
        {
            HttpStatusCode status = response.StatusCode;
            string message = Utils.Format(
                "HTTP {0} ({1}) returned by POST {2}.",
                (int)status,
                response.ReasonPhrase ?? status.ToString(),
                path);
#if NET5_0_OR_GREATER
            var inner = new HttpRequestException(message, null, status);
#else
            var inner = new HttpRequestException(message);
#endif
            return ServiceResultException.Create(
                ToStatusCode(status, credentialsSent),
                inner,
                "The REST request failed: {0}",
                message);
        }

        /// <summary>
        /// The exception for a request whose timeout elapsed; the transport
        /// channels report the same code.
        /// </summary>
        /// <param name="exception">The cancellation the timeout caused.</param>
        /// <param name="path">The route that was called.</param>
        /// <param name="timeout">The timeout that elapsed.</param>
        public static ServiceResultException FromTimeout(Exception exception, string path, TimeSpan timeout)
        {
            return ServiceResultException.Create(
                StatusCodes.BadRequestTimeout,
                exception,
                "The REST request POST {0} did not complete within {1}.",
                path,
                timeout);
        }

        /// <summary>
        /// The exception for a request that failed below HTTP, for example
        /// without a connection or with a failed TLS handshake. The
        /// StatusCode is the one <see cref="HttpsTransportChannel"/> reports
        /// for the same failure.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <param name="path">The route that was called.</param>
        public static ServiceResultException FromRequestFailure(HttpRequestException exception, string path)
        {
            return ServiceResultException.Create(
                HttpsTransportChannel.MapRequestFailure(exception),
                exception,
                "The REST request POST {0} failed: {1}",
                path,
                exception.InnerException?.Message ?? exception.Message);
        }
    }
}
