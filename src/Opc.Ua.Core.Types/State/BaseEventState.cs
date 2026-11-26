// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    public partial class BaseEventState
    {
        /// <summary>
        /// Initializes a new event.
        /// </summary>
        /// <param name="context">The current system context.</param>
        /// <param name="source">The source of the event.</param>
        /// <param name="severity">The severity for the event.</param>
        /// <param name="message">The default message.</param>
        public virtual void Initialize(
            ISystemContext context,
            NodeState? source,
            EventSeverity severity,
            LocalizedText message)
        {
            // the fields carry the event time as their source timestamp: a field without
            // one is stamped with the current time on every read, and an event's fields are
            // read once per select clause and monitored item it is delivered to.
            DateTimeUtc time = DateTimeUtc.Now;

            m_eventId = WithTimestamp(PropertyState<ByteString>.With<VariantBuilder>(this, Uuid.NewUuid().ToByteString()), time);
            m_eventType = WithTimestamp(PropertyState<NodeId>.With<VariantBuilder>(this, GetDefaultTypeDefinitionId(context.NamespaceUris)), time);

            TypeDefinitionId = m_eventType.Value;
            if (source != null)
            {
                if (!source.NodeId.IsNull)
                {
                    m_sourceNode = new PropertyState<NodeId>.Implementation<VariantBuilder>(this)
                    {
                        Value = source.NodeId,
                        Timestamp = time,
                        NodeId = source.NodeId
                    };
                    // the field is new: copying the source's permissions changes no node's
                    // permissions, so it must not retire the server's cached verdicts.
                    m_sourceNode.InitializeRolePermissions(source.RolePermissions, source.UserRolePermissions);
                }

                if (!source.BrowseName.IsNull)
                {
                    m_sourceName = WithTimestamp(PropertyState<string>.With<VariantBuilder>(this, source.BrowseName.Name!), time); // QualifiedName.IsNull is false here, so Name is non-null
                }
            }

            m_time = WithTimestamp(PropertyState<DateTimeUtc>.With<VariantBuilder>(this, time), time);
            DateTimeUtc receiveTime = DateTimeUtc.Now;
            m_receiveTime = WithTimestamp(PropertyState<DateTimeUtc>.With<VariantBuilder>(this, receiveTime), receiveTime);
            m_severity = WithTimestamp(PropertyState<ushort>.With<VariantBuilder>(this, (ushort)severity), time);
            m_message = WithTimestamp(PropertyState<LocalizedText>.With<VariantBuilder>(this, message), time);
        }

        /// <summary>
        /// Sets the source timestamp of an event field.
        /// </summary>
        /// <typeparam name="T">The type of the field.</typeparam>
        /// <param name="field">The field.</param>
        /// <param name="timestamp">The source timestamp.</param>
        private static T WithTimestamp<T>(T field, DateTimeUtc timestamp)
            where T : BaseVariableState
        {
            field.Timestamp = timestamp;
            return field;
        }
    }

    /// <summary>
    /// The severity for an event.
    /// </summary>
    /// <remarks>
    /// Event severities can have any value between 1 and 1000. This enumeration provides default values.
    /// </remarks>
    public enum EventSeverity
    {
        /// <summary>
        /// The lowest possible severity.
        /// </summary>
        Min = 1,

        /// <summary>
        /// The event has low severity.
        /// </summary>
        Low = 100,

        /// <summary>
        /// The event has medium-low severity.
        /// </summary>
        MediumLow = 300,

        /// <summary>
        /// The event has medium severity.
        /// </summary>
        Medium = 500,

        /// <summary>
        /// The event has medium high severity.
        /// </summary>
        MediumHigh = 700,

        /// <summary>
        /// The event has high severity.
        /// </summary>
        High = 900,

        /// <summary>
        /// The highest possible severity.
        /// </summary>
        Max = 1000
    }
}
