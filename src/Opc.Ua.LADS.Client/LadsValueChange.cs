// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A change of an observed LADS value.
    /// </summary>
    /// <param name="SourceId">The NodeId of the function or state machine the value belongs to.</param>
    /// <param name="Name">The relative path of the variable, for example <c>SensorValue</c>.</param>
    /// <param name="NodeId">The NodeId of the variable.</param>
    /// <param name="Value">The new value.</param>
    public sealed record LadsValueChange(NodeId SourceId, string Name, NodeId NodeId, DataValue Value);
}
