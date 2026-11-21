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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Default in-memory implementation of
    /// <see cref="IDataSetMetaDataRegistry"/>. Backed by a dictionary
    /// keyed on (PublisherId, WriterGroupId, DataSetWriterId); the
    /// MajorVersion of the registered entry is then compared against
    /// the requested key's MajorVersion to classify the lookup outcome.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.2.3">
    /// Part 14 §5.2.3 DataSetMetaData</see> and the version
    /// reconciliation rules from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.4">
    /// Part 14 §6.2.9.4 DataSetReader DataSetMetaData</see>. Mutations
    /// are serialised via an internal <see cref="Lock"/>; reads also
    /// take the lock so a <see cref="Register"/> appears atomic to a
    /// concurrent <see cref="TryGet(in DataSetMetaDataKey, out DataSetMetaDataType)"/>
    /// caller.
    /// </remarks>
    public sealed class DataSetMetaDataRegistry : IDataSetMetaDataRegistry
    {
        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly Dictionary<IdentityKey, RegisteredEntry> m_entries = [];

        /// <summary>
        /// Host-controlled descriptors shared without promoting received metadata between connections.
        /// </summary>
        private readonly IDataSetMetaDataRegistry? m_trustedRegistry;

        /// <summary>
        /// Initializes a new, empty <see cref="DataSetMetaDataRegistry"/>.
        /// </summary>
        /// <param name="logger">
        /// Optional contextual logger. Defaults to a no-op logger when
        /// <see langword="null"/>.
        /// </param>
        public DataSetMetaDataRegistry(ILogger<DataSetMetaDataRegistry>? logger = null)
        {
            m_logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        /// <summary>
        /// Creates a connection-local registry over host-trusted metadata.
        /// Network registrations remain local and cannot downgrade trusted entries.
        /// </summary>
        internal DataSetMetaDataRegistry(
            IDataSetMetaDataRegistry trustedRegistry,
            ILogger<DataSetMetaDataRegistry>? logger)
            : this(logger)
        {
            m_trustedRegistry = trustedRegistry ?? throw new ArgumentNullException(nameof(trustedRegistry));
        }

        /// <summary>
        /// Default for <see cref="MaxEntries"/>: one entry per possible
        /// DataSetWriterId.
        /// </summary>
        public const int DefaultMaxEntries = ushort.MaxValue;

        /// <summary>
        /// Maximum number of distinct identities the registry holds.
        /// <see cref="Register"/> refuses to add a new identity once the
        /// limit is reached (replacing an existing identity is always
        /// allowed). Zero or less disables the limit.
        /// </summary>
        public int MaxEntries { get; init; } = DefaultMaxEntries;

        /// <summary>
        /// Default for <see cref="MaxUnauthenticatedCost"/>.
        /// </summary>
        public const long DefaultMaxUnauthenticatedCost = 16 * 1024 * 1024;

        /// <summary>
        /// Budget for metadata received without verified message security.
        /// The count of identities alone does not bound the memory such
        /// metadata retains, because one announcement can be megabytes in
        /// size. Every unauthenticated entry is charged the length of the
        /// frame that carried it plus a fixed amount per decoded element, and
        /// an announcement that would take the total above this budget is not
        /// registered. Zero or less disables the limit.
        /// </summary>
        public long MaxUnauthenticatedCost { get; init; } = DefaultMaxUnauthenticatedCost;

        /// <summary>
        /// Number of unauthenticated announcements that were not registered
        /// because they would have exceeded <see cref="MaxUnauthenticatedCost"/>.
        /// </summary>
        public long RejectedUnauthenticatedCount => Interlocked.Read(ref m_rejectedUnauthenticatedCount);

        /// <summary>
        /// Whether this registry itself holds an entry for the identity of
        /// the key, not counting the host-trusted registry behind it.
        /// </summary>
        internal bool ContainsLocal(in DataSetMetaDataKey key)
        {
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            lock (m_lock)
            {
                return m_entries.ContainsKey(identity);
            }
        }

        /// <inheritdoc/>
        public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;

        /// <inheritdoc/>
        public ArrayOf<DataSetMetaDataKey> Keys
        {
            get
            {
                ArrayOf<DataSetMetaDataKey> trustedKeys = m_trustedRegistry is null ? [] : m_trustedRegistry.Keys;
                lock (m_lock)
                {
                    if (trustedKeys.Count == 0 && m_entries.Count == 0)
                    {
                        return [];
                    }
                    if (trustedKeys.Count == 0)
                    {
                        var localKeys = new DataSetMetaDataKey[m_entries.Count];
                        int i = 0;
                        foreach (RegisteredEntry entry in m_entries.Values)
                        {
                            localKeys[i++] = entry.Key;
                        }
                        return localKeys;
                    }
                    var snapshot = new Dictionary<IdentityKey, DataSetMetaDataKey>();
                    foreach (DataSetMetaDataKey key in trustedKeys)
                    {
                        snapshot[new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId)] = key;
                    }
                    foreach (RegisteredEntry entry in m_entries.Values)
                    {
                        DataSetMetaDataKey key = entry.Key;
                        var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
                        if (entry.Authenticated || !snapshot.ContainsKey(identity))
                        {
                            snapshot[identity] = key;
                        }
                    }
                    return [.. snapshot.Values];
                }
            }
        }

        /// <inheritdoc/>
        public MetaDataMatchResult TryGet(in DataSetMetaDataKey key, out DataSetMetaDataType? metaData)
        {
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            RegisteredEntry entry;
            bool found;
            lock (m_lock)
            {
                found = m_entries.TryGetValue(identity, out entry);
            }
            if ((!found || !entry.Authenticated) && m_trustedRegistry is not null)
            {
                MetaDataMatchResult trusted = m_trustedRegistry.TryGet(in key, out metaData);
                if (trusted != MetaDataMatchResult.NotFound)
                {
                    return trusted;
                }
            }
            if (!found)
            {
                metaData = null;
                return MetaDataMatchResult.NotFound;
            }
            metaData = entry.MetaData;
            ConfigurationVersionDataType version = entry.MetaData.ConfigurationVersion
                ?? new ConfigurationVersionDataType();
            return version.MajorVersion == key.MajorVersion
                ? MetaDataMatchResult.Match : MetaDataMatchResult.MajorVersionMismatch;
        }

        /// <summary>
        /// Looks up a metadata entry by identity (without MajorVersion)
        /// and reports the version-drift relative to a requested
        /// MinorVersion. Convenience overload to support
        /// <see cref="MetaDataMatchResult.MinorVersionMismatch"/>
        /// classification when the caller knows both the requested
        /// MajorVersion and MinorVersion.
        /// </summary>
        /// <param name="publisherId">PublisherId of the lookup.</param>
        /// <param name="writerGroupId">WriterGroupId of the lookup.</param>
        /// <param name="dataSetWriterId">DataSetWriterId of the lookup.</param>
        /// <param name="majorVersion">Requested MajorVersion.</param>
        /// <param name="minorVersion">Requested MinorVersion.</param>
        /// <param name="metaData">
        /// On match, the registered metadata description. On mismatch,
        /// the registered description for the same identity (for
        /// diagnostics) or <see langword="null"/> when no entry exists.
        /// </param>
        /// <returns>The match classification.</returns>
        public MetaDataMatchResult TryGet(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            uint majorVersion,
            uint minorVersion,
            out DataSetMetaDataType? metaData)
        {
            var key = new DataSetMetaDataKey(publisherId, writerGroupId, dataSetWriterId, Uuid.Empty, majorVersion);
            MetaDataMatchResult result = TryGet(in key, out metaData);
            if (result != MetaDataMatchResult.Match)
            {
                return result;
            }
            uint currentMinor = metaData?.ConfigurationVersion?.MinorVersion ?? 0;
            return currentMinor == minorVersion
                ? MetaDataMatchResult.Match : MetaDataMatchResult.MinorVersionMismatch;
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The identity is new
        /// and the registry already holds <see cref="MaxEntries"/>
        /// entries.</exception>
        public void Register(in DataSetMetaDataKey key, DataSetMetaDataType metaData)
        {
            _ = RegisterReceived(in key, metaData, authenticated: true);
        }

        /// <summary>
        /// Registers received metadata with its authentication provenance.
        /// Unauthenticated metadata may replace only other unauthenticated metadata.
        /// </summary>
        internal bool RegisterReceived(
            in DataSetMetaDataKey key,
            DataSetMetaDataType metaData,
            bool authenticated,
            int frameLength = 0)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }
            if (!authenticated &&
                m_trustedRegistry is not null &&
                m_trustedRegistry.TryGet(in key, out _) != MetaDataMatchResult.NotFound)
            {
                return false;
            }

            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            long cost = authenticated ? 0 : EstimateCost(metaData, frameLength);
            DataSetMetaDataChangedEventArgs? evt;
            lock (m_lock)
            {
                bool found = m_entries.TryGetValue(identity, out RegisteredEntry existing);
                if (found && existing.Authenticated && !authenticated)
                {
                    return false;
                }
                if (!found &&
                    MaxEntries > 0 &&
                    m_entries.Count >= MaxEntries)
                {
                    throw new InvalidOperationException(
                        "The DataSetMetaData registry is full (MaxEntries " +
                        MaxEntries.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ").");
                }
                long previousCost = found ? existing.Cost : 0;
                if (!authenticated &&
                    MaxUnauthenticatedCost > 0 &&
                    m_unauthenticatedCost - previousCost + cost > MaxUnauthenticatedCost)
                {
                    evt = null;
                }
                else
                {
                    m_unauthenticatedCost += cost - previousCost;
                    m_entries[identity] = new RegisteredEntry(key, metaData, authenticated, cost);
                    evt = new DataSetMetaDataChangedEventArgs(key, existing.MetaData, metaData);
                }
            }
            if (evt is null)
            {
                // The sender is unauthenticated and can repeat a rejected
                // announcement at packet rate, so the warning is emitted for
                // the 1st, 2nd, 4th, 8th ... rejection only; the counter
                // carries the total.
                long rejected = Interlocked.Increment(ref m_rejectedUnauthenticatedCount);
                if ((rejected & (rejected - 1)) == 0)
                {
                    m_logger.UnauthenticatedMetadataOverBudget(
                        key.PublisherId,
                        key.WriterGroupId,
                        key.DataSetWriterId,
                        cost,
                        MaxUnauthenticatedCost,
                        rejected);
                }
                return false;
            }
            m_logger.RegisteredMetadata(
                key.PublisherId,
                key.WriterGroupId,
                key.DataSetWriterId,
                metaData.ConfigurationVersion?.MajorVersion ?? 0,
                metaData.ConfigurationVersion?.MinorVersion ?? 0);
            RaiseChanged(evt);
            return true;
        }

        /// <inheritdoc/>
        public void Remove(in DataSetMetaDataKey key)
        {
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            lock (m_lock)
            {
                if (m_entries.TryGetValue(identity, out RegisteredEntry removed))
                {
                    m_unauthenticatedCost -= removed.Cost;
                    _ = m_entries.Remove(identity);
                }
            }
        }

        /// <summary>
        /// Reports whether a lookup resolves to authenticated or host-configured metadata.
        /// </summary>
        internal bool IsAuthenticated(in DataSetMetaDataKey key)
        {
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            lock (m_lock)
            {
                if (m_entries.TryGetValue(identity, out RegisteredEntry entry) && entry.Authenticated)
                {
                    return true;
                }
            }
            return m_trustedRegistry is not null &&
                m_trustedRegistry.TryGet(in key, out _) != MetaDataMatchResult.NotFound;
        }

        /// <summary>
        /// Estimates what an unauthenticated metadata entry retains: the bytes of
        /// the frame that carried it plus a fixed amount per decoded element, since
        /// an element that is a few bytes on the wire becomes a full object.
        /// </summary>
        private static long EstimateCost(DataSetMetaDataType metaData, int frameLength)
        {
            long elements = 1;
            elements += metaData.Namespaces.Count;
            elements += metaData.SimpleDataTypes.Count;
            foreach (StructureDescription structure in metaData.StructureDataTypes.Span)
            {
                elements += 1 + (structure?.StructureDefinition?.Fields.Count ?? 0);
            }
            foreach (EnumDescription enumeration in metaData.EnumDataTypes.Span)
            {
                elements += 1 + (enumeration?.EnumDefinition?.Fields.Count ?? 0);
            }
            long values = 0;
            bool opaqueValues = false;
            foreach (FieldMetaData field in metaData.Fields.Span)
            {
                if (field is null)
                {
                    elements++;
                    continue;
                }
                elements += 1 + field.Properties.Count;
                foreach (KeyValuePair property in field.Properties.Span)
                {
                    if (property is not null)
                    {
                        values += EstimateValueCost(property.Value, ref opaqueValues);
                    }
                }
            }
            long frame = Math.Max(frameLength, 0);
            if (opaqueValues)
            {
                // A property holds values whose decoded size cannot be walked
                // cheaply. Every such element occupies at least one byte of
                // the frame, so the frame length bounds what they retain.
                values += frame * kOpaqueExpansion;
            }
            return frame + (elements * kElementCost) + values;
        }

        /// <summary>
        /// Estimates what the value of a field property retains beyond the
        /// frame bytes. Arrays of types that are small on the wire and large
        /// in memory are charged per element; values that nest further values
        /// (DataValue, Variant, ExtensionObject) and matrices are reported as
        /// opaque and charged from the frame length by the caller.
        /// </summary>
        private static long EstimateValueCost(in Variant value, ref bool opaque)
        {
            if (value.IsNull)
            {
                return 0;
            }
            TypeInfo typeInfo = value.TypeInfo;
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.DataValue:
                case BuiltInType.Variant:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DiagnosticInfo:
                    opaque = true;
                    return 0;
            }
            if (typeInfo.IsScalar)
            {
                return 0;
            }
            if (value.TryGetValue(out ArrayOf<string> strings))
            {
                return strings.Count * (long)kReferenceElementCost;
            }
            if (value.TryGetValue(out ArrayOf<ByteString> byteStrings))
            {
                return byteStrings.Count * (long)kReferenceElementCost;
            }
            if (value.TryGetValue(out ArrayOf<NodeId> nodeIds))
            {
                return nodeIds.Count * (long)kReferenceElementCost;
            }
            if (value.TryGetValue(out ArrayOf<ExpandedNodeId> expandedNodeIds))
            {
                return expandedNodeIds.Count * (long)kReferenceElementCost;
            }
            if (value.TryGetValue(out ArrayOf<QualifiedName> qualifiedNames))
            {
                return qualifiedNames.Count * (long)kReferenceElementCost;
            }
            if (value.TryGetValue(out ArrayOf<LocalizedText> localizedTexts))
            {
                return localizedTexts.Count * (long)kReferenceElementCost;
            }
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.String:
                case BuiltInType.ByteString:
                case BuiltInType.XmlElement:
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                    // A matrix of one of the types above.
                    opaque = true;
                    break;
            }

            // Arrays of fixed-size primitives occupy as many bytes in memory
            // as in the frame, which is already charged.
            return 0;
        }

        private const int kElementCost = 128;
        private const int kReferenceElementCost = 48;
        private const int kOpaqueExpansion = 64;
        private long m_unauthenticatedCost;
        private long m_rejectedUnauthenticatedCount;

        private void RaiseChanged(DataSetMetaDataChangedEventArgs evt)
        {
            try
            {
                MetaDataChanged?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                m_logger.MetaDataChangedHandlerThrew(
                    ex,
                    evt.Key.PublisherId,
                    evt.Key.WriterGroupId,
                    evt.Key.DataSetWriterId);
            }
        }

        private readonly record struct IdentityKey(
            PublisherId PublisherId,
            ushort WriterGroupId,
            ushort DataSetWriterId);

        private readonly struct RegisteredEntry
        {
            public RegisteredEntry(
                DataSetMetaDataKey key,
                DataSetMetaDataType metaData,
                bool authenticated,
                long cost)
            {
                Cost = cost;
                Key = key;
                MetaData = metaData;
                Authenticated = authenticated;
            }

            public DataSetMetaDataKey Key { get; }
            public DataSetMetaDataType MetaData { get; }

            /// <summary>
            /// Amount charged to the budget for unauthenticated metadata;
            /// zero for an authenticated entry.
            /// </summary>
            public long Cost { get; }

            /// <summary>
            /// Whether this entry was authenticated or registered by the host.
            /// </summary>
            public bool Authenticated { get; }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DataSetMetaDataRegistry"/>.
    /// </summary>
    internal static partial class DataSetMetaDataRegistryLog
    {
        [LoggerMessage(EventId = PubSubEventIds.DataSetMetaDataRegistry + 0, Level = LogLevel.Debug,
            Message = "DataSetMetaDataRegistry registered metadata for {Publisher}/{Group}/{Writer} " +
                "v{Major}.{Minor}.")]
        public static partial void RegisteredMetadata(
            this ILogger logger,
            PublisherId publisher,
            ushort group,
            ushort writer,
            uint major,
            uint minor);

        [LoggerMessage(EventId = PubSubEventIds.DataSetMetaDataRegistry + 1, Level = LogLevel.Error,
            Message = "DataSetMetaDataRegistry MetaDataChanged handler threw for {Publisher}/{Group}/{Writer}.")]
        public static partial void MetaDataChangedHandlerThrew(
            this ILogger logger,
            Exception exception,
            PublisherId publisher,
            ushort group,
            ushort writer);

        [LoggerMessage(EventId = PubSubEventIds.DataSetMetaDataRegistry + 2, Level = LogLevel.Warning,
            Message = "DataSetMetaDataRegistry ignored unauthenticated metadata for {Publisher}/{Group}/{Writer}: " +
                "cost {Cost} exceeds the remaining budget ({Budget} in total). " +
                "{Rejected} announcements were rejected so far.")]
        public static partial void UnauthenticatedMetadataOverBudget(
            this ILogger logger,
            PublisherId publisher,
            ushort group,
            ushort writer,
            long cost,
            long budget,
            long rejected);
    }

}
