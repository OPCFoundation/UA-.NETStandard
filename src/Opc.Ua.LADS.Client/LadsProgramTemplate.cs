// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A program template of a functional unit's <c>ProgramTemplateSet</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the template.</param>
    /// <param name="Name">The browse name of the template, the template id expected by <c>StartProgram</c>.</param>
    public sealed record LadsProgramTemplate(NodeId NodeId, string Name)
    {
        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// Gets the author.
        /// </summary>
        public string? Author { get; init; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Gets the template id the device uses.
        /// </summary>
        public string? DeviceTemplateId { get; init; }

        /// <summary>
        /// Gets the template id the supervisory system uses.
        /// </summary>
        public string? SupervisoryTemplateId { get; init; }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// Gets the creation time.
        /// </summary>
        public DateTimeUtc Created { get; init; }

        /// <summary>
        /// Gets the time of the last modification.
        /// </summary>
        public DateTimeUtc Modified { get; init; }
    }
}
