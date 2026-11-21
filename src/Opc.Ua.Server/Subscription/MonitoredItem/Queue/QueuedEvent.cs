// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An event queued for a monitored item: the event fields sent to the client and the
    /// server-side state that travels with them until they are published.
    /// </summary>
    /// <remarks>
    /// The fields are a value (<see cref="EventFieldList"/> is a struct), so queueing an event
    /// for an item allocates nothing beyond the field values. The handle and the admission
    /// stamp are never sent; they are dropped when the event is copied into a notification
    /// message.
    /// </remarks>
    public readonly struct QueuedEvent
    {
        /// <summary>
        /// Creates a queued event.
        /// </summary>
        /// <param name="fields">The event fields sent to the client.</param>
        /// <param name="handle">The event instance the fields were selected from, if any.</param>
        public QueuedEvent(EventFieldList fields, object? handle = null)
            : this(fields, handle, null)
        {
        }

        /// <summary>
        /// Creates a queued event with the server's record of the admitted occurrence.
        /// </summary>
        internal QueuedEvent(EventFieldList fields, object? handle, object? admissionStamp)
        {
            Fields = fields;
            Handle = handle;
            AdmissionStamp = admissionStamp;
        }

        /// <summary>
        /// Queues event fields without an event instance, e.g. restored from a durable queue.
        /// </summary>
        /// <param name="fields">The event fields sent to the client.</param>
        public static implicit operator QueuedEvent(EventFieldList fields)
        {
            return new QueuedEvent(fields);
        }

        /// <summary>
        /// The event fields sent to the client.
        /// </summary>
        public EventFieldList Fields { get; }

        /// <summary>
        /// The event instance the fields were selected from, used to detect an event
        /// reported twice and to rebuild the fields after the select clauses changed.
        /// <c>null</c> for an event restored from a durable or mirrored queue.
        /// </summary>
        public object? Handle { get; }

        /// <summary>
        /// The client handle of the monitored item the event is queued for.
        /// </summary>
        public uint ClientHandle => Fields.ClientHandle;

        /// <summary>
        /// The event field values.
        /// </summary>
        public ArrayOf<Variant> EventFields => Fields.EventFields;

        /// <summary>
        /// The server's record of the admitted occurrence the fields were selected from
        /// (see <see cref="EventManager"/>).
        /// </summary>
        internal object? AdmissionStamp { get; }

        /// <summary>
        /// Returns a copy with another client handle.
        /// </summary>
        /// <param name="clientHandle">The new client handle.</param>
        public QueuedEvent WithClientHandle(uint clientHandle)
        {
            EventFieldList fields = Fields;
            fields.ClientHandle = clientHandle;
            return new QueuedEvent(fields, Handle, AdmissionStamp);
        }

        /// <summary>
        /// Returns a copy with other field values; the admission stamp is not carried over
        /// because it belongs to the original values.
        /// </summary>
        /// <param name="eventFields">The new field values.</param>
        public QueuedEvent WithEventFields(ArrayOf<Variant> eventFields)
        {
            EventFieldList fields = Fields;
            fields.EventFields = eventFields;
            return new QueuedEvent(fields, Handle);
        }
    }
}
