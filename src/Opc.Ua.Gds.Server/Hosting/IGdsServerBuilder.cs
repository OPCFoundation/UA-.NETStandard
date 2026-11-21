// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <see cref="OpcUaGdsServerBuilderExtensions.AddGdsServer(IOpcUaBuilder, Action{GdsServerOptions})"/>;
    /// allows chained registration of the pluggable services consumed
    /// by the GDS hosted service (applications database, certificate
    /// stores, user database, optional token / key-credential / managed
    /// application stores).
    /// </summary>
    public interface IGdsServerBuilder : IOpcUaTransportBuilder
    {
        /// <summary>
        /// Registers the in-box in-memory GDS stores:
        /// <see cref="LinqApplicationsDatabase"/> for applications and
        /// certificate requests, <see cref="CertificateGroup"/> for
        /// certificate groups, and
        /// <see cref="LinqUserDatabase"/> for
        /// users.
        /// </summary>
        IGdsServerBuilder AddInMemoryStores();

        /// <summary>
        /// Registers an <see cref="IApplicationsDatabase"/> implementation
        /// as a singleton, resolvable both as <typeparamref name="T"/>
        /// and as <see cref="IApplicationsDatabase"/>.
        /// </summary>
        /// <typeparam name="T">The concrete database type.</typeparam>
        IGdsServerBuilder AddApplicationsDatabase<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IApplicationsDatabase;

        /// <summary>
        /// Registers an <see cref="IApplicationsDatabase"/> singleton
        /// produced by the supplied <paramref name="factory"/>.
        /// </summary>
        IGdsServerBuilder AddApplicationsDatabase(
            Func<IServiceProvider, IApplicationsDatabase> factory);

        /// <summary>
        /// Registers an <see cref="IUserDatabase"/> implementation as a
        /// singleton, resolvable both as <typeparamref name="T"/> and
        /// as <see cref="IUserDatabase"/>. When <typeparamref name="T"/>
        /// also implements <see cref="IGdsUserDatabase"/> it is
        /// additionally resolvable via that interface.
        /// </summary>
        /// <typeparam name="T">The concrete user database type.</typeparam>
        IGdsServerBuilder AddUserDatabase<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IUserDatabase;

        /// <summary>
        /// Registers an <see cref="ICertificateGroup"/> implementation as
        /// a singleton, resolvable both as <typeparamref name="T"/> and
        /// as <see cref="ICertificateGroup"/>.
        /// </summary>
        /// <typeparam name="T">The certificate group factory type.</typeparam>
        IGdsServerBuilder AddCertificateGroup<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, ICertificateGroup;

        /// <summary>
        /// Registers an <see cref="ICertificateRequest"/> implementation
        /// as a singleton, resolvable both as <typeparamref name="T"/>
        /// and as <see cref="ICertificateRequest"/>.
        /// </summary>
        /// <typeparam name="T">The certificate-request store type.</typeparam>
        IGdsServerBuilder AddCertificateRequest<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, ICertificateRequest;

        /// <summary>
        /// Registers an optional <see cref="IAccessTokenProvider"/>
        /// implementation as a singleton, resolvable both as
        /// <typeparamref name="T"/> and as
        /// <see cref="IAccessTokenProvider"/>.
        /// </summary>
        /// <typeparam name="T">The access-token provider type.</typeparam>
        IGdsServerBuilder AddAccessTokenProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IAccessTokenProvider;

        /// <summary>
        /// Registers an optional <see cref="IKeyCredentialRequestStore"/>
        /// implementation as a singleton, resolvable both as
        /// <typeparamref name="T"/> and as
        /// <see cref="IKeyCredentialRequestStore"/>.
        /// </summary>
        /// <typeparam name="T">The key-credential request-store type.</typeparam>
        IGdsServerBuilder AddKeyCredentialRequestStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IKeyCredentialRequestStore;

        /// <summary>
        /// Registers an optional <see cref="IConfigurationDataStore"/>
        /// implementation as a singleton, resolvable both as
        /// <typeparamref name="T"/> and as
        /// <see cref="IConfigurationDataStore"/>.
        /// </summary>
        /// <typeparam name="T">The configuration data-store type.</typeparam>
        IGdsServerBuilder AddConfigurationDataStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IConfigurationDataStore;
    }
}
