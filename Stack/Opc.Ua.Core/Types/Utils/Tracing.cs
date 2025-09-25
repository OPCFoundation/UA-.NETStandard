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
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Used as underlying tracing object for event processing.
    /// </summary>
    public sealed class Tracing
    {
        private static readonly Lock s_syncRoot = new();
        private static Tracing s_instance;

        /// <summary>
        /// Private constructor.
        /// </summary>
        private Tracing()
        {
        }

        /// <summary>
        /// Whether the Trace Event Handler is active.
        /// </summary>
        public static bool IsEnabled()
        {
            return s_instance != null && s_instance.TraceEventHandler != null;
        }

        /// <summary>
        /// Public Singleton Instance getter.
        /// </summary>
        public static Tracing Instance
        {
            get
            {
                if (s_instance == null)
                {
                    lock (s_syncRoot)
                    {
                        s_instance ??= new Tracing();
                    }
                }
                return s_instance;
            }
        }

        /// <summary>
        /// Occurs when a trace call is made.
        /// </summary>
        public event EventHandler<TraceEventArgs> TraceEventHandler;

        internal void RaiseTraceEvent(TraceEventArgs eventArgs)
        {
            TraceEventHandler?.Invoke(this, eventArgs);
        }
    }
}
