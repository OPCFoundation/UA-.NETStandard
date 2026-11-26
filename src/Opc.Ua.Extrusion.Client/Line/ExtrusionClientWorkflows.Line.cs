// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Line;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Production-job workflows for OPC 40084-2.
    /// </summary>
    public static partial class ExtrusionClientWorkflows
    {
        /// <summary>
        /// Opens the exact OPC 40084-2 group/job methods, not a Machinery or ISA-95 replacement.
        /// </summary>
        public static async ValueTask<JobGroupsTypeClient> JobsAsync(
            this ExtrusionLine_InterfaceTypeClient line,
            CancellationToken cancellationToken = default)
        {
            if (line == null)
            {
                throw new ArgumentNullException(nameof(line));
            }
            return await line.GetJobGroupsAsync(line.Telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw Missing("JobGroups");
        }
    }
}
