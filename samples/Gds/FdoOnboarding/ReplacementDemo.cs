// Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
// SPDX-License-Identifier: MIT

using System;
using System.Formats.Cbor;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Enrollment;
using Fdo.Initialization;
using Fdo.Serialization;
using OnboardingRegistrar;
using Opc.Ua;

namespace FdoOnboarding
{
    /// <summary>
    /// Demonstrates one replacement. An encrypted Device checkpoint and a separate public Owner voucher
    /// are published on the callback boundaries; this sample does not resume a failed protocol run.
    /// </summary>
    internal sealed class ReplacementDemo : IFdoDeviceReplacementHandler, IFdoOwnerReplacementHandler, IDisposable
    {
        /// <summary>
        /// Borrows a distinct Owner2 key and takes the approved route from the enrolled Device.
        /// </summary>
        public ReplacementDemo(string root, FdoDeviceInitializeCredential original, ECDsa owner2)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(owner2);
            m_original = original;
            m_ownerKey = new FdoPublicKeyCodec().Encode(owner2);
            OwnerOptions = new FdoOwnerReplacementOptions
            {
                Owner2SigningKey = owner2,
                EncodedRendezvousInfo = original.EncodedRendezvousInfo,
                Handler = this
            };
            byte[] wrappingKey = Convert.FromBase64String(
                Environment.GetEnvironmentVariable("FDO_DEMO_WRAP_KEY") ??
                    throw new InvalidOperationException("Set FDO_DEMO_WRAP_KEY for replacement mode."));
            try
            {
                if (wrappingKey.Length != 32)
                {
                    throw new InvalidOperationException("The replacement wrapping key must contain 32 random bytes.");
                }
                m_deviceFiles = new DeviceCredentialStore(Path.Combine(root, "fdo-device"), wrappingKey, original.DeviceGuid);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
            }
            try
            {
                m_ownerFiles = new PushRegistrarCheckpointFiles(Path.Combine(root, "fdo-owner"));
            }
            catch
            {
                m_deviceFiles.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the approved Owner-side replacement configuration.
        /// </summary>
        public FdoOwnerReplacementOptions OwnerOptions { get; }

        /// <summary>
        /// Binds the sample's callback checks to the exact Device run.
        /// </summary>
        public void Bind(FdoDeviceSession device)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (m_device != null)
            {
                throw new InvalidOperationException("Replacement cannot be rebound to another Device run.");
            }
            m_device = device;
        }

        /// <inheritdoc/>
        public ValueTask<IFdoDeviceInitializeHmacSecret> ReserveSecretAsync(
            FdoDeviceReplacementProposal proposal,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_proposal != null || proposal.RunId != m_device?.RunId ||
                proposal.VoucherHmacAlgorithm != FdoDeviceHmacAlgorithm.HmacSha256 ||
                !proposal.PreviousDeviceGuid.Span.SequenceEqual(m_original.DeviceGuid.Span) ||
                proposal.ReplacementDeviceGuid.Span.SequenceEqual(m_original.DeviceGuid.Span) ||
                !proposal.EncodedOwnerPublicKey.Span.SequenceEqual(m_ownerKey.Span) ||
                !proposal.EncodedRendezvousInfo.Span.SequenceEqual(m_original.EncodedRendezvousInfo.Span))
            {
                throw new CryptographicException("The replacement credential does not match the approved Owner and route.");
            }
            m_proposal = proposal;
            m_pendingSecret = RandomNumberGenerator.GetBytes(ReplacementHandle.SecretSize);
            return ValueTask.FromResult<IFdoDeviceInitializeHmacSecret>(new ReplacementHandle(m_deviceFiles));
        }

        /// <inheritdoc/>
        public async ValueTask StageAsync(FdoDeviceReplacementCredential credential, CancellationToken cancellationToken)
        {
            if (m_device is not { DoneSendStarted: false, DoneAckAuthenticated: false } ||
                !ReferenceEquals(m_proposal, credential.Proposal) || m_pendingSecret is not { } secret)
            {
                throw new InvalidOperationException("The replacement must be staged before sending Done20.");
            }
            ReadOnlyMemory<byte> header = credential.Proposal.EncodedVoucherHeaderTag;
            byte[] record = new byte[secret.Length + header.Length];
            try
            {
                secret.CopyTo(record, 0);
                header.CopyTo(record.AsMemory(secret.Length));
                await m_deviceFiles.SetAsync(DeviceCredentialStore.Staged, record, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(record);
                CryptographicOperations.ZeroMemory(secret);
                m_pendingSecret = null;
            }
        }

        /// <inheritdoc/>
        public async ValueTask CommitAsync(FdoOwnerReplacementCommit replacement, CancellationToken cancellationToken)
        {
            if (m_ownerCommitted || !replacement.PreviousDeviceGuid.Span.SequenceEqual(m_original.DeviceGuid.Span))
            {
                throw new InvalidOperationException("Unexpected Owner replacement commit.");
            }
            await m_ownerFiles.WriteAsync(VoucherKey, replacement.EncodedVoucher, cancellationToken)
                .ConfigureAwait(false);
            m_ownerCommitted = true;
        }

        /// <inheritdoc/>
        public async ValueTask ActivateAsync(
            FdoDeviceReplacementCredential credential,
            CancellationToken cancellationToken)
        {
            if (m_device is not { DoneAckAuthenticated: true } ||
                !ReferenceEquals(m_proposal, credential.Proposal) || m_activated)
            {
                throw new InvalidOperationException("Replacement requires the authenticated DoneAck20 for the staged run.");
            }
            using ISecret staged = await m_deviceFiles.GetAsync(DeviceCredentialStore.Staged, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("The staged replacement is missing.");
            RequireHeader(staged, credential.Proposal.EncodedVoucherHeaderTag);
            cancellationToken.ThrowIfCancellationRequested();
            m_deviceFiles.Activate();
            m_activated = true;
        }

        /// <summary>
        /// Keeps the DCA installation separate and after the Device's FDO credential activation.
        /// </summary>
        public void RequireActivated()
        {
            if (!m_activated)
            {
                throw new InvalidOperationException("The DCA cannot be installed before FDO replacement activation.");
            }
        }

        /// <summary>
        /// Reopens the durable outputs and proves the active credential matches the Owner's replacement voucher.
        /// </summary>
        public async ValueTask VerifyAsync(
            FdoOwnerReplacementCommit? committed,
            FdoDeviceReplacementCredential? activated,
            CancellationToken ct)
        {
            RequireActivated();
            byte[]? voucher = await m_ownerFiles.ReadAsync(VoucherKey, ct).ConfigureAwait(false);
            if (!m_ownerCommitted || committed == null || activated == null || voucher == null ||
                !ReferenceEquals(m_proposal, activated.Proposal) ||
                !voucher.AsSpan().SequenceEqual(committed.EncodedVoucher.Span) ||
                !committed.EncodedVoucherHeaderTag.Span.SequenceEqual(activated.Proposal.EncodedVoucherHeaderTag.Span) ||
                !committed.ReplacementDeviceGuid.Span.SequenceEqual(activated.Proposal.ReplacementDeviceGuid.Span))
            {
                throw new InvalidOperationException("The Owner and Device did not retain the same replacement.");
            }
            using ISecret? staged = await m_deviceFiles.GetAsync(DeviceCredentialStore.Staged, ct).ConfigureAwait(false);
            using ISecret active = await m_deviceFiles.GetAsync(DeviceCredentialStore.Active, ct).ConfigureAwait(false) ??
                throw new InvalidOperationException("The active replacement could not be reopened.");
            RequireHeader(active, committed.EncodedVoucherHeaderTag);
            var reader = new CborReader(committed.EncodedVoucherHeaderTag, CborConformanceMode.Canonical);
            byte[] header = reader.ReadByteString();
            byte[] newMac = HMACSHA256.HashData(active.Bytes[..ReplacementHandle.SecretSize], header);
            ReadOnlyMemory<byte> oldMac = await m_original.VoucherHmacSecret.ComputeAsync(header, ct).ConfigureAwait(false);
            var hmac = new CborReader(committed.EncodedVoucherHeaderHmac, CborConformanceMode.Canonical);
            const int hmacSha256 = 5;
            if (hmac.ReadStartArray() != 2 || hmac.ReadInt32() != hmacSha256)
            {
                throw new CryptographicException("The replacement voucher did not use HMAC-SHA256.");
            }
            byte[] expected = hmac.ReadByteString();
            hmac.ReadEndArray();
            if (staged != null || reader.BytesRemaining != 0 || hmac.BytesRemaining != 0 ||
                !CryptographicOperations.FixedTimeEquals(expected, newMac) ||
                CryptographicOperations.FixedTimeEquals(expected, oldMac.Span))
            {
                throw new CryptographicException("Only the activated replacement secret must validate the new voucher.");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_pendingSecret is { } secret)
            {
                CryptographicOperations.ZeroMemory(secret);
            }
            m_ownerFiles.Dispose();
            m_deviceFiles.Dispose();
        }

        /// <summary>
        /// Rejects a mismatched or truncated encrypted Device record.
        /// </summary>
        private static void RequireHeader(ISecret record, ReadOnlyMemory<byte> header)
        {
            if (record.Bytes.Length <= ReplacementHandle.SecretSize ||
                !record.Bytes[ReplacementHandle.SecretSize..].SequenceEqual(header.Span))
            {
                throw new CryptographicException("The durable replacement does not match the authenticated header.");
            }
        }

        /// <summary>
        /// Computes the voucher HMAC without exporting the Device's staged secret to the FDO role.
        /// </summary>
        private sealed class ReplacementHandle(ISecretStore secrets) : IFdoDeviceInitializeHmacSecret
        {
            /// <inheritdoc/>
            public FdoDeviceHmacAlgorithm Algorithm => FdoDeviceHmacAlgorithm.HmacSha256;

            /// <inheritdoc/>
            public int KeySizeInBytes => SecretSize;

            /// <inheritdoc/>
            public async ValueTask<ReadOnlyMemory<byte>> ComputeAsync(
                ReadOnlyMemory<byte> encodedHeaderContents,
                CancellationToken cancellationToken)
            {
                using ISecret record = await secrets.GetAsync(DeviceCredentialStore.Staged, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidOperationException("The replacement was not staged.");
                return HMACSHA256.HashData(record.Bytes[..SecretSize], encodedHeaderContents.Span);
            }

            /// <summary>The required SHA-256 voucher secret size.</summary>
            public const int SecretSize = 32;
        }

        /// <summary>
        /// Stores only this run's encrypted credential. The caller supplies and retains the wrapping key;
        /// the sample never writes it. A same-directory move changes the active credential atomically.
        /// </summary>
        private sealed class DeviceCredentialStore : ISecretStore, IDisposable
        {
            /// <summary>
            /// Creates the private checkpoint directory under the caller-controlled root.
            /// </summary>
            public DeviceCredentialStore(string root, ReadOnlySpan<byte> wrappingKey, ReadOnlyMemory<byte> deviceGuid)
            {
                m_root = root;
                Directory.CreateDirectory(root);
                m_deviceGuid = deviceGuid.ToArray();
                m_aes = new AesGcm(wrappingKey, TagSize);
            }

            /// <inheritdoc/>
            public string StoreType => StoreName;

            /// <inheritdoc/>
            public ISecret? TryGet(SecretIdentifier id)
            {
                _ = GetPath(id);
                return null;
            }

            /// <inheritdoc/>
            public async ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                string path = GetPath(id);
                try
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                        FileOptions.Asynchronous);
                    long length = input.Length;
                    if (length <= PrefixSize + ReplacementHandle.SecretSize || length > MaximumSize)
                    {
                        throw new InvalidDataException("Invalid encrypted replacement checkpoint length.");
                    }
                    var encoded = new byte[(int)length];
                    await input.ReadExactlyAsync(encoded, ct).ConfigureAwait(false);
                    byte[] clear = new byte[encoded.Length - PrefixSize];
                    try
                    {
                        m_aes.Decrypt(encoded.AsSpan(0, NonceSize), encoded.AsSpan(PrefixSize),
                            encoded.AsSpan(NonceSize, TagSize), clear, m_deviceGuid);
                        return new CredentialSecret(clear);
                    }
                    catch (CryptographicException)
                    {
                        CryptographicOperations.ZeroMemory(clear);
                        throw;
                    }
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            /// <inheritdoc/>
            public async ValueTask SetAsync(
                SecretIdentifier id,
                ReadOnlyMemory<byte> bytes,
                CancellationToken ct = default)
            {
                string path = GetPath(id);
                if (id.Name != Staged.Name || bytes.Length <= ReplacementHandle.SecretSize ||
                    bytes.Length > MaximumSize - PrefixSize || File.Exists(GetPath(Active)))
                {
                    throw new InvalidOperationException("Only a new replacement can be staged.");
                }
                byte[] encoded = new byte[PrefixSize + bytes.Length];
                RandomNumberGenerator.Fill(encoded.AsSpan(0, NonceSize));
                m_aes.Encrypt(encoded.AsSpan(0, NonceSize), bytes.Span, encoded.AsSpan(PrefixSize),
                    encoded.AsSpan(NonceSize, TagSize), m_deviceGuid);
                string pending = path + ".pending";
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await output.WriteAsync(encoded, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                ct.ThrowIfCancellationRequested();
                File.Move(pending, path);
            }

            /// <inheritdoc/>
            public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                _ = GetPath(id);
                throw new NotSupportedException("The single-run demo never deletes an uncertain credential.");
            }

            /// <summary>
            /// Publishes the exact staged ciphertext after authorization.
            /// </summary>
            public void Activate()
            {
                File.Move(GetPath(Staged), GetPath(Active));
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_aes.Dispose();
            }

            /// <summary>
            /// Accepts only the two exact, sample-owned credential slots.
            /// </summary>
            private string GetPath(SecretIdentifier id)
            {
                ArgumentNullException.ThrowIfNull(id);
                if (id.StoreType != StoreName || id.StorePath != null ||
                    (id.Name != Staged.Name && id.Name != Active.Name))
                {
                    throw new ArgumentException("Unknown replacement credential slot.", nameof(id));
                }
                return Path.Combine(m_root, id.Name + ".bin");
            }

            /// <summary>
            /// Owns the decrypted record until the callback has consumed it.
            /// </summary>
            private sealed class CredentialSecret(byte[] bytes) : ISecret
            {
                /// <inheritdoc/>
                public ReadOnlySpan<byte> Bytes => bytes;

                /// <inheritdoc/>
                public void Dispose()
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }

            /// <summary>The inactive credential slot.</summary>
            public static readonly SecretIdentifier Staged = new("staged", StoreName);

            /// <summary>The active replacement slot.</summary>
            public static readonly SecretIdentifier Active = new("active", StoreName);

            /// <summary>Discriminator for the sample's isolated secret store.</summary>
            private const string StoreName = "FdoDemoReplacement";

            /// <summary>Size of the randomly generated AES-GCM nonce.</summary>
            private const int NonceSize = 12;

            /// <summary>Size of the AES-GCM authentication tag.</summary>
            private const int TagSize = 16;

            /// <summary>Length of the nonce and authentication tag prefix.</summary>
            private const int PrefixSize = NonceSize + TagSize;

            /// <summary>Caps one FDO header and its secret, including the encrypted envelope.</summary>
            private const int MaximumSize = ushort.MaxValue + ReplacementHandle.SecretSize + PrefixSize;

            /// <summary>Directory separate from the Owner voucher and registrar receipt.</summary>
            private readonly string m_root;

            /// <summary>Authenticated context for this Device's credential.</summary>
            private readonly byte[] m_deviceGuid;

            /// <summary>Owns the wrapping key until this run ends.</summary>
            private readonly AesGcm m_aes;
        }

        /// <summary>Identifier for the Owner's exact public replacement voucher.</summary>
        private const string VoucherKey = "replacement-voucher";

        /// <summary>Original live Device credential, unchanged while replacement is staged.</summary>
        private readonly FdoDeviceInitializeCredential m_original;

        /// <summary>Exact approved Owner2 public key.</summary>
        private readonly ReadOnlyMemory<byte> m_ownerKey;

        /// <summary>The Device's isolated encrypted credential store.</summary>
        private readonly DeviceCredentialStore m_deviceFiles;

        /// <summary>The Owner's public voucher checkpoint writer.</summary>
        private readonly PushRegistrarCheckpointFiles m_ownerFiles;

        /// <summary>The Device session that authenticated the completion acknowledgement.</summary>
        private FdoDeviceSession? m_device;

        /// <summary>The exact accepted replacement proposal.</summary>
        private FdoDeviceReplacementProposal? m_proposal;

        /// <summary>Secret held briefly between reservation and encrypted publication.</summary>
        private byte[]? m_pendingSecret;

        /// <summary>Whether the Owner's replacement commit returned successfully.</summary>
        private bool m_ownerCommitted;

        /// <summary>Whether the Device's activation returned successfully.</summary>
        private bool m_activated;
    }
}
