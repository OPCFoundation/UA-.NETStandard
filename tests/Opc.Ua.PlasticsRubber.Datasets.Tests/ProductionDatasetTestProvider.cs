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
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    /// <summary>
    /// An explicitly bounded in-memory test authority, not a production hardware adapter.
    /// </summary>
    /// <remarks>
    /// The test format has one byte per component occurrence. Partial activation replaces every
    /// matching occurrence in an existing equally shaped control document. All destinations are
    /// validated before the in-memory transaction is applied. No physical-machine claim is made.
    /// </remarks>
    internal sealed class ProductionDatasetTestProvider : IProductionDatasetProvider
    {
        /// <summary>
        /// Creates two empty in-memory control layers.
        /// </summary>
        internal ProductionDatasetTestProvider()
        {
            m_layers.Add(StorageEnumeration.PRODUCTION, EmptyLayer());
            m_layers.Add(StorageEnumeration.PREPARATION, EmptyLayer());
        }

        /// <summary>
        /// Gets or sets a cancellable external commit gate for failure and ordering tests.
        /// </summary>
        internal Func<PlasticsRubberRequestContext, CancellationToken, ValueTask>? BeforeCommit { get; set; }

        /// <summary>
        /// Gets the number of successfully committed control-layer activations.
        /// </summary>
        internal int ActivationCount { get; private set; }

        /// <summary>
        /// Gets the number of successfully committed uploads.
        /// </summary>
        internal int CommitCount { get; private set; }

        /// <summary>
        /// Gets the caller actually supplied to the latest provider operation.
        /// </summary>
        internal PlasticsRubberRequestContext LastRequest { get; private set; }

        /// <summary>
        /// Gets the caller actually supplied to the latest upload commit.
        /// </summary>
        internal PlasticsRubberRequestContext LastCommitRequest { get; private set; }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> ReadStatusAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                return new ValueTask<ProductionDatasetStatus>(Snapshot(m_layers[storage]));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetDocument> ReadAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetReadOptionsType options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                ProductionDatasetDocument document = options.Storage == StorageEnumeration.FILE_SYSTEM
                    ? FindFile(options.Name ?? string.Empty) : m_layers[options.Storage].Document;
                if (string.IsNullOrEmpty(document.Information.Name))
                {
                    throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
                }
                return new ValueTask<ProductionDatasetDocument>(Copy(document));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> LoadAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            ArrayOf<ushort> components,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                EnsureMutable(storage);
                ProductionDatasetDocument document = FindFile(name);
                ProductionDatasetTestLayer next = Activate(m_layers[storage], document, components);
                m_layers[storage] = next;
                ActivationCount++;
                return new ValueTask<ProductionDatasetStatus>(Snapshot(next));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> SaveAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                EnsureMutable(storage);
                CheckCapacity(name);
                ProductionDatasetTestLayer layer = m_layers[storage];
                if (string.IsNullOrEmpty(layer.Document.Information.Name))
                {
                    throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
                }
                ProductionDatasetDocument document = Copy(layer.Document);
                if (!string.Equals(document.Information.Name, name, StringComparison.Ordinal))
                {
                    document.Information.CreationTimestamp = NextTimestamp();
                }
                document.Information.Name = name;
                document.Information.LastSaveTimestamp = NextTimestamp();
                m_files[name] = Copy(document);
                var next = layer with { Document = document, Modified = false };
                m_layers[storage] = next;
                return new ValueTask<ProductionDatasetStatus>(Snapshot(next));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> SetFrozenAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            bool frozen,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                ProductionDatasetTestLayer next = m_layers[storage] with { Frozen = frozen };
                m_layers[storage] = next;
                return new ValueTask<ProductionDatasetStatus>(Snapshot(next));
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ProductionDatasetCommitResult> CommitAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetWriteOptionsType options,
            ByteString content,
            ProductionDatasetInformationType? information,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforeCommit != null)
            {
                await BeforeCommit(context, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                LastCommitRequest = context;
                string name = options.Name ?? string.Empty;
                if (content.Length > 1024)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if ((options.Storage & StorageEnumeration.FILE_SYSTEM) != 0)
                {
                    CheckCapacity(name);
                }
                var document = new ProductionDatasetDocument
                {
                    Content = ByteString.From(content.Span.ToArray()),
                    Information = information == null
                        ? new ProductionDatasetInformationType { Name = name }
                        : Copy(information)
                };
                var pending = new Dictionary<StorageEnumeration, ProductionDatasetTestLayer>();
                foreach (StorageEnumeration storage in s_controlLayers)
                {
                    if ((options.Storage & storage) != 0)
                    {
                        EnsureMutable(storage);
                        pending.Add(storage, Activate(m_layers[storage], document, options.Components));
                    }
                }
                if ((options.Storage & StorageEnumeration.FILE_SYSTEM) != 0)
                {
                    document.Information.LastSaveTimestamp = NextTimestamp();
                    m_files[name] = Copy(document);
                }
                foreach (KeyValuePair<StorageEnumeration, ProductionDatasetTestLayer> item in pending)
                {
                    m_layers[item.Key] = item.Value;
                    ActivationCount++;
                }
                CommitCount++;
                return new ProductionDatasetCommitResult
                {
                    Active = pending.TryGetValue(StorageEnumeration.PRODUCTION, out ProductionDatasetTestLayer? active)
                        ? Snapshot(active) : null,
                    Preparation = pending.TryGetValue(
                        StorageEnumeration.PREPARATION, out ProductionDatasetTestLayer? preparation)
                        ? Snapshot(preparation) : null
                };
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ProductionDatasetInformationType>> GetListAsync(
            PlasticsRubberRequestContext context,
            string nameFilter,
            string mouldId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                var information = new List<ProductionDatasetInformationType>();
                foreach (ProductionDatasetDocument document in m_files.Values)
                {
                    information.Add(Copy(document.Information));
                }
                return new ValueTask<ArrayOf<ProductionDatasetInformationType>>(information.ToArray().ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask ReceiveListAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<ProductionDatasetInformationType> information,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                LastRequest = context;
                if (context.SystemContext is not ISessionSystemContext { SessionId: { } session } || session.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                if ((!m_clientLists.ContainsKey(session) && m_clientLists.Count >= 4) || information.Count > 8)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                var copy = new ProductionDatasetInformationType[information.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = Copy(information[i]);
                }
                m_clientLists[session] = copy.ToArrayOf();
                return default;
            }
        }

        /// <summary>
        /// Reads the last client list belonging to a specific real session.
        /// </summary>
        internal ArrayOf<ProductionDatasetInformationType> ReadClientList(NodeId session)
        {
            lock (m_gate)
            {
                return m_clientLists.TryGetValue(session, out ArrayOf<ProductionDatasetInformationType> list)
                    ? list : [];
            }
        }

        /// <summary>
        /// Simulates an authoritative local parameter edit with actual changed bytes.
        /// </summary>
        internal void Modify(StorageEnumeration storage, ByteString content)
        {
            lock (m_gate)
            {
                EnsureMutable(storage);
                ProductionDatasetTestLayer layer = m_layers[storage];
                ProductionDatasetInformationType information = Copy(layer.Document.Information);
                information.LastModificationTimestamp = NextTimestamp();
                information.UserName = "test-operator";
                m_layers[storage] = layer with
                {
                    Document = new ProductionDatasetDocument
                    {
                        Content = ByteString.From(content.Span.ToArray()),
                        Information = information
                    },
                    Modified = true
                };
            }
        }

        /// <summary>
        /// Rejects additions beyond the deterministic test store's capacity.
        /// </summary>
        private void CheckCapacity(string name)
        {
            if (!m_files.ContainsKey(name) && m_files.Count >= 8)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfMemory);
            }
        }

        /// <summary>
        /// Reads a real in-memory persisted document.
        /// </summary>
        private ProductionDatasetDocument FindFile(string name)
        {
            return m_files.TryGetValue(name, out ProductionDatasetDocument? document) ? document
                : throw new ServiceResultException(StatusCodes.BadNotFound);
        }

        /// <summary>
        /// Enforces frozen state inside the authority transaction as well as at the protocol boundary.
        /// </summary>
        private void EnsureMutable(StorageEnumeration storage)
        {
            if (m_layers[storage].Frozen)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
        }

        /// <summary>
        /// Applies full or component-selective activation to actual bounded control bytes.
        /// </summary>
        private static ProductionDatasetTestLayer Activate(
            ProductionDatasetTestLayer previous,
            ProductionDatasetDocument document,
            ArrayOf<ushort> components)
        {
            if (components.IsEmpty)
            {
                return new ProductionDatasetTestLayer(Copy(document), false, previous.Frozen);
            }
            ArrayOf<ushort> codes = document.Information.Components;
            if (document.Content.Length != codes.Count ||
                previous.Document.Content.Length != codes.Count ||
                !previous.Document.Information.Components.Equals(codes))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            byte[] content = previous.Document.Content.Span.ToArray();
            for (int i = 0; i < codes.Count; i++)
            {
                foreach (ushort selected in components)
                {
                    if (codes[i] == selected)
                    {
                        content[i] = document.Content.Span[i];
                        break;
                    }
                }
            }
            return new ProductionDatasetTestLayer(
                new ProductionDatasetDocument
                {
                    Content = ByteString.From(content),
                    Information = Copy(document.Information)
                }, true, previous.Frozen);
        }

        /// <summary>
        /// Creates a deterministic UTC timestamp for a committed authority change.
        /// </summary>
        private DateTimeUtc NextTimestamp()
        {
            return (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(++m_revision);
        }

        /// <summary>
        /// Copies the generated metadata rather than sharing mutable provider state with callers.
        /// </summary>
        private static ProductionDatasetInformationType Copy(ProductionDatasetInformationType information)
        {
            return (ProductionDatasetInformationType)information.Clone();
        }

        /// <summary>
        /// Copies a document and its bytes.
        /// </summary>
        private static ProductionDatasetDocument Copy(ProductionDatasetDocument document)
        {
            return new ProductionDatasetDocument
            {
                Content = ByteString.From(document.Content.Span.ToArray()),
                Information = Copy(document.Information)
            };
        }

        /// <summary>
        /// Creates a detached status snapshot of a real control layer.
        /// </summary>
        private static ProductionDatasetStatus Snapshot(ProductionDatasetTestLayer layer)
        {
            return new ProductionDatasetStatus
            {
                Information = Copy(layer.Document.Information),
                Modified = layer.Modified,
                Frozen = layer.Frozen
            };
        }

        /// <summary>
        /// Creates an explicitly empty control layer.
        /// </summary>
        private static ProductionDatasetTestLayer EmptyLayer()
        {
            return new ProductionDatasetTestLayer(
                new ProductionDatasetDocument
                {
                    Content = ByteString.Empty,
                    Information = new ProductionDatasetInformationType()
                }, false, false);
        }

        /// <summary>The two simulated control layers.</summary>
        private static readonly StorageEnumeration[] s_controlLayers =
            [StorageEnumeration.PRODUCTION, StorageEnumeration.PREPARATION];

        /// <summary>Protects the test authority's atomic state transition.</summary>
        private readonly Lock m_gate = new();

        /// <summary>At most eight persisted test documents.</summary>
        private readonly Dictionary<string, ProductionDatasetDocument> m_files = new(StringComparer.Ordinal);

        /// <summary>The two real in-memory control-layer documents.</summary>
        private readonly Dictionary<StorageEnumeration, ProductionDatasetTestLayer> m_layers = [];

        /// <summary>At most four session-owned client lists.</summary>
        private readonly Dictionary<NodeId, ArrayOf<ProductionDatasetInformationType>> m_clientLists = [];

        /// <summary>The deterministic authority revision.</summary>
        private int m_revision;

        /// <summary>
        /// The complete state of one simulated layer.
        /// </summary>
        /// <param name="Document">The actual control bytes and metadata.</param>
        /// <param name="Modified">Whether parameters have changed since saving.</param>
        /// <param name="Frozen">Whether control mutation is forbidden.</param>
        private sealed record ProductionDatasetTestLayer(
            ProductionDatasetDocument Document, bool Modified, bool Frozen);
    }
}
