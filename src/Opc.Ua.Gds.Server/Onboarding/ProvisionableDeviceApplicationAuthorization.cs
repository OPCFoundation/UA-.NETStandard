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
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Gates independent application push access on installed DCA trust and the application's software approval.
    /// </summary>
    public sealed class ProvisionableDeviceApplicationAuthorization :
        IPushConfigurationAuthorization,
        IApplicationConfigurationPushOperations
    {
        /// <summary>
        /// Creates an application-specific gate without reusing the application's own trust for registrar access.
        /// An absent software policy explicitly declares that no software update is required.
        /// </summary>
        public ProvisionableDeviceApplicationAuthorization(
            string applicationUri,
            IProvisionableDeviceSecurity security,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates = null,
            IPushConfigurationAuthorization? applicationAuthorization = null)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw new ArgumentException("An application URI is required.", nameof(applicationUri));
            }
            m_applicationUri = applicationUri;
            m_security = security ?? throw new ArgumentNullException(nameof(security));
            m_operations = security.CreateApplicationOperations();
            m_softwareUpdates = softwareUpdates;
            m_applicationAuthorization = applicationAuthorization;
        }

        /// <inheritdoc/>
        public async ValueTask AuthorizeAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            await m_security.RequireInstalledRegistrarAsync(context, cancellationToken).ConfigureAwait(false);
            if (m_softwareUpdates != null)
            {
                await m_softwareUpdates.RequireReadyAsync(m_applicationUri, cancellationToken).ConfigureAwait(false);
            }
            if (m_applicationAuthorization != null)
            {
                await m_applicationAuthorization.AuthorizeAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask<T> ExecuteAsync<T>(
            Func<ValueTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return m_operations.ExecuteAsync(operation, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ExecuteAsync(
            Func<ValueTask> operation,
            CancellationToken cancellationToken = default)
        {
            return m_operations.ExecuteAsync(operation, cancellationToken);
        }

        /// <summary>The application URI checked by software management.</summary>
        private readonly string m_applicationUri;

        /// <summary>The installed DCA trust authority.</summary>
        private readonly IProvisionableDeviceSecurity m_security;

        /// <summary>The deployment-bound operation provider, revoked by device reset.</summary>
        private readonly IApplicationConfigurationPushOperations m_operations;

        /// <summary>The optional deployment-owned software prerequisite.</summary>
        private readonly IProvisionableDeviceSoftwareUpdatePolicy? m_softwareUpdates;

        /// <summary>The target application's additional access policy.</summary>
        private readonly IPushConfigurationAuthorization? m_applicationAuthorization;
    }
}
