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

namespace Opc.Ua
{
    /// <summary>
    /// Receives a one-shot notification when a long-running request parks, i.e.
    /// suspends waiting for an out-of-band completion (for example a held
    /// <c>Publish</c> waiting for subscription notifications).
    /// </summary>
    /// <remarks>
    /// A server request-processing worker normally remains associated with a
    /// request until it completes. For long-poll requests that would couple the
    /// worker/thread budget to the number of concurrently-parked requests. A
    /// request handler can call <see cref="NotifyParked"/> at the moment it
    /// parks so the worker is released to service other requests while the
    /// parked request completes independently.
    /// </remarks>
    public interface IRequestParkSink
    {
        /// <summary>
        /// Signals that the request has parked and its processing worker may be
        /// released. Idempotent: only the first invocation has an effect.
        /// </summary>
        void NotifyParked();
    }
}
