// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        private static Tracing? s_instance;

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
#pragma warning disable CA1508 // Avoid dead conditional code
                        s_instance ??= new Tracing();
#pragma warning restore CA1508 // Avoid dead conditional code
                    }
                }
                return s_instance;
            }
        }

        /// <summary>
        /// Occurs when a trace call is made.
        /// </summary>
        public event EventHandler<TraceEventArgs>? TraceEventHandler;

        internal void RaiseTraceEvent(TraceEventArgs eventArgs)
        {
            TraceEventHandler?.Invoke(this, eventArgs);
        }
    }
}
