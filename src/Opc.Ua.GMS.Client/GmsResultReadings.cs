// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// A detached published result slot. An absent UsedTools property is DataValue.Null, not an empty Good sample.
    /// </summary>
    public sealed class GmsResultVariableValue
    {
        /// <summary>
        /// Captures the result and optional tool-property samples.
        /// </summary>
        internal GmsResultVariableValue(NodeId nodeId, in DataValue value, in DataValue usedTools)
        {
            NodeId = nodeId;
            m_value = value.Copy();
            m_usedTools = usedTools.IsNull ? DataValue.Null : usedTools.Copy();
        }

        /// <summary>
        /// Gets the stable result-variable identity.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets the complete result sample, including bad or uncertain quality and acquisition time.
        /// </summary>
        public DataValue Value => m_value.Copy();

        /// <summary>
        /// Gets the optional used-tool sample without inventing an absent value.
        /// </summary>
        public DataValue UsedTools => m_usedTools.IsNull ? DataValue.Null : m_usedTools.Copy();

        private readonly DataValue m_value;
        private readonly DataValue m_usedTools;
    }

    /// <summary>
    /// A raw analog sample and its independently acquired engineering-unit metadata.
    /// </summary>
    public sealed class GmsAnalogReading
    {
        /// <summary>
        /// Captures both raw samples so measurement quality does not conceal metadata quality.
        /// </summary>
        internal GmsAnalogReading(in DataValue value, in DataValue units)
        {
            m_value = value.Copy();
            m_units = units.Copy();
        }

        /// <summary>
        /// Gets the acquired measurement sample.
        /// </summary>
        public DataValue Value => m_value.Copy();

        /// <summary>
        /// Gets the independently acquired EngineeringUnits property.
        /// </summary>
        public DataValue EngineeringUnits => m_units.Copy();

        private readonly DataValue m_value;
        private readonly DataValue m_units;
    }

    /// <summary>
    /// The actual correction variables, retaining optional absence and every value's quality and timestamps.
    /// </summary>
    public sealed record GmsCorrectionValues
    {
        /// <summary>
        /// Gets the correction object identity.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the mandatory correction identifier sample.
        /// </summary>
        public required DataValue Identifier { get; init; }

        /// <summary>
        /// Gets the mandatory characteristic-reference sample.
        /// </summary>
        public required DataValue CharacteristicIdentifier { get; init; }

        /// <summary>
        /// Gets the optional relative correction value.
        /// </summary>
        public GmsAnalogReading? RelativeValue { get; init; }

        /// <summary>
        /// Gets the optional absolute correction value.
        /// </summary>
        public GmsAnalogReading? AbsoluteValue { get; init; }

        /// <summary>
        /// Gets the optional lower control boundary.
        /// </summary>
        public GmsAnalogReading? LowerControlLimit { get; init; }

        /// <summary>
        /// Gets the optional upper control boundary.
        /// </summary>
        public GmsAnalogReading? UpperControlLimit { get; init; }

        /// <summary>
        /// Gets the optional description, or DataValue.Null when its node is absent.
        /// </summary>
        public DataValue Description { get; init; }

        /// <summary>
        /// Gets the optional program name, or DataValue.Null when its node is absent.
        /// </summary>
        public DataValue ProgramName { get; init; }
    }
}
