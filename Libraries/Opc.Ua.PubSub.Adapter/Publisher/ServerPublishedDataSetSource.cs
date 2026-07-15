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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// <see cref="IPublishedDataSetSource"/> that produces PubSub DataSet snapshots
    /// from an external OPC UA server. The field set comes from the configured
    /// PublishedDataSet's <see cref="PublishedDataItemsDataType"/> published
    /// variables; each publish cycle resolves their current values through an
    /// injected <see cref="IReadStrategy"/> (cyclic Read or subscription
    /// cache) and the metadata is produced by an
    /// <see cref="IDataSetMetaDataBuilder"/>.
    /// </summary>
    /// <remarks>
    /// Sampling is fail-soft: the read strategy already maps faults to Bad-quality
    /// values, and any positional gap in the returned values is filled with a
    /// Bad-quality field so the writer always emits a complete DataSetMessage.
    /// </remarks>
    public sealed class ServerPublishedDataSetSource : IPublishedDataSetSource, IMetaDataChangeNotifier
    {
        private readonly PublishedDataSetDataType m_configuration;
        private readonly IReadStrategy m_strategy;
        private readonly IDataSetMetaDataBuilder m_metaDataBuilder;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// Creates a new external-server published dataset source.
        /// </summary>
        /// <param name="configuration">
        /// The configured PublishedDataSet whose published variables are sampled.
        /// </param>
        /// <param name="strategy">
        /// The read strategy that resolves current values for the published
        /// variables each publish cycle.
        /// </param>
        /// <param name="metaDataBuilder">
        /// The metadata builder that describes the emitted field set.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        /// <param name="timeProvider">
        /// The clock used to stamp snapshots; defaults to
        /// <see cref="TimeProvider.System"/> when not supplied.
        /// </param>
        public ServerPublishedDataSetSource(
            PublishedDataSetDataType configuration,
            IReadStrategy strategy,
            IDataSetMetaDataBuilder metaDataBuilder,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            m_metaDataBuilder = metaDataBuilder
                ?? throw new ArgumentNullException(nameof(metaDataBuilder));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<ServerPublishedDataSetSource>();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_metaDataBuilder.MetaDataChanged += OnMetaDataChanged;
        }

        /// <inheritdoc/>
        public event EventHandler? MetaDataChanged;

        private void OnMetaDataChanged(object? sender, EventArgs e)
        {
            MetaDataChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            return m_metaDataBuilder.BuildMetaData();
        }

        /// <inheritdoc/>
        public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureMetaDataResolvedAsync(cancellationToken).ConfigureAwait(false);

            ArrayOf<PublishedVariableDataType> publishedData =
                GetPublishedVariables(m_configuration);

            var fields = new List<DataSetField>(publishedData.Count);
            if (publishedData.Count > 0)
            {
                var nodesToRead = new ReadValueId[publishedData.Count];
                for (int i = 0; i < publishedData.Count; i++)
                {
                    nodesToRead[i] = CreateReadValueId(publishedData[i]);
                }

                ArrayOf<DataValue> values = await m_strategy
                    .ReadAsync(nodesToRead, cancellationToken)
                    .ConfigureAwait(false);

                for (int i = 0; i < publishedData.Count; i++)
                {
                    string fieldName = metaData is not null &&
                        !metaData.Fields.IsNull &&
                        i < metaData.Fields.Count
                        ? metaData.Fields[i]?.Name ?? string.Empty
                        : string.Empty;

                    DataValue value = !values.IsNull && i < values.Count
                        ? values[i]
                        : DataValue.FromStatusCode(StatusCodes.BadNoData);

                    fields.Add(new DataSetField
                    {
                        Name = fieldName,
                        Value = value.WrappedValue,
                        StatusCode = value.StatusCode,
                        SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                            ? default
                            : DateTimeUtc.From(value.SourceTimestamp)
                    });
                }
            }

            return new PublishedDataSetSnapshot(
                metaData?.ConfigurationVersion ?? new ConfigurationVersionDataType(),
                fields,
                DateTimeUtc.From(m_timeProvider.GetUtcNow()));
        }

        private async ValueTask EnsureMetaDataResolvedAsync(CancellationToken cancellationToken)
        {
            // The builder caches a fully-resolved result and otherwise retries on
            // each call, so delegating every cycle keeps metadata fresh without an
            // extra one-shot gate here.
            try
            {
                await m_metaDataBuilder.ResolveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.MetadataResolutionFailed(ex, m_configuration.Name);
            }
        }

        private static ReadValueId CreateReadValueId(PublishedVariableDataType publishedVariable)
        {
            var readValueId = new ReadValueId
            {
                NodeId = publishedVariable?.PublishedVariable ?? NodeId.Null,
                AttributeId = publishedVariable?.AttributeId ?? Attributes.Value
            };
            if (publishedVariable is not null &&
                !string.IsNullOrEmpty(publishedVariable.IndexRange))
            {
                readValueId.IndexRange = publishedVariable.IndexRange;
            }
            return readValueId;
        }

        private static ArrayOf<PublishedVariableDataType> GetPublishedVariables(
            PublishedDataSetDataType configuration)
        {
            ExtensionObject source = configuration.DataSetSource;
            if (!source.IsNull &&
                source.TryGetValue(out PublishedDataItemsDataType? items) &&
                items is not null &&
                !items.PublishedData.IsNull)
            {
                return items.PublishedData;
            }
            return [];
        }
    }

    /// <summary>
    /// Source-generated log messages for ServerPublishedDataSetSource.
    /// </summary>
    internal static partial class ServerPublishedDataSetSourceLog
    {
        [LoggerMessage(EventId = PubSubAdapterEventIds.ServerPublishedDataSetSource + 0,
            Level = LogLevel.Information,
            Message = "Metadata resolution for PublishedDataSet '{Name}' failed; " +
                "continuing with configured field types and retrying next cycle.")]
        public static partial void MetadataResolutionFailed(this ILogger logger, Exception exception, string? name);
    }

}
