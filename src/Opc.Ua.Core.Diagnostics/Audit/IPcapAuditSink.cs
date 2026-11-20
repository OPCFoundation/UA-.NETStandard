// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pcap.Audit
{
    /// <summary>
    /// Receives Pcap binding audit events for security-sensitive operations.
    /// </summary>
    public interface IPcapAuditSink
    {
        /// <summary>
        /// Emits an audit event.
        /// </summary>
        ValueTask OnEventAsync(PcapAuditEvent auditEvent, CancellationToken cancellationToken);
    }
}
