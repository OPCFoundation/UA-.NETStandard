// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            if (configuration.OutputFilePath != null)
            {
                Utils.SetTraceLog(configuration.OutputFilePath, configuration.DeleteOnLoad);
            }
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
