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
