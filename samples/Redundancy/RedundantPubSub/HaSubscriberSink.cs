// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace RedundantPubSub
{
    /// <summary>
    /// Subscribed data-set sink that logs the shape of each received data set. Only the
    /// activation-coordinator-active subscriber replica dispatches to this sink, so its log
    /// output identifies the currently active subscriber in a high-availability reader set.
    /// </summary>
    internal sealed class HaSubscriberSink : ISubscribedDataSetSink
    {
        /// <summary>
        /// Initializes a new <see cref="HaSubscriberSink"/>.
        /// </summary>
        /// <param name="logger">Logger used to report received data sets.</param>
        public HaSubscriberSink(ILogger<HaSubscriberSink> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Logs the field count and names of a received data set.
        /// </summary>
        /// <param name="fields">The decoded data-set fields.</param>
        /// <param name="cancellationToken">Token used to observe cancellation.</param>
        /// <returns>A completed task.</returns>
        public ValueTask WriteAsync(IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.DataSetReceived(fields.Count, FormatFieldNames(fields));
            }

            return ValueTask.CompletedTask;
        }

        private static string FormatFieldNames(IReadOnlyList<DataSetField> fields)
        {
            var builder = new StringBuilder();
            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(", ");
                }

                DataSetField field = fields[ii];
                builder.Append(string.IsNullOrEmpty(field.Name)
                    ? string.Create(CultureInfo.InvariantCulture, $"f{ii}")
                    : field.Name);
            }
            return builder.ToString();
        }

        private readonly ILogger<HaSubscriberSink> m_logger;
    }

    internal static partial class HaSubscriberSinkLog
    {
        [LoggerMessage(EventId = RedundantPubSubEventIds.HaSubscriberSink + 0, Level = LogLevel.Information,
            Message = "DataSet with {FieldCount} field(s) received: {FieldNames}.")]
        public static partial void DataSetReceived(this ILogger logger, int fieldCount, string fieldNames);
    }
}
