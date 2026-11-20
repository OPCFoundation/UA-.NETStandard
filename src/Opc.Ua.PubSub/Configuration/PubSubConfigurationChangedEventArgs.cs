// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Event payload raised by an
    /// <see cref="IPubSubConfigurationStore"/> when the persisted
    /// configuration changes. Carries the previous configuration
    /// (or <see langword="null"/> on first load) and the new one so
    /// the application can compute a delta and cascade re-enable /
    /// disable transitions.
    /// </summary>
    /// <remarks>
    /// Implements the configuration-change notification surface
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PublishedDataSetFolder / configuration root</see>.
    /// </remarks>
    public sealed class PubSubConfigurationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationChangedEventArgs"/>.
        /// </summary>
        /// <param name="previous">
        /// Prior configuration, or <see langword="null"/> if this is
        /// the first load.
        /// </param>
        /// <param name="current">New configuration.</param>
        public PubSubConfigurationChangedEventArgs(
            PubSubConfigurationDataType? previous,
            PubSubConfigurationDataType current)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            Previous = previous;
            Current = current;
        }

        /// <summary>
        /// Prior configuration, or <see langword="null"/> if this
        /// is the first load.
        /// </summary>
        public PubSubConfigurationDataType? Previous { get; }

        /// <summary>
        /// New configuration.
        /// </summary>
        public PubSubConfigurationDataType Current { get; }
    }
}
