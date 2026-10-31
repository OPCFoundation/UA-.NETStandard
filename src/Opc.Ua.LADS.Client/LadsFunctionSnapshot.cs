/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A snapshot of the variables of a LADS function.
    /// </summary>
    /// <param name="FunctionId">The NodeId of the function.</param>
    /// <param name="DisplayName">The display name of the function.</param>
    /// <param name="Kind">The most specific standard function type.</param>
    /// <param name="Values">The variables the function exposes.</param>
    public sealed record LadsFunctionSnapshot(
        NodeId FunctionId,
        string DisplayName,
        LadsFunctionKind Kind,
        ArrayOf<LadsFunctionValue> Values)
    {
        /// <summary>
        /// Gets the <c>IsEnabled</c> flag, or <c>null</c> when absent or bad.
        /// </summary>
        public bool? IsEnabled =>
            ValueOf(LadsFunctionMembers.IsEnabled).TryGetValue(out bool enabled)
                ? enabled
                : null;

        /// <summary>
        /// Gets the <c>SensorValue</c> of a sensor function.
        /// </summary>
        public Variant SensorValue => ValueOf(LadsFunctionMembers.SensorValue);

        /// <summary>
        /// Gets the <c>CurrentValue</c> of a control function.
        /// </summary>
        public Variant CurrentValue => ValueOf(LadsFunctionMembers.CurrentValue);

        /// <summary>
        /// Gets the <c>TargetValue</c> of a control function.
        /// </summary>
        public Variant TargetValue => ValueOf(LadsFunctionMembers.TargetValue);

        /// <summary>
        /// Gets the current state of the control function or cover state machine,
        /// or <c>null</c> when the function has none.
        /// </summary>
        public string? State
        {
            get
            {
                if (TryGetValue(LadsFunctionMembers.ControlFunctionState, out DataValue value) ||
                    TryGetValue(LadsFunctionMembers.CoverState, out value))
                {
                    return LadsClient.AsString(value);
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the value of a variable by its relative path.
        /// </summary>
        /// <param name="name">The relative path, for example <c>RawValue</c>.</param>
        /// <param name="value">The value.</param>
        /// <returns><c>true</c> when the function exposes the variable.</returns>
        public bool TryGetValue(string name, out DataValue value)
        {
            foreach (LadsFunctionValue member in Values)
            {
                if (string.Equals(member.Name, name, StringComparison.Ordinal))
                {
                    value = member.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Gets the value of a variable, or a null variant when absent or bad.
        /// </summary>
        /// <param name="name">The relative path, for example <c>RawValue</c>.</param>
        public Variant ValueOf(string name)
        {
            return TryGetValue(name, out DataValue value) ? LadsClient.AsVariant(value) : Variant.Null;
        }
    }
}
