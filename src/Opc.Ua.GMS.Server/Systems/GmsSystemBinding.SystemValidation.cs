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
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Validates native system fields before any shared or native value is committed.
        /// </summary>
        private void ValidateSystems(GmsSystemSnapshot snapshot)
        {
            if (snapshot.Identification is { } identification)
            {
                Raw(identification.SubDeviceClass, BuiltInType.String);
                if (identification.SubDeviceClass.WrappedValue.TryGetValue(out string subClass))
                {
                    Text(subClass, true);
                }
                Raw(identification.Workspace, BuiltInType.ExtensionObject);
                if (!identification.Workspace.WrappedValue.IsNull)
                {
                    if (identification.Workspace.WrappedValue.TryGetStructure<CartesianWorkspaceType>(
                        out CartesianWorkspaceType? box))
                    {
                        Dimension(box.Length);
                        Dimension(box.Width);
                        Dimension(box.Height);
                    }
                    else if (identification.Workspace.WrappedValue.TryGetStructure<CylindricalWorkspaceType>(
                        out CylindricalWorkspaceType? cylinder))
                    {
                        Dimension(cylinder.Length);
                        Dimension(cylinder.Radius);
                    }
                    else
                    {
                        throw Invalid("Workspace must retain a released Cartesian or Cylindrical workspace structure.");
                    }
                }
            }
            if (snapshot.Loading is { } loading)
            {
                Text(loading.Name, true);
                Unsigned(loading.LoadStatus, 3, required: true);
                Raw(loading.IsInLoadingPosition, BuiltInType.Boolean);
            }
            if (snapshot.ToolMonitoring is { } tools)
            {
                Text(tools.Name, true);
                if ((!tools.ActiveSensorKeys.IsNull && !tools.ActiveToolIds.IsNull) ||
                    tools.ActiveSensorKeys.Count > m_options.MaximumSensors)
                {
                    throw Invalid("ActiveTool uses either bounded local keys or actual local NodeIds, not both.");
                }
                foreach (string key in tools.ActiveSensorKeys)
                {
                    Text(key, true);
                }
                Raw(tools.ActiveToolIds, BuiltInType.NodeId, rank: ValueRanks.OneDimension);
                if (tools.ActiveToolIds.WrappedValue.TryGetValue(out ArrayOf<NodeId> ids) &&
                    ids.Count > m_options.MaximumSensors)
                {
                    throw Invalid("ActiveTool exceeds the configured aggregate sensor bound.");
                }
                if (!StatusCode.IsBad(tools.Sample.StatusCode) && tools.Sample.SourceTimestamp == default)
                {
                    throw Invalid("Usable tool-monitoring observations need an acquisition timestamp.");
                }
            }
            if (snapshot.Calibration is { } calibration)
            {
                ValidateCalibration(calibration);
            }
        }

        /// <summary>
        /// Validates calibration as an observation, never deriving a successful calibration or scheduling one.
        /// </summary>
        private void ValidateCalibration(GmsCalibrationObservation value)
        {
            Raw(value.Calibrated, BuiltInType.Boolean, required: true);
            Raw(value.DateOfCalibration, BuiltInType.DateTime, required: true);
            Raw(value.PredictedTime, BuiltInType.DateTime, required: true);
            Nonnegative(value.CalibrationInterval, positive: true);
            Nonnegative(value.CalibrationPreptime);
            Strings(value.CalibrationCertificate);
            if (value.CalibrationInterval.WrappedValue.TryGetValue(out double interval) &&
                value.CalibrationPreptime.WrappedValue.TryGetValue(out double preparation) && preparation > interval)
            {
                throw Invalid("Calibration preparation lead time cannot exceed its reported calibration interval.");
            }
            bool dated = value.DateOfCalibration.WrappedValue.TryGetValue(out DateTimeUtc date) &&
                !StatusCode.IsBad(value.DateOfCalibration.StatusCode);
            if (dated && (date == default || date > value.DateOfCalibration.SourceTimestamp))
            {
                throw Invalid("The last actual calibration date cannot be unspecified or after its acquisition time.");
            }
            if (value.Calibrated.WrappedValue.TryGetValue(out bool calibrated) && calibrated &&
                !StatusCode.IsBad(value.Calibrated.StatusCode) && !dated)
            {
                throw Invalid("A usable calibrated state requires the actual last calibration date.");
            }
            GmsCalibrationObservation? previous = Observation?.Calibration;
            if (dated && previous != null &&
                !StatusCode.IsBad(previous.DateOfCalibration.StatusCode) &&
                previous.DateOfCalibration.WrappedValue.TryGetValue(out DateTimeUtc last) && date < last)
            {
                throw Invalid("The observed last calibration date cannot move backwards.");
            }
            if (dated && value.PredictedTime.WrappedValue.TryGetValue(out DateTimeUtc predicted) &&
                !StatusCode.IsBad(value.PredictedTime.StatusCode) && predicted < date)
            {
                throw Invalid("The next reported calibration cannot precede the last actual calibration.");
            }
            if (value.CalibrationCertificate.WrappedValue.TryGetValue(out ArrayOf<string> certificates))
            {
                foreach (string certificate in certificates)
                {
                    if (!Uri.TryCreate(certificate, UriKind.Absolute, out _))
                    {
                        throw Invalid("CalibrationCertificate entries must be absolute reference URIs.");
                    }
                }
            }
        }

        /// <summary>
        /// Accepts finite nonnegative native geometry, without inventing units or additional coordinate systems.
        /// </summary>
        private static void Dimension(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw Invalid("Workspace dimensions must be finite and nonnegative.");
            }
        }
    }
}
