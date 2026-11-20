/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Identifies a PubSub Action target on a DataSetWriter.
    /// </summary>
    public sealed record PubSubActionTarget
    {
        /// <summary>
        /// Optional connection name used by application-level routing.
        /// </summary>
        public string ConnectionName { get; init; } = string.Empty;

        /// <summary>
        /// DataSetWriterId that owns the Action metadata.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// ActionTargetId unique within the Action metadata.
        /// </summary>
        public ushort ActionTargetId { get; init; }

        /// <summary>
        /// Optional target name used to resolve <see cref="ActionTargetId"/>
        /// from configured PublishedAction metadata.
        /// </summary>
        public string ActionName { get; init; } = string.Empty;
    }
}
