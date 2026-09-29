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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// The operation class checked by a registry access policy.
    /// </summary>
    public enum RegistryAccessKind
    {
        /// <summary>
        /// A native read or snapshot.
        /// </summary>
        Read = 0,

        /// <summary>
        /// A metadata mutation.
        /// </summary>
        Write = 1
    }

    /// <summary>
    /// The default registry access policy and the caller identities used to bind native state.
    /// </summary>
    public static class RegistryAccessPolicy
    {
        /// <summary>
        /// Allows reads from any Session and requires a SignAndEncrypt channel and the
        /// ConfigureAdmin or SecurityAdmin role for mutations. In-process calls without a
        /// Session are trusted.
        /// </summary>
        public static ServiceResult Default(ISystemContext context, RegistryAccessKind kind)
        {
            if (kind == RegistryAccessKind.Read || context is not SessionSystemContext session)
            {
                return ServiceResult.Good;
            }
            if (SecurityMode(session) != MessageSecurityMode.SignAndEncrypt)
            {
                return StatusCodes.BadSecurityModeInsufficient;
            }
            ArrayOf<NodeId> roles = session.UserIdentity?.GrantedRoleIds ?? default;
            return roles.Contains(Ua.ObjectIds.WellKnownRole_ConfigureAdmin) ||
                roles.Contains(Ua.ObjectIds.WellKnownRole_SecurityAdmin)
                ? ServiceResult.Good
                : StatusCodes.BadUserAccessDenied;
        }

        /// <summary>
        /// Returns a server-owned identity of the caller's effective access: user, granted roles
        /// and channel security. Native continuation points and snapshots are bound to it.
        /// </summary>
        public static string AuthorizationView(ISystemContext context)
        {
            if (context is not SessionSystemContext session)
            {
                return "in-process";
            }
            var view = new StringBuilder();
            view.Append(session.UserIdentity?.TokenType.ToString() ?? "none").Append('|')
                .Append(session.UserIdentity?.DisplayName ?? string.Empty).Append('|')
                .Append(SecurityMode(session)?.ToString() ?? string.Empty);
            ArrayOf<NodeId> roles = session.UserIdentity?.GrantedRoleIds ?? default;
            var sorted = new string[roles.Count];
            for (int index = 0; index < sorted.Length; index++)
            {
                sorted[index] = roles[index].ToString();
            }
            Array.Sort(sorted, StringComparer.Ordinal);
            foreach (string role in sorted)
            {
                view.Append('|').Append(role);
            }
            return view.ToString();
        }

        private static MessageSecurityMode? SecurityMode(SessionSystemContext session)
        {
            return session.OperationContext is OperationContext { ChannelContext.EndpointDescription: { } endpoint }
                ? endpoint.SecurityMode
                : null;
        }

        /// <summary>
        /// Returns the Session and authorization view of a call.
        /// </summary>
        public static RegistryCaller Caller(ISystemContext context)
        {
            string session = context is ISessionSystemContext { SessionId: { IsNull: false } id }
                ? id.ToString()
                : string.Empty;
            return new RegistryCaller(session, AuthorizationView(context));
        }
    }

    /// <summary>
    /// Binds the generated TypedAccess Methods of one registry instance to a
    /// <see cref="RegistryNativeHost"/>: ReadDocument, WriteDocument, ApplyChanges and the
    /// native snapshot Methods. Disposing unbinds them.
    /// </summary>
    /// <remarks>
    /// Channel security and access failures fail the Method call; domain failures are returned as
    /// typed diagnostics in the result Structures.
    /// </remarks>
    public sealed class RegistryNativeAccessBinding : IDisposable
    {
        /// <summary>
        /// Installs the handlers on <paramref name="access"/>, adding the optional Methods.
        /// </summary>
        public RegistryNativeAccessBinding(
            NativeRegistryAccessState access,
            ISystemContext context,
            RegistryNativeHost host,
            RegistryNativeSnapshots snapshots,
            Func<ISystemContext, RegistryAccessKind, ServiceResult>? authorize = null)
        {
            if (access is null)
            {
                throw new ArgumentNullException(nameof(access));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            if (snapshots is null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }
            Func<ISystemContext, RegistryAccessKind, ServiceResult> policy = authorize ?? RegistryAccessPolicy.Default;
            access.AddReadDocument(context).AddWriteDocument(context).AddApplyChanges(context);
            var oldRead = access.ReadDocument!.OnCallAsync;
            var oldWrite = access.WriteDocument!.OnCallAsync;
            var oldChange = access.ApplyChanges!.OnCallAsync;
            access.ReadDocument.OnCallAsync = (caller, _, _, request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult allowed = policy(caller, RegistryAccessKind.Read);
                return new ValueTask<ReadDocumentMethodStateResult>(ServiceResult.IsBad(allowed)
                    ? new ReadDocumentMethodStateResult { ServiceResult = allowed }
                    : new ReadDocumentMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Result = host.Read(RegistryAccessPolicy.Caller(caller), request)
                    });
            };
            access.WriteDocument.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                ServiceResult allowed = policy(caller, RegistryAccessKind.Write);
                if (ServiceResult.IsBad(allowed))
                {
                    return new WriteDocumentMethodStateResult { ServiceResult = allowed };
                }
                return new WriteDocumentMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Result = await host.WriteAsync(request, ct).ConfigureAwait(false)
                };
            };
            access.ApplyChanges.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                ServiceResult allowed = policy(caller, RegistryAccessKind.Write);
                if (ServiceResult.IsBad(allowed))
                {
                    return new ApplyChangesMethodStateResult { ServiceResult = allowed };
                }
                return new ApplyChangesMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Result = await host.ApplyChangesAsync(request, ct).ConfigureAwait(false)
                };
            };
            m_snapshots = new RegistrySnapshotBinding(access, context, snapshots,
                RegistryAccessPolicy.AuthorizationView,
                (caller, request, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    ServiceResult allowed = policy(caller, RegistryAccessKind.Read);
                    if (ServiceResult.IsBad(allowed))
                    {
                        throw new ServiceResultException(allowed);
                    }
                    return new ValueTask<RegistrySnapshotSource>(host.SelectSnapshot(request));
                });
            m_restore = () =>
            {
                access.ReadDocument.OnCallAsync = oldRead;
                access.WriteDocument.OnCallAsync = oldWrite;
                access.ApplyChanges.OnCallAsync = oldChange;
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_restore, null) is Action restore)
            {
                m_snapshots.Dispose();
                restore();
            }
        }

        private readonly RegistrySnapshotBinding m_snapshots;
        private Action? m_restore;
    }
}
