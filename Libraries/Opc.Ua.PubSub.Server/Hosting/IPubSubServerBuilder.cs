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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <c>Microsoft.Extensions.DependencyInjection.OpcUaServerBuilderPubSubExtensions.AddPubSub</c>;
    /// allows chained registration of optional PubSub features
    /// (Security Key Service, default SecurityGroup, custom
    /// diagnostics binding) on the OPC UA server.
    /// </summary>
    /// <remarks>
    /// The PubSub server feature is hosted as a node manager
    /// attached to the regular OPC UA server registered via
    /// <c>.AddServer(...)</c>. It is therefore registered
    /// <em>against</em> the same service collection, but composes
    /// additional services (the SKS server and the default
    /// SecurityGroup seed) that are resolved at server start.
    /// </remarks>
    public interface IPubSubServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register
        /// additional services consumed by the PubSub server node
        /// manager (e.g. a custom
        /// <see cref="IPubSubKeyServiceServer"/> implementation).
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Adjusts the <see cref="PubSubServerOptions"/> via an
        /// imperative callback. Multiple calls compose; the last
        /// configuration wins for the same property.
        /// </summary>
        /// <param name="configure">Mutation callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        IPubSubServerBuilder Configure(Action<PubSubServerOptions> configure);

        /// <summary>
        /// Marks the host as an SKS for other Publishers and
        /// Subscribers by setting
        /// <see cref="PubSubServerOptions.ExposeSecurityKeyService"/>
        /// to <see langword="true"/>. The matching
        /// <see cref="IPubSubKeyServiceServer"/> implementation must
        /// already be registered (or registered via
        /// <see cref="PubSubServerBuilderExtensions.WithSecurityKeyServiceServer(IPubSubServerBuilder, Action{InMemoryPubSubKeyServiceServer})"/>).
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        IPubSubServerBuilder ExposeSecurityKeyService();

        /// <summary>
        /// Configures a SecurityGroup that will be created on
        /// start-up when the SKS is exposed. No-op when the
        /// SecurityGroup already exists.
        /// </summary>
        /// <param name="id">SecurityGroup identifier.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="id"/> is empty.
        /// </exception>
        IPubSubServerBuilder WithDefaultSecurityGroup(string id);
    }
}
