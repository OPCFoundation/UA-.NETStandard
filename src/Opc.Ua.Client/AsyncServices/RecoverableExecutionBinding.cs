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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Separates operation ownership from strict per-Session send-once contexts.
    /// Owner recovery may acquire a new context only through its original managed owner and verified continuity.
    /// </summary>
    internal sealed class RecoverableExecutionBinding : IAsyncDisposable
    {
        /// <summary>
        /// Captures the immutable operation interpretation and initial Session without relaxing its strict seam.
        /// </summary>
        private RecoverableExecutionBinding(
            ManagedSession owner, SessionRequestContext context, RecoverableSessionOptions options,
            RecoverableExecutionIdentity? identity, RecoverableExecutionTypeContext? types)
        {
            m_owner = owner;
            m_context = context;
            Options = options;
            Identity = identity;
            TypeContext = types;
            SessionId = context.SessionId;
            m_originalContext = context.Session.MessageContext;
            MessageContext = types?.MessageContext ?? m_originalContext;
            NamespaceUris = context.Session.NamespaceUris.ToArrayOf();
            ServerUris = context.Session.ServerUris.ToArrayOf();
        }

        /// <summary>
        /// Gets the explicitly selected operation policy.
        /// </summary>
        internal RecoverableSessionOptions Options { get; }

        /// <summary>
        /// Gets the original issuing Session ID, never the identity of an adopted replacement.
        /// </summary>
        internal NodeId SessionId { get; }

        /// <summary>
        /// Gets the current strict, non-owning Session facade.
        /// </summary>
        internal ISession Session => m_context.Session;

        /// <summary>
        /// Gets the captured type factory and decoding limits for the original request and response.
        /// </summary>
        internal IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets the original namespace interpretation.
        /// </summary>
        internal ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Gets the original Server URI interpretation.
        /// </summary>
        internal ArrayOf<string> ServerUris { get; }

        /// <summary>
        /// Gets the explicit authenticated owner mapping when selected or required for checkpoint storage.
        /// </summary>
        internal RecoverableExecutionIdentity? Identity { get; }

        /// <summary>
        /// Gets the pinned shared persistence interpretation when local checkpoint storage is selected.
        /// </summary>
        internal RecoverableExecutionTypeContext? TypeContext { get; }

        /// <summary>
        /// Gets terminal binding invalidation without treating a replaceable Owner-mode Session as the operation.
        /// </summary>
        internal Task Invalidated => Options.RecoveryMode == ServiceOperationRecoveryMode.Session
            ? m_context.Invalidated : m_invalidated.Task;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_invalidated.TrySetResult(true);
            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                m_capabilities?.Dispose();
                await m_context.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        /// <summary>
        /// Captures only a stack-managed owner and verifies any required authenticated continuity mapping.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<RecoverableExecutionBinding> CaptureAsync(
            ISession session, RecoverableSessionOptions options, CancellationToken ct)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            RecoverableSessionExecution.ValidateOptions(options);
            if (session is not ManagedSession managed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Recoverable execution requires a stack ManagedSession.");
            }
            RegisterMissingRseTypes(session);
            SessionRequestContext context = await SessionRequestContext.CaptureRecoverableAsync(session, ct)
                .ConfigureAwait(false);
            try
            {
                RecoverableExecutionIdentity? identity = await VerifyIdentityAsync(context.Session, options, ct)
                    .ConfigureAwait(false);
                RecoverableExecutionTypeContext? types = options.CheckpointStore == null ? null :
                    await options.CheckpointStore.CaptureTypeContextAsync(context.Session.MessageContext, ct)
                        .ConfigureAwait(false);
                return new RecoverableExecutionBinding(managed, context, options with { }, identity, types);
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Discovers capabilities once for a caller operation; no capability cache is shared across operations.
        /// </summary>
        internal async ValueTask<RecoverableCapabilities> DiscoverAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                return m_capabilities ??= await RecoverableManagerClient.DiscoverAsync(Session, Options, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Returns manager plumbing after verifying the current Session and, if needed, explicit owner recovery.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<RecoverableManagerClient> GetManagerAsync(
            CancellationToken ct, bool allowReplacement = true)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_invalidated.Task.IsCompleted)
                {
                    throw InvalidIdentity();
                }
                if (m_context.Invalidated.IsCompleted)
                {
                    if (!allowReplacement || Options.RecoveryMode == ServiceOperationRecoveryMode.Session)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                    }
                    await ReplaceContextAsync(ct).ConfigureAwait(false);
                }
                if (Identity != null)
                {
                    RecoverableExecutionIdentity? current = await VerifyIdentityAsync(Session, Options, ct)
                        .ConfigureAwait(false);
                    if (current == null || !Identity.Matches(current))
                    {
                        m_invalidated.TrySetResult(true);
                        throw InvalidIdentity();
                    }
                }
                await VerifyTypeContextAsync(ct).ConfigureAwait(false);
                m_capabilities ??= await RecoverableManagerClient.DiscoverAsync(Session, Options, ct)
                    .ConfigureAwait(false);
                return m_capabilities.Manager ??
                    throw new ServiceResultException(
                        StatusCodes.BadServiceUnsupported, "The recoverable manager is unavailable.");
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Requests managed reconnection without replaying any request or retargeting a captured transport.
        /// </summary>
        internal async ValueTask RecoverAsync(CancellationToken ct)
        {
            if (Options.RecoveryMode == ServiceOperationRecoveryMode.Session)
            {
                await m_context.RecoverAsync(ct).ConfigureAwait(false);
                return;
            }
            ThrowIfDisposed();
            await m_owner.ReconnectAsync(null, null, ct).ConfigureAwait(false);
            _ = await GetManagerAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies current registrations and definitions before accepting an outcome or sending acknowledgement.
        /// </summary>
        internal async ValueTask VerifyTypeContextAsync(CancellationToken ct)
        {
            if (TypeContext != null)
            {
                _ = await Options.CheckpointStore!.VerifyTypeContextAsync(
                    Session.MessageContext, TypeContext.Witness, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Captures a newly activated Session and accepts it only after all ownership and interpretation checks.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask ReplaceContextAsync(CancellationToken ct)
        {
            await m_owner.StateMachine.WaitForConnectedAsync(ct).ConfigureAwait(false);
            SessionRequestContext replacement = await SessionRequestContext.CaptureRecoverableAsync(m_owner, ct)
                .ConfigureAwait(false);
            try
            {
                RecoverableExecutionIdentity? identity = await VerifyIdentityAsync(replacement.Session, Options, ct)
                    .ConfigureAwait(false);
                IServiceMessageContext current = replacement.Session.MessageContext;
                if (Identity == null ||
                    identity == null ||
                    !Identity.Matches(identity) ||
                    !MatchesTables(current.NamespaceUris, NamespaceUris) ||
                    !MatchesTables(current.ServerUris, ServerUris) ||
                    (TypeContext == null && !ReferenceEquals(current.Factory, m_originalContext.Factory)) ||
                    current.MaxStringLength != m_originalContext.MaxStringLength ||
                    current.MaxByteStringLength != m_originalContext.MaxByteStringLength ||
                    current.MaxArrayLength != m_originalContext.MaxArrayLength ||
                    current.MaxEncodingNestingLevels != m_originalContext.MaxEncodingNestingLevels ||
                    current.MaxDecoderRecoveries != m_originalContext.MaxDecoderRecoveries)
                {
                    m_invalidated.TrySetResult(true);
                    throw InvalidIdentity();
                }
                if (TypeContext != null)
                {
                    _ = await Options.CheckpointStore!.VerifyTypeContextAsync(current, TypeContext.Witness, ct)
                        .ConfigureAwait(false);
                }
                RecoverableCapabilities capabilities = await RecoverableManagerClient.DiscoverAsync(
                    replacement.Session, Options, ct).ConfigureAwait(false);
                if (!capabilities.Available)
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                }
                SessionRequestContext previous = m_context;
                m_capabilities?.Dispose();
                m_context = replacement;
                m_capabilities = capabilities;
                replacement = null!;
                await previous.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (replacement != null)
                {
                    await replacement.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Requires actual application and user authentication before consulting the authorized continuity map.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<RecoverableExecutionIdentity?> VerifyIdentityAsync(
            ISession session, RecoverableSessionOptions options, CancellationToken ct)
        {
            if (options.IdentityProvider == null)
            {
                if (options.RecoveryMode != ServiceOperationRecoveryMode.Session || options.CheckpointStore != null)
                {
                    throw InvalidIdentity();
                }
                return null;
            }
            ITransportChannel transport = session.TransportChannel;
            if (session.Identity.TokenType == UserTokenType.Anonymous ||
                transport.ClientChannelCertificate is not { Length: > 0 } ||
                transport.ServerChannelCertificate is not { Length: > 0 } ||
                (transport.EndpointDescription.SecurityMode == MessageSecurityMode.None &&
                    transport.EndpointDescription.TransportProfileUri is not
                        (Profiles.HttpsBinaryTransport or Profiles.HttpsJsonTransport)))
            {
                throw InvalidIdentity();
            }
            return await options.IdentityProvider.VerifyAsync(session, ct).ConfigureAwait(false) ??
                throw InvalidIdentity();
        }

        /// <summary>
        /// Registers missing generated types without concealing a changed existing codec before compatibility checks.
        /// </summary>
        private static void RegisterMissingRseTypes(ISession session)
        {
            var trusted = ServiceMessageContext.CreateEmpty(session.MessageContext.Telemetry);
            trusted.Factory.Builder.AddOpcUaRse().Commit();
            IEncodeableFactoryBuilder builder = session.Factory.Builder;
            bool changed = false;
            foreach (ExpandedNodeId id in trusted.Factory.KnownTypeIds)
            {
                if (trusted.Factory.TryGetEncodeableType(id, out IEncodeableType? structure) &&
                    !session.Factory.TryGetEncodeableType(id, out _))
                {
                    builder.AddEncodeableType(id, structure);
                    changed = true;
                }
                if (trusted.Factory.TryGetEnumeratedType(id, out IEnumeratedType? enumeration) &&
                    !session.Factory.TryGetEnumeratedType(id, out _))
                {
                    builder.AddEnumeratedType(id, enumeration);
                    changed = true;
                }
            }
            if (changed)
            {
                builder.Commit();
            }
        }

        /// <summary>
        /// Preserves original namespace interpretation instead of silently reinterpreting saved NodeIds.
        /// </summary>
        internal static bool MatchesTables(StringTable table, ArrayOf<string> expected)
        {
            if (table.Count != expected.Count)
            {
                return false;
            }
            for (int i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(table.GetString((uint)i), expected[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Rejects local use after disposal.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RecoverableExecutionBinding));
            }
        }

        /// <summary>
        /// Fails closed without logging credentials, continuity keys or tokens.
        /// </summary>
        private static ServiceResultException InvalidIdentity()
        {
            return new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected, "Authenticated recoverable owner continuity was not verified.");
        }

        private readonly ManagedSession m_owner;
        private readonly IServiceMessageContext m_originalContext;
        private readonly SemaphoreSlim m_gate = new(1, 1);

        private readonly TaskCompletionSource<bool> m_invalidated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private SessionRequestContext m_context;
        private RecoverableCapabilities? m_capabilities;
        private int m_disposed;
    }
}
