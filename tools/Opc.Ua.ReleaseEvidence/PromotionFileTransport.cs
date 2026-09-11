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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Offline fixture transport, never an official registry or a distributed lease implementation.
    /// </summary>
    /// <param name="root">The isolated local fixture root containing objects, aliases, and lease files.</param>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class PromotionFileTransport(string root, EvidenceFiles files) : IPromotionTransport
    {
        /// <summary>
        /// Gets a value indicating that this local fixture is never an official delivery transport.
        /// </summary>
        public bool IsOfficial => false;

        /// <summary>
        /// Acquires an exclusive local file lease for the selected artifact group and destination.
        /// </summary>
        public Task<IPromotionLease> AcquireLeaseAsync(
            string group,
            string destination,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                group + "\n" + destination)));
            string path = EvidenceFiles.Confined(root, "leases/" + name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return Task.FromResult<IPromotionLease>(new FileLease(path));
        }

        /// <summary>
        /// Reads content and evidence digests and the selected alias target from the isolated local fixture.
        /// </summary>
        public async Task<PromotionObservation> ReadAsync(
            PromotionMember member,
            CancellationToken cancellationToken)
        {
            string path = ContentPath(member);
            string? digest = File.Exists(path)
                ? await files.DigestAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
            var evidence = new System.Collections.Generic.List<string>();
            foreach (PromotionFile item in member.Evidence)
            {
                string evidencePath = EvidencePath(member, item);
                if (File.Exists(evidencePath))
                {
                    evidence.Add(await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false));
                }
            }
            string? alias = null;
            if (member.Alias != null)
            {
                string aliasPath = AliasPath(member);
                if (File.Exists(aliasPath))
                {
                    byte[] bytes = await files.ReadAsync(aliasPath, cancellationToken).ConfigureAwait(false);
                    if (bytes.Length != 71)
                    {
                        throw new InvalidDataException("An offline alias has malformed content.");
                    }
                    alias = Encoding.UTF8.GetString(bytes);
                }
            }
            return new PromotionObservation(digest, [.. evidence], alias);
        }

        /// <summary>
        /// Copies exact candidate content into the fixture under a held lease without replacing conflicting bytes.
        /// </summary>
        public async Task CreateImmutableAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken)
        {
            await lease.AssertHeldAsync(cancellationToken).ConfigureAwait(false);
            await CopyExactAsync(
                EvidenceFiles.Confined(candidateRoot, member.Content.Path),
                ContentPath(member), member.Content.Digest, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Restores each required evidence object in the local fixture after checking the lease.
        /// </summary>
        public async Task RestoreEvidenceAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken)
        {
            foreach (PromotionFile item in member.Evidence)
            {
                await lease.AssertHeldAsync(cancellationToken).ConfigureAwait(false);
                await CopyExactAsync(
                    EvidenceFiles.Confined(candidateRoot, item.Path),
                    EvidencePath(member, item), item.Digest, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Conditionally updates a fixture alias under the local lease and rejects a changed prior digest.
        /// </summary>
        public async Task CompareExchangeAliasAsync(
            PromotionMember member,
            string? expectedDigest,
            IPromotionLease lease,
            CancellationToken cancellationToken)
        {
            await lease.AssertHeldAsync(cancellationToken).ConfigureAwait(false);
            string path = AliasPath(member);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // All writers in this explicitly local fixture hold the same destination/group lease.
            string? current = File.Exists(path)
                ? Encoding.UTF8.GetString(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false))
                : null;
            if (current == member.Content.Digest)
            {
                return;
            }
            if (current != expectedDigest)
            {
                throw new PromotionRejectedException("Alias precondition changed; refusing an out-of-order release.");
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                await File.WriteAllTextAsync(
                    temporary, member.Content.Digest, cancellationToken).ConfigureAwait(false);
                await lease.AssertHeldAsync(cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: current != null);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private string ContentPath(PromotionMember member)
        {
            return EvidenceFiles.Confined(root, "objects/" + Key(member) + "/content");
        }

        private string EvidencePath(PromotionMember member, PromotionFile item)
        {
            return EvidenceFiles.Confined(root, "objects/" + Key(member) + "/evidence/" + item.Digest[7..]);
        }

        private string AliasPath(PromotionMember member)
        {
            if (member.Alias == null)
            {
                throw new InvalidDataException("No alias was authorized for this member.");
            }
            return EvidenceFiles.Confined(root, "aliases/" + Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(PromotionRequestValidator.AliasIdentity(member)))));
        }

        private static string Key(PromotionMember member)
        {
            string identity = member.Kind is "oci-index" or "oci-manifest"
                ? string.Join('\n', member.Destination, member.Id, member.Content.Digest)
                : PromotionRequestValidator.MemberIdentity(member);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                identity)));
        }

        private async Task CopyExactAsync(
            string source,
            string destination,
            string digest,
            CancellationToken cancellationToken)
        {
            if (File.Exists(destination))
            {
                if (await files.DigestAsync(destination, cancellationToken).ConfigureAwait(false) != digest)
                {
                    throw new PromotionRejectedException("Immutable destination already contains conflicting bytes.");
                }
                return;
            }
            EvidenceFiles.RejectLinks(source);
            if (new FileInfo(source).Length > 512L * 1024 * 1024)
            {
                throw new InvalidDataException("An offline transport input exceeds 512 MiB.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using (input.ConfigureAwait(false))
                {
                    var output = new FileStream(
                        temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await using (output.ConfigureAwait(false))
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                if (await files.DigestAsync(temporary, cancellationToken).ConfigureAwait(false) != digest)
                {
                    throw new PromotionRejectedException("Candidate bytes changed during acquisition.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private sealed class FileLease : IPromotionLease
        {
            /// <summary>
            /// Acquires an exclusive file handle that represents ownership of the local promotion lease.
            /// </summary>
            public FileLease(string path)
            {
                m_stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }

            /// <summary>
            /// Gets the unique ownership marker for this local lease instance.
            /// </summary>
            public string Fence { get; } = Guid.NewGuid().ToString("N");

            /// <summary>
            /// Checks cancellation and confirms that the local lease is undisposed and its file handle remains
            /// writable.
            /// </summary>
            public ValueTask AssertHeldAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (!m_stream.CanWrite)
                {
                    throw new IOException("The offline lease was lost.");
                }
                return ValueTask.CompletedTask;
            }

            /// <summary>
            /// Releases the exclusive file handle for this local promotion lease.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    await m_stream.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly FileStream m_stream;
            private bool m_disposed;
        }
    }

    /// <summary>
    /// Stores each promotion event as a separate immutable JSON file in a local journal directory.
    /// </summary>
    /// <param name="root">The local directory in which immutable event JSON files are created.</param>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class PromotionFileJournal(string root, EvidenceFiles files) : IPromotionJournal
    {
        /// <summary>
        /// Creates a new journal file named for the event identifier without overwriting an existing event.
        /// </summary>
        public Task AppendAsync(PromotionEvent entry, CancellationToken cancellationToken)
        {
            return files.WriteModelAsync(
                EvidenceFiles.Confined(root, entry.EventId + ".json"),
                entry, PromotionJsonContext.Default.PromotionEvent, cancellationToken);
        }
    }
}
