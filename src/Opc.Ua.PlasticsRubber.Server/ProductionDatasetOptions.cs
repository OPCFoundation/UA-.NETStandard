/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Selects optional chapter 20 members implemented by the supplied authority.
    /// </summary>
    [Flags]
    public enum ProductionDatasetFeatures
    {
        /// <summary>
        /// The mandatory active information and file transfer only.
        /// </summary>
        None = 0,

        /// <summary>
        /// The preparation control layer.
        /// </summary>
        Preparation = 1,

        /// <summary>
        /// Dataset-list exchange and list-request events.
        /// </summary>
        Lists = 2,

        /// <summary>
        /// File-handle-scoped information exchange.
        /// </summary>
        Information = 4,

        /// <summary>
        /// The writable frozen policy on each selected control layer.
        /// </summary>
        Frozen = 8,

        /// <summary>
        /// The modification flag on each selected control layer.
        /// </summary>
        Modified = 16,

        /// <summary>
        /// Load and Save on each selected control layer.
        /// </summary>
        LoadSave = 32,

        /// <summary>
        /// All optional chapter 20 capabilities.
        /// </summary>
        All = Preparation | Lists | Information | Frozen | Modified | LoadSave
    }

    /// <summary>
    /// Immutable feature selection and resource limits for a single management instance.
    /// </summary>
    public sealed record ProductionDatasetOptions
    {
        /// <summary>
        /// Gets the selected optional features; every selection requires real provider support.
        /// </summary>
        public ProductionDatasetFeatures Features { get; init; } = ProductionDatasetFeatures.All;

        /// <summary>
        /// Gets whether to bind exactly the supplied subtree without adding any optional members.
        /// </summary>
        /// <remarks>
        /// This supports independent per-layer selections made by a device-family materializer.
        /// When true, Features is inferred from existing nodes instead of expanding its grouped selections.
        /// </remarks>
        public bool PreserveExistingSelection { get; init; }

        /// <summary>
        /// Gets the maximum bytes in one transferred file.
        /// </summary>
        public int MaximumFileBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum simultaneous temporary files, including pending uploads.
        /// </summary>
        public int MaximumTransfers { get; init; } = 16;

        /// <summary>
        /// Gets the maximum datasets in one incoming or outgoing list.
        /// </summary>
        public int MaximumDatasetCount { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum component codes or product names in one description.
        /// </summary>
        public int MaximumComponents { get; init; } = 256;

        /// <summary>
        /// Gets the maximum characters in a name, filter, or descriptive string.
        /// </summary>
        public int MaximumStringLength { get; init; } = 1024;

        /// <summary>
        /// Gets the inactivity limit advertised as ClientProcessingTimeout.
        /// </summary>
        public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets the deadline supplied to authorization and provider operations.
        /// </summary>
        public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Rejects unbounded or unknown settings before any handlers are installed.
        /// </summary>
        internal void Validate()
        {
            if ((Features & ~ProductionDatasetFeatures.All) != 0 ||
                MaximumFileBytes <= 0 || MaximumTransfers <= 0 || MaximumDatasetCount <= 0 ||
                MaximumComponents <= 0 || MaximumStringLength <= 0 ||
                TransferTimeout < TimeSpan.FromMilliseconds(1) || TransferTimeout > TimeSpan.FromDays(1) ||
                OperationTimeout < TimeSpan.FromMilliseconds(1) || OperationTimeout > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(ProductionDatasetOptions));
            }
        }
    }
}
