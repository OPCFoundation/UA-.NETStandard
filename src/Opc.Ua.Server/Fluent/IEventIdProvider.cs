// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Supplies event ids for fluent event publishing.
    /// </summary>
    /// <remarks>
    /// When no provider is configured, fluent event publishing keeps the existing random UUID EventId behavior.
    /// Distributed deployments can configure a provider that derives stable ids from replica-shared event identity.
    /// </remarks>
    public interface IEventIdProvider
    {
        /// <summary>
        /// Creates an event id for an event whose <c>EventId</c> field was not already populated.
        /// </summary>
        /// <param name="notifier">The notifier that reports the event.</param>
        /// <param name="context">The system context.</param>
        /// <param name="eventState">The event being reported.</param>
        /// <returns>The event id.</returns>
        ByteString CreateEventId(BaseObjectState notifier, ISystemContext context, BaseEventState eventState);
    }
}
