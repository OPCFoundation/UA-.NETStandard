/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    /// 
    /// </summary>
    public class TraceEventLogger : ILogger
    {
        /// <summary>
        /// 
        /// </summary>
        public TraceEventLogger()
        { }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        /// <param name="state"></param>
        public IDisposable BeginScope<TState>(TState state) => default;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logLevel"></param>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        /// <param name="logLevel"></param>
        /// <param name="eventId"></param>
        /// <param name="state"></param>
        /// <param name="exception"></param>
        /// <param name="formatter"></param>
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

            if (Tracing.IsEnabled())
            {
                var message = formatter(state, exception);
                Utils.Trace(exception, eventId.Id & 0x1ff, message, false, null);
            }
        }
    }
}
