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
        public async Task ActionArgumentIdentitiesCannotAliasAuthoredActionNames(bool reverse)
        {
            using var harness = new ManagerHarness(_tempFolder, new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry.CreateAssetAsync("removal", CancellationToken.None)
                .ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            ServiceResult result = await harness.Registry.RebuildAsync(
                entry, ActionIdentityDescription(reverse), false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(entry.Actions, Has.Count.EqualTo(3));
            NodeState[] nodes = entry.Actions.Values.SelectMany(action => new NodeState[]
            {
                action.Method, action.Method.InputArguments!, action.Method.OutputArguments!
            }).ToArray();
            Assert.That(nodes, Has.Length.EqualTo(9));
            Assert.That(nodes.Select(node => node.NodeId), Is.Unique);
            foreach (NodeState node in nodes)
            {
                Assert.That(harness.Manager.Find(node.NodeId), Is.SameAs(node));
            }
            foreach (var action in entry.Actions.Values)
            {
                Assert.That(action.Method.NodeId,
                    Is.EqualTo(harness.Manager.AllocateChildNodeId("removal", "actions", action.Tag.Name)));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ActionArgumentSubtreesAreRemovedForSuffixNamedActions(bool reverse)
        {
            using var harness = new ManagerHarness(_tempFolder, new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry.CreateAssetAsync("removal", CancellationToken.None)
                .ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            ServiceResult initial = await harness.Registry.RebuildAsync(
                entry, ActionIdentityDescription(reverse), false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(initial), Is.True);
            NodeId[] identities = entry.Actions.Values.SelectMany(action => new[]
            {
                action.Method.NodeId, action.Method.InputArguments!.NodeId, action.Method.OutputArguments!.NodeId
            }).ToArray();

            ServiceResult removed = await harness.Registry.RebuildAsync(
                entry, RemovalDescription(null), false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(removed), Is.True);
            Assert.That(entry.Actions, Is.Empty);
            foreach (NodeId identity in identities)
            {
                Assert.That(harness.Manager.Find(identity), Is.Null, identity.ToString());
            }
        }

        private static ThingDescription ActionIdentityDescription(bool reverse)
        {
            ThingDescription description = RemovalDescription("Template");
            WotAction action = description.Actions!["TemplateAction"];
            description.Properties = null;
            description.Actions = new Dictionary<string, WotAction>();
            IEnumerable<string> names = reverse ? s_actionCollisionNames.Reverse() : s_actionCollisionNames;
            foreach (string name in names)
            {
                description.Actions.Add(name, action);
            }
            return description;
        }

        private static readonly string[] s_actionCollisionNames = ["Reset", "Reset_in", "Reset_out"];
    }
}
