// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    public partial class AuditEventState
    {
        /// <summary>
        /// Initializes a new event.
        /// </summary>
        /// <param name="context">The current system context.</param>
        /// <param name="source">The source of the event.</param>
        /// <param name="severity">The severity for the event.</param>
        /// <param name="message">The default message.</param>
        /// <param name="status">Whether the operation that caused the event succeeded.</param>
        /// <param name="actionTimestamp">When the operation started.</param>
        public virtual void Initialize(
            ISystemContext context,
            NodeState? source,
            EventSeverity severity,
            LocalizedText message,
            bool status,
            DateTime actionTimestamp)
        {
            base.Initialize(context, source, severity, message);

            m_status = PropertyState<bool>.With<VariantBuilder>(this, status);

            if (actionTimestamp != DateTime.MinValue)
            {
                m_actionTimeStamp = PropertyState<DateTimeUtc>.With<VariantBuilder>(this, actionTimestamp);
            }

            if (context.NamespaceUris != null)
            {
                m_serverId = PropertyState<string>.With<VariantBuilder>(this, context.NamespaceUris.GetString(1)!); // namespace index 1 (server) expected to be present
            }

            if (context.AuditEntryId != null)
            {
                m_clientAuditEntryId = PropertyState<string>.With<VariantBuilder>(this, context.AuditEntryId);
            }

            if (context.UserId != null)
            {
                m_clientUserId = PropertyState<string>.With<VariantBuilder>(this, context.UserId);
            }
        }
    }
}
