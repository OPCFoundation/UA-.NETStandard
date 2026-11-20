// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Integrates generation-bound borrowed clients with managed service dispatch.
    /// </summary>
    public partial class ManagedSession
    {
        /// <summary>
        /// Captures the real session only after entering the same gate as ordinary service requests.
        /// </summary>
        internal ValueTask<SessionRequestContext> CaptureRequestContextAsync(
            bool requireConfidentiality, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return CallServiceAsync(
                (owner: this, requireConfidentiality),
                static (session, args, _) => new ValueTask<SessionRequestContext>(
                    session.CreateRequestContext(args.owner, args.requireConfidentiality)),
                ct);
        }

        /// <summary>
        /// Rechecks the captured identity at dispatch, including after waiting behind recovery.
        /// </summary>
        internal ValueTask<IServiceResponse> SendRequestContextAsync(
            SessionRequestContext context, IServiceRequest request, CancellationToken ct)
        {
            return CallServiceAsync(
                (context, request),
                static (session, args, token) => session.SendRequestContextAsync(args.context, args.request, token),
                ct);
        }
    }
}
