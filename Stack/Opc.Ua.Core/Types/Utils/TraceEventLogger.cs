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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The TraceEvent logger calls the legacy Utils.Trace API to
    /// support applications which use TraceEvent or file/debug logging.
    /// </summary>
    public class TraceEventLogger : ILogger
    {
        /// <summary>
        /// Set the log level
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Trace;

        /// <inheritdoc/>
        public IDisposable BeginScope<TState>(TState state) => default;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            int traceMask = Utils.GetTraceMask(eventId, logLevel);
            Utils.Trace(state, exception, traceMask, formatter);
        }
    }
}
