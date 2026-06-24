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

using System;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.Subscriber
{
    /// <summary>
    /// Convenience factory that builds a subscriber-side
    /// <see cref="ISubscribedDataSetSink"/> which materialises received DataSet
    /// fields onto an external OPC UA server. It wires a
    /// <see cref="TargetVariablesSink"/> over an
    /// <see cref="ExternalServerTargetVariableWriter"/> so the wiring stage only
    /// needs the TargetVariables configuration and a connected session.
    /// </summary>
    public static class ExternalServerSubscribedDataSetSink
    {
        /// <summary>
        /// Creates a <see cref="TargetVariablesSink"/> that writes the configured
        /// target variables to the supplied external-server session.
        /// </summary>
        /// <param name="configuration">
        /// The TargetVariables configuration holding the per-field
        /// <see cref="FieldTargetDataType"/> entries.
        /// </param>
        /// <param name="session">
        /// The external-server session used to apply the writes.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the writer's logger.
        /// </param>
        /// <returns>
        /// A subscribed dataset sink backed by the external server.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="configuration"/>, <paramref name="session"/>
        /// or <paramref name="telemetry"/> is <see langword="null"/>.
        /// </exception>
        public static ISubscribedDataSetSink Create(
            TargetVariablesDataType configuration,
            IExternalServerSession session,
            ITelemetryContext telemetry)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var writer = new ExternalServerTargetVariableWriter(session, telemetry);
            return new TargetVariablesSink(configuration, writer);
        }
    }
}
