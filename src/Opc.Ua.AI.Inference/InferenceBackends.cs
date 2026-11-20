/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// The backends behind the deployments this Server publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two deployments mean two backends, and that is the whole point rather than
    /// an implementation detail. A fallback reached through the same client, the
    /// same connection and the same credentials as the primary is not a fallback -
    /// it is a retry, and it fails for every reason the primary just failed for.
    /// </para>
    /// <para>
    /// Keeping them as separate instances is what makes it possible to say, and to
    /// test, that the two are independently reachable.
    /// </para>
    /// </remarks>
    public sealed class InferenceBackends
    {
        /// <summary>
        /// Creates the set.
        /// </summary>
        /// <param name="primary">Backend behind the primary deployment.</param>
        /// <param name="fallback">
        /// Backend behind the fallback deployment, or <c>null</c> when this Server
        /// publishes no fallback.
        /// </param>
        public InferenceBackends(IInferenceBackend primary, IInferenceBackend? fallback = null)
        {
            Primary = primary ?? throw new ArgumentNullException(nameof(primary));
            Fallback = fallback;
        }

        /// <summary>
        /// Backend behind the primary deployment.
        /// </summary>
        public IInferenceBackend Primary { get; }

        /// <summary>
        /// Backend behind the fallback deployment, if there is one.
        /// </summary>
        public IInferenceBackend? Fallback { get; }
    }
}
