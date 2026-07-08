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
    public sealed class SharedStorePubSubSecurityKeyStore : IPubSubSecurityKeyStore
    {
        /// <summary>
        /// Initializes a new <see cref="SharedStorePubSubSecurityKeyStore"/>.
        /// </summary>
        /// <param name="store">Shared key/value store used by the redundant set.</param>
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
            m_logger = context.Telemetry.CreateLogger<SharedStorePubSubSecurityKeyStore>();
        }

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
            if (securityGroupId is null)
            {
                throw new ArgumentNullException(nameof(securityGroupId));
            }

            (bool found, ByteString protectedRecord) = await m_store
                .TryGetAsync(GetSecurityGroupKey(securityGroupId), cancellationToken)
                .ConfigureAwait(false);
            if (!found)
            {
                return null;
            }

            if (!m_protector.TryUnprotect(protectedRecord, out ByteString plaintext))
            {
                m_logger.LogWarning(
                    "Unable to unprotect shared PubSub SecurityGroup record for {SecurityGroupId}.",
                    securityGroupId);
                return null;
            }

            return DeserializeSecurityGroup(plaintext);
        }

        /// <inheritdoc/>
        public async ValueTask SaveSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken = default)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            ByteString plaintext = SerializeSecurityGroup(group);
            ByteString protectedRecord = m_protector.Protect(plaintext);
            await m_store
                .SetAsync(GetSecurityGroupKey(group.SecurityGroupId), protectedRecord, cancellationToken)
                .ConfigureAwait(false);
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

        private ByteString SerializeSecurityGroup(SksSecurityGroup group)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(SerializationVersion);
                writer.Write(group.SecurityGroupId);
                writer.Write(group.SecurityPolicyUri);
                writer.Write(group.KeyLifetime.Ticks);
                writer.Write(group.MaxFutureKeyCount);
                writer.Write(group.MaxPastKeyCount);
                WriteSecurityKeys(writer, group.Keys);
                WriteStrings(writer, group.AuthorizedCallerIdentities);
                WriteRolePermissions(writer, group.RolePermissions);
            }

            return new ByteString(stream.ToArray());
        }

        private SksSecurityGroup DeserializeSecurityGroup(ByteString plaintext)
        {
            byte[] buffer = plaintext.IsNull ? Array.Empty<byte>() : plaintext.ToArray();
            using var stream = new MemoryStream(buffer, false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, false);
            int version = reader.ReadInt32();
            if (version != SerializationVersion)
            {
                throw new InvalidDataException("Unsupported shared PubSub SecurityGroup record version.");
            }

            string securityGroupId = reader.ReadString();
            string securityPolicyUri = reader.ReadString();
            var keyLifetime = TimeSpan.FromTicks(reader.ReadInt64());
            int maxFutureKeyCount = reader.ReadInt32();
            int maxPastKeyCount = reader.ReadInt32();
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
                rolePermissions);
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
                    encoder.Close();
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

            var values = new string[count];
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
                return Array.Empty<byte>();
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

        private const int SerializationVersion = 1;
        private const int NullArrayLength = -1;
        private const int NullByteStringLength = -1;

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly IServiceMessageContext m_context;
        private readonly ILogger m_logger;
    }
}
