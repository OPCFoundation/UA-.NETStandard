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
    /// The event arguments provided when a trace event is raised.
    /// </summary>
    public class TraceEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the TraceEventArgs class.
        /// </summary>
        /// <param name="traceMask">The trace mask.</param>
        /// <param name="format">The format.</param>
        /// <param name="message">The message.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="args">The arguments.</param>
        internal TraceEventArgs(int traceMask, string format, string message, Exception exception, object[] args)
        {
            TraceMask = traceMask;
            Format = format;
            Message = message;
            Exception = exception;
            Arguments = args;
        }
        #endregion Constructors

        #region Public Properties
        /// <summary>
        /// Gets the trace mask.
        /// </summary>
        public int TraceMask { get; private set; }

        /// <summary>
        /// Gets the format.
        /// </summary>
        public string Format { get; private set; }

        /// <summary>
        /// Gets the arguments.
        /// </summary>
        public object[] Arguments { get; private set; }

        /// <summary>
        /// Gets the message.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Gets the exception.
        /// </summary>
        public Exception Exception { get; private set; }
        #endregion Public Properties
    }
}
