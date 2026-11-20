/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The value of a variable of a LADS function, for example
    /// <c>SensorValue</c>, <c>TargetValue</c> or <c>ControlFunctionState/CurrentState</c>.
    /// </summary>
    /// <param name="Name">The relative path of the variable, separated by <c>/</c>.</param>
    /// <param name="NodeId">The NodeId of the variable.</param>
    /// <param name="Value">The value.</param>
    public sealed record LadsFunctionValue(string Name, NodeId NodeId, DataValue Value);
}
