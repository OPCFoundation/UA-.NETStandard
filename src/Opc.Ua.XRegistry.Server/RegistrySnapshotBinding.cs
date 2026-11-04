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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// An authorized native document selected from one immutable registry generation.
    /// </summary>
    public readonly record struct RegistrySnapshotSource(IEncodeable Document, uint TargetEpoch, uint RegistryEpoch)
    {
        /// <summary>
        /// Gets an optional check of current access to the pinned selection before exposing a part.
        /// </summary>
        public Func<ServiceResult>? Reauthorize { get; init; }
    }

    /// <summary>
    /// Binds the generated snapshot Methods to a per-registry snapshot store and an authorized source.
    /// Disposing unbinds the Methods; the host still owns the store and Session cleanup.
    /// </summary>
    public sealed class RegistrySnapshotBinding : IDisposable
    {
        /// <summary>
        /// Installs native snapshot handlers without adding a separate selection or authorization policy.
        /// The source callback must return a document authorized for the calling Session.
        /// The view callback must return a server-owned identity that changes with the caller's effective access.
        /// </summary>
        public RegistrySnapshotBinding(
            NativeRegistryAccessState access,
            ISystemContext context,
            RegistryNativeSnapshots snapshots,
            Func<ISystemContext, string> authorizationView,
            Func<ISystemContext, RegistrySnapshotOpenRequestDataType, CancellationToken,
                ValueTask<RegistrySnapshotSource>> source)
        {
            if (access is null)
            {
                throw new ArgumentNullException(nameof(access));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (snapshots is null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }
            if (authorizationView is null)
            {
                throw new ArgumentNullException(nameof(authorizationView));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            access.AddOpenDocument(context).AddReadDocumentPart(context).AddCloseDocument(context)
                .AddSnapshotLimits(context);
            access.SnapshotLimits!.Value = snapshots.Limits;
            var oldOpen = access.OpenDocument!.OnCallAsync;
            var oldRead = access.ReadDocumentPart!.OnCallAsync;
            var oldClose = access.CloseDocument!.OnCallAsync;
            access.OpenDocument.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                NodeId session = Session(caller);
                string view = authorizationView(caller);
                RegistrySnapshotSource selected = await source(caller, request, ct).ConfigureAwait(false);
                if (!string.Equals(view, authorizationView(caller), StringComparison.Ordinal))
                {
                    return new OpenDocumentMethodStateResult { ServiceResult = StatusCodes.BadUserAccessDenied };
                }
                RegistrySnapshotOpenResultDataType result = snapshots.Open(session, view, request,
                    selected.Document, selected.TargetEpoch, selected.RegistryEpoch, selected.Reauthorize);
                return new OpenDocumentMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.StatusCode),
                    Result = result
                };
            };
            access.ReadDocumentPart.OnCallAsync = (caller, _, _, request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                RegistrySnapshotReadResultDataType result = snapshots.Read(
                    Session(caller), authorizationView(caller), request);
                return new ValueTask<ReadDocumentPartMethodStateResult>(new ReadDocumentPartMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.StatusCode),
                    Result = result
                });
            };
            access.CloseDocument.OnCallAsync = (caller, _, _, id, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                RegistrySnapshotCloseResultDataType result = snapshots.Close(
                    Session(caller), authorizationView(caller), id);
                return new ValueTask<CloseDocumentMethodStateResult>(new CloseDocumentMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.StatusCode),
                    Result = result
                });
            };
            m_restore = () =>
            {
                access.OpenDocument.OnCallAsync = oldOpen;
                access.ReadDocumentPart.OnCallAsync = oldRead;
                access.CloseDocument.OnCallAsync = oldClose;
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Interlocked.Exchange(ref m_restore, null)?.Invoke();
        }

        private static NodeId Session(ISystemContext context)
        {
            if (context is ISessionSystemContext { SessionId: { IsNull: false } sessionId })
            {
                return sessionId;
            }
            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
        }

        private Action? m_restore;
    }
}
