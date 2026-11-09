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
        internal bool RegisterReceived(in DataSetMetaDataKey key, DataSetMetaDataType metaData, bool authenticated)
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
                m_entries[identity] = new RegisteredEntry(key, metaData, authenticated);
                evt = new DataSetMetaDataChangedEventArgs(key, existing.MetaData, metaData);
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
                _ = m_entries.Remove(identity);
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
            public RegisteredEntry(DataSetMetaDataKey key, DataSetMetaDataType metaData, bool authenticated)
            {
                Key = key;
                MetaData = metaData;
                Authenticated = authenticated;
            }

            public DataSetMetaDataKey Key { get; }
            public DataSetMetaDataType MetaData { get; }

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
    }

}
