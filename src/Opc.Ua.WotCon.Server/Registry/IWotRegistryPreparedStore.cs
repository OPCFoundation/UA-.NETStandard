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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Describes which parts of a captured registry generation a prepared commit may change.
    /// </summary>
    public enum WotRegistryCommitScope
    {
        /// <summary>
        /// Permits a complete replacement and requires validation of any newly referenced content.
        /// </summary>
        Full,

        /// <summary>
        /// Permits projection metadata only, preserving identities, document content and entity epochs.
        /// </summary>
        ProjectionMetadata
    }

    /// <summary>
    /// Optional prepared commit capability on the same owner as <see cref="IWotRegistryStore"/>.
    /// </summary>
    public interface IWotRegistryPreparedStore : IWotRegistryStore
    {
        /// <summary>
        /// Gets whether authoritative immutable content evidence can support isolated prepared commits.
        /// </summary>
        bool SupportsPreparedCommits { get; }

        /// <summary>
        /// Acquires the store's validated current generation and retains its immutable content evidence.
        /// Capture may validate content; reusing the captured evidence for projection metadata does not
        /// reread independent document content. An unverified content digest is not sufficient evidence.
        /// </summary>
        ValueTask<IWotRegistryValidatedGeneration> CaptureValidatedGenerationAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Privately prepares a replacement against the exact captured generation and commit scope.
        /// Foreign, disposed, or stale evidence is rejected before the durable decision.
        /// Preparation does not publish the intended snapshot or change the committed generation.
        /// </summary>
        ValueTask<IWotRegistryPreparedCommit> PrepareCommitAsync(
            WotRegistrySnapshot intendedSnapshot,
            IWotRegistryValidatedGeneration expectedGeneration,
            WotRegistryCommitScope scope,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retains owner-issued evidence for an exact validated manifest and its immutable document versions.
    /// Disposal releases the retained evidence; it never commits or changes registry state.
    /// </summary>
    public interface IWotRegistryValidatedGeneration : IDisposable
    {
        /// <summary>
        /// Gets the immutable snapshot whose generation, identities and content were validated.
        /// </summary>
        WotRegistrySnapshot Snapshot { get; }
    }

    /// <summary>
    /// Owns a privately prepared store mutation until its durable decision or asynchronous abort.
    /// </summary>
    public interface IWotRegistryPreparedCommit : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact intended immutable snapshot.
        /// </summary>
        WotRegistrySnapshot IntendedSnapshot { get; }

        /// <summary>
        /// Rechecks the captured generation and commits through the store's authoritative outcome
        /// contract. NotCommitted, DurabilityUncertain, and Indeterminate remain distinct outcomes.
        /// Disposal must not roll back a committed or indeterminate decision.
        /// </summary>
        ValueTask CommitAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional immutable-content lease capability on the actual injected resource-store owner.
    /// </summary>
    public interface IWotRegistryContentLeaseProvider : IXRegistryResourceStore
    {
        /// <summary>
        /// Gets whether leases protect the authoritative resource-key mapping and bytes against
        /// overwrite, replacement and deletion until disposal, including other storage writers.
        /// Providers that cannot establish this guarantee must return false.
        /// </summary>
        bool SupportsImmutableContentLeases { get; }

        /// <summary>
        /// Acquires the immutable content version currently addressed by the resource key.
        /// A missing resource is an error; an old digest or mutable byte copy is not a substitute.
        /// </summary>
        ValueTask<IWotRegistryContentLease> AcquireContentLeaseAsync(
            string resourceKey,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retains one authoritative immutable content version and its resource-key association.
    /// </summary>
    public interface IWotRegistryContentLease : IDisposable
    {
        /// <summary>
        /// Gets the protected resource key.
        /// </summary>
        string ResourceKey { get; }

        /// <summary>
        /// Gets the exact length of the protected immutable content.
        /// </summary>
        long ContentLength { get; }

        /// <summary>
        /// Reads the protected version for integrity validation. Callers must validate these bytes
        /// before using the lease as evidence for a metadata-only commit.
        /// </summary>
        ValueTask<ByteString> ReadAsync(
            long offset,
            int count,
            CancellationToken cancellationToken = default);
    }
}
