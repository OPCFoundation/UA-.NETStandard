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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Tests.Providers;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotLegacyMaterializationLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyReplacementExposesOnlyCurrentInteractionsOverTransport(bool replacement)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string directory = Path.Combine(
                Path.GetTempPath(), nameof(WotLegacyMaterializationLiveTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                AutoAccept = true,
                SecurityNone = false
            };
            ReferenceServer? server = null;
            ClientFixture? clientFixture = null;
            ISession? session = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
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
                WotAssetClient asset = await client.CreateAssetAsync("legacy-rebuild").ConfigureAwait(false);
                await asset.UploadThingDescriptionAsync(Content("Old")).ConfigureAwait(false);

                List<WotAssetVariableEntry> oldProperties = await CollectAsync(asset.EnumeratePropertiesAsync())
                    .ConfigureAwait(false);
                List<WotAssetVariableEntry> oldActions = await CollectAsync(asset.EnumerateActionsAsync())
                    .ConfigureAwait(false);
                Assert.That(oldProperties, Has.Count.EqualTo(1));
                Assert.That(oldActions, Has.Count.EqualTo(1));
                Assert.That(oldProperties[0].BrowseName, Is.EqualTo("OldReading"));
                Assert.That(oldActions[0].BrowseName, Is.EqualTo("OldAction"));
                NodeId oldProperty = oldProperties.Single().NodeId;
                NodeId oldAction = oldActions.Single().NodeId;
                DataValue initial = await session.ReadValueAsync(oldProperty).ConfigureAwait(false);
                Assert.That(initial.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.WrappedValue.TryGetValue(out double initialValue), Is.True);
                Assert.That(initialValue, Is.Zero);
                CallResponse call = await CallAsync(session, asset.AssetId, oldAction).ConfigureAwait(false);
                Assert.That(call.Results, Has.Count.EqualTo(1));
                Assert.That(call.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                byte[] replacementContent = Content(replacement ? "New" : null);
                await asset.UploadThingDescriptionAsync(replacementContent).ConfigureAwait(false);

                List<WotAssetVariableEntry> properties = await CollectAsync(asset.EnumeratePropertiesAsync())
                    .ConfigureAwait(false);
                List<WotAssetVariableEntry> actions = await CollectAsync(asset.EnumerateActionsAsync())
                    .ConfigureAwait(false);
                Assert.That(properties, Has.Count.EqualTo(replacement ? 1 : 0));
                Assert.That(actions, Has.Count.EqualTo(replacement ? 1 : 0));
                Assert.That(properties.Any(entry => entry.NodeId == oldProperty), Is.False);
                Assert.That(actions.Any(entry => entry.NodeId == oldAction), Is.False);
                ReadResponse removed = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = oldProperty, AttributeId = Attributes.NodeClass },
                        new ReadValueId { NodeId = oldAction, AttributeId = Attributes.NodeClass }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(removed.Results, Has.Count.EqualTo(2));
                Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(removed.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                CallResponse staleCall = await CallAsync(session, asset.AssetId, oldAction).ConfigureAwait(false);
                Assert.That(staleCall.Results, Has.Count.EqualTo(1));
                Assert.That(staleCall.Results[0].StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadMethodInvalid).Or.EqualTo(StatusCodes.BadNodeIdUnknown));
                if (replacement)
                {
                    Assert.That(properties.Single().BrowseName, Is.EqualTo("NewReading"));
                    Assert.That(actions.Single().BrowseName, Is.EqualTo("NewAction"));
                    DataValue current = await session.ReadValueAsync(properties.Single().NodeId).ConfigureAwait(false);
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(current.WrappedValue.TryGetValue(out double value), Is.True);
                    Assert.That(value, Is.Zero);
                    CallResponse currentCall = await CallAsync(session, asset.AssetId, actions.Single().NodeId)
                        .ConfigureAwait(false);
                    Assert.That(currentCall.Results, Has.Count.EqualTo(1));
                    Assert.That(currentCall.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                byte[] downloaded = await asset.DownloadThingDescriptionAsync().ConfigureAwait(false);
                Assert.That(downloaded, Is.EqualTo(replacementContent));
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

        private static async Task<List<WotAssetVariableEntry>> CollectAsync(
            IAsyncEnumerable<WotAssetVariableEntry> source)
        {
            var entries = new List<WotAssetVariableEntry>();
            await foreach (WotAssetVariableEntry entry in source.ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return entries;
        }

        private static async Task<CallResponse> CallAsync(ISession session, NodeId owner, NodeId method)
        {
            return await session.CallAsync(null,
                [new CallMethodRequest { ObjectId = owner, MethodId = method, InputArguments = [5.5] }],
                CancellationToken.None).ConfigureAwait(false);
        }

        private static byte[] Content(string? prefix)
        {
            string json = prefix is null
                ? /*lang=json,strict*/ """
                {
                  "@context":"https://www.w3.org/2022/wot/td/v1.1",
                  "title":"Legacy replacement",
                  "name":"legacy-rebuild",
                  "base":"sim://opcua.test/wot/legacy-rebuild",
                  "security":"none",
                  "securityDefinitions":{"none":{"scheme":"nosec"}}
                }
                """
                : $$$$"""
                {
                  "@context":"https://www.w3.org/2022/wot/td/v1.1",
                  "title":"Legacy replacement",
                  "name":"legacy-rebuild",
                  "base":"sim://opcua.test/wot/legacy-rebuild",
                  "security":"none",
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "properties":{
                    "{{{{prefix}}}}Reading":{
                      "type":"number",
                      "forms":[{"href":"sim://opcua.test/wot/reading"}]
                    }
                  },
                  "actions":{
                    "{{{{prefix}}}}Action":{
                      "input":{"type":"object","properties":{"value":{"type":"number"}}},
                      "output":{"type":"object","properties":{"echo":{"type":"number"}}},
                      "forms":[{"href":"sim://opcua.test/wot/action"}]
                    }
                  }
                }
                """;
            return Encoding.UTF8.GetBytes(json);
        }
    }
}
