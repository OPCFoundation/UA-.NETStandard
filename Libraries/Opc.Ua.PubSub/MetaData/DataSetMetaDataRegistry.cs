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

        /// <inheritdoc/>
        public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;

        /// <inheritdoc/>
        public ArrayOf<DataSetMetaDataKey> Keys
        {
            get
            {
                lock (m_lock)
                {
                    if (m_entries.Count == 0)
                    {
                        return [];
                    }
                    var snapshot = new DataSetMetaDataKey[m_entries.Count];
                    int i = 0;
                    foreach (RegisteredEntry entry in m_entries.Values)
                    {
                        snapshot[i++] = entry.Key;
                    }
                    return snapshot;
                }
            }
        }

        /// <inheritdoc/>
        public MetaDataMatchResult TryGet(in DataSetMetaDataKey key, out DataSetMetaDataType? metaData)
        {
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            lock (m_lock)
            {
                if (!m_entries.TryGetValue(identity, out RegisteredEntry entry))
                {
                    metaData = null;
                    return MetaDataMatchResult.NotFound;
                }
                metaData = entry.MetaData;
                ConfigurationVersionDataType version = entry.MetaData.ConfigurationVersion
                    ?? new ConfigurationVersionDataType();
                if (version.MajorVersion != key.MajorVersion)
                {
                    return MetaDataMatchResult.MajorVersionMismatch;
                }
                return MetaDataMatchResult.Match;
            }
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
            var identity = new IdentityKey(publisherId, writerGroupId, dataSetWriterId);
            lock (m_lock)
            {
                if (!m_entries.TryGetValue(identity, out RegisteredEntry entry))
                {
                    metaData = null;
                    return MetaDataMatchResult.NotFound;
                }
                metaData = entry.MetaData;
                ConfigurationVersionDataType version = entry.MetaData.ConfigurationVersion
                    ?? new ConfigurationVersionDataType();
                if (version.MajorVersion != majorVersion)
                {
                    return MetaDataMatchResult.MajorVersionMismatch;
                }
                if (version.MinorVersion != minorVersion)
                {
                    return MetaDataMatchResult.MinorVersionMismatch;
                }
                return MetaDataMatchResult.Match;
            }
        }

        /// <inheritdoc/>
        public void Register(in DataSetMetaDataKey key, DataSetMetaDataType metaData)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }
            DataSetMetaDataChangedEventArgs? evt = null;
            var identity = new IdentityKey(key.PublisherId, key.WriterGroupId, key.DataSetWriterId);
            lock (m_lock)
            {
                m_entries.TryGetValue(identity, out RegisteredEntry existing);
                m_entries[identity] = new RegisteredEntry(key, metaData);
                evt = new DataSetMetaDataChangedEventArgs(key, existing.MetaData, metaData);
            }
            m_logger.LogDebug(
                "DataSetMetaDataRegistry registered metadata for {Publisher}/{Group}/{Writer} v{Major}.{Minor}.",
                key.PublisherId,
                key.WriterGroupId,
                key.DataSetWriterId,
                metaData.ConfigurationVersion?.MajorVersion ?? 0,
                metaData.ConfigurationVersion?.MinorVersion ?? 0);
            RaiseChanged(evt);
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

        private void RaiseChanged(DataSetMetaDataChangedEventArgs evt)
        {
            try
            {
                MetaDataChanged?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "DataSetMetaDataRegistry MetaDataChanged handler threw for {Publisher}/{Group}/{Writer}.",
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
            public RegisteredEntry(DataSetMetaDataKey key, DataSetMetaDataType metaData)
            {
                Key = key;
                MetaData = metaData;
            }

            public DataSetMetaDataKey Key { get; }
            public DataSetMetaDataType MetaData { get; }
        }
    }
}
