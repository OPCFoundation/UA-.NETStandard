// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports that prepared lifecycle changes conflict with a change another caller published after they were
    /// prepared. Nothing of the rejected changes was published and the durable decision callback did not run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conflict is retryable: prepare the changes again against the current address space (for example with
    /// <c>PrepareAsync</c> or <c>ApplyAsync</c>) and commit
    /// the new preparation. Calling <c>CommitAsync</c> again on the rejected preparation
    /// does not retry it.
    /// </para>
    /// <para>
    /// Changes that touch disjoint NodeManagers do not conflict: a commit on other managers only moves the batch
    /// onto the newer address space. A conflict is reported when another publication replaced or removed a
    /// registration the batch changes, appended a different namespace or server URI at an index the batch
    /// assigned, registered or removed a type or encodeable type the batch registers, or removed a supertype the
    /// batch depends on, and when an explicit <see cref="ILifecycleCapture"/> no longer matches the address space.
    /// </para>
    /// <para>
    /// Callers can catch this type to tell a retryable conflict from other <see cref="InvalidOperationException"/>
    /// failures, which a retry does not resolve.
    /// </para>
    /// </remarks>
    public class NodeManagerLifecycleConflictException : InvalidOperationException
    {
        /// <summary>
        /// Initializes the exception with the default conflict message.
        /// </summary>
        public NodeManagerLifecycleConflictException()
            : base("The prepared changes conflict with a newer address-space publication.")
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerLifecycleConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message and the conflict it reports.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that detected the conflict.</param>
        public NodeManagerLifecycleConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
