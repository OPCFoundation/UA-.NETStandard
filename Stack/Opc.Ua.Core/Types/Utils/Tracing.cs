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

namespace Opc.Ua
{
    /// <summary>
    /// Used as underlying tracing object for event processing.
    /// </summary>
    public class Tracing
    {
        #region Private Members
        private static object m_syncRoot = new Object();
        private static Tracing s_instance;
        #endregion Private Members

        #region Singleton Instance
        /// <summary>
        /// Private constructor.
        /// </summary>
        private Tracing()
        { }

        /// <summary>
        /// Public Singleton Instance getter.
        /// </summary>
        public static Tracing Instance
        {
            get
            {
                if (s_instance == null)
                {
                    lock (m_syncRoot)
                    {
                        if (s_instance == null)
                        {
                            s_instance = new Tracing();
                        }
                    }
                }
                return s_instance;
            }
        }
        #endregion Singleton Instance

        #region Public Events
        /// <summary>
        /// Occurs when a trace call is made.
        /// </summary>
        public event EventHandler<TraceEventArgs> TraceEventHandler;
        #endregion Public Events

        #region Internal Members
        internal void RaiseTraceEvent(TraceEventArgs eventArgs)
        {
            if (TraceEventHandler != null)
            {
                try
                {
                    TraceEventHandler(this, eventArgs);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Exception invoking Trace Event Handler", true, null);
                }
            }
        }
        #endregion
    }
}
