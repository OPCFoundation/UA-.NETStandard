/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports that a NodeManager factory returned an instance that is already registered.
    /// </summary>
    /// <remarks>
    /// Allows callers of <see cref="INodeManagerLifecycle"/> to distinguish duplicate
    /// registration attempts from other <see cref="InvalidOperationException"/> failures.
    /// </remarks>
    public sealed class NodeManagerAlreadyRegisteredException : InvalidOperationException
    {
        /// <summary>
        /// Initializes the exception with the default duplicate-registration message.
        /// </summary>
        public NodeManagerAlreadyRegisteredException()
            : base("The NodeManager is already registered.")
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerAlreadyRegisteredException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this error.</param>
        public NodeManagerAlreadyRegisteredException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
