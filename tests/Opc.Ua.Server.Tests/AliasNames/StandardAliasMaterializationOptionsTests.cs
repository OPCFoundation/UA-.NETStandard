/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Verifies opt-in alias materialization through the normal configuration
    /// node manager and its factory without starting a network listener.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public sealed class StandardAliasMaterializationOptionsTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_registry = new AliasNameStoreRegistry();
            m_store = new InMemoryAliasNameStore(
                [
                    new AliasNameCategoryDescriptor(
                        ObjectIds.TagVariables,
                        new QualifiedName(BrowseNames.TagVariables),
                        AliasNameCapabilities.All)
                ]);
            m_registry.Register(m_store);
            await m_store.AddAliasesAsync(
                ObjectIds.TagVariables,
                [
                    new AliasAddRequest(
                        kAliasName,
                        new ExpandedNodeId(kTargetName, 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            m_server = CreateServer();
            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration(),
                SecurityConfiguration = new SecurityConfiguration()
            };
        }

        [TearDown]
        public void TearDown()
        {
            m_server?.Dispose();
            m_registry?.Dispose();
            m_store?.Dispose();
        }

        [Test]
        public void MaterializationIsDisabledByDefault()
        {
            var options = new AliasNameServerOptions();

            Assert.That(options.MaterializeAliasNodes, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationManagerHonorsMaterializationOptionAsync(bool enabled)
        {
            using var manager = new ConfigurationNodeManager(
                m_server,
                m_configuration,
                NullLogger.Instance,
                timeProvider: null,
                coordinator: null,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: new ServerConfigurationOptions { HasSecureElement = true },
                aliasNameOptions: new AliasNameServerOptions { MaterializeAliasNodes = enabled });

            await AssertMaterializationAsync(manager, enabled).ConfigureAwait(false);

            ServerConfigurationState configuration =
                manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
            Assert.That(configuration.HasSecureElement, Is.Not.Null,
                "The new overload must also retain the existing ServerConfiguration options.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FactoryPropagatesMaterializationOptionAsync(bool enabled)
        {
            var factory = new MainNodeManagerFactory(
                m_configuration,
                m_server,
                coordinator: null,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: new ServerConfigurationOptions { HasSecureElement = true },
                aliasNameOptions: new AliasNameServerOptions { MaterializeAliasNodes = enabled });
            using var manager = (ConfigurationNodeManager)factory.CreateConfigurationNodeManager();

            await AssertMaterializationAsync(manager, enabled).ConfigureAwait(false);

            ServerConfigurationState configuration =
                manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
            Assert.That(configuration.HasSecureElement, Is.Not.Null,
                "The factory must continue forwarding the existing ServerConfiguration options.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NullOptionsLeaveMaterializationDisabledAsync(bool throughFactory)
        {
            using ConfigurationNodeManager manager = throughFactory
                ? (ConfigurationNodeManager)new MainNodeManagerFactory(
                    m_configuration,
                    m_server,
                    coordinator: null,
                    pendingKeyStore: null,
                    keyGenerator: null,
                    trustListEffectHandler: null,
                    serverConfigurationOptions: null,
                    aliasNameOptions: null).CreateConfigurationNodeManager()
                : new ConfigurationNodeManager(
                    m_server,
                    m_configuration,
                    NullLogger.Instance,
                    timeProvider: null,
                    coordinator: null,
                    pendingKeyStore: null,
                    keyGenerator: null,
                    trustListEffectHandler: null,
                    serverConfigurationOptions: null,
                    aliasNameOptions: null);

            await AssertMaterializationAsync(manager, enabled: false).ConfigureAwait(false);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(9)]
        public async Task ExistingConfigurationConstructorsLeaveMaterializationDisabledAsync(int parameterCount)
        {
            using ConfigurationNodeManager manager = parameterCount switch
            {
                2 => new ConfigurationNodeManager(m_server, m_configuration),
                3 => new ConfigurationNodeManager(m_server, m_configuration, NullLogger.Instance),
                4 => new ConfigurationNodeManager(m_server, m_configuration, NullLogger.Instance, timeProvider: null),
                9 => new ConfigurationNodeManager(
                    m_server,
                    m_configuration,
                    NullLogger.Instance,
                    timeProvider: null,
                    coordinator: null,
                    pendingKeyStore: null,
                    keyGenerator: null,
                    trustListEffectHandler: null,
                    serverConfigurationOptions: null),
                _ => throw new ArgumentOutOfRangeException(nameof(parameterCount))
            };

            await AssertMaterializationAsync(manager, enabled: false).ConfigureAwait(false);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(7)]
        public async Task ExistingFactoryConstructorsLeaveMaterializationDisabledAsync(int parameterCount)
        {
            MainNodeManagerFactory factory = parameterCount switch
            {
                2 => new MainNodeManagerFactory(m_configuration, m_server),
                3 => new MainNodeManagerFactory(m_configuration, m_server, coordinator: null),
                7 => new MainNodeManagerFactory(
                    m_configuration,
                    m_server,
                    coordinator: null,
                    pendingKeyStore: null,
                    keyGenerator: null,
                    trustListEffectHandler: null,
                    serverConfigurationOptions: null),
                _ => throw new ArgumentOutOfRangeException(nameof(parameterCount))
            };
            using var manager = (ConfigurationNodeManager)factory.CreateConfigurationNodeManager();

            await AssertMaterializationAsync(manager, enabled: false).ConfigureAwait(false);
        }

        private IServerInternal CreateServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:opcfoundation.org:tests:standard-alias-materialization");
            var serverUris = new StringTable();
            var factory = EncodeableFactory.Create();
            var server = new Mock<IServerInternal>();
            server.As<IAliasNameStoreRegistryProvider>()
                .Setup(provider => provider.AliasNameStoreRegistry)
                .Returns(m_registry);
            server.Setup(instance => instance.Telemetry).Returns(telemetry);
            server.Setup(instance => instance.NamespaceUris).Returns(namespaces);
            server.Setup(instance => instance.ServerUris).Returns(serverUris);
            server.Setup(instance => instance.TypeTree).Returns(new TypeTable(namespaces));
            server.Setup(instance => instance.Factory).Returns(factory);
            server.Setup(instance => instance.MessageContext)
                .Returns(new ServiceMessageContext(telemetry, factory)
                {
                    NamespaceUris = namespaces,
                    ServerUris = serverUris
                });
            server.Setup(instance => instance.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            server.Setup(instance => instance.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.Setup(instance => instance.SubscriptionManager).Returns(Mock.Of<ISubscriptionManager>());
            server.Setup(instance => instance.DefaultSystemContext)
                .Returns(new ServerSystemContext(server.Object));
            return server.Object;
        }

        private async Task AssertMaterializationAsync(ConfigurationNodeManager manager, bool enabled)
        {
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);

            AliasNameCategoryState category =
                manager.FindPredefinedNode<AliasNameCategoryState>(ObjectIds.TagVariables);
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null);
            Assert.That(category.FindAlias.OnCallAsync, Is.Not.Null,
                "Alias query methods must remain available independently of materialization.");

            ushort namespaceIndex = m_server.NamespaceUris.GetIndexOrAppend(Ua.Namespaces.OpcUa + "Diagnostics");
            var aliasId = new NodeId(Utils.Format("{0}.{1}", ObjectIds.TagVariables, kAliasName), namespaceIndex);
            AliasNameState alias = manager.FindPredefinedNode<AliasNameState>(aliasId);
            var targetId = new NodeId(kTargetName, 1);

            if (enabled)
            {
                Assert.That(alias, Is.Not.Null);
                Assert.That(alias.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.AliasNameType));
                Assert.That(alias.BrowseName, Is.EqualTo(new QualifiedName(kAliasName, namespaceIndex)));
                Assert.That(category.ReferenceExists(ReferenceTypeIds.Organizes, false, aliasId), Is.True);
                Assert.That(alias.ReferenceExists(ReferenceTypeIds.AliasFor, false, targetId), Is.True);
                Assert.That(externalReferences.TryGetValue(targetId, out IList<IReference> references), Is.True);
                Assert.That(references, Has.Count.EqualTo(1));
                Assert.That(references[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.AliasFor));
                Assert.That(references[0].IsInverse, Is.True);
                Assert.That(references[0].TargetId, Is.EqualTo(new ExpandedNodeId(aliasId)));
                Assert.That(category.FindAliasVerbose?.OnCallAsync, Is.Not.Null);
                Assert.That(category.AddAliasesToCategory?.OnCallAsync, Is.Not.Null);
                Assert.That(category.DeleteAliasesFromCategory?.OnCallAsync, Is.Not.Null);
                Assert.That(category.LastChange, Is.Not.Null);
            }
            else
            {
                Assert.That(alias, Is.Null);
                Assert.That(externalReferences.ContainsKey(targetId), Is.False);
                Assert.That(category.FindAliasVerbose, Is.Null);
                Assert.That(category.AddAliasesToCategory, Is.Null);
                Assert.That(category.DeleteAliasesFromCategory, Is.Null);
            }

            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias.CallAsync(
                manager.SystemContext,
                category.NodeId,
                [new Variant(kAliasName), new Variant(NodeId.Null)],
                [],
                output,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].TryGetStructure(out ArrayOf<AliasNameDataType> aliases), Is.True);
            Assert.That(aliases.Count, Is.EqualTo(1));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo(kAliasName));
            Assert.That(aliases[0].ReferencedNodes, Is.EqualTo(new[] { new ExpandedNodeId(targetId) }));
        }

        private const string kAliasName = "Temperature";
        private const string kTargetName = "TemperatureValue";

        private AliasNameStoreRegistry m_registry;
        private InMemoryAliasNameStore m_store;
        private IServerInternal m_server;
        private ApplicationConfiguration m_configuration;
    }
}
