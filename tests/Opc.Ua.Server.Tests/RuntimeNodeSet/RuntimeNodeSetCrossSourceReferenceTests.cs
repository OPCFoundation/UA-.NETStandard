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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// A runtime NodeSet may reference a Node that another runtime NodeSet owns.
    /// OPC 10000-3 requires a Reference to be visible from both of its endpoints,
    /// so browsing the target must find the source just as browsing the source
    /// finds the target.
    /// </summary>
    /// <remarks>
    /// This is the shape the WoT pump sample needs in order to place its
    /// aggregate pump Object under the DI <c>DeviceSet</c>: the pump Object and
    /// <c>DeviceSet</c> are materialized from different NodeSet documents, so
    /// the inverse edge the document declares has to become a forward edge on a
    /// Node this manager does not own.
    /// </remarks>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RuntimeNodeSetCrossSourceReferenceTests
    {
        private const string kTargetNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetCrossSource:Target";

        private const string kSourceNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetCrossSource:Source";

        private const uint kTargetFolderNodeId = 9100;
        private const uint kSourceObjectNodeId = 9200;

        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(RuntimeNodeSetCrossSourceReferenceTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_server?.Dispose();

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The target NodeSet is registered first, so the Node the source
        /// references already exists when the source is added.
        /// </summary>
        [Test]
        public async Task ReferenceIntoAnotherRuntimeNodeSetIsVisibleFromBothEndsAsync()
        {
            await AddTargetAsync().ConfigureAwait(false);
            await AddSourceAsync().ConfigureAwait(false);

            await AssertSymmetricAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The source NodeSet is registered first, so the Node it references does
        /// not exist yet. A reference to a Node that is not in the address space
        /// is dropped on import, so if nothing reconciles the edge when the
        /// target arrives the two ends disagree permanently.
        /// </summary>
        [Test]
        public async Task ReferenceIntoALaterRuntimeNodeSetIsVisibleFromBothEndsAsync()
        {
            await AddSourceAsync().ConfigureAwait(false);
            await AddTargetAsync().ConfigureAwait(false);

            await AssertSymmetricAsync().ConfigureAwait(false);
        }

        [TestCase("UAObjectType", "i=58", "")]
        [TestCase("UAVariableType", "i=63", " DataType=\"i=12\"")]
        [TestCase("UADataType", "i=6", "")]
        [TestCase("UAReferenceType", "i=33", "")]
        public async Task DerivedTypesCanPrecedeTheirSupertypesInTheSource(
            string nodeClass, string baseType, string attributes)
        {
            string xml = $"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>{kSourceNamespaceUri}</Uri></NamespaceUris>
                  <Models><Model ModelUri="{kSourceNamespaceUri}" /></Models>
                  <{nodeClass} NodeId="ns=1;i=2" BrowseName="1:Derived"{attributes}>
                    <DisplayName>Derived</DisplayName>
                    <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference></References>
                  </{nodeClass}>
                  <{nodeClass} NodeId="ns=1;i=1" BrowseName="1:Parent"{attributes}>
                    <DisplayName>Parent</DisplayName>
                    <References><Reference ReferenceType="i=45" IsForward="false">{baseType}</Reference></References>
                  </{nodeClass}>
                </UANodeSet>
                """;

            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSourceNamespaceUri, xml), null).ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = checked((ushort)server.NamespaceUris.GetIndex(kSourceNamespaceUri));
            var parent = new NodeId(1, ns);
            var derived = new NodeId(2, ns);
            Assert.That(server.TypeTree.FindSuperType(derived), Is.EqualTo(parent));
            Assert.That(server.TypeTree.IsTypeOf(derived, NodeId.Parse(baseType)), Is.True);
            Assert.That(await server.NodeManager.FindNodeInAddressSpaceAsync(derived).ConfigureAwait(false),
                Is.Not.Null);
        }

        [Test]
        public void TypeInheritanceCyclesAreRejectedBeforeRegistration()
        {
            string xml = $"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>{kSourceNamespaceUri}</Uri></NamespaceUris>
                  <Models><Model ModelUri="{kSourceNamespaceUri}" /></Models>
                  <UAObjectType NodeId="ns=1;i=1" BrowseName="1:First">
                    <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=2</Reference></References>
                  </UAObjectType>
                  <UAObjectType NodeId="ns=1;i=2" BrowseName="1:Second">
                    <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference></References>
                  </UAObjectType>
                </UANodeSet>
                """;

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                    CreateOptions(kSourceNamespaceUri, xml), null).ConfigureAwait(false));

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            Assert.That(failure.Message, Does.Contain("cycle"));
        }

        [TestCase("InputArguments")]
        [TestCase("OutputArguments")]
        public async Task ImportedArgumentPropertiesPopulateTheMethodSignature(string argumentName)
        {
            string xml = $"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris><Uri>{kSourceNamespaceUri}</Uri></NamespaceUris>
                  <Models><Model ModelUri="{kSourceNamespaceUri}" /></Models>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Owner">
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=47">ns=1;i=2</Reference>
                    </References>
                  </UAObject>
                  <UAMethod NodeId="ns=1;i=2" BrowseName="1:Call" ParentNodeId="ns=1;i=1">
                    <References><Reference ReferenceType="i=46">ns=1;i=3</Reference></References>
                  </UAMethod>
                  <UAVariable NodeId="ns=1;i=3" BrowseName="{argumentName}" ParentNodeId="ns=1;i=2"
                              DataType="i=296" ValueRank="1" ArrayDimensions="1">
                    <References><Reference ReferenceType="i=40">i=68</Reference></References>
                    <Value>
                      <uax:ListOfExtensionObject>
                        <uax:ExtensionObject>
                          <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                          <uax:Body>
                            <uax:Argument>
                              <uax:Name>EventId</uax:Name>
                              <uax:DataType><uax:Identifier>i=15</uax:Identifier></uax:DataType>
                              <uax:ValueRank>-1</uax:ValueRank>
                              <uax:ArrayDimensions />
                            </uax:Argument>
                          </uax:Body>
                        </uax:ExtensionObject>
                      </uax:ListOfExtensionObject>
                    </Value>
                  </UAVariable>
                </UANodeSet>
                """;
            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSourceNamespaceUri, xml), null).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = checked((ushort)server.NamespaceUris.GetIndex(kSourceNamespaceUri));
            NodeState node = await server.NodeManager.FindNodeInAddressSpaceAsync(new NodeId(2, ns))
                .ConfigureAwait(false);
            Assert.That(node, Is.InstanceOf<MethodState>());
            var method = (MethodState)node;
            PropertyState<ArrayOf<Argument>> arguments = argumentName == "InputArguments"
                ? method.InputArguments : method.OutputArguments;
            Assert.That(arguments, Is.Not.Null);
            Assert.That(arguments.NodeId, Is.EqualTo(new NodeId(3, ns)));
            Assert.That(arguments.Value.Count, Is.EqualTo(1));
            Assert.That(arguments.Value[0].Name, Is.EqualTo("EventId"));
            Assert.That(arguments.Value[0].DataType, Is.EqualTo(DataTypeIds.ByteString));
            var children = new List<BaseInstanceState>();
            method.GetChildren(server.DefaultSystemContext, children);
            Assert.That(children.Count(child => child.BrowseName.Name == argumentName), Is.EqualTo(1));
        }

        private async Task AssertSymmetricAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ISystemContext context = server.DefaultSystemContext;
            ushort targetNs = (ushort)server.NamespaceUris.GetIndex(kTargetNamespaceUri);
            ushort sourceNs = (ushort)server.NamespaceUris.GetIndex(kSourceNamespaceUri);
            Assert.That(targetNs, Is.GreaterThan(0));
            Assert.That(sourceNs, Is.GreaterThan(0));

            var targetId = new NodeId(kTargetFolderNodeId, targetNs);
            var sourceId = new NodeId(kSourceObjectNodeId, sourceNs);

            NodeState target = await server.NodeManager
                .FindNodeInAddressSpaceAsync(targetId).ConfigureAwait(false);
            NodeState source = await server.NodeManager
                .FindNodeInAddressSpaceAsync(sourceId).ConfigureAwait(false);
            Assert.That(target, Is.Not.Null, "The target NodeSet must be in the address space.");
            Assert.That(source, Is.Not.Null, "The source NodeSet must be in the address space.");

            Assert.That(
                ReferenceExists(source, context, ReferenceTypeIds.Organizes, isInverse: true, targetId),
                Is.True,
                "The source Node must keep the inverse edge its document declares.");
            Assert.That(
                ReferenceExists(target, context, ReferenceTypeIds.Organizes, isInverse: false, sourceId),
                Is.True,
                "The target Node must expose the matching forward edge; OPC 10000-3 " +
                "requires a Reference to be visible from both of its endpoints.");
        }

        private static bool ReferenceExists(
            NodeState node,
            ISystemContext context,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            var references = new System.Collections.Generic.List<IReference>();
            node.GetReferences(context, references);
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == referenceTypeId &&
                    reference.IsInverse == isInverse &&
                    !reference.TargetId.IsAbsolute &&
                    (NodeId)reference.TargetId == targetId)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task AddTargetAsync()
        {
            await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(
                    CreateOptions(kTargetNamespaceUri, BuildTargetXml()), null)
                .ConfigureAwait(false);
        }

        private async Task AddSourceAsync()
        {
            await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(
                    CreateOptions(kSourceNamespaceUri, BuildSourceXml()), null)
                .ConfigureAwait(false);
        }

        private static RuntimeNodeSetOptions CreateOptions(string namespaceUri, string xml)
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        namespaceUri,
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [namespaceUri])
                ]
            };
        }

        private static string BuildTargetXml()
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris>
                    <Uri>{kTargetNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kTargetNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={kTargetFolderNodeId}" BrowseName="1:CrossSourceTarget">
                    <DisplayName>CrossSourceTarget</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=61</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;
        }

        private static string BuildSourceXml()
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris>
                    <Uri>{kSourceNamespaceUri}</Uri>
                    <Uri>{kTargetNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kSourceNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={kSourceObjectNodeId}" BrowseName="1:CrossSourceChild">
                    <DisplayName>CrossSourceChild</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">ns=2;i={kTargetFolderNodeId}</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;
        }
    }
}
