/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Identifies a serving revision that changed after an optimistic publication captured it.
    /// </summary>
    internal sealed class OptimisticPublicationConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default stale-revision diagnostic.
        /// </summary>
        public OptimisticPublicationConflictException()
            : base("A serving revision changed after it was captured.")
        {
        }

        /// <summary>
        /// Creates a conflict for a stale serving revision.
        /// </summary>
        /// <param name="message">The stale revision diagnostic.</param>
        public OptimisticPublicationConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The stale revision diagnostic.</param>
        /// <param name="innerException">The failure that caused the conflict.</param>
        public OptimisticPublicationConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
