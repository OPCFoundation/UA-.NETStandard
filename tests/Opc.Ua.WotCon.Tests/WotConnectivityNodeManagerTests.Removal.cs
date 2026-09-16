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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotConnectivityNodeManagerTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RebuildRemovesObsoleteReferencesAndHandlers(bool replacement)
        {
            using var harness = new ManagerHarness(_tempFolder, new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("removal", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            ServiceResult first = await harness.Registry.RebuildAsync(
                entry, RemovalDescription("Old"), false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(first), Is.True);
            BaseDataVariableState oldProperty = entry.Properties.Values.Single().Variable;
            MethodState oldAction = entry.Actions.Values.Single().Method;
            NodeId oldInputId = oldAction.InputArguments!.NodeId;
            NodeId oldOutputId = oldAction.OutputArguments!.NodeId;
            NodeId component = ExpandedNodeId.ToNodeId(
                Opc.Ua.WotCon.ReferenceTypeIds.HasWoTComponent, harness.Manager.Server.NamespaceUris);
            Assert.That(entry.Asset.ReferenceExists(component, false, oldProperty.NodeId), Is.True);
            Assert.That(oldProperty.ReferenceExists(component, true, assetId), Is.True);
            Assert.That(entry.Asset.ReferenceExists(Ua.ReferenceTypeIds.HasComponent, false, oldAction.NodeId),
                Is.True);
            Assert.That(oldAction.ReferenceExists(Ua.ReferenceTypeIds.HasComponent, true, assetId), Is.True);

            ServiceResult second = await harness.Registry.RebuildAsync(
                entry, RemovalDescription(replacement ? "New" : null), false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(second), Is.True);
            Assert.That(entry.Asset.ReferenceExists(component, false, oldProperty.NodeId), Is.False);
            Assert.That(oldProperty.ReferenceExists(component, true, assetId), Is.False);
            Assert.That(entry.Asset.ReferenceExists(Ua.ReferenceTypeIds.HasComponent, false, oldAction.NodeId),
                Is.False);
            Assert.That(oldAction.ReferenceExists(Ua.ReferenceTypeIds.HasComponent, true, assetId), Is.False);
            Assert.That(oldProperty.OnSimpleReadValueAsync, Is.Null);
            Assert.That(oldProperty.OnSimpleWriteValueAsync, Is.Null);
            Assert.That(oldProperty.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(oldAction.OnCallMethod2Async, Is.Null);
            Assert.That(oldAction.Executable, Is.False);
            Assert.That(harness.Registry.TryGetProperty(oldProperty.NodeId, out _, out _, out _), Is.False);
            Assert.That(harness.Registry.TryGetAction(oldAction.NodeId, out _, out _, out _), Is.False);
            Assert.That(harness.Manager.Find(oldProperty.NodeId), Is.Null);
            Assert.That(harness.Manager.Find(oldAction.NodeId), Is.Null);
            Assert.That(harness.Manager.Find(oldInputId), Is.Null);
            Assert.That(harness.Manager.Find(oldOutputId), Is.Null);
            Assert.That(entry.Properties, Has.Count.EqualTo(replacement ? 1 : 0));
            Assert.That(entry.Actions, Has.Count.EqualTo(replacement ? 1 : 0));
            if (replacement)
            {
                Assert.That(entry.Properties.Values.Single().Tag.Name, Is.EqualTo("NewReading"));
                Assert.That(entry.Actions.Values.Single().Tag.Name, Is.EqualTo("NewAction"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RebuildIndexesCurrentAffordancesAndArguments(bool replaced)
        {
            using var harness = new ManagerHarness(_tempFolder, new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("removal", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            if (replaced)
            {
                ServiceResult previous = await harness.Registry.RebuildAsync(
                    entry, RemovalDescription("Previous"), false, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(previous), Is.True);
            }

            ServiceResult result = await harness.Registry.RebuildAsync(
                entry, RemovalDescription("Current"), false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            BaseDataVariableState property = entry.Properties.Values.Single().Variable;
            MethodState action = entry.Actions.Values.Single().Method;
            Assert.That(harness.Manager.Find(property.NodeId), Is.SameAs(property));
            Assert.That(harness.Manager.Find(action.NodeId), Is.SameAs(action));
            Assert.That(harness.Manager.Find(action.InputArguments!.NodeId), Is.SameAs(action.InputArguments));
            Assert.That(harness.Manager.Find(action.OutputArguments!.NodeId), Is.SameAs(action.OutputArguments));
            Assert.That(property.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(property.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(property.OnSimpleWriteValueAsync, Is.Not.Null);
            Assert.That(action.OnCallMethod2Async, Is.Not.Null);
            Assert.That(action.Executable, Is.True);
        }

        private static ThingDescription RemovalDescription(string? prefix)
        {
            var description = new ThingDescription
            {
                Name = "removal",
                Base = "sim://opcua.test/wot/removal"
            };
            if (prefix is not null)
            {
                description.Properties = new Dictionary<string, WotProperty>
                {
                    [prefix + "Reading"] = new WotProperty { Type = "number" }
                };
                description.Actions = new Dictionary<string, WotAction>
                {
                    [prefix + "Action"] = new WotAction
                    {
                        Input = new WotActionSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, WotActionMember>
                            {
                                ["input"] = new WotActionMember { Type = "string" }
                            }
                        },
                        Output = new WotActionSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, WotActionMember>
                            {
                                ["output"] = new WotActionMember { Type = "number" }
                            }
                        }
                    }
                };
            }
            return description;
        }
    }
}
