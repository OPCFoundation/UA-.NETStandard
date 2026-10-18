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
using System.Threading.Tasks;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Storage collections of the OPC 40100-1 runtime.
    /// </summary>
    public enum MachineVisionDocumentKind
    {
        /// <summary>
        /// Recipe metadata, preparation state, product links, and opaque content.
        /// </summary>
        Recipe,

        /// <summary>
        /// Configuration metadata and opaque content.
        /// </summary>
        Configuration,

        /// <summary>
        /// Result metadata and optional transferable content.
        /// </summary>
        Result,

        /// <summary>
        /// Persisted runtime selection, separate from the published model.
        /// </summary>
        Runtime
    }

    /// <summary>
    /// A storage snapshot. Metadata contains OPC UA values, not CLR object graphs.
    /// Null content means no transferred payload; an empty ByteString is a valid empty file.
    /// </summary>
    /// <param name="Kind">The collection.</param>
    /// <param name="Id">The system-owned, stable internal identifier.</param>
    /// <param name="Metadata">The collection's metadata, encoded as OPC UA values.</param>
    /// <param name="Content">The opaque file content, if committed.</param>
    public sealed record MachineVisionDocument(
        MachineVisionDocumentKind Kind,
        string Id,
        Variant Metadata,
        ByteString Content)
    {
        /// <summary>
        /// Returns an independent copy so applications cannot mutate stored structures in place.
        /// </summary>
        public MachineVisionDocument Copy()
        {
            return this with
            {
                Metadata = CoreUtils.Clone(Metadata),
                Content = Content.IsNull ? default : ByteString.From(Content.Span.ToArray())
            };
        }
    }

    /// <summary>
    /// Concurrent storage for one vision system. A successful write atomically replaces
    /// metadata and content together. Returned values are independent snapshots.
    /// </summary>
    /// <remarks>
    /// Implement this interface over a transactional shared store for distributed deployments.
    /// The reference memory and file-system implementations are single-host stores.
    /// </remarks>
    public interface IMachineVisionStore
    {
        /// <summary>
        /// Reads a document, returning null only when the identifier is absent.
        /// </summary>
        ValueTask<MachineVisionDocument?> ReadAsync(
            MachineVisionDocumentKind kind,
            string id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the collection in stable identifier order.
        /// </summary>
        ValueTask<ArrayOf<MachineVisionDocument>> ListAsync(
            MachineVisionDocumentKind kind,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or replaces a complete document, or fails without modifying the existing value.
        /// </summary>
        ValueTask WriteAsync(
            MachineVisionDocument document,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a document, returning false only when it was absent.
        /// </summary>
        ValueTask<bool> DeleteAsync(
            MachineVisionDocumentKind kind,
            string id,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Bounded reference store for a single vision system. Capacity errors never evict
    /// a prepared recipe, active configuration, or result silently.
    /// </summary>
    public sealed class InMemoryMachineVisionStore : IMachineVisionStore
    {
        /// <summary>
        /// Creates a store with limits on document count and the total opaque payload size.
        /// </summary>
        public InMemoryMachineVisionStore(int maximumDocuments = 1024, long maximumContentBytes = 64 * 1024 * 1024)
        {
            if (maximumDocuments <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDocuments));
            }
            if (maximumContentBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumContentBytes));
            }
            m_maximumDocuments = maximumDocuments;
            m_maximumContentBytes = maximumContentBytes;
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionDocument?> ReadAsync(
            MachineVisionDocumentKind kind,
            string id,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(kind, id);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<MachineVisionDocument?>(
                    m_documents.TryGetValue((kind, id), out MachineVisionDocument? value) ? value.Copy() : null);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<MachineVisionDocument>> ListAsync(
            MachineVisionDocumentKind kind,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(kind, nameof(kind));
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<MachineVisionDocument>();
            lock (m_gate)
            {
                foreach (MachineVisionDocument document in m_documents.Values)
                {
                    if (document.Kind == kind)
                    {
                        entries.Add(document.Copy());
                    }
                }
            }
            entries.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
            return new ValueTask<ArrayOf<MachineVisionDocument>>(entries.ToArrayOf());
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(MachineVisionDocument document, CancellationToken cancellationToken = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            ValidateKey(document.Kind, document.Id);
            cancellationToken.ThrowIfCancellationRequested();
            MachineVisionDocument snapshot = document.Copy();
            lock (m_gate)
            {
                bool exists = m_documents.TryGetValue((document.Kind, document.Id), out MachineVisionDocument? old);
                long contentBytes = m_contentBytes - (old?.Content.Span.Length ?? 0) + snapshot.Content.Span.Length;
                if ((!exists && m_documents.Count >= m_maximumDocuments) || contentBytes > m_maximumContentBytes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadResourceUnavailable, "The vision document store capacity has been reached.");
                }
                m_documents[(document.Kind, document.Id)] = snapshot;
                m_contentBytes = contentBytes;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(
            MachineVisionDocumentKind kind,
            string id,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(kind, id);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_documents.TryGetValue((kind, id), out MachineVisionDocument? existing))
                {
                    return new ValueTask<bool>(false);
                }
                m_documents.Remove((kind, id));
                m_contentBytes -= existing.Content.Span.Length;
                return new ValueTask<bool>(true);
            }
        }

        /// <summary>
        /// Checks the storage identity independently of any filesystem path.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static void ValidateKey(MachineVisionDocumentKind kind, string id)
        {
            if (kind is < MachineVisionDocumentKind.Recipe or > MachineVisionDocumentKind.Runtime)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A non-empty internal identifier is required.", nameof(id));
            }
        }

        private readonly Lock m_gate = new();

        private readonly Dictionary<(MachineVisionDocumentKind Kind, string Id), MachineVisionDocument> m_documents =
            [];

        private readonly int m_maximumDocuments;
        private readonly long m_maximumContentBytes;
        private long m_contentBytes;
    }
}
