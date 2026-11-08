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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.ECM.Server;
using Opc.Ua.ECM.Client;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises direct model composition and fluent ownership without starting a server.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmCompositionTests
    {
        /// <summary>
        /// Proves standalone hosting advertises each required namespace once.
        /// </summary>
        [Test]
        public void StandaloneFactoryIncludesEcmIaAndDiExactlyOnce()
        {
            var factory = new EcmNodeManagerFactory(providers: [new EcmModelProvider()]);
            ArrayOf<string> advertised = factory.NamespacesUris;
            Assert.That(advertised.IsNull, Is.False);
            string[] namespaces = advertised.Span.ToArray();

            Assert.That(namespaces.Count(uri => uri == Namespaces.ECM), Is.EqualTo(1));
            Assert.That(namespaces.Count(uri => uri == IA.Namespaces.IA), Is.EqualTo(1));
            Assert.That(namespaces.Count(uri => uri == Di.Namespaces.OpcUaDi), Is.EqualTo(1));
            Assert.That(namespaces.Distinct().Count(), Is.EqualTo(namespaces.Length));
        }

        /// <summary>
        /// Proves composition loads missing models only once with a non-default namespace order.
        /// </summary>
        [Test]
        public void DirectModelProviderLoadsIaAndEcmWithoutDuplicatingExistingNodes()
        {
            var messageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            messageContext.NamespaceUris.Append("urn:ecm-tests:unrelated");
            messageContext.NamespaceUris.Append(Namespaces.ECM);
            messageContext.NamespaceUris.Append(IA.Namespaces.IA);
            messageContext.NamespaceUris.Append(Di.Namespaces.OpcUaDi);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            var provider = new EcmModelProvider();

            provider.AddPredefinedNodes(nodes, context);
            int count = nodes.Count;
            NodeState standby = nodes.Single(node => node.NodeId == ExpandedNodeId.ToNodeId(
                ObjectTypeIds.EnergyStandbyManagementType, context.NamespaceUris));
            provider.AddPredefinedNodes(nodes, context);

            Assert.That(nodes, Has.Count.EqualTo(count));
            Assert.That(nodes.Single(node => node.NodeId == standby.NodeId), Is.SameAs(standby));
            Assert.That(nodes.Count(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.StacklightType, context.NamespaceUris)), Is.EqualTo(1));
            Assert.That(context.EncodeableFactory.ContainsEncodeableType(
                DataTypeIds.EnergyStateInformationDataType), Is.True);
        }

        /// <summary>
        /// Proves ECM cannot silently compose without the DI model it references.
        /// </summary>
        [Test]
        public void DirectModelProviderRequiresDiBeforeChangingCollection()
        {
            var messageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            var nodes = new NodeStateCollection();
            var provider = new EcmModelProvider();

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                provider.AddPredefinedNodes(nodes, context))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(nodes, Is.Empty);
        }

        /// <summary>
        /// Proves composed ECM contributes a model rather than registering another DI owner.
        /// </summary>
        [Test]
        public void FluentCompositionDeduplicatesModelUnderExistingDiOwner()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "EcmComposition");
            builder.AddOpcUaDi();

            Assert.That(builder.AddEcmModel().AddEcmModel(), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<IDiModelProvider>().Single(), Is.TypeOf<EcmModelProvider>());
            Assert.That(provider.GetService<EcmNodeManagerFactory>(), Is.Null);
            Assert.That(provider.GetRequiredService<DiNodeManagerFactory>().NamespacesUris.ToArray(),
                Does.Contain(Namespaces.ECM));
        }

        /// <summary>
        /// Proves standalone ECM and another DI owner cannot silently coexist.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FluentStandaloneRejectsDuplicateAddressSpaceOwners(bool ecmFirst)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "EcmOwnership");
            if (ecmFirst)
            {
                builder.AddEcm();
                Assert.Throws<InvalidOperationException>(() => builder.AddOpcUaDi());
            }
            else
            {
                builder.AddOpcUaDi();
                Assert.Throws<InvalidOperationException>(() => builder.AddEcm());
            }
        }

        /// <summary>
        /// Proves the standalone factory and post-setup callback are registered for the ECM owner.
        /// </summary>
        [Test]
        public void FluentStandaloneRegistersFactoryAndTargetsEcmConfiguration()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "EcmStandalone");

            Assert.That(builder.AddEcm().ConfigureEcm(static (_, _) => default), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<EcmNodeManagerFactory>(), Is.Not.Null);
            Assert.That(provider.GetServices<IDiPostSetupConfigurator>().Single().TargetManagerType,
                Is.EqualTo(typeof(EcmNodeManager)));
        }

        /// <summary>
        /// Proves invalid and cancelled factory requests cannot start a managed-session acquisition.
        /// </summary>
        [Test]
        public async Task ClientFactoryValidatesRequestBeforeAcquiringSharedSession()
        {
            int calls = 0;
            var factory = new EcmClientFactory(_ =>
            {
                calls++;
                throw new InvalidOperationException("The session must not be acquired.");
            }, NUnitTelemetryContext.Create());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.CreateAsync(NodeId.Null).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await factory.CreateAsync(new NodeId("standby", 2),
                    cancellationToken: cancelled.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.CreateWolAsync(NodeId.Null).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await factory.CreateWolAsync(new NodeId("wol", 2), cancelled.Token).ConfigureAwait(false));

            Assert.That(calls, Is.Zero);
        }

        /// <summary>
        /// Proves repeated fluent client registration reuses the factory instead of opening a session.
        /// </summary>
        [Test]
        public void FluentClientRegistrationIsIdempotentAndDoesNotConnect()
        {
            var services = new ServiceCollection();
            var builder = services.AddOpcUa().AddClient(options => options.ApplicationName = "EcmClientComposition");

            Assert.That(builder.AddEcmClient().AddEcmClient(), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();

            EcmClientFactory factory = provider.GetRequiredService<EcmClientFactory>();
            Assert.That(provider.GetServices<EcmClientFactory>().Count(), Is.EqualTo(1));
            Assert.That(provider.GetRequiredService<EcmClientFactory>(), Is.SameAs(factory));
        }
    }
}
