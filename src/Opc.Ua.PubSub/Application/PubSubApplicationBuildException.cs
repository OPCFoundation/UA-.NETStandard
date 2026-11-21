// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Thrown by <c>PubSubApplicationBuilder.Build</c> when the
    /// accumulated configuration cannot be materialised into a working
    /// runtime — typically because validation failed or a required
    /// transport factory was not registered.
    /// </summary>
    /// <remarks>
    /// Surfaces builder-side validation failures referenced from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2</see>.
    /// </remarks>
    public sealed class PubSubApplicationBuildException : Exception
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationBuildException"/>.
        /// </summary>
        public PubSubApplicationBuildException()
        {
        }

        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationBuildException"/>.
        /// </summary>
        /// <param name="message">Human-readable description.</param>
        public PubSubApplicationBuildException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationBuildException"/>.
        /// </summary>
        /// <param name="message">Human-readable description.</param>
        /// <param name="innerException">Underlying cause.</param>
        public PubSubApplicationBuildException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
