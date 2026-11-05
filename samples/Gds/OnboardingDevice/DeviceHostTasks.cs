/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingDevice
{
    /// <summary>
    /// Adds SecurityAdmin only to the explicitly selected account after username/password authentication.
    /// It never derives privileges from a claimed identity display name or an unverified token.
    /// </summary>
    internal sealed class DeviceAdministratorAugmenter : IIdentityAugmenter
    {
        /// <summary>
        /// Receives an optional explicit administrator mapping, separate from the login account configuration.
        /// </summary>
        public DeviceAdministratorAugmenter(string? administrator)
        {
            m_administrator = administrator;
        }

        /// <inheritdoc/>
        public ValueTask<AuthenticationResult> AugmentAsync(
            IUserIdentity identity,
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (m_administrator == null || context.TokenHandler is not UserNameIdentityTokenHandler token ||
                !string.Equals(token.UserName, m_administrator, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(AuthenticationResult.NotHandled);
            }
            return ValueTask.FromResult(AuthenticationResult.Accept(
                new RoleBasedIdentity(
                    identity, [Role.AuthenticatedUser, Role.SecurityAdmin],
                    context.MessageContext.NamespaceUris)));
        }

        /// <summary>The explicit administrator, or null when administrative mapping is disabled.</summary>
        private readonly string? m_administrator;
    }

    /// <summary>
    /// Executes an operator-requested local reset after production recovery and before listeners are opened.
    /// This is a physical/local command, not a bypass exposed through an unauthenticated network method.
    /// </summary>
    internal sealed class DevicePhysicalResetTask : IServerPreStartupTask
    {
        /// <summary>
        /// Borrows the same state machine that owns standard network reset and push completion.
        /// </summary>
        public DevicePhysicalResetTask(IProvisionableDeviceSecurity security)
        {
            m_security = security ?? throw new ArgumentNullException(nameof(security));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            await m_security.ResetToServerDefaultsAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine("ONBOARDING_DEVICE_PHYSICAL_RESET_COMPLETE");
        }

        /// <summary>The container-owned shared device security provider.</summary>
        private readonly IProvisionableDeviceSecurity m_security;
    }

    /// <summary>
    /// Emits readiness only after standard startup, including real security recovery and model publication.
    /// </summary>
    internal sealed class DeviceReadyTask : IServerStartupTask
    {
        /// <summary>
        /// Receives the explicit listener URL.
        /// </summary>
        public DeviceReadyTask(DeviceSampleOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"ONBOARDING_DEVICE_READY {m_options.EndpointUrl}");
            Console.WriteLine($"DEVICE_NAMESPACE {ProvisionableDeviceNodeManagerFactory.NamespaceUri}");
            Console.WriteLine("DEVICE_ID ProvisionableDevice; REQUEST_TICKETS ProvisionableDevice/RequestTickets; " +
                "REGISTRAR_ENDPOINTS ProvisionableDevice/SetRegistrarEndpoints");
            Console.WriteLine("DEVICE_PARENT Resources");
            return default;
        }

        /// <summary>The validated host deployment inputs.</summary>
        private readonly DeviceSampleOptions m_options;
    }
}
