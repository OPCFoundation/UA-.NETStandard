// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The source-generated log messages of the LADS server runtime.
    /// </summary>
    internal static partial class LadsServerLog
    {
        /// <summary>
        /// Reports a device that was published.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsRuntime + 0,
            Level = LogLevel.Information,
            Message = "Published LADS device '{Name}' with {FunctionalUnits} functional unit(s), NodeId={NodeId}.")]
        public static partial void DevicePublished(
            this ILogger logger,
            string name,
            int functionalUnits,
            NodeId nodeId);

        /// <summary>
        /// Reports a state machine transition.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsRuntime + 1,
            Level = LogLevel.Debug,
            Message = "LADS '{Owner}' moved to state '{State}'.")]
        public static partial void StateChanged(this ILogger logger, string owner, Enum state);

        /// <summary>
        /// Reports a command a provider rejected.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsRuntime + 2,
            Level = LogLevel.Warning,
            Message = "The provider rejected the command '{Command}' of '{Owner}': {Status}.")]
        public static partial void CommandRejected(
            this ILogger logger,
            string owner,
            string command,
            StatusCode status);

        /// <summary>
        /// Reports a failure while the runtime was released.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsRuntime + 3,
            Level = LogLevel.Warning,
            Message = "Releasing the LADS runtime failed.")]
        public static partial void ReleaseFailed(this ILogger logger, Exception exception);

        /// <summary>
        /// Reports that the Machines folder is not available.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsRuntime + 4,
            Level = LogLevel.Debug,
            Message = "LADS device '{Name}' is not organized under the Machines folder.")]
        public static partial void MachinesFolderUnavailable(this ILogger logger, string name);

        /// <summary>
        /// Reports a program run that started.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsPrograms + 0,
            Level = LogLevel.Information,
            Message = "Functional unit '{FunctionalUnit}' started program run '{RunId}' of template '{TemplateId}'.")]
        public static partial void ProgramStarted(
            this ILogger logger,
            string functionalUnit,
            string runId,
            string? templateId);

        /// <summary>
        /// Reports a program run that ended.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsPrograms + 1,
            Level = LogLevel.Information,
            Message = "Program run '{RunId}' of functional unit '{FunctionalUnit}' ended with outcome {Outcome}.")]
        public static partial void ProgramEnded(
            this ILogger logger,
            string functionalUnit,
            string runId,
            LadsProgramOutcome outcome);

        /// <summary>
        /// Reports a program executor that failed.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsPrograms + 2,
            Level = LogLevel.Warning,
            Message = "The program executor of run '{RunId}' failed; the run is aborted.")]
        public static partial void ProgramFailed(this ILogger logger, string runId, Exception exception);

        /// <summary>
        /// Reports a result that was removed because of the retention limit.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsPrograms + 3,
            Level = LogLevel.Debug,
            Message = "Removed the retained result '{Name}' of functional unit '{FunctionalUnit}'.")]
        public static partial void ResultRemoved(this ILogger logger, string functionalUnit, string name);

        /// <summary>
        /// Reports a result file that exceeded the size limit and was not published.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsPrograms + 4,
            Level = LogLevel.Warning,
            Message = "The result file '{Name}' of functional unit '{FunctionalUnit}' exceeds the size limit.")]
        public static partial void ResultFileSkipped(this ILogger logger, string functionalUnit, string name);

        /// <summary>
        /// Reports a maintenance task state change.
        /// </summary>
        [LoggerMessage(
            EventId = LadsServerEventIds.LadsMaintenance + 0,
            Level = LogLevel.Information,
            Message = "Maintenance task '{Task}' of device '{Device}' moved to {State}.")]
        public static partial void MaintenanceStateChanged(
            this ILogger logger,
            string device,
            string task,
            LadsMaintenanceStateValue state);
    }
}
