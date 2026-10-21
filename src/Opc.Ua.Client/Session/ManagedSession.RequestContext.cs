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
            bool requireConfidentiality, CancellationToken ct, bool allowSameSessionRecovery = false)
        {
            ct.ThrowIfCancellationRequested();
            return CallServiceAsync(
                (owner: this, requireConfidentiality, allowSameSessionRecovery),
                static (session, args, _) => new ValueTask<SessionRequestContext>(
                    session.CreateRequestContext(
                        args.owner, args.requireConfidentiality, args.allowSameSessionRecovery)),
                ct, recoveryControl: true);
        }

        /// <summary>
        /// Rechecks the captured identity at dispatch, including after waiting behind recovery.
        /// </summary>
        internal ValueTask<IServiceResponse> SendRequestContextAsync(
            SessionRequestContext context, IServiceRequest request, CancellationToken ct)
        {
            if (context.AllowsSameSessionRecovery)
            {
                return SendRecoverableRequestContextAsync(context, request, ct);
            }
            return CallServiceAsync(
                (context, request),
                static (session, args, token) => session.SendRequestContextAsync(args.context, args.request, token),
                ct, recoveryControl: true);
        }

        /// <summary>
        /// Waits outside the service gate so channel recovery can acquire its writer before one guarded send.
        /// </summary>
        private async ValueTask<IServiceResponse> SendRecoverableRequestContextAsync(
            SessionRequestContext context, IServiceRequest request, CancellationToken ct)
        {
            if (m_session?.ManagedChannel is ManagedTransportChannelLease lease)
            {
                await lease.Entry.WaitForReadyAsync(ct).ConfigureAwait(false);
            }
            return await CallServiceAsync(
                (context, request),
                static (session, args, token) => session.SendRequestContextAsync(args.context, args.request, token),
                ct, recoveryControl: true).ConfigureAwait(false);
        }
    }
}
