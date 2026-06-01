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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Gds.Server.Database;
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
    public interface IGdsServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register additional
        /// services the GDS implementation may need.
        /// </summary>
        IServiceCollection Services { get; }

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
