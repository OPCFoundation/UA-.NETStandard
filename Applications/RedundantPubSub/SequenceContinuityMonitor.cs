/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace RedundantPubSub
{
    public sealed class SequenceContinuityMonitor
    {
        public SequenceContinuityMonitor(ILogger<SequenceContinuityMonitor> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnSequence(uint sequenceNumber, ArrayOf<DataSetField> fields)
        {
            string owner = FindOwner(fields);
            lock (m_lock)
            {
                if (m_lastSequence is null)
                {
                    m_lastSequence = sequenceNumber;
                    m_lastOwner = owner;
                    m_logger.LogInformation("SequenceNumber {SequenceNumber} received.", sequenceNumber);
                    return;
                }

                uint last = m_lastSequence.Value;
                if (sequenceNumber <= last)
                {
                    m_logger.LogError(
                        "DATA LOSS: sequence reset {Last} -> {Current} (subscriber must reset de-duplication).",
                        last,
                        sequenceNumber);
                }
                else if (!string.Equals(m_lastOwner, owner, StringComparison.Ordinal))
                {
                    m_logger.LogInformation(
                        "HA OK: sequence continued {Last} -> {Current} across failover (gap, no reset).",
                        last,
                        sequenceNumber);
                }
                else
                {
                    m_logger.LogInformation("SequenceNumber {Last} -> {Current}.", last, sequenceNumber);
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
}
