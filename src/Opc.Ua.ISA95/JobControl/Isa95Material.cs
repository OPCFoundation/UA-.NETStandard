// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// A version-neutral material resource entry captured by the in-memory
    /// engine. The same structure describes a job-order material requirement and
    /// a job-response material actual in both Job Control V1 and V2.
    /// </summary>
    public sealed record Isa95Material
    {
        internal uint V2EncodingMask { get; init; }

        /// <summary>
        /// The material class identifier.
        /// </summary>
        public string? MaterialClassId { get; init; }

        /// <summary>
        /// The material definition identifier.
        /// </summary>
        public string? MaterialDefinitionId { get; init; }

        /// <summary>
        /// The material lot identifier.
        /// </summary>
        public string? MaterialLotId { get; init; }

        /// <summary>
        /// The material sub-lot identifier.
        /// </summary>
        public string? MaterialSublotId { get; init; }

        /// <summary>
        /// The localized descriptions of the material. V1 carries a single string
        /// description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The use of the material (the V1/V2 <c>MaterialUse</c> field).
        /// </summary>
        public string? Use { get; init; }

        /// <summary>
        /// The quantity of the material.
        /// </summary>
        public string? Quantity { get; init; }

        /// <summary>
        /// The engineering unit of measure as the version-neutral core
        /// <see cref="EUInformation"/> structure.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The properties of the material.
        /// </summary>
        public ArrayOf<Isa95Property> Properties { get; init; } = [];
    }
}
