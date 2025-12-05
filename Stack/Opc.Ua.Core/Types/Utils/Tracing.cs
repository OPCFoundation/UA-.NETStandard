/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public event EventHandler<TraceEventArgs> TraceEventHandler;

        internal void RaiseTraceEvent(TraceEventArgs eventArgs)
        {
            TraceEventHandler?.Invoke(this, eventArgs);
        }
    }
}
