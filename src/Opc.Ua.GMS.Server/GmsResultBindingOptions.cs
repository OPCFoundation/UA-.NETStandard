// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Explicit result-subtree capabilities; these options do not select equipment or production facets.
    /// </summary>
    public sealed record GmsResultBindingOptions
    {
        /// <summary>
        /// Gets the provider and response bounds for this instance.
        /// </summary>
        public GmsResultLimits Limits { get; init; } = new();

        /// <summary>
        /// Gets the number of stable derived GMS result-variable slots; zero selects method-only access.
        /// </summary>
        public int PublishedResults { get; init; } = 4;

        /// <summary>
        /// Gets whether result variables expose the optional UsedTools property.
        /// </summary>
        public bool IncludeUsedTools { get; init; }

        /// <summary>
        /// Gets whether the shared Machinery temporary-file transfer path is enabled.
        /// </summary>
        public bool EnableFileTransfer { get; init; } = true;

        /// <summary>
        /// Gets whether intermediate results may emit their own concrete IntermediateResultEventType subtype.
        /// </summary>
        public bool EnableIntermediateEvents { get; init; }

        /// <summary>
        /// Gets the initially configured corrections; null omits the optional folder, empty exposes an empty folder.
        /// </summary>
        public ArrayOf<GmsCorrection> Corrections { get; init; }

        /// <summary>
        /// Validates the requested subtree before any instance is published.
        /// </summary>
        internal void Validate()
        {
            if (Limits == null)
            {
                throw new ArgumentNullException(nameof(Limits));
            }
            Limits.Validate();
            if (PublishedResults < 0 || PublishedResults > Limits.MaximumResults ||
                Corrections.Count > Limits.MaximumCorrections)
            {
                throw new ArgumentOutOfRangeException(nameof(PublishedResults));
            }
        }
    }
}
