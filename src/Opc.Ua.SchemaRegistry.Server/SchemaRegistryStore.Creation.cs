/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Server
{
    public sealed partial class SchemaRegistryStore
    {
        /// <summary>
        /// Gets detached durable draft descriptors, which do not claim parsed schema content.
        /// </summary>
        public ArrayOf<SchemaRegistrationDataType> Drafts =>
            [.. Current.Drafts.Values.Select(draft => (SchemaRegistrationDataType)draft.Clone())];

        /// <summary>
        /// Gets admitted empty or populated Group source identities.
        /// </summary>
        public ArrayOf<SchemaGroupSource> Groups
        {
            get
            {
                Generation generation = Current;
                return [.. generation.Groups.Select(group =>
                    new SchemaGroupSource(group.Key, group.Value, generation.EntityEpochs["/schemagroups/" + group.Key]))];
            }
        }

        /// <summary>
        /// Admits a Group's independently configured namespace identity through the same durable CAS.
        /// </summary>
        public async ValueTask CreateGroupAsync(string groupId, string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out _) ||
                XRegistryIdentifier.FromSourceIdentity(namespaceUri) != groupId)
            {
                throw Input("The Group identifier must match its namespace source identity.");
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Generation current = Current;
                if (current.Groups.TryGetValue(groupId, out string? previous))
                {
                    if (previous != namespaceUri)
                    {
                        throw Input("The Group identifier collides with another namespace.");
                    }
                    RequireActiveProjection();
                    return;
                }
                var groups = new Dictionary<string, string>(current.Groups, StringComparer.Ordinal) { [groupId] = namespaceUri };
                StatusCode status = await PublishAsync(current with { Groups = groups }, cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsUncertain(status))
                {
                    throw new ServiceResultException(status, "The Group is committed but its projection is pending.");
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Creates a durable exact-Version draft with no fabricated document or fingerprint.
        /// Empty VersionId is assigned by the server. Source identities must be independently supplied.
        /// </summary>
        public async ValueTask<SchemaRegistrationDataType> CreateDraftAsync(SchemaRegistrationDataType descriptor,
            bool getOrCreate = false, CancellationToken cancellationToken = default)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Generation current = Current;
                var draft = (SchemaRegistrationDataType)descriptor.Clone();
                if (string.IsNullOrEmpty(draft.VersionId))
                {
                    draft.VersionId = checked(current.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                SchemaReferenceDataType reference = RegistrationReference(draft);
                Validate(reference, draft);
                string xid = reference.Entity.Xid!;
                if (current.Entries.TryGetValue(xid, out SchemaEntry? entry))
                {
                    if (!getOrCreate || entry.Reference.EntityUri != draft.EntityUri || entry.Format != draft.Format)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                    }
                    RequireActiveProjection();
                    return entry.Registration is { } registered ? (SchemaRegistrationDataType)registered.Clone() : draft;
                }
                if (current.Drafts.TryGetValue(xid, out SchemaRegistrationDataType? existing))
                {
                    if (!getOrCreate || !existing.IsEqual(draft))
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                    }
                    RequireActiveProjection();
                    return (SchemaRegistrationDataType)existing.Clone();
                }
                string group = xid.Split('/')[2];
                if (current.Groups.TryGetValue(group, out string? uri) && uri != draft.NamespaceUri)
                {
                    throw Input("The draft namespace collides with the Group source identity.");
                }
                string resource = ResourceXid(xid, "ExactVersion");
                foreach (SchemaRegistrationDataType source in current.Drafts.Values)
                {
                    SchemaReferenceDataType admitted = RegistrationReference(source);
                    if (ResourceXid(admitted.Entity.Xid!, "ExactVersion") == resource &&
                        source.SchemaName != draft.SchemaName)
                    {
                        throw Input("The draft subject collides with an admitted source identity.");
                    }
                }
                var groups = new Dictionary<string, string>(current.Groups, StringComparer.Ordinal)
                {
                    [group] = draft.NamespaceUri!
                };
                var drafts = new SortedDictionary<string, SchemaRegistrationDataType>(current.Drafts, StringComparer.Ordinal)
                {
                    [xid] = draft
                };
                StatusCode status = await PublishAsync(current with { Groups = groups, Drafts = drafts }, cancellationToken)
                    .ConfigureAwait(false);
                if (StatusCode.IsUncertain(status))
                {
                    throw new ServiceResultException(status, "The draft is committed but its projection is pending.");
                }
                return (SchemaRegistrationDataType)draft.Clone();
            }
            finally
            {
                m_gate.Release();
            }
        }

        internal Generation CaptureCatalog() => Current;

        private void RequireActiveProjection()
        {
            if (m_projectionPending)
            {
                throw new ServiceResultException(StatusCodes.UncertainNotAllNodesAvailable,
                    "The catalog is committed but its projection is pending.");
            }
        }

        internal async ValueTask<T> PrepareFileOpenAsync<T>(Func<ValueTask<T>> prepare, CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await prepare().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private static uint ReadCount(BinaryDecoder decoder)
        {
            uint count = decoder.ReadUInt32(null);
            return count <= 100000 ? count : throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }

        private static Generation WithEntityEpochs(Generation current, Generation next)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string group in next.Groups.Keys)
            {
                paths.Add("/schemagroups/" + group);
            }
            foreach (string xid in next.Entries.Keys.Concat(next.Drafts.Keys))
            {
                string[] parts = xid.Split('/');
                paths.Add("/schemagroups/" + parts[2]);
                paths.Add("/schemagroups/" + parts[2] + "/schemas/" + parts[4]);
                paths.Add(xid);
            }
            var epochs = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                uint old = current.EntityEpochs.TryGetValue(path, out uint value) ? value : 0u;
                bool changed = old == 0 || Changed(current, next, path);
                if (changed && old == uint.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange, "The schema entity epoch is exhausted.");
                }
                epochs.Add(path, changed ? old + 1 : old);
            }
            return next with { EntityEpochs = epochs };
        }

        private static bool Changed(Generation current, Generation next, string path)
        {
            string prefix = path + "/";
            bool Contains(string xid) => xid == path || xid.StartsWith(prefix, StringComparison.Ordinal);
            string[] oldIds = [.. current.Entries.Keys.Where(Contains)];
            string[] newIds = [.. next.Entries.Keys.Where(Contains)];
            if (!oldIds.SequenceEqual(newIds, StringComparer.Ordinal))
            {
                return true;
            }
            foreach (string xid in newIds)
            {
                SchemaEntry old = current.Entries[xid];
                SchemaEntry now = next.Entries[xid];
                if (old.Document != now.Document || old.Metadata != now.Metadata)
                {
                    return true;
                }
            }
            if (!current.Drafts.Where(pair => Contains(pair.Key)).SequenceEqual(next.Drafts.Where(pair => Contains(pair.Key))))
            {
                return true;
            }
            return !current.Defaults.Where(pair => Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(next.Defaults.Where(pair => Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        /// <summary>
        /// An admitted namespace source identity and its own metadata revision.
        /// </summary>
        public readonly record struct SchemaGroupSource(string GroupId, string NamespaceUri, uint Epoch);
    }
}
