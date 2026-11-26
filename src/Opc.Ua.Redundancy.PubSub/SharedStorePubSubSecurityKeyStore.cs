// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Distributed PubSub SKS key store backed by a protected shared key/value store.
    /// </summary>
    /// <remarks>
    /// <see cref="TryCommitSecurityGroupAsync"/> keeps authorization and the current key in
    /// the same conditionally committed record.
    /// </remarks>
    public sealed class SharedStorePubSubSecurityKeyStore : ICoordinatedPubSubSecurityKeyStore
    {
        /// <summary>
        /// Initializes a new <see cref="SharedStorePubSubSecurityKeyStore"/>.
        /// </summary>
        /// <param name="store">
        /// Freshness-protected store of the redundant set, or an in-memory store for use in one process.
        /// </param>
        /// <param name="protector">Record protector used to encrypt and authenticate stored key material.</param>
        /// <param name="context">Service message context used for UA binary fields.</param>
        public SharedStorePubSubSecurityKeyStore(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            IServiceMessageContext context)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (store is not (FreshnessProtectedKeyValueStore or InMemorySharedKeyValueStore))
            {
                throw new ArgumentException("Distributed SKS requires a freshness-protected store.", nameof(store));
            }
            if (store is FreshnessProtectedKeyValueStore && protector is NullRecordProtector)
            {
                throw new ArgumentException("Distributed SKS requires authenticated encryption.", nameof(protector));
            }
            m_logger = context.Telemetry.CreateLogger<SharedStorePubSubSecurityKeyStore>();
        }

        /// <inheritdoc/>
        public bool IsProcessLocal => m_store is InMemorySharedKeyValueStore;

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<string>> GetSecurityGroupIdsAsync(
            CancellationToken cancellationToken = default)
        {
            var groupIds = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(PubSubRedundancyStoreKeys.SecurityKeyPrefix, cancellationToken)
                .ConfigureAwait(false))
            {
                if (entry.Key.StartsWith(PubSubRedundancyStoreKeys.SecurityKeyPrefix, StringComparison.Ordinal))
                {
                    groupIds.Add(entry.Key[PubSubRedundancyStoreKeys.SecurityKeyPrefix.Length..]);
                }
            }

            return new ArrayOf<string>(groupIds.ToArray());
        }

        /// <inheritdoc/>
        public async ValueTask<SksSecurityGroup?> GetSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default)
        {
            (SksSecurityGroup? group, _) = await ReadSecurityGroupCoreAsync(
                securityGroupId, !IsProcessLocal, cancellationToken)
                .ConfigureAwait(false);
            return group;
        }

        /// <inheritdoc/>
        public ValueTask<(SksSecurityGroup? Group, ByteString Revision)> ReadSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default)
        {
            return ReadSecurityGroupCoreAsync(securityGroupId, true, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<bool> TryCommitSecurityGroupAsync(
            SksSecurityGroup group,
            ByteString expectedRevision,
            CancellationToken cancellationToken = default)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            cancellationToken.ThrowIfCancellationRequested();

            string key = GetSecurityGroupKey(group.SecurityGroupId);
            ByteString record = ProtectSecurityGroup(group, key);
            if (FencedSharedStoreValue.TryExtractPayload(expectedRevision, out _, out long token) && token != 0)
            {
                record = FencedSharedStoreValue.Wrap(record, token);
            }

            return m_store.CompareAndSwapAsync(key, expectedRevision, record, cancellationToken);
        }

        /// <inheritdoc/>
        ValueTask IPubSubSecurityKeyStore.SaveSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken)
        {
            return SaveSecurityGroupCoreAsync(group, null, cancellationToken);
        }

        /// <inheritdoc/>
        ValueTask IPubSubSecurityKeyStore.SaveSecurityGroupAsync(
            SksSecurityGroup group,
            long fencingToken,
            CancellationToken cancellationToken)
        {
            return SaveSecurityGroupCoreAsync(group, fencingToken, cancellationToken);
        }

        private ValueTask SaveSecurityGroupCoreAsync(
            SksSecurityGroup group,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            if (!IsProcessLocal)
            {
                throw new NotSupportedException("Distributed SKS writes require an expected group revision.");
            }

            string key = GetSecurityGroupKey(group.SecurityGroupId);
            return FencedSharedStoreValue.StoreAsync(
                m_store,
                key,
                ProtectSecurityGroup(group, key),
                fencingToken,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default)
        {
            if (securityGroupId is null)
            {
                throw new ArgumentNullException(nameof(securityGroupId));
            }

            return m_store.DeleteAsync(GetSecurityGroupKey(securityGroupId), cancellationToken);
        }

        private async ValueTask<(SksSecurityGroup? Group, ByteString Revision)> ReadSecurityGroupCoreAsync(
            string securityGroupId,
            bool requireValidRecord,
            CancellationToken cancellationToken)
        {
            if (securityGroupId is null)
            {
                throw new ArgumentNullException(nameof(securityGroupId));
            }

            string key = GetSecurityGroupKey(securityGroupId);
            (bool found, ByteString record) = await m_store.TryGetAsync(key, cancellationToken)
                .ConfigureAwait(false);
            if (!found)
            {
                return default;
            }

            if (FencedSharedStoreValue.TryExtractPayload(record, out ByteString payload, out _) &&
                m_protector.TryUnprotect(
                    RecordProtectionContext.Create("pubsub-security-group", key), payload, out ByteString plaintext))
            {
                SksSecurityGroup group = DeserializeSecurityGroup(plaintext);
                if (string.Equals(group.SecurityGroupId, securityGroupId, StringComparison.Ordinal))
                {
                    return (group, record);
                }
            }

            m_logger.UnableToUnprotectSharedPubSubSecurityGroupRecord(securityGroupId);
            if (requireValidRecord)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "The stored SecurityGroup could not be authenticated.");
            }
            return default;
        }

        private ByteString ProtectSecurityGroup(SksSecurityGroup group, string key)
        {
            return m_protector.Protect(
                RecordProtectionContext.Create("pubsub-security-group", key), SerializeSecurityGroup(group));
        }

        private ByteString SerializeSecurityGroup(SksSecurityGroup group)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(SerializationVersion);
                // Distinguish revisions even when a process-local store has no record protector.
                writer.Write(Guid.NewGuid().ToByteArray());
                writer.Write(group.SecurityGroupId);
                writer.Write(group.SecurityPolicyUri);
                writer.Write(group.KeyLifetime.Ticks);
                writer.Write(group.MaxFutureKeyCount);
                writer.Write(group.MaxPastKeyCount);
                writer.Write(group.CurrentKeyIndex);
                WriteSecurityKeys(writer, group.Keys);
                WriteStrings(writer, group.AuthorizedCallerIdentities);
                WriteRolePermissions(writer, group.RolePermissions);
            }

            return new ByteString(stream.ToArray());
        }

        private SksSecurityGroup DeserializeSecurityGroup(ByteString plaintext)
        {
            byte[] buffer = plaintext.IsNull ? [] : plaintext.ToArray();
            using var stream = new MemoryStream(buffer, false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, false);
            int version = reader.ReadInt32();
            if (version is not (1 or SerializationVersion))
            {
                throw new InvalidDataException("Unsupported shared PubSub SecurityGroup record version.");
            }
            if (version == SerializationVersion && reader.ReadBytes(16).Length != 16)
            {
                throw new EndOfStreamException();
            }

            string securityGroupId = reader.ReadString();
            string securityPolicyUri = reader.ReadString();
            var keyLifetime = TimeSpan.FromTicks(reader.ReadInt64());
            int maxFutureKeyCount = reader.ReadInt32();
            int maxPastKeyCount = reader.ReadInt32();
            int currentKeyIndex = version == SerializationVersion ? reader.ReadInt32() : 0;
            ArrayOf<PubSubSecurityKey> keys = ReadSecurityKeys(reader);
            ArrayOf<string> authorizedCallerIdentities = ReadStrings(reader);
            ArrayOf<RolePermissionType> rolePermissions = ReadRolePermissions(reader);

            return new SksSecurityGroup(
                securityGroupId,
                securityPolicyUri,
                keyLifetime,
                maxFutureKeyCount,
                maxPastKeyCount,
                keys,
                authorizedCallerIdentities,
                rolePermissions)
            {
                CurrentKeyIndex = currentKeyIndex
            };
        }

        private void WriteRolePermissions(BinaryWriter writer, ArrayOf<RolePermissionType> rolePermissions)
        {
            if (rolePermissions.IsNull)
            {
                writer.Write(NullArrayLength);
                return;
            }

            writer.Write(rolePermissions.Count);
            for (int ii = 0; ii < rolePermissions.Count; ii++)
            {
                using var stream = new MemoryStream();
                using (var encoder = new BinaryEncoder(stream, m_context, true))
                {
                    rolePermissions[ii].Encode(encoder);
                }

                byte[] buffer = stream.ToArray();
                writer.Write(buffer.Length);
                writer.Write(buffer);
            }
        }

        private ArrayOf<RolePermissionType> ReadRolePermissions(BinaryReader reader)
        {
            int count = ReadArrayLength(reader);
            if (count == NullArrayLength)
            {
                return default;
            }

            var rolePermissions = new RolePermissionType[count];
            for (int ii = 0; ii < rolePermissions.Length; ii++)
            {
                byte[] buffer = ReadByteArray(reader);
                using var stream = new MemoryStream(buffer, false);
                using var decoder = new BinaryDecoder(stream, m_context, false);
                var rolePermission = new RolePermissionType();
                rolePermission.Decode(decoder);
                rolePermissions[ii] = rolePermission;
            }

            return new ArrayOf<RolePermissionType>(rolePermissions);
        }

        private static string GetSecurityGroupKey(string securityGroupId)
        {
            return PubSubRedundancyStoreKeys.SecurityKeyPrefix + securityGroupId;
        }

        private static void WriteSecurityKeys(BinaryWriter writer, ArrayOf<PubSubSecurityKey> keys)
        {
            if (keys.IsNull)
            {
                writer.Write(NullArrayLength);
                return;
            }

            writer.Write(keys.Count);
            for (int ii = 0; ii < keys.Count; ii++)
            {
                PubSubSecurityKey key = keys[ii];
                writer.Write(key.TokenId);
                WriteByteString(writer, key.SigningKey);
                WriteByteString(writer, key.EncryptingKey);
                WriteByteString(writer, key.KeyNonce);
                writer.Write(key.IssuedAt.Value);
                writer.Write(key.Lifetime.Ticks);
            }
        }

        private static ArrayOf<PubSubSecurityKey> ReadSecurityKeys(BinaryReader reader)
        {
            int count = ReadArrayLength(reader);
            if (count == NullArrayLength)
            {
                return default;
            }

            var keys = new PubSubSecurityKey[count];
            for (int ii = 0; ii < keys.Length; ii++)
            {
                keys[ii] = new PubSubSecurityKey(
                    reader.ReadUInt32(),
                    ReadByteString(reader),
                    ReadByteString(reader),
                    ReadByteString(reader),
                    new DateTimeUtc(reader.ReadInt64()),
                    TimeSpan.FromTicks(reader.ReadInt64()));
            }

            return new ArrayOf<PubSubSecurityKey>(keys);
        }

        private static void WriteStrings(BinaryWriter writer, ArrayOf<string> values)
        {
            if (values.IsNull)
            {
                writer.Write(NullArrayLength);
                return;
            }

            writer.Write(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                writer.Write(values[ii]);
            }
        }

        private static ArrayOf<string> ReadStrings(BinaryReader reader)
        {
            int count = ReadArrayLength(reader);
            if (count == NullArrayLength)
            {
                return default;
            }

            string[] values = new string[count];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = reader.ReadString();
            }

            return new ArrayOf<string>(values);
        }

        private static void WriteByteString(BinaryWriter writer, ByteString value)
        {
            if (value.IsNull)
            {
                writer.Write(NullByteStringLength);
                return;
            }

            byte[] bytes = value.ToArray();
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static ByteString ReadByteString(BinaryReader reader)
        {
            byte[] buffer = ReadByteArray(reader);
            return buffer.Length == 0 ? ByteString.Empty : new ByteString(buffer);
        }

        private static byte[] ReadByteArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == NullByteStringLength)
            {
                return [];
            }
            if (length < NullByteStringLength)
            {
                throw new InvalidDataException("Invalid byte string length.");
            }

            return reader.ReadBytes(length);
        }

        private static int ReadArrayLength(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < NullArrayLength)
            {
                throw new InvalidDataException("Invalid array length.");
            }

            return count;
        }

        private const int SerializationVersion = 2;
        private const int NullArrayLength = -1;
        private const int NullByteStringLength = -1;

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly IServiceMessageContext m_context;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="SharedStorePubSubSecurityKeyStore"/>.
    /// </summary>
    internal static partial class SharedStorePubSubSecurityKeyStoreLog
    {
        [LoggerMessage(EventId = RedundancyPubSubEventIds.SharedStorePubSubSecurityKeyStore + 0,
            Level = LogLevel.Warning,
            Message = "Unable to unprotect shared PubSub SecurityGroup record for {SecurityGroupId}.")]
        public static partial void UnableToUnprotectSharedPubSubSecurityGroupRecord(
            this ILogger logger,
            string securityGroupId);
    }
}
