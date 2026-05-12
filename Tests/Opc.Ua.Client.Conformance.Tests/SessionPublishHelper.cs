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

using System;
using System.Threading;
using System.Threading.Tasks;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// Extension helpers for ISession used by the conformance test suite.
    /// </summary>
    /// <remarks>
    /// The bare <c>Session.PublishAsync(null, default, CancellationToken.None)</c>
    /// pattern that many tests use waits client-side via
    /// <c>operation.EndAsync(int.MaxValue, true, ct)</c> in
    /// UaSCBinaryClientChannel — the request <c>TimeoutHint</c> is only sent
    /// to the server as advisory; the client itself does NOT bound the wait
    /// unless the caller supplies a cancellation token. On slow CI runners
    /// this regularly produced 2+ minute hangs that poisoned the shared
    /// session and cascaded into many follow-on test failures.
    /// <para>
    /// <see cref="PublishWithTimeoutAsync"/> wraps the call with a short
    /// CancellationTokenSource so each Publish is bounded client-side.
    /// A cancellation is reported as <see cref="ServiceResultException"/>
    /// with <see cref="StatusCodes.BadRequestTimeout"/>, which is the same
    /// status the underlying channel would eventually surface.
    /// </para>
    /// </remarks>
    internal static class SessionPublishHelper
    {
        /// <summary>
        /// Default per-call Publish timeout for conformance tests (15 s).
        /// Generous enough for slow CI runners but short enough that a
        /// hung server fails fast instead of hanging the test fixture.
        /// </summary>
        public const int DefaultPublishTimeoutMs = 15_000;

        /// <summary>
        /// Issues a <see cref="ISession.PublishAsync"/> with a bounded
        /// client-side timeout. Equivalent to
        /// <c>session.PublishAsync(null, default, CancellationToken.None)</c>
        /// except that the call is guaranteed to return (or throw) within
        /// <paramref name="timeoutMs"/> milliseconds rather than waiting
        /// indefinitely on a slow/hung server.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadRequestTimeout"/> when the timeout
        /// elapses before the server responds, otherwise propagates the
        /// underlying service result exception.
        /// </exception>
        public static async Task<PublishResponse> PublishWithTimeoutAsync(
            this ISession session,
            int timeoutMs = DefaultPublishTimeoutMs)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            using var cts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(timeoutMs));
            try
            {
                return await session
                    .PublishAsync(null, default, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestTimeout,
                    $"Conformance test Publish exceeded {timeoutMs} ms.");
            }
        }

        /// <summary>
        /// Issues a <see cref="ISession.PublishAsync"/> with a bounded
        /// client-side timeout AND a non-empty subscription-acknowledgement
        /// list. Same hang protection as <see cref="PublishWithTimeoutAsync"/>.
        /// </summary>
        public static async Task<PublishResponse> PublishWithTimeoutAsync(
            this ISession session,
            ArrayOf<SubscriptionAcknowledgement> acks,
            int timeoutMs = DefaultPublishTimeoutMs)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            using var cts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(timeoutMs));
            try
            {
                return await session
                    .PublishAsync(null, acks, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestTimeout,
                    $"Conformance test Publish exceeded {timeoutMs} ms.");
            }
        }
    }
}
