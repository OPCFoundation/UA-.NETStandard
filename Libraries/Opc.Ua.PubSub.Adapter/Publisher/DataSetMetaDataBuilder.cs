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
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// Config-first, server-fallback <see cref="IDataSetMetaDataBuilder"/>.
    /// The field set, order and names come from the configured PublishedDataSet
    /// (its <see cref="PublishedDataItemsDataType"/> published variables and any
    /// declared <see cref="DataSetMetaDataType"/>). For fields whose data-type
    /// information is not declared in the configuration the builder reads the
    /// source nodes' DataType, ValueRank and ArrayDimensions attributes from the
    /// external server to complete the <see cref="FieldMetaData"/>.
    /// </summary>
    /// <remarks>
    /// Resolution is fail-soft: a failing server read leaves the affected fields at
    /// the conservative default of <see cref="DataTypeIds.BaseDataType"/> /
    /// <see cref="BuiltInType.Variant"/> / <see cref="ValueRanks.Scalar"/>.
    /// </remarks>
    public sealed class DataSetMetaDataBuilder : IDataSetMetaDataBuilder, IDisposable
    {
        private readonly PublishedDataSetDataType m_configuration;
        private readonly IServerSession m_session;
        private readonly AdapterMetrics? m_metrics;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private DataSetMetaDataType? m_resolved;
        private int m_modelChangeMonitoringStarted;
        private int m_modelChangeRefreshRunning;
        private int m_modelChangeRefreshPending;
        private bool m_fullyResolved;

        /// <summary>
        /// Creates a metadata builder for the supplied PublishedDataSet configuration
        /// using the external-server session for the fallback attribute reads.
        /// </summary>
        /// <param name="configuration">
        /// The configured PublishedDataSet whose published variables describe the
        /// field set.
        /// </param>
        /// <param name="session">
        /// The external-server session used to read DataType / ValueRank /
        /// ArrayDimensions when they are not declared in the configuration.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        /// <param name="metrics">
        /// Optional metrics sink that records metadata resolution activity.
        /// </param>
        public DataSetMetaDataBuilder(
            PublishedDataSetDataType configuration,
            IServerSession session,
            ITelemetryContext telemetry,
            AdapterMetrics? metrics = null)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_metrics = metrics;
            m_logger = telemetry.CreateLogger<DataSetMetaDataBuilder>();
            m_session.ModelChanged += OnSessionModelChanged;
        }

        /// <inheritdoc/>
        public event EventHandler? MetaDataChanged;

        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            DataSetMetaDataType? resolved = Volatile.Read(ref m_resolved);
            if (resolved is not null)
            {
                return resolved;
            }
            FieldMetaData[] fields = BuildConfigFields(out _);
            return BuildMetaDataType(fields);
        }

        /// <inheritdoc/>
        public async ValueTask<DataSetMetaDataType> ResolveAsync(
            CancellationToken cancellationToken = default)
        {
            StartModelChangeMonitoring();

            DataSetMetaDataType? resolved = Volatile.Read(ref m_resolved);
            if (resolved is not null && Volatile.Read(ref m_fullyResolved))
            {
                return resolved;
            }

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            DataSetMetaDataType metaData;
            bool changed;
            try
            {
                if (m_resolved is not null && m_fullyResolved)
                {
                    return m_resolved;
                }
                (metaData, changed) = await ResolveCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }

            if (changed)
            {
                MetaDataChanged?.Invoke(this, EventArgs.Empty);
            }
            return metaData;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RefreshAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool changed;
            try
            {
                (_, changed) = await ResolveCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }

            if (changed)
            {
                MetaDataChanged?.Invoke(this, EventArgs.Empty);
            }
            return changed;
        }

        /// <summary>
        /// Builds the field set, resolves the unresolved field types from the
        /// server (recording success or failure), publishes the new metadata and
        /// reports whether it differs from the previously cached metadata. The
        /// caller must hold <see cref="m_gate"/>.
        /// </summary>
        private async ValueTask<(DataSetMetaDataType MetaData, bool Changed)> ResolveCoreAsync(
            CancellationToken cancellationToken)
        {
            DataSetMetaDataType? previous = m_resolved;

            FieldMetaData[] fields = BuildConfigFields(out List<UnresolvedField> unresolved);
            bool serverComplete = true;
            if (unresolved.Count > 0)
            {
                serverComplete = await ResolveFromServerAsync(fields, unresolved, cancellationToken)
                    .ConfigureAwait(false);
            }

            DataSetMetaDataType metaData = BuildMetaDataType(fields);
            Volatile.Write(ref m_resolved, metaData);
            Volatile.Write(ref m_fullyResolved, serverComplete);

            bool changed = previous is null || !MetaDataEquals(previous, metaData);
            return (metaData, changed);
        }

        /// <summary>
        /// Releases the resources owned by the builder.
        /// </summary>
        public void Dispose()
        {
            m_session.ModelChanged -= OnSessionModelChanged;
            m_gate.Dispose();
        }

        private void StartModelChangeMonitoring()
        {
            if (Interlocked.CompareExchange(ref m_modelChangeMonitoringStarted, 1, 0) != 0)
            {
                return;
            }

            _ = StartModelChangeMonitoringSafeAsync();
        }

        private async Task StartModelChangeMonitoringSafeAsync()
        {
            try
            {
                await m_session.StartModelChangeMonitoringAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogInformation(
                    ex,
                    "Metadata model-change monitoring could not be started.");
            }
        }

        private void OnSessionModelChanged(object? sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref m_modelChangeRefreshRunning, 1, 0) != 0)
            {
                Volatile.Write(ref m_modelChangeRefreshPending, 1);
                return;
            }

            _ = RefreshFromModelChangeAsync();
        }

        private async Task RefreshFromModelChangeAsync()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogInformation(
                            ex,
                            "Metadata refresh after a model-change event failed.");
                    }

                    if (Interlocked.Exchange(ref m_modelChangeRefreshPending, 0) == 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Volatile.Write(ref m_modelChangeRefreshRunning, 0);
                if (Volatile.Read(ref m_modelChangeRefreshPending) != 0 &&
                    Interlocked.CompareExchange(ref m_modelChangeRefreshRunning, 1, 0) == 0)
                {
                    _ = RefreshFromModelChangeAsync();
                }
            }
        }

        private async Task<bool> ResolveFromServerAsync(
            FieldMetaData[] fields,
            List<UnresolvedField> unresolved,
            CancellationToken cancellationToken)
        {
            var reads = new ReadValueId[unresolved.Count * 3];
            for (int t = 0; t < unresolved.Count; t++)
            {
                NodeId node = unresolved[t].SourceNode;
                int baseIndex = t * 3;
                reads[baseIndex] = new ReadValueId
                {
                    NodeId = node,
                    AttributeId = Attributes.DataType
                };
                reads[baseIndex + 1] = new ReadValueId
                {
                    NodeId = node,
                    AttributeId = Attributes.ValueRank
                };
                reads[baseIndex + 2] = new ReadValueId
                {
                    NodeId = node,
                    AttributeId = Attributes.ArrayDimensions
                };
            }

            ArrayOf<DataValue> results;
            try
            {
                if (!m_session.IsConnected)
                {
                    await m_session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                results = await m_session.ReadAsync(reads, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_metrics?.RecordMetadataResolution(false);
                m_logger.LogInformation(
                    ex,
                    "Metadata fallback read of {Count} field(s) failed; using default " +
                    "BaseDataType/Variant/Scalar field types and retrying later.",
                    unresolved.Count);
                return false;
            }

            for (int t = 0; t < unresolved.Count; t++)
            {
                int baseIndex = t * 3;
                if (baseIndex + 2 >= results.Count)
                {
                    break;
                }

                NodeId dataType = DataTypeIds.BaseDataType;
                if (results[baseIndex].WrappedValue.TryGetValue(out NodeId resolvedType) &&
                    !resolvedType.IsNull)
                {
                    dataType = resolvedType;
                }

                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType);
                if (builtInType == BuiltInType.Null)
                {
                    builtInType = BuiltInType.Variant;
                }

                int valueRank = ValueRanks.Scalar;
                if (results[baseIndex + 1].WrappedValue.TryGetValue(out int resolvedRank))
                {
                    valueRank = resolvedRank;
                }

                ArrayOf<uint> arrayDimensions = ArrayOf<uint>.Null;
                if (results[baseIndex + 2].WrappedValue.TryGetValue(out ArrayOf<uint> resolvedDims))
                {
                    arrayDimensions = resolvedDims;
                }

                FieldMetaData field = fields[unresolved[t].FieldIndex];
                field.DataType = dataType;
                field.BuiltInType = (byte)builtInType;
                field.ValueRank = valueRank;
                field.ArrayDimensions = arrayDimensions;
            }

            m_metrics?.RecordMetadataResolution(true);
            return true;
        }

        private static bool MetaDataEquals(DataSetMetaDataType left, DataSetMetaDataType right)
        {
            if (left.Fields.IsNull ||
                right.Fields.IsNull ||
                left.Fields.Count != right.Fields.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Fields.Count; i++)
            {
                FieldMetaData a = left.Fields[i];
                FieldMetaData b = right.Fields[i];
                if (a is null ||
                    b is null ||
                    !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                    a.BuiltInType != b.BuiltInType ||
                    a.ValueRank != b.ValueRank ||
                    a.DataType != b.DataType)
                {
                    return false;
                }
            }
            return true;
        }

        private FieldMetaData[] BuildConfigFields(out List<UnresolvedField> unresolved)
        {
            unresolved = [];
            ArrayOf<PublishedVariableDataType> publishedData = GetPublishedVariables(m_configuration);
            var fields = new FieldMetaData[publishedData.Count];
            for (int i = 0; i < publishedData.Count; i++)
            {
                PublishedVariableDataType pv = publishedData[i];
                FieldMetaData? configured = GetConfiguredField(i);
                string name = ResolveFieldName(configured, i);

                if (IsTypeKnown(configured))
                {
                    fields[i] = CreateField(
                        name,
                        configured!.DataType,
                        (BuiltInType)configured.BuiltInType,
                        configured.ValueRank,
                        configured.ArrayDimensions,
                        configured);
                    continue;
                }

                fields[i] = CreateField(
                    name,
                    DataTypeIds.BaseDataType,
                    BuiltInType.Variant,
                    ValueRanks.Scalar,
                    ArrayOf<uint>.Null,
                    configured);

                NodeId node = pv?.PublishedVariable ?? NodeId.Null;
                if (!node.IsNull)
                {
                    unresolved.Add(new UnresolvedField(i, node));
                }
            }
            return fields;
        }

        private DataSetMetaDataType BuildMetaDataType(FieldMetaData[] fields)
        {
            DataSetMetaDataType? configured = m_configuration.DataSetMetaData;
            var metaData = new DataSetMetaDataType
            {
                Name = !string.IsNullOrEmpty(configured?.Name)
                    ? configured!.Name
                    : m_configuration.Name ?? string.Empty,
                Fields = fields,
                ConfigurationVersion = configured?.ConfigurationVersion
                    ?? new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
            };
            if (configured is not null)
            {
                metaData.Description = configured.Description;
                metaData.DataSetClassId = configured.DataSetClassId;
                if (!configured.Namespaces.IsNull)
                {
                    metaData.Namespaces = configured.Namespaces;
                }
            }
            return metaData;
        }

        private FieldMetaData? GetConfiguredField(int index)
        {
            DataSetMetaDataType? configured = m_configuration.DataSetMetaData;
            if (configured is null ||
                configured.Fields.IsNull ||
                index >= configured.Fields.Count)
            {
                return null;
            }
            return configured.Fields[index];
        }

        private static bool IsTypeKnown(FieldMetaData? configured)
        {
            return configured is not null && configured.BuiltInType != (byte)BuiltInType.Null;
        }

        private static string ResolveFieldName(FieldMetaData? configured, int index)
        {
            if (configured is not null && !string.IsNullOrEmpty(configured.Name))
            {
                return configured.Name;
            }
            return $"Field{index + 1}";
        }

        private static FieldMetaData CreateField(
            string name,
            NodeId dataType,
            BuiltInType builtInType,
            int valueRank,
            ArrayOf<uint> arrayDimensions,
            FieldMetaData? template)
        {
            var field = new FieldMetaData
            {
                Name = name,
                DataType = dataType,
                BuiltInType = (byte)builtInType,
                ValueRank = valueRank,
                ArrayDimensions = arrayDimensions,
                Properties = template is not null && !template.Properties.IsNull
                    ? template.Properties
                    : []
            };
            if (template is not null)
            {
                field.Description = template.Description;
                field.DataSetFieldId = template.DataSetFieldId;
                field.FieldFlags = template.FieldFlags;
                field.MaxStringLength = template.MaxStringLength;
            }
            return field;
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
            return ArrayOf<PublishedVariableDataType>.Empty;
        }

        private readonly record struct UnresolvedField(int FieldIndex, NodeId SourceNode);
    }
}
