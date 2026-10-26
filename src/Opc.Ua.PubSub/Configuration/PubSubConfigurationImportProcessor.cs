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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Prepares isolated Part 14 reference edits. The application owns serialization and commit.
    /// </summary>
    internal sealed class PubSubConfigurationImportProcessor(
        IPubSubCommunicationIdAllocation? allocation,
        List<IPubSubCommunicationIdAllocationLease> leases,
        PubSubConfigurationValidator validator)
    {
        /// <summary>
        /// Applies removals, then parents before children, preserving the original result order.
        /// </summary>
        internal async ValueTask<(
            PubSubConfigurationDataType Configuration, PubSubConfigurationImportResult Result, bool HasChanges)>
            ApplyAsync(
                PubSubConfigurationDataType current,
                PubSubConfiguration2DataType input,
                ArrayOf<PubSubConfigurationRefDataType> references,
                bool complete,
                bool allowSecurity,
                CancellationToken cancellationToken)
        {
            PubSubConfiguration2DataType working = Promote(current);
            var results = new StatusCode[references.Count];
            var values = new List<PubSubConfigurationValueDataType>();
            bool changed = false;
            bool failed = false;
            foreach (int index in Enumerable.Range(0, references.Count)
                .OrderBy(i => Order(references[i])))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PubSubConfigurationRefDataType reference = references[index];
                PubSubConfiguration2DataType candidate = (PubSubConfiguration2DataType)working.Clone();
                var assigned = new List<PubSubConfigurationValueDataType>();
                try
                {
                    ValidateReference(input, reference);
                    if (!allowSecurity && ((uint)reference.ConfigurationMask & 0x1800) != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    bool edited = await ApplyReferenceAsync(
                        candidate, input, reference, assigned, cancellationToken).ConfigureAwait(false);
                    if (edited)
                    {
                        validator.Validate(candidate).ThrowIfInvalid();
                        _ = PubSubConfigurationSnapshot.Create(candidate);
                        if (allocation is not null)
                        {
                            await allocation.ValidateOwnershipAsync(candidate, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    working = candidate;
                    values.AddRange(assigned);
                    changed |= edited;
                    results[index] = StatusCodes.Good;
                }
                catch (ServiceResultException ex)
                {
                    results[index] = ex.StatusCode;
                    failed = true;
                }
                catch (PubSubConfigurationException)
                {
                    results[index] = StatusCodes.BadConfigurationError;
                    failed = true;
                }
            }
            if (complete && failed)
            {
                return (current, new PubSubConfigurationImportResult(false, results, [], []), false);
            }
            if (input.DefaultSecurityKeyServices.Count > 0)
            {
                working.DefaultSecurityKeyServices = input.DefaultSecurityKeyServices;
                changed = true;
            }
            var properties = working.ConfigurationProperties.ToList();
            foreach (KeyValuePair property in input.ConfigurationProperties)
            {
                properties.RemoveAll(p => p.Key == property.Key);
                if (!property.Value.IsNull)
                {
                    properties.Add((KeyValuePair)property.Clone());
                }
                changed = true;
            }
            working.ConfigurationProperties = new ArrayOf<KeyValuePair>(properties.ToArray());
            return (working, new PubSubConfigurationImportResult(
                changed, results, new ArrayOf<PubSubConfigurationValueDataType>(values.ToArray()), []), changed);
        }

        /// <summary>
        /// Preserves derived fields when the previous configuration is already the extended type.
        /// </summary>
        private static PubSubConfiguration2DataType Promote(PubSubConfigurationDataType value)
        {
            if (value is PubSubConfiguration2DataType extended)
            {
                return (PubSubConfiguration2DataType)extended.Clone();
            }
            return new PubSubConfiguration2DataType
            {
                Enabled = value.Enabled,
                Connections = value.Connections,
                PublishedDataSets = value.PublishedDataSets
            };
        }

        /// <summary>
        /// Orders dependency parents before their referenced children after all removals.
        /// </summary>
        private static int Order(PubSubConfigurationRefDataType? reference)
        {
            uint mask = (uint)(reference?.ConfigurationMask ?? 0);
            uint kind = mask & 0x1FF0;
            if ((mask & 15) == 8)
            {
                return kind switch
                {
                    16 or 32 => 0,
                    64 or 128 => 1,
                    256 => 2,
                    512 => 3,
                    _ => 0
                };
            }
            return kind switch
            {
                512 or 1024 or 2048 or 4096 => 1,
                256 => 12,
                64 or 128 => 13,
                _ => 14
            };
        }

        /// <summary>
        /// Checks every used and unused index and every mask bit before traversing input arrays.
        /// </summary>
        private static void ValidateReference(
            PubSubConfiguration2DataType input, PubSubConfigurationRefDataType? reference)
        {
            if (reference is null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            uint mask = (uint)reference.ConfigurationMask;
            uint kind = mask & 0x1FF0;
            uint operation = mask & 15;
            if ((mask & ~0x1FFFu) != 0 || kind == 0 || (kind & (kind - 1)) != 0 ||
                (operation != 1 && operation != 2 && operation != 3 && operation != 4 && operation != 8) ||
                ((operation & 2) != 0 && kind != 64 && kind != 128 && kind != 256))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            int count;
            if (kind <= 256)
            {
                Require(reference.ConnectionIndex < input.Connections.Count);
                PubSubConnectionDataType connection = input.Connections[reference.ConnectionIndex];
                if (connection is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                if (kind == 256)
                {
                    Require(reference.GroupIndex == 0 && reference.ElementIndex == 0);
                    return;
                }
                bool writer = kind == 16 || kind == 64;
                count = writer ? connection.WriterGroups.Count : connection.ReaderGroups.Count;
                Require(reference.GroupIndex < count);
                Require(writer
                    ? connection.WriterGroups[reference.GroupIndex] is not null
                    : connection.ReaderGroups[reference.GroupIndex] is not null);
                if (kind == 64 || kind == 128)
                {
                    Require(reference.ElementIndex == 0);
                    return;
                }
                count = writer
                    ? connection.WriterGroups[reference.GroupIndex].DataSetWriters.Count
                    : connection.ReaderGroups[reference.GroupIndex].DataSetReaders.Count;
            }
            else
            {
                Require(reference.ConnectionIndex == 0 && reference.GroupIndex == 0);
                count = kind switch
                {
                    512 => input.PublishedDataSets.Count,
                    1024 => input.SubscribedDataSets.Count,
                    2048 => input.SecurityGroups.Count,
                    _ => input.PubSubKeyPushTargets.Count
                };
            }
            Require(reference.ElementIndex < count);
            bool validElement = kind switch
            {
                16 => input.Connections[reference.ConnectionIndex].WriterGroups[reference.GroupIndex]
                    .DataSetWriters[reference.ElementIndex] is not null,
                32 => input.Connections[reference.ConnectionIndex].ReaderGroups[reference.GroupIndex]
                    .DataSetReaders[reference.ElementIndex] is not null,
                512 => input.PublishedDataSets[reference.ElementIndex] is not null,
                1024 => input.SubscribedDataSets[reference.ElementIndex] is not null,
                2048 => input.SecurityGroups[reference.ElementIndex] is not null,
                _ => input.PubSubKeyPushTargets[reference.ElementIndex] is not null
            };
            Require(validElement);
        }

        /// <summary>
        /// Applies one validated reference using only the names of unreferenced ancestors.
        /// </summary>
        private async ValueTask<bool> ApplyReferenceAsync(
            PubSubConfiguration2DataType target,
            PubSubConfiguration2DataType input,
            PubSubConfigurationRefDataType reference,
            List<PubSubConfigurationValueDataType> values,
            CancellationToken cancellationToken)
        {
            uint kind = (uint)reference.ConfigurationMask & 0x1FF0;
            uint operation = (uint)reference.ConfigurationMask & 15;
            int e = reference.ElementIndex;
            int c = reference.ConnectionIndex;
            int g = reference.GroupIndex;
            if (kind == 512)
            {
                target.PublishedDataSets = Edit(target.PublishedDataSets, input.PublishedDataSets[e],
                    p => p.Name, (p, n) => p.Name = n, operation, reference, values);
                if (operation == 8)
                {
                    string? removed = input.PublishedDataSets[e].Name;
                    foreach (PubSubConnectionDataType conn in target.Connections)
                    {
                        foreach (WriterGroupDataType group in conn.WriterGroups)
                        {
                            group.DataSetWriters = new ArrayOf<DataSetWriterDataType>(
                                group.DataSetWriters.ToList().Where(w => w.DataSetName != removed).ToArray());
                        }
                    }
                }
                return true;
            }
            if (kind == 1024)
            {
                target.SubscribedDataSets = Edit(target.SubscribedDataSets, input.SubscribedDataSets[e],
                    p => p.Name, (p, n) => p.Name = n, operation, reference, values);
                return true;
            }
            if (kind == 2048)
            {
                target.SecurityGroups = Edit(target.SecurityGroups, input.SecurityGroups[e],
                    p => p.Name, (p, n) => p.Name = n, operation, reference, values);
                return true;
            }
            if (kind == 4096)
            {
                target.PubSubKeyPushTargets = Edit(target.PubSubKeyPushTargets, input.PubSubKeyPushTargets[e],
                    p => p.ApplicationUri, (p, n) => p.ApplicationUri = n, operation, reference, values);
                return true;
            }
            PubSubConnectionDataType incomingConnection = input.Connections[c];
            if (kind == 256)
            {
                var incoming = (PubSubConnectionDataType)incomingConnection.Clone();
                incoming.WriterGroups = [];
                incoming.ReaderGroups = [];
                RequireMatchIdentity(operation, incoming.Name, incoming.PublisherId.IsNull);
                PubSubConnectionDataType? matched = (operation & 2) != 0
                    ? target.Connections.ToList().FirstOrDefault(p => MatchConnection(p, incoming)) : null;
                if (matched is not null)
                {
                    m_connections[c] = matched.Name!;
                    Assigned(reference, matched.Name, matched.PublisherId, values);
                    return false;
                }
                if (operation == 2)
                {
                    throw new ServiceResultException(StatusCodes.BadNoMatch);
                }
                if ((operation & 1) != 0 && incoming.PublisherId.IsNull)
                {
                    incoming.PublisherId = await GetAllocation().ResolvePublisherIdAsync(
                        incoming.TransportProfileUri ?? string.Empty, cancellationToken).ConfigureAwait(false);
                    Assigned(reference, incoming.Name, incoming.PublisherId, values);
                }
                target.Connections = Edit(target.Connections, incoming, p => p.Name, (p, n) => p.Name = n,
                    operation & ~2u, reference, values, (old, replacement) =>
                    {
                        replacement.WriterGroups = old.WriterGroups;
                        replacement.ReaderGroups = old.ReaderGroups;
                    });
                m_connections[c] = (operation & 1) != 0
                    ? target.Connections[target.Connections.Count - 1].Name! : incoming.Name!;
                return true;
            }
            string? connectionName = m_connections.TryGetValue(c, out string? mapped)
                ? mapped : incomingConnection.Name;
            PubSubConnectionDataType connection = target.Connections.ToList()
                .FirstOrDefault(p => p.Name == connectionName)
                ?? throw new ServiceResultException(StatusCodes.BadNotFound);
            if (kind == 64 || kind == 16)
            {
                WriterGroupDataType incomingGroup = incomingConnection.WriterGroups[g];
                if (kind == 64)
                {
                    var incoming = (WriterGroupDataType)incomingGroup.Clone();
                    incoming.DataSetWriters = [];
                    RequireMatchIdentity(operation, incoming.Name, incoming.WriterGroupId == 0);
                    if ((operation & 2) != 0 && GroupHeaderActive(incoming))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    WriterGroupDataType? matched = (operation & 2) != 0
                        ? connection.WriterGroups.ToList().FirstOrDefault(p => MatchWriterGroup(p, incoming)) : null;
                    if (matched is not null)
                    {
                        if (GroupHeaderActive(matched))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState);
                        }
                        m_writerGroups[(c, g)] = matched.Name!;
                        Assigned(reference, matched.Name, Variant.From(matched.WriterGroupId), values);
                        return false;
                    }
                    if (operation == 2)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoMatch);
                    }
                    if ((operation & 1) != 0 && incoming.WriterGroupId == 0)
                    {
                        incoming.WriterGroupId = await AllocateIdAsync(connection, false, target, cancellationToken)
                            .ConfigureAwait(false);
                        Assigned(reference, incoming.Name, Variant.From(incoming.WriterGroupId), values);
                    }
                    connection.WriterGroups = Edit(connection.WriterGroups, incoming,
                        p => p.Name, (p, n) => p.Name = n, operation & ~2u, reference, values,
                        (old, replacement) => replacement.DataSetWriters = old.DataSetWriters);
                    m_writerGroups[(c, g)] = (operation & 1) != 0
                        ? connection.WriterGroups[connection.WriterGroups.Count - 1].Name! : incoming.Name!;
                    return true;
                }
                string? groupName = m_writerGroups.TryGetValue((c, g), out string? groupMapped)
                    ? groupMapped : incomingGroup.Name;
                WriterGroupDataType group = connection.WriterGroups.ToList().FirstOrDefault(p => p.Name == groupName)
                    ?? throw new ServiceResultException(StatusCodes.BadNotFound);
                var writer = (DataSetWriterDataType)incomingGroup.DataSetWriters[e].Clone();
                if ((operation & 1) != 0 && writer.DataSetWriterId == 0)
                {
                    writer.DataSetWriterId = await AllocateIdAsync(connection, true, target, cancellationToken)
                        .ConfigureAwait(false);
                    Assigned(reference, writer.Name, Variant.From(writer.DataSetWriterId), values);
                }
                group.DataSetWriters = Edit(group.DataSetWriters, writer,
                    p => p.Name, (p, n) => p.Name = n, operation, reference, values);
                return true;
            }
            ReaderGroupDataType incomingReaderGroup = incomingConnection.ReaderGroups[g];
            if (kind == 128)
            {
                var incoming = (ReaderGroupDataType)incomingReaderGroup.Clone();
                incoming.DataSetReaders = [];
                RequireMatchIdentity(operation, incoming.Name, true);
                ReaderGroupDataType? matched = (operation & 2) != 0
                    ? connection.ReaderGroups.ToList().FirstOrDefault(p => MatchReaderGroup(p, incoming)) : null;
                if (matched is not null)
                {
                    m_readerGroups[(c, g)] = matched.Name!;
                    Assigned(reference, matched.Name, Variant.Null, values);
                    return false;
                }
                if (operation == 2)
                {
                    throw new ServiceResultException(StatusCodes.BadNoMatch);
                }
                connection.ReaderGroups = Edit(connection.ReaderGroups, incoming,
                    p => p.Name, (p, n) => p.Name = n, operation & ~2u, reference, values,
                    (old, replacement) => replacement.DataSetReaders = old.DataSetReaders);
                m_readerGroups[(c, g)] = (operation & 1) != 0
                    ? connection.ReaderGroups[connection.ReaderGroups.Count - 1].Name! : incoming.Name!;
                return true;
            }
            string? readerGroupName = m_readerGroups.TryGetValue((c, g), out string? readerMapped)
                ? readerMapped : incomingReaderGroup.Name;
            ReaderGroupDataType readerGroup = connection.ReaderGroups.ToList()
                .FirstOrDefault(p => p.Name == readerGroupName)
                ?? throw new ServiceResultException(StatusCodes.BadNotFound);
            readerGroup.DataSetReaders = Edit(readerGroup.DataSetReaders, incomingReaderGroup.DataSetReaders[e],
                p => p.Name, (p, n) => p.Name = n, operation, reference, values);
            return true;
        }

        /// <summary>
        /// Applies name-based operations without importing nested children.
        /// </summary>
        /// <typeparam name="T">The generated configuration element type.</typeparam>
        private static ArrayOf<T> Edit<T>(
            ArrayOf<T> existing, T input, Func<T, string?> name, Action<T, string> setName,
            uint operation, PubSubConfigurationRefDataType reference,
            List<PubSubConfigurationValueDataType> values, Action<T, T>? preserveChildren = null)
            where T : class, IEncodeable
        {
            var items = existing.ToList();
            T incoming = (T)input.Clone();
            string? requested = name(incoming);
            int index = items.FindIndex(p => name(p) == requested);
            if (operation == 1)
            {
                if (string.IsNullOrEmpty(requested))
                {
                    string assigned = $"Imported-{Guid.NewGuid():N}";
                    setName(incoming, assigned);
                    Assigned(reference, assigned, Variant.Null, values);
                }
                else if (index >= 0)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                items.Add(incoming);
            }
            else
            {
                if (index < 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNoMatch);
                }
                if (operation == 8)
                {
                    items.RemoveAt(index);
                }
                else
                {
                    preserveChildren?.Invoke(items[index], incoming);
                    items[index] = incoming;
                }
            }
            return new ArrayOf<T>(items.ToArray());
        }

        /// <summary>
        /// Returns an explicit provider capability rather than silently coercing publisher domains.
        /// </summary>
        private IPubSubCommunicationIdAllocation GetAllocation()
        {
            return allocation ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Allocates a transaction-owned identifier and retains its lease until commit or rollback.
        /// </summary>
        private async ValueTask<ushort> AllocateIdAsync(
            PubSubConnectionDataType connection, bool writer, PubSubConfiguration2DataType target,
            CancellationToken cancellationToken)
        {
            IPubSubCommunicationIdAllocationLease lease = await GetAllocation().AllocateAsync(
                connection.TransportProfileUri ?? string.Empty, connection.PublisherId, writer, target,
                cancellationToken).ConfigureAwait(false);
            leases.Add(lease);
            return lease.Id;
        }

        /// <summary>
        /// Combines assigned names and identifiers into one entry per reference.
        /// </summary>
        private static void Assigned(
            PubSubConfigurationRefDataType reference, string? name, Variant identifier,
            List<PubSubConfigurationValueDataType> values)
        {
            PubSubConfigurationValueDataType? value = values.FirstOrDefault(v => v.ConfigurationElement == reference);
            if (value is null)
            {
                value = new PubSubConfigurationValueDataType
                {
                    ConfigurationElement = (PubSubConfigurationRefDataType)reference.Clone()
                };
                values.Add(value);
            }
            if (!string.IsNullOrEmpty(name))
            {
                value.Name = name;
            }
            if (!identifier.IsNull)
            {
                value.Identifier = identifier;
            }
        }

        /// <summary>
        /// Enforces null incoming match identities.
        /// </summary>
        private static void RequireMatchIdentity(uint operation, string? name, bool nullIdentifier)
        {
            if ((operation & 2) != 0)
            {
                Require(string.IsNullOrEmpty(name) && nullIdentifier);
            }
        }

        /// <summary>
        /// Rejects malformed references with the standard element status.
        /// </summary>
        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
        }

        /// <summary>
        /// Matches only the requested property subset.
        /// </summary>
        private static bool PropertiesMatch(ArrayOf<KeyValuePair> existing, ArrayOf<KeyValuePair> requested)
        {
            return requested.ToList().All(p => existing.ToList().Any(q => q.Key == p.Key && q.Value == p.Value));
        }

        /// <summary>
        /// Matches the connection fields specified by Part 14.
        /// </summary>
        private static bool MatchConnection(PubSubConnectionDataType a, PubSubConnectionDataType b)
        {
            return a.TransportProfileUri == b.TransportProfileUri && a.Address == b.Address &&
                a.TransportSettings == b.TransportSettings &&
                PropertiesMatch(a.ConnectionProperties, b.ConnectionProperties);
        }

        /// <summary>
        /// Matches the writer-group fields specified by Part 14.
        /// </summary>
        private static bool MatchWriterGroup(WriterGroupDataType a, WriterGroupDataType b)
        {
            return a.SecurityMode == b.SecurityMode && a.SecurityGroupId == b.SecurityGroupId &&
                a.SecurityKeyServices == b.SecurityKeyServices && a.MaxNetworkMessageSize == b.MaxNetworkMessageSize &&
                a.PublishingInterval == b.PublishingInterval && a.KeepAliveTime == b.KeepAliveTime &&
                a.Priority == b.Priority && a.HeaderLayoutUri == b.HeaderLayoutUri &&
                a.TransportSettings == b.TransportSettings && a.MessageSettings == b.MessageSettings &&
                PropertiesMatch(a.GroupProperties, b.GroupProperties);
        }

        /// <summary>
        /// Matches the reader-group fields specified by Part 14.
        /// </summary>
        private static bool MatchReaderGroup(ReaderGroupDataType a, ReaderGroupDataType b)
        {
            return a.SecurityMode == b.SecurityMode && a.SecurityGroupId == b.SecurityGroupId &&
                a.SecurityKeyServices == b.SecurityKeyServices && a.MaxNetworkMessageSize == b.MaxNetworkMessageSize &&
                a.TransportSettings == b.TransportSettings && a.MessageSettings == b.MessageSettings &&
                PropertiesMatch(a.GroupProperties, b.GroupProperties);
        }

        /// <summary>
        /// Detects the GroupHeader option in UADP writer-group message settings.
        /// </summary>
        private static bool GroupHeaderActive(WriterGroupDataType group)
        {
            return group.MessageSettings.TryGetValue(out UadpWriterGroupMessageDataType? settings) &&
                settings is not null &&
                (settings.NetworkMessageContentMask & (uint)UadpNetworkMessageContentMask.GroupHeader) != 0;
        }

        /// <summary>Resolved connection names indexed by imported parent index.</summary>
        private readonly Dictionary<int, string> m_connections = [];

        /// <summary>Resolved writer-group names indexed by imported parent indices.</summary>
        private readonly Dictionary<(int Connection, int Group), string> m_writerGroups = [];

        /// <summary>Resolved reader-group names indexed by imported parent indices.</summary>
        private readonly Dictionary<(int Connection, int Group), string> m_readerGroups = [];
    }
}
