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
 *
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class ActivatorOpcUaServerFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TServer>
        : IOpcUaServerFactory
        where TServer : StandardServer
    {
        public ActivatorOpcUaServerFactory(IServiceProvider services)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            ThrowIfConstructorHooksCannotApply();
            return ActivatorUtilities.CreateInstance<TServer>(m_services, telemetry, timeProvider);
        }

        private void ThrowIfConstructorHooksCannotApply()
        {
            if (typeof(DependencyInjectionStandardServer).IsAssignableFrom(typeof(TServer)))
            {
                return;
            }
            if (!HasConstructorHookRegistrations())
            {
                return;
            }
            throw new InvalidOperationException(
                "The configured custom OPC UA server type does not derive from DependencyInjectionStandardServer. " +
                "AddSessionManager, AddSubscriptionManager, AddDurableSubscriptions, ConfigureRoles and " +
                "AddRoleManager require a DI-aware server type so their StandardServer hooks are not silently ignored.");
        }

        private bool HasConstructorHookRegistrations()
        {
            return m_services.GetServices<OpcUaServerSessionManagerRegistration>().Any() ||
                m_services.GetServices<OpcUaServerSubscriptionManagerRegistration>().Any() ||
                m_services.GetService<OpcUaServerRoleManagerRegistration>() != null ||
                m_services.GetService<ISubscriptionStore>() != null ||
                m_services.GetService<IMonitoredItemQueueFactory>() != null;
        }

        private readonly IServiceProvider m_services;
    }
}
