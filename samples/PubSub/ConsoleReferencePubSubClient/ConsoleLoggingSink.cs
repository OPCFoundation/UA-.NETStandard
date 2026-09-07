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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// <see cref="ISubscribedDataSetSink"/> that prints every received
    /// DataSetMessage to the console via an
    /// <see cref="ILogger"/>. Demonstrates the Part 14 §6.2.9 sink
    /// extension point.
    /// </summary>
    public sealed class ConsoleLoggingSink : ISubscribedDataSetSink
    {
        private readonly ILogger m_logger;
        private long m_received;

        public ConsoleLoggingSink(ILogger logger)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            m_logger = logger;
        }

        public ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                long sequence = Interlocked.Increment(ref m_received);
                var builder = new StringBuilder();
                for (int i = 0; i < fields.Count; i++)
                {
                    DataSetField field = fields[i];
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }
                    builder
                        .Append(string.IsNullOrEmpty(field.Name) ? $"f{i}" : field.Name)
                        .Append('=')
                        .Append(field.Value.IsNull ? "(null)" : field.Value.ToString());
                }

                string fieldsText = builder.ToString();
                m_logger.DataSetReceived(sequence, fields.Count, fieldsText);
            }

            return ValueTask.CompletedTask;
        }
    }

    internal static partial class ConsoleLoggingSinkLog
    {
        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.ConsoleLoggingSink + 0,
            Level = LogLevel.Information,
            Message = "DataSet #{Sequence} received ({FieldCount} fields): {Fields}")]
        public static partial void DataSetReceived(
            this ILogger logger,
            long sequence,
            int fieldCount,
            string fields);
    }
}
