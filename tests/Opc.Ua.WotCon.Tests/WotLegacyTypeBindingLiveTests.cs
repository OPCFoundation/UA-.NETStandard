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
 *
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Tests.Providers;
using Quickstarts.ReferenceServer;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotLegacyTypeBindingLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyUploadUsesLoadedTypeAndItsMandatoryProperty(bool byName)
        {
            await WithAssetAsync(async (session, asset) =>
            {
                byte[] content = Content(byName);
                NodeId originalId = asset.AssetId;
                await asset.UploadThingDescriptionAsync(content).ConfigureAwait(false);

                NodeId type = await TypeDefinitionAsync(session, asset.AssetId).ConfigureAwait(false);
                ushort modelIndex = session.NamespaceUris.GetIndexOrAppend(ModelUri);
                Assert.That(type, Is.EqualTo(new NodeId(4001u, modelIndex)));
                Assert.That(asset.AssetId, Is.EqualTo(originalId));
                var properties = new List<WotAssetVariableEntry>();
                await foreach (WotAssetVariableEntry property in asset
                    .EnumeratePropertiesAsync().ConfigureAwait(false))
                {
                    properties.Add(property);
                }
                Assert.That(properties, Has.Count.EqualTo(1));
                Assert.That(properties[0].BrowseName, Is.EqualTo("Speed"));
                NodeId propertyType = await TypeDefinitionAsync(session, properties[0].NodeId).ConfigureAwait(false);
                Assert.That(propertyType, Is.EqualTo(VariableTypeIds.PropertyType));
                ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = properties[0].NodeId, AttributeId = Attributes.BrowseName },
                        new ReadValueId { NodeId = properties[0].NodeId, AttributeId = Attributes.DataType },
                        new ReadValueId { NodeId = properties[0].NodeId, AttributeId = Attributes.Value }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(3));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
                Assert.That(name, Is.EqualTo(new QualifiedName("Speed", modelIndex)));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
                Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
                Assert.That(response.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[2].WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.Zero);
                byte[] downloaded = await asset.DownloadThingDescriptionAsync().ConfigureAwait(false);
                Assert.That(downloaded, Is.EqualTo(content));
            }).ConfigureAwait(false);
        }

        private static async Task<NodeId> TypeDefinitionAsync(ClientSession session, NodeId node)
        {
            BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), 0,
                [
                    new BrowseDescription
                    {
                        NodeId = node,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Ua.ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)(NodeClass.ObjectType | NodeClass.VariableType),
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].References, Has.Count.EqualTo(1));
            return ExpandedNodeId.ToNodeId(response.Results[0].References[0].NodeId, session.NamespaceUris);
        }

        private static async Task WithAssetAsync(Func<ClientSession, WotAssetClient, Task> action)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string directory = Path.Combine(
                Path.GetTempPath(), nameof(WotLegacyTypeBindingLiveTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                AutoAccept = true,
                SecurityNone = false
            };
            ReferenceServer? server = null;
            ClientFixture? clientFixture = null;
            ClientSession? session = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                await server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                    new RuntimeNodeSetOptions
                    {
                        Sources =
                        [
                            RuntimeNodeSetSource.FromStream("LegacyBoundType",
                                _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(s_model), false)),
                                [ModelUri])
                        ]
                    }, callerContext: null).ConfigureAwait(false);
                var options = new WotConnectivityServerOptions
                {
                    ThingDescriptionStorageFolder = Path.Combine(directory, "documents"),
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                };
                options.Bindings.Add(new SimulatedWotAssetProviderFactory());
                await server.NodeManagerLifecycle.AddAsync(
                    new WotConnectivityNodeManagerFactory(options), callerContext: null).ConfigureAwait(false);
                clientFixture = new ClientFixture(false, false, telemetry);
                await clientFixture.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                session = await clientFixture.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                WotConnectivityClient client = await WotConnectivityClient.ForServerAsync(session, telemetry)
                    .ConfigureAwait(false);
                WotAssetClient asset = await client.CreateAssetAsync("typed-legacy").ConfigureAwait(false);
                await action(session, asset).ConfigureAwait(false);
                await client.DeleteAssetAsync(asset.AssetId).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (session is not null)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        session?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (clientFixture is not null)
                            {
                                await clientFixture.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                await fixture.StopAsync().ConfigureAwait(false);
                            }
                            finally
                            {
                                try
                                {
                                    server?.Dispose();
                                }
                                finally
                                {
                                    if (Directory.Exists(directory))
                                    {
                                        Directory.Delete(directory, recursive: true);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static byte[] Content(bool byName)
        {
            string binding = byName
                ? "\"@type\":[\"Thing\",\"uav:object\",\"model:PumpType\"],"
                : """
                "@type":["Thing","uav:object"],
                "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:legacy-bound-type;i=4001"}],
                """;
            return Encoding.UTF8.GetBytes(
                $$$$"""
                {
                  "@context":[
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "ua":"http://opcfoundation.org/UA/",
                      "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                      "model":"urn:test:legacy-bound-type"
                    }
                  ],
                  {{{{binding}}}}
                  "name":"typed-legacy",
                  "title":"Typed legacy asset",
                  "base":"sim://opcua.test/wot/typed-legacy",
                  "security":"none",
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "properties":{
                    "Speed":{"type":"number","forms":[{"href":"sim://opcua.test/wot/speed"}]}
                  }
                }
                """);
        }

        private const string ModelUri = "urn:test:legacy-bound-type";
        private const string s_model = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:legacy-bound-type</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:test:legacy-bound-type" Version="1.0.0"
                PublicationDate="2026-01-01T00:00:00Z">
                <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.04"
                  PublicationDate="2025-01-08T00:00:00Z" />
              </Model></Models>
              <Aliases>
                <Alias Alias="HasSubtype">i=45</Alias>
                <Alias Alias="HasProperty">i=46</Alias>
                <Alias Alias="HasTypeDefinition">i=40</Alias>
                <Alias Alias="HasModellingRule">i=37</Alias>
                <Alias Alias="Double">i=11</Alias>
              </Aliases>
              <UAObjectType NodeId="ns=1;i=4001" BrowseName="1:PumpType">
                <DisplayName>PumpType</DisplayName>
                <References>
                  <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="HasProperty">ns=1;i=4002</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;i=4002" BrowseName="1:Speed" ParentNodeId="ns=1;i=4001"
                DataType="Double" ValueRank="-1" AccessLevel="3">
                <DisplayName>Speed</DisplayName>
                <References>
                  <Reference ReferenceType="HasProperty" IsForward="false">ns=1;i=4001</Reference>
                  <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                  <Reference ReferenceType="HasModellingRule">i=78</Reference>
                </References>
              </UAVariable>
            </UANodeSet>
            """;
    }
}
