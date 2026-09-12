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
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    internal static class HttpResponseBodyReader
    {
        public static async ValueTask<byte[]> ReadAsync(
            HttpContent content,
            int maxMessageSize,
            CancellationToken cancellationToken)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (maxMessageSize > 0 && content.Headers.ContentLength > maxMessageSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResponseTooLarge,
                    "Response body exceeds the configured MaxMessageSize ({0} bytes).",
                    maxMessageSize);
            }

#if NET5_0_OR_GREATER
            Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            try
            {
                // A Content-Length hint cannot truncate verification of the actual body.
                return await WebApiBodyCodec.ReadAllBoundedAsync(
                    stream, maxMessageSize, -1, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadRequestTooLarge)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResponseTooLarge,
                    exception,
                    "Response body exceeds the configured MaxMessageSize ({0} bytes).",
                    maxMessageSize);
            }
        }
    }
}
