// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Selects the registrar namespace and issuance gate for the hosted GDS, not a cohosted regular server.
    /// </summary>
    internal sealed class GdsDeviceRegistrarRegistration
    {
        /// <summary>
        /// Borrows the container-owned registrar factory and its certificate authorization.
        /// </summary>
        public GdsDeviceRegistrarRegistration(
            DeviceRegistrarNodeManagerFactory factory,
            IDeviceRegistrarCertificateAuthorization certificateAuthorization)
        {
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            CertificateAuthorization = certificateAuthorization ??
                throw new ArgumentNullException(nameof(certificateAuthorization));
        }

        /// <summary>
        /// Gets the registrar factory selected through the GDS builder.
        /// </summary>
        public DeviceRegistrarNodeManagerFactory Factory { get; }

        /// <summary>
        /// Gets the argument-specific issuance gate selected for that registrar.
        /// </summary>
        public IDeviceRegistrarCertificateAuthorization CertificateAuthorization { get; }
    }

    /// <summary>
    /// Records a call of the GDS builder's <c>AddDefaultIdentityAuthenticators</c>.
    /// </summary>
    /// <remarks>
    /// The GDS supplies its own UserName and X.509 authenticators. The UserName one
    /// grants the roles the <see cref="Opc.Ua.Server.UserDatabase.IUserDatabase"/>
    /// assigns; the X.509 one validates the certificate against the configured user
    /// trust list and grants AuthenticatedUser. The hosted GDS skips those token types
    /// from every generic default authenticator registration and configures its
    /// built-ins from <see cref="Options"/>.
    /// </remarks>
    internal sealed class GdsDefaultIdentityAuthenticatorsRegistration
    {
        public GdsDefaultIdentityAuthenticatorsRegistration(
            GdsDefaultIdentityAuthenticatorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// The options the builder was called with.
        /// </summary>
        public GdsDefaultIdentityAuthenticatorOptions Options { get; }
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
