// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports a removal failure that occurred after the registration was removed from the published view.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A committed removal exception requires its authoritative registration and result.")]
    public sealed class NodeManagerRemovalCommittedException : InvalidOperationException, INodeManagerCommittedFailure
    {
        /// <summary>
        /// Initializes an exception for a removal whose publication already became authoritative.
        /// </summary>
        /// <param name="registration">The registration removed from the published view.</param>
        /// <param name="result">The committed result, including its retirement task.</param>
        /// <param name="innerException">The failure raised during post-commit reconciliation.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registration"/>, <paramref name="result"/>, or <paramref name="innerException"/> is
        /// <see langword="null"/>.
        /// </exception>
        public NodeManagerRemovalCommittedException(
            NodeManagerRegistration registration,
            NodeManagerBatchResult result,
            Exception innerException)
            : base("The NodeManager was removed, but post-commit reconciliation failed.", innerException)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            CommittedResult = result.CommittedResult;
        }

        /// <summary>
        /// Initializes an exception carrying the authoritative change-set result.
        /// </summary>
        /// <param name="registration">The removed registration.</param>
        /// <param name="result">The authoritative result and retirement task.</param>
        /// <param name="innerException">The post-publication failure.</param>
        public NodeManagerRemovalCommittedException(
            NodeManagerRegistration registration,
            LifecycleResult result,
            Exception innerException)
            : base("The NodeManager was removed, but post-commit reconciliation failed.", innerException)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            CommittedResult = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// Gets the registration removed from the published view.
        /// </summary>
        public NodeManagerRegistration Registration { get; }

        /// <summary>
        /// Gets the legacy result view when one was supplied.
        /// </summary>
        public NodeManagerBatchResult? Result { get; }

        /// <inheritdoc/>
        public LifecycleResult CommittedResult { get; }
    }
}
