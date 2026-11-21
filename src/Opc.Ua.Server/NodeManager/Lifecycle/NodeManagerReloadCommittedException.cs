// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports a reload failure that occurred after the replacement generation
    /// was committed and provides its authoritative registration.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A committed reload exception is meaningful only with its authoritative registration.")]
    public sealed class NodeManagerReloadCommittedException : InvalidOperationException, INodeManagerCommittedFailure
    {
        /// <summary>
        /// Initializes an exception for a reload whose replacement registration already became authoritative.
        /// </summary>
        /// <param name="registration">The committed replacement registration that callers must keep.</param>
        /// <param name="message">The failure message describing the post-commit error.</param>
        /// <param name="innerException">The exception raised after commit completed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="registration"/> is <c>null</c>.</exception>
        public NodeManagerReloadCommittedException(
            NodeManagerRegistration registration,
            string message,
            Exception innerException)
            : base(message, innerException)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        /// <summary>
        /// Initializes an exception carrying the authoritative committed transaction.
        /// </summary>
        /// <param name="registration">The committed replacement registration.</param>
        /// <param name="result">The committed transaction result, including asynchronous retirement.</param>
        /// <param name="message">The post-commit failure message.</param>
        /// <param name="innerException">The failure raised after publication.</param>
        public NodeManagerReloadCommittedException(
            NodeManagerRegistration registration,
            NodeManagerBatchResult result,
            string message,
            Exception innerException)
            : this(registration, message, innerException)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// Initializes an exception carrying the authoritative change-set result.
        /// </summary>
        /// <param name="registration">The committed replacement registration.</param>
        /// <param name="result">The authoritative result.</param>
        /// <param name="message">The post-commit failure message.</param>
        /// <param name="innerException">The failure raised after publication.</param>
        public NodeManagerReloadCommittedException(
            NodeManagerRegistration registration,
            LifecycleResult result,
            string message,
            Exception innerException)
            : this(registration, message, innerException)
        {
            m_committedResult = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// Gets the committed replacement registration that remains the active NodeManager generation.
        /// </summary>
        public NodeManagerRegistration Registration { get; }

        /// <summary>
        /// Gets the committed transaction, or null for exceptions constructed with the legacy constructor.
        /// </summary>
        public NodeManagerBatchResult? Result { get; }

        /// <inheritdoc/>
        public LifecycleResult? CommittedResult => m_committedResult ?? Result?.CommittedResult;

        private readonly LifecycleResult? m_committedResult;
    }
}
