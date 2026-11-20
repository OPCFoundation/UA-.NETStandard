// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral work master reference captured by the in-memory engine.
    /// Work masters are referenced by a job order in both Job Control V1 and V2.
    /// V1 carries a single string description; V2 carries a single localized text,
    /// both unified onto <see cref="Description"/>.
    /// </summary>
    internal sealed record Isa95WorkMaster
    {
        /// <summary>
        /// The identifier of the work master.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The localized descriptions of the work master.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The parameters of the work master.
        /// </summary>
        public ArrayOf<Isa95Parameter> Parameters { get; init; } = [];
    }
}
