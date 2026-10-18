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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An opaque, same-Session receipt for an accepted caller operation. It never contains a public Server token.
    /// </summary>
    public sealed class RecoverableReceipt
    {
        /// <summary>
        /// Associates the receipt with its original operation group.
        /// </summary>
        internal RecoverableReceipt(RecoverableOperationGroup group)
        {
            m_group = group;
        }

        /// <summary>
        /// Gets the Session to which this receipt belongs.
        /// </summary>
        public NodeId SessionId => m_group.SessionId;

        /// <summary>
        /// Gets whether every enclosed original request used recoverable execution.
        /// </summary>
        public bool IsFullyRecoverable => !m_group.UsedOrdinary;

        /// <summary>
        /// Gets whether all actual receipt exchanges have been confirmed.
        /// </summary>
        public bool IsAcknowledged => m_group.IsAcknowledged;

        /// <summary>
        /// Acknowledges accepted outcomes. A retry repeats only unconfirmed ACKs, never the original operation.
        /// </summary>
        public ValueTask AcknowledgeAsync(CancellationToken ct = default)
        {
            return m_group.AcknowledgeAsync(ct);
        }

        private readonly RecoverableOperationGroup m_group;
    }

    /// <summary>
    /// Defers acknowledgement for one logical call on the existing session in the current asynchronous flow.
    /// Dispose performs local cleanup only; it never acknowledges, abandons or cancels remote work.
    /// </summary>
    public sealed class RecoverableAcknowledgementScope : IAsyncDisposable
    {
        /// <summary>
        /// Creates a local receipt scope without dispatching or acquiring another Session.
        /// </summary>
        internal RecoverableAcknowledgementScope(
            bool requireReceipt,
            Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? accept,
            Action<RecoverableAcknowledgementScope> leave)
        {
            RequireRecoverableReceipt = requireReceipt;
            AcceptAsync = accept;
            m_leave = leave;
        }

        /// <summary>
        /// Gets whether unsupported recovery must fail before ordinary application dispatch.
        /// </summary>
        public bool RequireRecoverableReceipt { get; }

        /// <summary>
        /// Gets the actual receipt, or null when there is no admitted recoverable operation.
        /// </summary>
        public RecoverableReceipt? Receipt => Volatile.Read(ref m_group)?.Receipt;

        /// <summary>
        /// Gets the optional local acceptance callback for this scope.
        /// </summary>
        internal Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? AcceptAsync { get; }

        /// <summary>
        /// Acknowledges the accepted operation; ordinary fallback cannot manufacture a successful receipt.
        /// </summary>
        public ValueTask AcknowledgeAsync(CancellationToken ct = default)
        {
            return (Receipt ?? throw new ServiceResultException(
                StatusCodes.BadNotSupported, "This operation has no recoverable receipt.")).AcknowledgeAsync(ct);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return default;
            }
            m_leave(this);
            RecoverableOperationGroup? group;
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    return default;
                }
                m_disposed = 1;
                group = m_group;
            }
            return group?.DisposeAsync() ?? default;
        }

        /// <summary>
        /// Claims the scope exactly once; unrelated or concurrent calls cannot share its receipt.
        /// </summary>
        internal RecoverableOperationGroup Claim(NodeId sessionId)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(RecoverableAcknowledgementScope));
                }
                if (m_group != null)
                {
                    throw new InvalidOperationException("An acknowledgement scope belongs to one caller operation.");
                }
                m_group = new RecoverableOperationGroup(sessionId, RequireRecoverableReceipt, this);
                return m_group;
            }
        }

        private readonly Action<RecoverableAcknowledgementScope> m_leave;
        private readonly Lock m_gate = new();
        private RecoverableOperationGroup? m_group;
        private int m_disposed;
    }

    /// <summary>
    /// Adds local receipt control without adding members to ISession or changing ordinary operation signatures.
    /// </summary>
    public static class RecoverableSessionExtensions
    {
        /// <summary>
        /// Begins a local explicit-acknowledgement scope on an already opted-in ManagedSession.
        /// The scope must be disposed in reverse nesting order in the same asynchronous flow.
        /// </summary>
        public static RecoverableAcknowledgementScope BeginAcknowledgementScope(
            this ISession session,
            bool requireRecoverableReceipt = false,
            Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? acceptAsync = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return session is ManagedSession managed
                ? managed.BeginAcknowledgementScope(requireRecoverableReceipt, acceptAsync)
                : throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Receipt scopes require an opted-in ManagedSession.");
        }
    }

    /// <summary>
    /// Owns the protocol handles underneath a single caller completion context.
    /// </summary>
    internal sealed class RecoverableOperationGroup : IAsyncDisposable
    {
        /// <summary>
        /// Creates local ownership and cancellation for one caller operation.
        /// </summary>
        internal RecoverableOperationGroup(
            NodeId sessionId, bool requireReceipt, RecoverableAcknowledgementScope? scope)
        {
            SessionId = sessionId;
            RequireReceipt = requireReceipt;
            Scope = scope;
            m_token = m_lifetime.Token;
            m_receipt = new RecoverableReceipt(this);
        }

        /// <summary>
        /// Gets the actual Session captured before protocol dispatch.
        /// </summary>
        internal NodeId SessionId { get; private set; }

        /// <summary>
        /// Gets whether ordinary fallback is forbidden.
        /// </summary>
        internal bool RequireReceipt { get; }

        /// <summary>
        /// Gets the explicit owner, or null for automatically acknowledged calls.
        /// </summary>
        internal RecoverableAcknowledgementScope? Scope { get; }

        /// <summary>
        /// Gets or sets whether the current flow is inside external acceptance.
        /// </summary>
        internal bool AcceptanceInProgress { get; set; }

        /// <summary>
        /// Gets cancellation of local waiting, never a remote work-control action.
        /// </summary>
        internal CancellationToken Token => m_token;

        /// <summary>
        /// Gets whether the logical operation includes ordinary dispatch.
        /// </summary>
        internal bool UsedOrdinary { get; private set; }

        /// <summary>
        /// Gets whether an original Call used recoverable execution.
        /// </summary>
        internal bool HasRecoverableCall { get; private set; }

        /// <summary>
        /// Gets the shared same-Session binding.
        /// </summary>
        internal SessionRequestContext? Binding { get; private set; }

        /// <summary>
        /// Gets a receipt only after a real recoverable outcome has been received.
        /// </summary>
        internal RecoverableReceipt? Receipt
        {
            get
            {
                lock (m_gate)
                {
                    return m_operations.Exists(operation => operation.HasOutcome) ? m_receipt : null;
                }
            }
        }

        /// <summary>
        /// Gets whether every original receipt exchange was confirmed.
        /// </summary>
        internal bool IsAcknowledged
        {
            get
            {
                lock (m_gate)
                {
                    return m_operations.Count > 0 && m_operations.TrueForAll(operation => operation.Acknowledged);
                }
            }
        }

        /// <summary>
        /// Shares one capability lookup between nested requests without caching it across caller operations.
        /// </summary>
        internal Task<RecoverableCapabilities> GetCapabilitiesAsync(Func<Task<RecoverableCapabilities>> discover)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(RecoverableAcknowledgementScope));
                }
                return m_capabilities ??= discover();
            }
        }

        /// <summary>
        /// Transfers binding ownership only if the scope has not already been disposed.
        /// </summary>
        internal bool TryAttachBinding(SessionRequestContext binding)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    return false;
                }
                Binding = binding;
                SessionId = binding.SessionId;
                return true;
            }
        }

        /// <summary>
        /// Records ordinary dispatch or refuses it when a recoverable receipt is required.
        /// </summary>
        internal void MarkOrdinary()
        {
            if (RequireReceipt)
            {
                throw new ServiceResultException(
                    StatusCodes.BadServiceUnsupported, "This scope requires a real recoverable receipt.");
            }
            UsedOrdinary = true;
        }

        /// <summary>
        /// Takes ownership of an operation before its cancellable submission starts.
        /// </summary>
        internal void Add(IRecoverableOperation operation)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(RecoverableAcknowledgementScope));
                }
                m_operations.Add(operation);
            }
        }

        /// <summary>
        /// Returns ownership of an operation whose unsupported submission was never admitted.
        /// </summary>
        internal void Remove(IRecoverableOperation operation)
        {
            lock (m_gate)
            {
                m_operations.Remove(operation);
            }
        }

        /// <summary>
        /// Records a recoverable Call independently of its result status.
        /// </summary>
        internal void RecordOutcome(bool call)
        {
            lock (m_gate)
            {
                HasRecoverableCall |= call;
            }
        }

        /// <summary>
        /// Records the exact exception produced from an original response, never just its status code.
        /// </summary>
        internal void RecordOriginalFailure(ServiceResultException failure)
        {
            lock (m_gate)
            {
                if (!m_originalFailures.Exists(original => ReferenceEquals(original, failure)))
                {
                    m_originalFailures.Add(failure);
                }
            }
        }

        /// <summary>
        /// Checks whether an exception describes one of the received original outcomes.
        /// </summary>
        internal bool IsOriginalFailure(ServiceResultException failure)
        {
            lock (m_gate)
            {
                return m_originalFailures.Exists(original => ReferenceEquals(original, failure));
            }
        }

        /// <summary>
        /// Accepts into the actual caller completion context only while local ownership remains active.
        /// </summary>
        internal bool TryAccept(Func<bool> complete)
        {
            lock (m_gate)
            {
                if (m_disposed != 0 || !complete())
                {
                    return false;
                }
                m_accepted = true;
                return true;
            }
        }

        /// <summary>
        /// Serializes idempotent receipt confirmation without rerunning original work.
        /// </summary>
        internal async ValueTask AcknowledgeAsync(CancellationToken ct)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(RecoverableAcknowledgementScope));
                }
                if (!m_accepted)
                {
                    throw new InvalidOperationException("The caller must accept the result before acknowledgement.");
                }
                if (m_operations.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "No recoverable receipt exists.");
                }
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_token);
            await m_acknowledgement.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                IRecoverableOperation[] operations;
                lock (m_gate)
                {
                    operations = [.. m_operations];
                }
                foreach (IRecoverableOperation operation in operations)
                {
                    if (!operation.Acknowledged)
                    {
                        await operation.AcknowledgeAsync(linked.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_acknowledgement.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            await m_acknowledgement.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                IRecoverableOperation[] operations;
                lock (m_gate)
                {
                    operations = [.. m_operations];
                }
                foreach (IRecoverableOperation operation in operations)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                if (Binding != null)
                {
                    await Binding.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_acknowledgement.Release();
                m_acknowledgement.Dispose();
                m_lifetime.Dispose();
            }
        }

        private readonly Lock m_gate = new();
        private readonly List<IRecoverableOperation> m_operations = [];
        private readonly List<ServiceResultException> m_originalFailures = [];
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly CancellationToken m_token;
        private readonly SemaphoreSlim m_acknowledgement = new(1, 1);
        private readonly RecoverableReceipt m_receipt;
        private Task<RecoverableCapabilities>? m_capabilities;
        private bool m_accepted;
        private int m_disposed;
    }

    /// <summary>
    /// Internal lifetime and receipt operations shared by differently typed original Service responses.
    /// </summary>
    internal interface IRecoverableOperation : IAsyncDisposable
    {
        /// <summary>
        /// Gets whether a validated original or terminal outcome has been received.
        /// </summary>
        bool HasOutcome { get; }

        /// <summary>
        /// Gets whether the Server confirmed this outcome's receipt.
        /// </summary>
        bool Acknowledged { get; }

        /// <summary>
        /// Acknowledges only this original operation.
        /// </summary>
        ValueTask AcknowledgeAsync(CancellationToken ct);
    }
}
