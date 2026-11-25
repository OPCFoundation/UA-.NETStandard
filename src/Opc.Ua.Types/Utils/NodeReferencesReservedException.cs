// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Identifies a node whose references a pending publication has reserved until its decision completes.
    /// </summary>
    /// <remarks>
    /// The reservation ends when the publication commits or is abandoned, so a caller can retry the rejected
    /// mutation afterwards. Any other <see cref="InvalidOperationException"/> is not a reservation conflict.
    /// </remarks>
    internal sealed class NodeReferencesReservedException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default reservation diagnostic.
        /// </summary>
        public NodeReferencesReservedException()
            : base("The node references have a pending publication.")
        {
        }

        /// <summary>
        /// Creates a conflict with a specific reservation diagnostic.
        /// </summary>
        /// <param name="message">The reservation diagnostic.</param>
        public NodeReferencesReservedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The reservation diagnostic.</param>
        /// <param name="innerException">The failure that caused the conflict.</param>
        public NodeReferencesReservedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}