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
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-21
    /// <c>DeviceRegistrarAdminType</c> facet.
    /// </summary>
    /// <remarks>
    /// The specification-native <c>RegisterTickets</c> and
    /// <c>UnregisterTickets</c> methods are inherited from
    /// <see cref="DeviceRegistrarAdminTypeClient"/>.
    /// </remarks>
    public sealed class OnboardingClient : DeviceRegistrarAdminTypeClient
    {
        /// <summary>
        /// Creates a new onboarding client rooted at the supplied
        /// <c>DeviceRegistrarAdminType</c> instance.
        /// </summary>
        public OnboardingClient(
            ISession session,
            NodeId registrarNodeId,
            ITelemetryContext telemetry)
            : base(
                PrepareSession(session),
                ValidateRegistrarNodeId(registrarNodeId),
                telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
        {
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public new ISession Session => (ISession)base.Session;

        /// <summary>
        /// The NodeId of the registrar administration instance.
        /// </summary>
        public NodeId RegistrarNodeId => ObjectId;

        private static ISession PrepareSession(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
            return session;
        }

        private static NodeId ValidateRegistrarNodeId(NodeId registrarNodeId)
        {
            if (registrarNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Registrar NodeId is required.",
                    nameof(registrarNodeId));
            }
            return registrarNodeId;
        }
    }
}
