// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Gds.Fdo;

namespace FdoOnboarding
{
    /// <summary>
    /// Single-owner sample storage. A distributed deployment must supply shared transactional storage.
    /// </summary>
    internal sealed class FdoSampleReceiptStore : IFdoEnrollmentReceiptStore, IDisposable
    {
        /// <summary>
        /// Leases the checkpoint directory and chooses the receipt for the approved product and application.
        /// </summary>
        public FdoSampleReceiptStore(string directory, string productInstanceUri, string applicationUri)
        {
            using var scope = new MemoryStream();
            using (var writer = new BinaryWriter(scope, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(productInstanceUri);
                writer.Write(applicationUri);
            }
            m_key = "FdoExternalEnrollment-" + Convert.ToHexString(SHA256.HashData(scope.ToArray()));
            m_files = new PushRegistrarCheckpointFiles(directory);
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadAsync(CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await ReadCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask CreateAsync(
            Func<CancellationToken, ValueTask<ByteString>> create,
            CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!(await ReadCoreAsync(ct).ConfigureAwait(false)).IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Reconcile the retained enrollment before requesting issuance.");
                }
                ByteString receipt = await create(ct).ConfigureAwait(false);
                await m_files.WriteAsync(m_key, receipt.Span.ToArray(), ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryReplaceAsync(
            ByteString expected, ByteString replacement, CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (await ReadCoreAsync(ct).ConfigureAwait(false) != expected)
                {
                    return false;
                }
                await m_files.WriteAsync(m_key, replacement.Span.ToArray(), ct).ConfigureAwait(false);
                return true;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_files.Dispose();
            m_gate.Dispose();
        }

        private async ValueTask<ByteString> ReadCoreAsync(CancellationToken ct)
        {
            byte[]? bytes = await m_files.ReadAsync(m_key, ct).ConfigureAwait(false);
            return bytes == null ? default : ByteString.From(bytes);
        }

        private readonly PushRegistrarCheckpointFiles m_files;
        private readonly string m_key;
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
