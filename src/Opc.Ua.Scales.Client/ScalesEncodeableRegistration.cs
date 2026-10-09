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
using Opc.Ua.Client;

namespace Opc.Ua.Scales.Client
{
    /// <summary>
    /// Registers the OPC 40200 structures (<c>WeightType</c>,
    /// <c>PrintableWeightType</c>, the recipe structures) with a session's
    /// encodeable factory, so values read from a scale decode into the
    /// generated types.
    /// </summary>
    internal static class ScalesEncodeableRegistration
    {
        public static void Register(ISession session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException(
                    "The session must provide a message context.",
                    nameof(session));
            IEncodeableFactory factory = messageContext.Factory;
            if (factory.ContainsEncodeableType(DataTypeIds.WeightType))
            {
                return;
            }
            factory.Builder.AddOpcUaScales().Commit();
        }
    }

    /// <summary>
    /// Session extensions for OPC 40200 Scales.
    /// </summary>
    public static class SessionScalesExtensions
    {
        /// <summary>
        /// Creates a <see cref="ScalesClient"/> over the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public static ScalesClient Scales(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new ScalesClient(session, telemetry);
        }
    }
}
