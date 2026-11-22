// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.TestFramework
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
    public static class SessionPublishHelper
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
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
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
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
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
