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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Records a call of the GDS builder's <c>AddDefaultIdentityAuthenticators</c>.
    /// </summary>
    /// <remarks>
    /// The GDS supplies its own UserName and X.509 authenticators, which grant the
    /// roles the <see cref="Opc.Ua.Server.UserDatabase.IUserDatabase"/> assigns. The
    /// hosted GDS therefore skips those token types from <see cref="Registration"/>
    /// (the generic default set, which a co-hosted regular server still uses) and
    /// configures its built-ins from <see cref="Options"/>.
    /// </remarks>
    internal sealed class GdsDefaultIdentityAuthenticatorsRegistration
    {
        public GdsDefaultIdentityAuthenticatorsRegistration(
            GdsDefaultIdentityAuthenticatorOptions options,
            OpcUaServerIdentityAuthenticatorRegistration? registration)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Registration = registration;
        }

        /// <summary>
        /// The options the builder was called with.
        /// </summary>
        public GdsDefaultIdentityAuthenticatorOptions Options { get; }

        /// <summary>
        /// The generic default authenticator registration the call deposited.
        /// </summary>
        public OpcUaServerIdentityAuthenticatorRegistration? Registration { get; }
    }

    /// <summary>
    /// A startup task registered for the hosted GDS only.
    /// </summary>
    internal sealed class GdsServerStartupTaskRegistration
    {
        public GdsServerStartupTaskRegistration(
            Func<IServiceProvider, IServerStartupTask> factory,
            Type? taskType = null)
        {
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            TaskType = taskType;
        }

        /// <summary>
        /// Resolves the task.
        /// </summary>
        public Func<IServiceProvider, IServerStartupTask> Factory { get; }

        /// <summary>
        /// The task type of a type registration, <c>null</c> for a callback.
        /// </summary>
        public Type? TaskType { get; }
    }

    /// <summary>
    /// A pre-startup task registered for the hosted GDS only.
    /// </summary>
    internal sealed class GdsServerPreStartupTaskRegistration
    {
        public GdsServerPreStartupTaskRegistration(
            Func<IServiceProvider, IServerPreStartupTask> factory,
            Type taskType)
        {
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            TaskType = taskType ?? throw new ArgumentNullException(nameof(taskType));
        }

        /// <summary>
        /// Resolves the task.
        /// </summary>
        public Func<IServiceProvider, IServerPreStartupTask> Factory { get; }

        /// <summary>
        /// The task type.
        /// </summary>
        public Type TaskType { get; }
    }

    /// <summary>
    /// Runs a startup callback registered through the GDS builder.
    /// </summary>
    internal sealed class DelegateGdsServerStartupTask : IServerStartupTask
    {
        private readonly IServiceProvider m_services;
        private readonly Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> m_callback;

        public DelegateGdsServerStartupTask(
            IServiceProvider services,
            Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> callback)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            return m_callback(m_services, server, cancellationToken);
        }
    }
}
