/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Default key escrow provider that writes encrypted key-log
    /// artifacts into the capture session folder.
    /// </summary>
    internal sealed class DiskKeyEscrowProvider : IKeyEscrowProvider
    {
        internal const string KeyLogJsonFileName = "keys.uakeys.json";
        internal const string KeyLogTextFileName = "keys.uakeys.txt";

        /// <inheritdoc/>
        public ValueTask<IKeyEscrowSession> BeginSessionAsync(
            string sessionId,
            string sessionFolder,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionFolder);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(sessionFolder);
            string jsonPath = Path.Combine(sessionFolder, KeyLogJsonFileName);
            string textPath = Path.Combine(sessionFolder, KeyLogTextFileName);
            // CA2000: ownership of the DiskKeyEscrowSession transfers to the caller via
            // the returned ValueTask; the caller is responsible for DisposeAsync.
#pragma warning disable CA2000
            return new ValueTask<IKeyEscrowSession>(new DiskKeyEscrowSession(jsonPath, textPath));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Per-session disk escrow handle.
    /// </summary>
    internal sealed class DiskKeyEscrowSession : IKeyEscrowSession
    {
        private readonly string m_jsonPath;
        private readonly string m_textPath;
        private UaKeyLogJsonWriter? m_jsonWriter;
        private UaKeyLogTextWriter? m_textWriter;

        public DiskKeyEscrowSession(string jsonPath, string textPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(textPath);

            m_jsonPath = jsonPath;
            m_textPath = textPath;
        }

        /// <inheritdoc/>
        public async ValueTask EscrowAsync(ChannelKeyMaterial material, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(material);
            EnsureWriters();

            await m_jsonWriter!.AppendAsync(material, ct).ConfigureAwait(false);
            await m_textWriter!.AppendAsync(material, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken ct)
        {
            if (m_jsonWriter is not null)
            {
                await m_jsonWriter.FlushAsync(ct).ConfigureAwait(false);
            }

            if (m_textWriter is not null)
            {
                await m_textWriter.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_jsonWriter is not null)
            {
                await m_jsonWriter.DisposeAsync().ConfigureAwait(false);
                m_jsonWriter = null;
            }

            if (m_textWriter is not null)
            {
                await m_textWriter.DisposeAsync().ConfigureAwait(false);
                m_textWriter = null;
            }
        }

        private void EnsureWriters()
        {
            if (m_jsonWriter is not null && m_textWriter is not null)
            {
                return;
            }

            byte[] sessionKey = SessionKeyManager.CreateAndPersistKey(m_jsonPath);
            m_jsonWriter = new UaKeyLogJsonWriter(m_jsonPath, sessionKey);
            m_textWriter = new UaKeyLogTextWriter(m_textPath, sessionKey);
        }
    }
}
