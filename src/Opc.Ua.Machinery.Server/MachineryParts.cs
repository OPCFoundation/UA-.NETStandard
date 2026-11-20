// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// The parts of the OPC 40001 specification series a server exposes.
    /// </summary>
    /// <remarks>
    /// Each part is its own model assembly, so a server takes only what it
    /// serves: a machine tool that reports jobs and results does not have to
    /// carry PADIM through OPC 40001-2. The selection decides which models the
    /// node manager loads, which namespaces it announces and which conformance
    /// units it can advertise.
    /// </remarks>
    [Flags]
    public enum MachineryParts
    {
        /// <summary>
        /// No part. Not a valid configuration on its own.
        /// </summary>
        None = 0,

        /// <summary>
        /// OPC 40001-1 Basic Building Blocks — the <c>Machines</c> folder,
        /// identification, monitoring, components, equipment and
        /// notifications. Required by every other part except
        /// <see cref="Result"/>.
        /// </summary>
        BuildingBlocks = 1,

        /// <summary>
        /// OPC 40001-2 Process Values. Pulls in OPC 30081 PADIM.
        /// </summary>
        ProcessValues = 2,

        /// <summary>
        /// OPC 40001-3 Job Management. Pulls in OPC 10031-4 ISA-95 Job Control
        /// V2; it needs no Device Integration of its own.
        /// </summary>
        Jobs = 4,

        /// <summary>
        /// OPC 40001-4 Energy Management. Pulls in OPC 34100 ECM.
        /// </summary>
        Energy = 8,

        /// <summary>
        /// OPC 40001-101 Result Transfer. Needs UA core only — a pure result
        /// server carries neither Device Integration nor the machine model.
        /// </summary>
        Result = 16,

        /// <summary>
        /// Every part of the series.
        /// </summary>
        All = BuildingBlocks | ProcessValues | Jobs | Energy | Result
    }
}
