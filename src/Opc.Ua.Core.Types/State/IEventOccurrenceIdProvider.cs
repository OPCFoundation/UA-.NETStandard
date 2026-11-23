// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Supplies EventIds for locally generated occurrences before they are reported or snapshotted.
    /// </summary>
    public interface IEventOccurrenceIdProvider
    {
        /// <summary>
        /// Creates an EventId from the populated occurrence fields.
        /// </summary>
        /// <param name="context">The context of the event source.</param>
        /// <param name="eventState">The event being reported.</param>
        /// <returns>The occurrence ID, or a null ByteString to leave it unchanged.</returns>
        ByteString CreateEventId(ISystemContext context, BaseEventState eventState);
    }

    /// <summary>
    /// A system context that can supply IDs for locally generated event occurrences.
    /// </summary>
    public interface IEventOccurrenceIdContext
    {
        /// <summary>
        /// Gets the provider, if the server configured one.
        /// </summary>
        IEventOccurrenceIdProvider? EventOccurrenceIdProvider { get; }
    }
}
