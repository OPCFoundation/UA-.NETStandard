// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral parameter captured by the in-memory engine. The engine
    /// stores this reduced projection of the Job Control V1/V2
    /// <c>ISA95ParameterDataType</c> so that no generated type leaks into the
    /// engine. Conversions between this record and the generated types are
    /// intentionally loss-aware (see <see cref="Isa95JobControlConversions"/>).
    /// </summary>
    internal sealed record Isa95Parameter
    {
        /// <summary>
        /// The identifier of the parameter.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The value of the parameter.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// The localized descriptions of the parameter. V1 carries a single
        /// string description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The engineering unit of measure of the parameter as the version-neutral
        /// core <see cref="EUInformation"/> structure. V1 stores a plain string
        /// (<c>UoM</c>) which is projected onto <see cref="EUInformation.DisplayName"/>;
        /// V2 stores the full structure which is preserved as-is.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The nested sub-parameters.
        /// </summary>
        public ArrayOf<Isa95Parameter> Subparameters { get; init; } = [];
    }
}
