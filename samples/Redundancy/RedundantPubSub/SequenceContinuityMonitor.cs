/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace RedundantPubSub
{
    /// <summary>
    /// Tracks the per-writer SequenceNumber stream and reports whether it continued across a
    /// publisher failover (Hot/Warm) or was reset (Cold), so the sample can demonstrate data
    /// continuity versus data loss.
    /// </summary>
    public sealed class SequenceContinuityMonitor
    {
        /// <summary>
        /// Initializes a new <see cref="SequenceContinuityMonitor"/>.
        /// </summary>
        /// <param name="logger">Logger used to report continuity and data-loss transitions.</param>
        public SequenceContinuityMonitor(ILogger<SequenceContinuityMonitor> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Records the next observed SequenceNumber and logs whether it continued, reset
        /// (data loss), or continued across a failover to a different publisher owner.
        /// </summary>
        /// <param name="sequenceNumber">The SequenceNumber of the received data-set message.</param>
        /// <param name="fields">The received data-set fields (used to read the OwnerId).</param>
        public void OnSequence(uint sequenceNumber, ArrayOf<DataSetField> fields)
        {
            string owner = FindOwner(fields);
            lock (m_lock)
            {
                if (m_lastSequence is null)
                {
                    m_lastSequence = sequenceNumber;
                    m_lastOwner = owner;
                    m_logger.SequenceNumberReceived(sequenceNumber);
                    return;
                }

                uint last = m_lastSequence.Value;
                if (sequenceNumber <= last)
                {
                    m_logger.SequenceReset(last, sequenceNumber);
                }
                else if (!string.Equals(m_lastOwner, owner, StringComparison.Ordinal))
                {
                    m_logger.SequenceContinuedAcrossFailover(last, sequenceNumber);
                }
                else
                {
                    m_logger.SequenceNumberContinued(last, sequenceNumber);
                }

                m_lastSequence = sequenceNumber;
                m_lastOwner = owner;
            }
        }

        private static string FindOwner(ArrayOf<DataSetField> fields)
        {
            for (int ii = 0; ii < fields.Count; ii++)
            {
                DataSetField field = fields[ii];
                if (string.Equals(field.Name, "OwnerId", StringComparison.Ordinal) && !field.Value.IsNull)
                {
                    return field.Value.ToString() ?? "unknown";
                }
            }
            return "unknown";
        }

        private readonly Lock m_lock = new();
        private readonly ILogger<SequenceContinuityMonitor> m_logger;
        private uint? m_lastSequence;
        private string m_lastOwner = "unknown";
    }

    internal static partial class SequenceContinuityMonitorLog
    {
        [LoggerMessage(EventId = RedundantPubSubEventIds.SequenceContinuityMonitor + 0, Level = LogLevel.Information,
            Message = "SequenceNumber {SequenceNumber} received.")]
        public static partial void SequenceNumberReceived(this ILogger logger, uint sequenceNumber);

        [LoggerMessage(EventId = RedundantPubSubEventIds.SequenceContinuityMonitor + 1, Level = LogLevel.Error,
            Message = "DATA LOSS: sequence reset {Last} -> {Current} (subscriber must reset de-duplication).")]
        public static partial void SequenceReset(this ILogger logger, uint last, uint current);

        [LoggerMessage(EventId = RedundantPubSubEventIds.SequenceContinuityMonitor + 2, Level = LogLevel.Information,
            Message = "HA OK: sequence continued {Last} -> {Current} across failover (gap, no reset).")]
        public static partial void SequenceContinuedAcrossFailover(this ILogger logger, uint last, uint current);

        [LoggerMessage(EventId = RedundantPubSubEventIds.SequenceContinuityMonitor + 3, Level = LogLevel.Information,
            Message = "SequenceNumber {Last} -> {Current}.")]
        public static partial void SequenceNumberContinued(this ILogger logger, uint last, uint current);
    }
}
