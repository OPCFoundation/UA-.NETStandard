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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// An authoritative snapshot of a control layer, not a proposed parameter change.
    /// </summary>
    public sealed record ProductionDatasetStatus
    {
        /// <summary>
        /// Gets the generated chapter 20 description of the current dataset.
        /// </summary>
        public required ProductionDatasetInformationType Information { get; init; }

        /// <summary>
        /// Gets whether parameters have changed since the last save.
        /// </summary>
        public bool Modified { get; init; }

        /// <summary>
        /// Gets whether the authority prevents changes to this control layer.
        /// </summary>
        public bool Frozen { get; init; }
    }

    /// <summary>
    /// A bounded file snapshot and its generated description.
    /// </summary>
    public sealed record ProductionDatasetDocument
    {
        /// <summary>
        /// Gets the dataset bytes in the device's own file format.
        /// </summary>
        public required ByteString Content { get; init; }

        /// <summary>
        /// Gets the matching description, including repeated component codes.
        /// </summary>
        public required ProductionDatasetInformationType Information { get; init; }
    }

    /// <summary>
    /// The control-layer snapshots returned after a single committed write transaction.
    /// </summary>
    public sealed record ProductionDatasetCommitResult
    {
        /// <summary>
        /// Gets the committed active state when PRODUCTION was requested.
        /// </summary>
        public ProductionDatasetStatus? Active { get; init; }

        /// <summary>
        /// Gets the committed preparation state when PREPARATION was requested.
        /// </summary>
        public ProductionDatasetStatus? Preparation { get; init; }
    }

    /// <summary>
    /// Supplies per-machine storage and activation authority for OPC 40083 chapter 20.
    /// </summary>
    /// <remarks>
    /// Every request contains the original authenticated operation context. Implementations must
    /// atomically enforce frozen state, persist or activate before returning, and throw on failure.
    /// A multi-destination commit is one transaction: validate all destinations before changing any,
    /// and do not return success after a partial application. Cancellation must not produce a
    /// successful-looking response. Component selection is device-format-specific; an empty list
    /// means all components, and repeated information codes must not be deduplicated.
    /// The binding never implements a machine's activation by assigning UA variables.
    /// </remarks>
    public interface IProductionDatasetProvider
    {
        /// <summary>
        /// Reads the current PRODUCTION or PREPARATION state without changing its parameters.
        /// </summary>
        ValueTask<ProductionDatasetStatus> ReadStatusAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Captures a file and its matching information from exactly one storage location.
        /// </summary>
        ValueTask<ProductionDatasetDocument> ReadAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetReadOptionsType options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Activates selected components of a stored file in the specified control layer.
        /// </summary>
        ValueTask<ProductionDatasetStatus> LoadAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            ArrayOf<ushort> components,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the selected control layer under the supplied name and returns its committed status.
        /// </summary>
        ValueTask<ProductionDatasetStatus> SaveAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the device's frozen policy and returns the resulting control-layer status.
        /// </summary>
        ValueTask<ProductionDatasetStatus> SetFrozenAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            bool frozen,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits uploaded bytes to all selected destinations before returning authoritative snapshots.
        /// </summary>
        /// <remarks>
        /// Information is absent when the client did not call SendProductionDatasetInformation.
        /// In that case the provider must obtain it from its file format, not invent a successful
        /// activation. Supplied information must be validated and retained consistently, not silently discarded.
        /// A FILE_SYSTEM-only commit returns a result with neither control layer set.
        /// </remarks>
        ValueTask<ProductionDatasetCommitResult> CommitAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetWriteOptionsType options,
            ByteString content,
            ProductionDatasetInformationType? information,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets bounded file-system candidates, optionally narrowing them with the supplied filters.
        /// </summary>
        /// <remarks>
        /// The binding applies the standard '*'/'?' name matching and exact mould filter to the returned candidates.
        /// </remarks>
        ValueTask<ArrayOf<ProductionDatasetInformationType>> GetListAsync(
            PlasticsRubberRequestContext context,
            string nameFilter,
            string mouldId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Accepts the client's available-dataset list, including an empty response to a request event.
        /// </summary>
        ValueTask ReceiveListAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<ProductionDatasetInformationType> information,
            CancellationToken cancellationToken = default);
    }
}
