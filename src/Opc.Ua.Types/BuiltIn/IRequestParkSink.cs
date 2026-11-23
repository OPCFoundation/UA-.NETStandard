// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// This is a cooperative terminal waiting point, not forced preemption: finish
    /// execution-intensive work before signaling and do not resume unbounded CPU work
    /// afterwards. Bounded completion and response delivery may continue. Cancellation
    /// or shutdown does not release retained-request accounting until the handler actually
    /// finishes; the handler must still observe its request lifetime.
    /// </remarks>
    public interface IRequestParkSink
    {
        /// <summary>
        /// Signals that the request has parked and its processing worker may be
        /// released. Idempotent: only the first invocation has an effect.
        /// The request must remain pending until its actual completion.
        /// </summary>
        void NotifyParked();
    }
}
