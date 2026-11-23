// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// A version-neutral property captured by the in-memory engine. Properties
    /// appear on the personnel, equipment, physical-asset and material
    /// requirement and actual structures of both Job Control V1 and V2.
    /// </summary>
    public sealed record Isa95Property
    {
        internal uint V2EncodingMask { get; init; }

        /// <summary>
        /// The identifier of the property.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The value of the property.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// The localized descriptions of the property. V1 carries a single
        /// string description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The engineering unit of measure of the property as the version-neutral
        /// core <see cref="EUInformation"/> structure. V1 stores a plain string
        /// (<c>UoM</c>) projected onto <see cref="EUInformation.DisplayName"/>.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The nested sub-properties.
        /// </summary>
        public ArrayOf<Isa95Property> Subproperties { get; init; } = [];
    }
}
