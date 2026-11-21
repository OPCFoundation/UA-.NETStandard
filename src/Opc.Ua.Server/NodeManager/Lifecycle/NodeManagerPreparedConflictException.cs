// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds a typed stale serving-revision conflict to the prepared batch that observed it.
    /// </summary>
    internal sealed class NodeManagerPreparedConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default stale-publication diagnostic.
        /// </summary>
        public NodeManagerPreparedConflictException()
            : base("A prepared batch observed a stale serving revision.")
        {
        }

        /// <summary>
        /// Creates a conflict with a diagnostic.
        /// </summary>
        /// <param name="message">The stale-publication diagnostic.</param>
        public NodeManagerPreparedConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The stale-publication diagnostic.</param>
        /// <param name="innerException">The originating stale serving-revision conflict.</param>
        public NodeManagerPreparedConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a conflict bound to its prepared batch.
        /// </summary>
        /// <param name="failure">The stale serving-revision conflict.</param>
        /// <param name="batch">The prepared batch whose capture became stale.</param>
        internal NodeManagerPreparedConflictException(
            OptimisticPublicationConflictException failure,
            IPreparedNodeManagerBatch batch)
            : base(failure.Message, failure)
        {
            Batch = batch;
        }

        /// <summary>
        /// Gets the prepared batch whose captured serving revision became stale.
        /// </summary>
        internal IPreparedNodeManagerBatch? Batch { get; }
    }
}
