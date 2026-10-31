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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The relative paths of the function variables that
    /// <see cref="LadsClient.ReadFunctionAsync(NodeId, System.Threading.CancellationToken)"/>
    /// and <see cref="LadsClient.ObserveFunctionAsync"/>
    /// look for. A function exposes the subset its type defines.
    /// </summary>
    public static class LadsFunctionMembers
    {
        /// <summary>
        /// <c>IsEnabled</c> of every function.
        /// </summary>
        public const string IsEnabled = BrowseNames.IsEnabled;

        /// <summary>
        /// <c>SensorValue</c> of the sensor functions.
        /// </summary>
        public const string SensorValue = BrowseNames.SensorValue;

        /// <summary>
        /// <c>RawValue</c> of the analog sensor functions.
        /// </summary>
        public const string RawValue = BrowseNames.RawValue;

        /// <summary>
        /// <c>CompensationValue</c> of the compensated analog scalar sensor.
        /// </summary>
        public const string CompensationValue = BrowseNames.CompensationValue;

        /// <summary>
        /// <c>CurrentValue</c> of the control functions.
        /// </summary>
        public const string CurrentValue = BrowseNames.CurrentValue;

        /// <summary>
        /// <c>TargetValue</c> of the control functions.
        /// </summary>
        public const string TargetValue = BrowseNames.TargetValue;

        /// <summary>
        /// <c>TotalizedValue</c> of the analog control function with totalizer.
        /// </summary>
        public const string TotalizedValue = BrowseNames.TotalizedValue;

        /// <summary>
        /// <c>DifferenceValue</c> of the timer control function.
        /// </summary>
        public const string DifferenceValue = BrowseNames.DifferenceValue;

        /// <summary>
        /// <c>CurrentMode</c> of the multi-mode analog control function.
        /// </summary>
        public const string CurrentMode = BrowseNames.CurrentMode;

        /// <summary>
        /// <c>ControlFunctionState/CurrentState</c> of the control functions.
        /// </summary>
        public const string ControlFunctionState = BrowseNames.ControlFunctionState + "/CurrentState";

        /// <summary>
        /// <c>CoverState/CurrentState</c> of the cover function.
        /// </summary>
        public const string CoverState = BrowseNames.CoverState + "/CurrentState";

        /// <summary>
        /// Gets all paths in the order they are read.
        /// </summary>
        public static ArrayOf<string> All { get; } =
        [
            IsEnabled,
            SensorValue,
            RawValue,
            CompensationValue,
            CurrentValue,
            TargetValue,
            TotalizedValue,
            DifferenceValue,
            CurrentMode,
            ControlFunctionState,
            CoverState
        ];
    }
}
