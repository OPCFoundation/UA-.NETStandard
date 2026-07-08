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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Immutable wrapper around a loaded
    /// <see cref="PubSubConfigurationDataType"/> plus the materialised
    /// lookup tables the runtime needs for O(1) access to connections,
    /// writer groups, data set writers, reader groups, data set readers
    /// and published data sets. The snapshot is intentionally read-only:
    /// configuration mutations are expressed by building a *new*
    /// snapshot from a *new* <see cref="PubSubConfigurationDataType"/>
    /// and atomically swapping it in.
    /// </summary>
    /// <remarks>
    /// Implements the runtime view of the configuration model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PubSub configuration model</see>. Snapshots are
    /// created via <see cref="Create(PubSubConfigurationDataType, TimeProvider)"/>;
    /// the constructor only seeds the underlying configuration so that
    /// the index dictionaries can be built atomically before publication.
    /// </remarks>
    public sealed class PubSubConfigurationSnapshot
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationSnapshot"/>.
        /// Prefer
        /// <see cref="Create(PubSubConfigurationDataType, TimeProvider)"/>
        /// for normal use — it materialises the lookup indices in one
        /// pass and validates that the configuration has no duplicate
        /// names that would otherwise collide in those indices.
        /// </summary>
        /// <param name="configuration">Underlying configuration.</param>
        /// <param name="createdAt">Load / build timestamp.</param>
        public PubSubConfigurationSnapshot(
            PubSubConfigurationDataType configuration,
            DateTimeUtc createdAt)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Configuration = configuration;
            CreatedAt = createdAt;
            ConnectionsByName = EmptyConnections;
            WriterGroupsById = EmptyWriterGroups;
            DataSetWritersById = EmptyDataSetWriters;
            ReaderGroupsByName = EmptyReaderGroups;
            DataSetReadersByName = EmptyDataSetReaders;
            PublishedDataSetsByName = EmptyPublishedDataSets;
        }

        private PubSubConfigurationSnapshot(
            PubSubConfigurationDataType configuration,
            DateTimeUtc createdAt,
            IReadOnlyDictionary<string, PubSubConnectionDataType> connectionsByName,
            IReadOnlyDictionary<WriterGroupKey, WriterGroupDataType> writerGroupsById,
            IReadOnlyDictionary<DataSetWriterKey, DataSetWriterDataType> dataSetWritersById,
            IReadOnlyDictionary<ReaderGroupKey, ReaderGroupDataType> readerGroupsByName,
            IReadOnlyDictionary<DataSetReaderKey, DataSetReaderDataType> dataSetReadersByName,
            IReadOnlyDictionary<string, PublishedDataSetDataType> publishedDataSetsByName)
        {
            Configuration = configuration;
            CreatedAt = createdAt;
            ConnectionsByName = connectionsByName;
            WriterGroupsById = writerGroupsById;
            DataSetWritersById = dataSetWritersById;
            ReaderGroupsByName = readerGroupsByName;
            DataSetReadersByName = dataSetReadersByName;
            PublishedDataSetsByName = publishedDataSetsByName;
        }

        /// <summary>
        /// Underlying configuration.
        /// </summary>
        public PubSubConfigurationDataType Configuration { get; }

        /// <summary>
        /// Timestamp at which the snapshot was loaded or computed.
        /// </summary>
        public DateTimeUtc CreatedAt { get; }

        /// <summary>
        /// Connections keyed by
        /// <see cref="PubSubConnectionDataType.Name"/>.
        /// </summary>
        public IReadOnlyDictionary<string, PubSubConnectionDataType> ConnectionsByName { get; }

        /// <summary>
        /// Writer groups keyed by
        /// (<see cref="PubSubConnectionDataType.Name"/>,
        /// <see cref="WriterGroupDataType.WriterGroupId"/>).
        /// </summary>
        public IReadOnlyDictionary<WriterGroupKey, WriterGroupDataType> WriterGroupsById { get; }

        /// <summary>
        /// DataSet writers keyed by
        /// (<see cref="PubSubConnectionDataType.Name"/>,
        /// <see cref="WriterGroupDataType.WriterGroupId"/>,
        /// <see cref="DataSetWriterDataType.DataSetWriterId"/>).
        /// </summary>
        public IReadOnlyDictionary<DataSetWriterKey, DataSetWriterDataType> DataSetWritersById { get; }

        /// <summary>
        /// Reader groups keyed by
        /// (<see cref="PubSubConnectionDataType.Name"/>,
        /// <c>ReaderGroupDataType.Name</c>).
        /// </summary>
        public IReadOnlyDictionary<ReaderGroupKey, ReaderGroupDataType> ReaderGroupsByName { get; }

        /// <summary>
        /// DataSet readers keyed by
        /// (<see cref="PubSubConnectionDataType.Name"/>,
        /// <c>ReaderGroupDataType.Name</c>,
        /// <see cref="DataSetReaderDataType.Name"/>).
        /// </summary>
        public IReadOnlyDictionary<DataSetReaderKey, DataSetReaderDataType> DataSetReadersByName { get; }

        /// <summary>
        /// Published data sets keyed by
        /// <see cref="PublishedDataSetDataType.Name"/>.
        /// </summary>
        public IReadOnlyDictionary<string, PublishedDataSetDataType> PublishedDataSetsByName { get; }

        /// <summary>
        /// Builds an immutable snapshot from
        /// <paramref name="configuration"/>, materialising all lookup
        /// indices used by the runtime. The factory validates that no
        /// duplicate names cause an index collision (a duplicate
        /// connection name, a duplicate writer-group id within a
        /// connection, etc.). Deeper Part 14 validation is performed by
        /// <see cref="PubSubConfigurationValidator"/>.
        /// </summary>
        /// <param name="configuration">Source configuration.</param>
        /// <param name="timeProvider">
        /// Optional clock used to seed
        /// <see cref="CreatedAt"/>. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="PubSubConfigurationException">
        /// One or more of the configuration's identity dimensions
        /// (connection name, (connection, writer group id), (connection,
        /// writer group, data set writer id), (connection, reader group
        /// name), (connection, reader group, reader name), published
        /// data set name) contains a collision.
        /// </exception>
        public static PubSubConfigurationSnapshot Create(
            PubSubConfigurationDataType configuration,
            TimeProvider? timeProvider = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            TimeProvider clock = timeProvider ?? TimeProvider.System;
            var createdAt = DateTimeUtc.From(clock.GetUtcNow());

            var issues = new List<PubSubConfigurationIssue>();
            var connections = new Dictionary<string, PubSubConnectionDataType>(StringComparer.Ordinal);
            var writerGroups = new Dictionary<WriterGroupKey, WriterGroupDataType>();
            var dataSetWriters = new Dictionary<DataSetWriterKey, DataSetWriterDataType>();
            var readerGroups = new Dictionary<ReaderGroupKey, ReaderGroupDataType>();
            var dataSetReaders = new Dictionary<DataSetReaderKey, DataSetReaderDataType>();
            var publishedDataSets = new Dictionary<string, PublishedDataSetDataType>(StringComparer.Ordinal);

            if (!configuration.Connections.IsNull)
            {
                int connectionIndex = 0;
                foreach (PubSubConnectionDataType connection in configuration.Connections)
                {
                    string connectionName = connection.Name ?? string.Empty;
                    string connectionPath = $"Connections[{connectionIndex}]";
                    if (connectionName.Length == 0)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IndexIssueCodes.MissingConnectionName,
                            "PubSubConnection has an empty Name.",
                            connectionPath));
                    }
                    else if (!connections.TryAdd(connectionName, connection))
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IndexIssueCodes.DuplicateConnectionName,
                            $"Duplicate PubSubConnection name '{connectionName}'.",
                            connectionPath));
                    }
                    IndexWriterGroups(
                        connection,
                        connectionName,
                        connectionPath,
                        writerGroups,
                        dataSetWriters,
                        issues);
                    IndexReaderGroups(
                        connection,
                        connectionName,
                        connectionPath,
                        readerGroups,
                        dataSetReaders,
                        issues);
                    connectionIndex++;
                }
            }

            if (!configuration.PublishedDataSets.IsNull)
            {
                int pdsIndex = 0;
                foreach (PublishedDataSetDataType publishedDataSet in configuration.PublishedDataSets)
                {
                    string name = publishedDataSet.Name ?? string.Empty;
                    string path = $"PublishedDataSets[{pdsIndex}]";
                    if (name.Length == 0)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IndexIssueCodes.MissingPublishedDataSetName,
                            "PublishedDataSet has an empty Name.",
                            path));
                    }
                    else if (!publishedDataSets.TryAdd(name, publishedDataSet))
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IndexIssueCodes.DuplicatePublishedDataSetName,
                            $"Duplicate PublishedDataSet name '{name}'.",
                            path));
                    }
                    pdsIndex++;
                }
            }

            if (issues.Count > 0)
            {
                throw new PubSubConfigurationException(issues);
            }

            return new PubSubConfigurationSnapshot(
                configuration,
                createdAt,
                connections,
                writerGroups,
                dataSetWriters,
                readerGroups,
                dataSetReaders,
                publishedDataSets);
        }

        private static void IndexWriterGroups(
            PubSubConnectionDataType connection,
            string connectionName,
            string connectionPath,
            Dictionary<WriterGroupKey, WriterGroupDataType> writerGroups,
            Dictionary<DataSetWriterKey, DataSetWriterDataType> dataSetWriters,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.WriterGroups.IsNull)
            {
                return;
            }
            int wgIndex = 0;
            foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
            {
                string wgPath = $"{connectionPath}.WriterGroups[{wgIndex}]";
                ushort wgId = writerGroup.WriterGroupId;
                if (connectionName.Length > 0 && !writerGroups.TryAdd(new WriterGroupKey(connectionName, wgId), writerGroup))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IndexIssueCodes.DuplicateWriterGroupId,
                        $"Duplicate WriterGroupId '{wgId}' within connection '{connectionName}'.",
                        wgPath));
                }
                if (!writerGroup.DataSetWriters.IsNull)
                {
                    int dswIndex = 0;
                    foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
                    {
                        string dswPath = $"{wgPath}.DataSetWriters[{dswIndex}]";
                        ushort dswId = writer.DataSetWriterId;
                        if (connectionName.Length > 0 &&
                            !dataSetWriters.TryAdd(new DataSetWriterKey(connectionName, wgId, dswId), writer))
                        {
                            issues.Add(new PubSubConfigurationIssue(
                                PubSubConfigurationIssueSeverity.Error,
                                IndexIssueCodes.DuplicateDataSetWriterId,
                                $"Duplicate DataSetWriterId '{dswId}' within WriterGroup '{wgId}' of connection '{connectionName}'.",
                                dswPath));
                        }
                        dswIndex++;
                    }
                }
                wgIndex++;
            }
        }

        private static void IndexReaderGroups(
            PubSubConnectionDataType connection,
            string connectionName,
            string connectionPath,
            Dictionary<ReaderGroupKey, ReaderGroupDataType> readerGroups,
            Dictionary<DataSetReaderKey, DataSetReaderDataType> dataSetReaders,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.ReaderGroups.IsNull)
            {
                return;
            }
            int rgIndex = 0;
            foreach (ReaderGroupDataType readerGroup in connection.ReaderGroups)
            {
                string rgPath = $"{connectionPath}.ReaderGroups[{rgIndex}]";
                string rgName = readerGroup.Name ?? string.Empty;
                if (rgName.Length == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IndexIssueCodes.MissingReaderGroupName,
                        "ReaderGroup has an empty Name.",
                        rgPath));
                }
                else if (connectionName.Length > 0 &&
                    !readerGroups.TryAdd(new ReaderGroupKey(connectionName, rgName), readerGroup))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IndexIssueCodes.DuplicateReaderGroupName,
                        $"Duplicate ReaderGroup name '{rgName}' within connection '{connectionName}'.",
                        rgPath));
                }
                if (!readerGroup.DataSetReaders.IsNull)
                {
                    int drIndex = 0;
                    foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                    {
                        string drPath = $"{rgPath}.DataSetReaders[{drIndex}]";
                        string drName = reader.Name ?? string.Empty;
                        if (drName.Length == 0)
                        {
                            issues.Add(new PubSubConfigurationIssue(
                                PubSubConfigurationIssueSeverity.Error,
                                IndexIssueCodes.MissingDataSetReaderName,
                                "DataSetReader has an empty Name.",
                                drPath));
                        }
                        else if (connectionName.Length > 0 && rgName.Length > 0 &&
                            !dataSetReaders.TryAdd(new DataSetReaderKey(connectionName, rgName, drName), reader))
                        {
                            issues.Add(new PubSubConfigurationIssue(
                                PubSubConfigurationIssueSeverity.Error,
                                IndexIssueCodes.DuplicateDataSetReaderName,
                                $"Duplicate DataSetReader name '{drName}' within ReaderGroup '{rgName}' of connection '{connectionName}'.",
                                drPath));
                        }
                        drIndex++;
                    }
                }
                rgIndex++;
            }
        }

        private static readonly IReadOnlyDictionary<string, PubSubConnectionDataType> EmptyConnections
            = new Dictionary<string, PubSubConnectionDataType>(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<
            WriterGroupKey,
            WriterGroupDataType> EmptyWriterGroups
            = new Dictionary<WriterGroupKey, WriterGroupDataType>();
        private static readonly IReadOnlyDictionary<
            DataSetWriterKey,
            DataSetWriterDataType> EmptyDataSetWriters
            = new Dictionary<DataSetWriterKey, DataSetWriterDataType>();
        private static readonly IReadOnlyDictionary<
            ReaderGroupKey,
            ReaderGroupDataType> EmptyReaderGroups
            = new Dictionary<ReaderGroupKey, ReaderGroupDataType>();
        private static readonly IReadOnlyDictionary<
            DataSetReaderKey,
            DataSetReaderDataType> EmptyDataSetReaders
            = new Dictionary<DataSetReaderKey, DataSetReaderDataType>();
        private static readonly IReadOnlyDictionary<string, PublishedDataSetDataType> EmptyPublishedDataSets
            = new Dictionary<string, PublishedDataSetDataType>(StringComparer.Ordinal);

        private static class IndexIssueCodes
        {
            public const string MissingConnectionName = "PSC0101";
            public const string DuplicateConnectionName = "PSC0102";
            public const string DuplicateWriterGroupId = "PSC0103";
            public const string DuplicateDataSetWriterId = "PSC0104";
            public const string MissingReaderGroupName = "PSC0105";
            public const string DuplicateReaderGroupName = "PSC0106";
            public const string MissingDataSetReaderName = "PSC0107";
            public const string DuplicateDataSetReaderName = "PSC0108";
            public const string MissingPublishedDataSetName = "PSC0109";
            public const string DuplicatePublishedDataSetName = "PSC0110";
        }
    }

    /// <summary>
    /// Composite key identifying a <see cref="WriterGroupDataType"/>
    /// within a <see cref="PubSubConfigurationSnapshot"/>.
    /// </summary>
    /// <param name="Connection">
    /// Owning <see cref="PubSubConnectionDataType.Name"/>.
    /// </param>
    /// <param name="WriterGroupId">
    /// <see cref="WriterGroupDataType.WriterGroupId"/> unique within the
    /// connection.
    /// </param>
    public readonly record struct WriterGroupKey(
        string Connection,
        ushort WriterGroupId);

    /// <summary>
    /// Composite key identifying a <see cref="DataSetWriterDataType"/>
    /// within a <see cref="PubSubConfigurationSnapshot"/>.
    /// </summary>
    /// <param name="Connection">
    /// Owning <see cref="PubSubConnectionDataType.Name"/>.
    /// </param>
    /// <param name="WriterGroupId">
    /// Owning <see cref="WriterGroupDataType.WriterGroupId"/>.
    /// </param>
    /// <param name="DataSetWriterId">
    /// <see cref="DataSetWriterDataType.DataSetWriterId"/> unique within
    /// the writer group.
    /// </param>
    public readonly record struct DataSetWriterKey(
        string Connection,
        ushort WriterGroupId,
        ushort DataSetWriterId);

    /// <summary>
    /// Composite key identifying a <see cref="ReaderGroupDataType"/>
    /// within a <see cref="PubSubConfigurationSnapshot"/>.
    /// </summary>
    /// <param name="Connection">
    /// Owning <see cref="PubSubConnectionDataType.Name"/>.
    /// </param>
    /// <param name="ReaderGroupName">
    /// <c>ReaderGroupDataType.Name</c> unique within the connection.
    /// </param>
    public readonly record struct ReaderGroupKey(
        string Connection,
        string ReaderGroupName);

    /// <summary>
    /// Composite key identifying a <see cref="DataSetReaderDataType"/>
    /// within a <see cref="PubSubConfigurationSnapshot"/>.
    /// </summary>
    /// <param name="Connection">
    /// Owning <see cref="PubSubConnectionDataType.Name"/>.
    /// </param>
    /// <param name="ReaderGroupName">
    /// Owning <c>ReaderGroupDataType.Name</c>.
    /// </param>
    /// <param name="ReaderName">
    /// <see cref="DataSetReaderDataType.Name"/> unique within the reader
    /// group.
    /// </param>
    public readonly record struct DataSetReaderKey(
        string Connection,
        string ReaderGroupName,
        string ReaderName);
}
