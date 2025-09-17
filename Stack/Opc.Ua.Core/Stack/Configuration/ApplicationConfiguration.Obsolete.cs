/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A class that install, configures and runs a UA application.
    /// </summary>
    public static class ApplicationConfigurationObsolete
    {
        /// <summary>
        /// Applies the trace settings to the current process.
        /// </summary>
        [Obsolete("Use ITelemetryContext configuration surface")]
        public static void ApplySettings(this TraceConfiguration configuration)
        {
            Utils.SetTraceLog(configuration.OutputFilePath, configuration.DeleteOnLoad);
            Utils.SetTraceMask(configuration.TraceMasks);

            if (configuration.TraceMasks == 0)
            {
                Utils.SetTraceOutput(Utils.TraceOutput.Off);
            }
            else
            {
                Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);
            }
        }
    }
}
