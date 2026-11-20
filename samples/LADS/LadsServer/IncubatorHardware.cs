// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.LADS.Server;

namespace LadsSample.Server
{
    /// <summary>
    /// Accepts the commands of clients and programs. Unlike the in-memory default it does not jump
    /// to a new target value; <see cref="IncubatorSimulation"/> ramps the measured values instead.
    /// </summary>
    public sealed class IncubatorHardware : ILadsFunctionProvider
    {
        /// <summary>
        /// Creates the hardware.
        /// </summary>
        public IncubatorHardware(ILogger<IncubatorHardware> logger)
        {
            m_logger = logger;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> OnCommandAsync(LadsFunctionCommand command, CancellationToken cancellationToken)
        {
            m_logger.CommandReceived(command.Function.Name, command.Kind, command.Value);
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated logging of the incubator hardware.
    /// </summary>
    internal static partial class IncubatorHardwareLog
    {
        /// <summary>
        /// Reports a command sent to a function.
        /// </summary>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "{Function}: {Command} {Value}")]
        public static partial void CommandReceived(
            this ILogger logger,
            string function,
            LadsFunctionCommandKind command,
            Variant value);
    }
}
