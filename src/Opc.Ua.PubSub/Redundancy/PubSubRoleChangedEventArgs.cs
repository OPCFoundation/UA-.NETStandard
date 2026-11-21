// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Raised by an <see cref="IPubSubActivationCoordinator"/> when the
    /// redundancy role of a component changes on this instance.
    /// </summary>
    public sealed class PubSubRoleChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubRoleChangedEventArgs"/>.
        /// </summary>
        /// <param name="componentId">
        /// Deterministic component identifier whose role changed.
        /// </param>
        /// <param name="role">The new role.</param>
        public PubSubRoleChangedEventArgs(string componentId, PubSubComponentRole role)
        {
            ComponentId = componentId ?? throw new ArgumentNullException(nameof(componentId));
            Role = role;
        }

        /// <summary>
        /// Deterministic component identifier whose role changed (for
        /// example <c>pubsub:writergroup:WriterGroup1</c>).
        /// </summary>
        public string ComponentId { get; }

        /// <summary>
        /// The role now held by the component on this instance.
        /// </summary>
        public PubSubComponentRole Role { get; }
    }
}
