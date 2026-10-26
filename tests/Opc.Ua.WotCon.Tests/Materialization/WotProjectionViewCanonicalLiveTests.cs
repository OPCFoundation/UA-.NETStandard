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
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Registry;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT projection view canonical live.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotProjectionViewCanonicalLiveTests
    {
        /// <summary>
        /// Verifies that canonical view identity cannot be taken by another resource.
        /// </summary>
        [Test]
        public async Task CanonicalViewIdentityCannotBeTakenByAnotherResourceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("canonical-owned-view", harness.NamespaceIndex);
            var first = new WotViewProjectionRequest(
                "first", "resource:first", NodeId.Null, identity, LegacyPlan(101));
            await harness.Views.ApplyAsync(first).ConfigureAwait(false);

            ServiceResultException? rejection = null;
            try
            {
                var conflicting = new WotViewProjectionRequest(
                    "second", "resource:second", NodeId.Null, identity, LegacyPlan(202));
                await harness.Views.ApplyAsync(conflicting).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            uint retainedVersion = await harness.ReadViewVersionAsync(identity).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "A canonical View cannot be reassigned to another Resource.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(retainedVersion, Is.EqualTo(101u),
                    "Rejected ownership must not replace the original native publication.");
            });
        }

        /// <summary>
        /// Verifies that authored canonical view is browsable in its own namespace.
        /// </summary>
        [Test]
        public async Task AuthoredCanonicalViewIsBrowsableInItsOwnNamespaceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            NodeId identity = harness.Identity("urn:c2:authored-views", "Authored");
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "authored", "resource:authored", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);

            ReadResponse read = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = identity, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(read.Results, Has.Count.EqualTo(1));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.View));
            Assert.That(await harness.ReadViewVersionAsync(identity).ConfigureAwait(false), Is.EqualTo(1u));
        }

        /// <summary>
        /// Verifies that a local-only affordance in a projection is materialized and organized by its canonical View.
        /// </summary>
        /// <param name="affordanceMap">The local affordance map.</param>
        /// <param name="expectedNodeClass">The ordinary Binding materialization's NodeClass.</param>
        /// <param name="authoredIdentity">Whether the local affordance declares its identity.</param>
        /// <param name="thingModel">Whether the projection and source are Thing Models.</param>
        [TestCase("properties", NodeClass.Variable, true, false)]
        [TestCase("actions", NodeClass.Method, true, false)]
        [TestCase("events", NodeClass.ObjectType, true, false)]
        [TestCase("properties", NodeClass.Variable, false, false)]
        [TestCase("actions", NodeClass.Method, false, false)]
        [TestCase("events", NodeClass.ObjectType, false, false)]
        [TestCase("properties", NodeClass.Variable, true, true)]
        [TestCase("actions", NodeClass.Method, true, true)]
        [TestCase("events", NodeClass.ObjectType, true, true)]
        [TestCase("properties", NodeClass.Variable, false, true)]
        [TestCase("actions", NodeClass.Method, false, true)]
        [TestCase("events", NodeClass.ObjectType, false, true)]
        public async Task ProjectionLocalOnlyAffordanceIsMaterializedAndOrganizedAsync(
            string affordanceMap,
            NodeClass expectedNodeClass,
            bool authoredIdentity,
            bool thingModel)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            string group = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions;
            string rootType = thingModel ? "tm:ThingModel" : "Thing";
            string contentType = thingModel ? "application/tm+json" : "application/td+json";
            string sourceUri = $"urn:wot:{group}/local-unused-source";
            string schema = affordanceMap switch
            {
                "properties" => "\"type\":\"number\",",
                "events" => "\"data\":{\"type\":\"object\",\"properties\":{}},",
                _ => string.Empty
            };
            string identity = authoredIdentity
                ? $"\"uav:id\":\"nsu=urn:wot:{group}/projection-local;s=ProjectionLocal/localOnly\","
                : string.Empty;
            await harness.RegisterTdAsync("local-unused-source", $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/",
                     "tm":"https://www.w3.org/2019/wot/tm#"}],
                  "@type":"{{{rootType}}}",
                  "id":"urn:local-unused-source","title":"Local unused source",
                  "properties":{"unused":{"type":"number","forms":[{"href":"https://example.test/unused"}]}}
                }
                """, thingModel).ConfigureAwait(false);
            await harness.RegisterTdAsync("projection-local", $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/",
                     "tm":"https://www.w3.org/2019/wot/tm#"}],
                  "@type":["{{{rootType}}}","uav:projection"],
                  "id":"urn:projection:local-only","title":"Local only",
                  "uav:scenario":"urn:scenario:local-only",
                  "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},
                  "security":"nosec_sc",
                  "uav:projects":[{
                    "uav:sourceName":"unused",
                    "href":"{{{sourceUri}}}",
                    "type":"{{{contentType}}}",
                    "uav:selectAll":false
                  }],
                  "{{{affordanceMap}}}":{
                    "localOnly":{
                      {{{identity}}}
                      {{{schema}}}
                      "forms":[{"href":"https://example.test/local"}]
                    }
                  }
                }
                """, thingModel).ConfigureAwait(false);

            WotRefreshResult result = await harness.RefreshAsync().ConfigureAwait(false);

            NodeId resourceNode = harness.ResourceNodeId("projection-local", thingModel);
            NodeId viewNode = new(resourceNode.IdentifierAsString + "/View", resourceNode.NamespaceIndex);
            WoTResourceLoadResultDataType load = result.Results.Single(
                row => row.ResourceId == "projection-local");
            string resultDetails = string.Join(
                Environment.NewLine,
                result.Results.Select(row => $"{row.ResourceId}: {row.Phase}/{row.Outcome}: {row.Message}"));
            Assert.That(load.LoadState, Is.EqualTo(WoTLoadStateEnum.Active), resultDetails);
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(
                viewNode, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            ReferenceDescription local = members.ToArray()!.Single();

            Assert.Multiple(() =>
            {
                Assert.That(local.BrowseName.Name, Is.EqualTo("localOnly"));
                Assert.That(local.NodeClass, Is.EqualTo(expectedNodeClass));
                if (authoredIdentity)
                {
                    Assert.That(harness.Target(local), Is.EqualTo(harness.Identity(
                        $"urn:wot:{group}/projection-local", "ProjectionLocal/localOnly")));
                }
                Assert.That(load.LoadState, Is.EqualTo(WoTLoadStateEnum.Active), load.Message);
                Assert.That(load.RootNodeId, Is.EqualTo(viewNode),
                    "Local affordance materialization must not replace the canonical View identity.");
            });
            Assert.That(await ReadNodeClassAsync(harness, harness.Target(local)).ConfigureAwait(false),
                Is.EqualTo(expectedNodeClass));
            Assert.That(await ReadNodeClassAsync(harness, viewNode).ConfigureAwait(false),
                Is.EqualTo(NodeClass.View));
        }

        /// <summary>
        /// Same-title projections have distinct generated local identities and retain complete counts in every refresh.
        /// </summary>
        [Test]
        public async Task ProjectionGeneratedLocalIdentitiesAndCountsRemainResourceScopedAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            await harness.RegisterTdAsync("unused-count-source", """
                {
                  "@type":"Thing","id":"urn:unused-count-source","title":"Unused",
                  "properties":{"unused":{"type":"number","forms":[{"href":"https://example.test/unused"}]}}
                }
                """).ConfigureAwait(false);
            string[] resources = ["generated-first", "generated-second"];
            foreach (string resource in resources)
            {
                await harness.RegisterTdAsync(resource, $$$"""
                    {
                      "@type":["Thing","uav:projection"],
                      "id":"urn:{{{resource}}}","title":"Same title",
                      "uav:scenario":"urn:scenario:generated-locals",
                      "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},"security":"nosec_sc",
                      "uav:projects":[{
                        "uav:sourceName":"unused",
                        "href":"urn:wot:thingdescriptions/unused-count-source",
                        "type":"application/td+json","uav:selectAll":false
                      }],
                      "properties":{"local":{"type":"number","forms":[{"href":"https://example.test/local"}]}}
                    }
                    """).ConfigureAwait(false);
            }

            WotRefreshResult preview = await harness.RefreshAsync(dryRun: true).ConfigureAwait(false);
            foreach (string resource in resources)
            {
                Assert.That(preview.Results.Single(row => row.ResourceId == resource).MaterializedNodeCount,
                    Is.EqualTo(3u), "The local root, local Variable and canonical View are all owned Nodes.");
                Assert.That(harness.Registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, resource)!.MaterializedNodeCount, Is.Zero);
            }

            WotRefreshResult loaded = await harness.RefreshAsync().ConfigureAwait(false);
            var identities = new HashSet<NodeId>();
            foreach (string resource in resources)
            {
                WoTResourceLoadResultDataType row = loaded.Results.Single(value => value.ResourceId == resource);
                Assert.That(row.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), row.Message);
                Assert.That(row.MaterializedNodeCount, Is.EqualTo(3u));
                WotResource persisted = harness.Registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, resource)!;
                Assert.That(persisted.MaterializedNodeCount, Is.EqualTo(3));
                Assert.That(row.RootNodeId, Is.EqualTo(persisted.RootNodeId));
                Assert.That(await ReadNodeClassAsync(harness, row.RootNodeId).ConfigureAwait(false),
                    Is.EqualTo(NodeClass.View));
                ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(
                    row.RootNodeId, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
                Assert.That(members.Count, Is.EqualTo(1));
                NodeId local = harness.Target(members[0]);
                Assert.That(identities.Add(local), Is.True, "Generated local Nodes cannot be shared across resources.");
                Assert.That(await ReadNodeClassAsync(harness, local).ConfigureAwait(false),
                    Is.EqualTo(NodeClass.Variable));
            }

            WotRefreshResult unchanged = await harness.RefreshAsync().ConfigureAwait(false);
            WotRefreshResult unchangedPreview = await harness.RefreshAsync(dryRun: true).ConfigureAwait(false);
            foreach (string resource in resources)
            {
                Assert.That(unchanged.Results.Single(row => row.ResourceId == resource).MaterializedNodeCount,
                    Is.EqualTo(3u));
                Assert.That(unchangedPreview.Results.Single(row => row.ResourceId == resource).MaterializedNodeCount,
                    Is.EqualTo(3u));
                Assert.That(harness.Registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, resource)!.MaterializedNodeCount, Is.EqualTo(3));
            }
        }

        /// <summary>
        /// Verifies that local projection affordances coexist with imported members and override name collisions.
        /// </summary>
        /// <param name="affordanceMap">The local and imported affordance map.</param>
        /// <param name="expectedNodeClass">The ordinary Binding materialization's NodeClass.</param>
        /// <param name="thingModel">Whether the projection and source are Thing Models.</param>
        [TestCase("properties", NodeClass.Variable, false)]
        [TestCase("actions", NodeClass.Method, false)]
        [TestCase("events", NodeClass.ObjectType, false)]
        [TestCase("properties", NodeClass.Variable, true)]
        [TestCase("actions", NodeClass.Method, true)]
        [TestCase("events", NodeClass.ObjectType, true)]
        public async Task ProjectionLocalAffordancesJoinImportedMembersAndOverrideCollisionsAsync(
            string affordanceMap,
            NodeClass expectedNodeClass,
            bool thingModel)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            const string sourceResource = "mixed-source";
            const string projectionResource = "projection-mixed";
            string group = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions;
            string rootType = thingModel ? "tm:ThingModel" : "Thing";
            string contentType = thingModel ? "application/tm+json" : "application/td+json";
            string sourceUri = $"urn:wot:{group}/{sourceResource}";
            string projectionUri = $"urn:wot:{group}/{projectionResource}";
            string schema = affordanceMap switch
            {
                "properties" => "\"type\":\"number\",",
                "events" => "\"data\":{\"type\":\"object\",\"properties\":{}},",
                _ => string.Empty
            };
            NodeId importedShared = harness.Identity(sourceUri, "MixedSource/shared");
            NodeId importedOnly = harness.Identity(sourceUri, "MixedSource/importedOnly");
            NodeId localShared = harness.Identity(projectionUri, "MixedProjection/shared");
            NodeId localOnly = harness.Identity(projectionUri, "MixedProjection/localOnly");

            await harness.RegisterTdAsync(sourceResource, $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/",
                     "tm":"https://www.w3.org/2019/wot/tm#"}],
                  "@type":"{{{rootType}}}",
                  "id":"urn:mixed-source","title":"Mixed source",
                  "uav:id":"nsu={{{sourceUri}}};s=MixedSource",
                  "{{{affordanceMap}}}":{
                    "shared":{
                      "uav:id":"nsu={{{sourceUri}}};s=MixedSource/shared",
                      {{{schema}}}
                      "forms":[{"href":"https://example.test/imported-shared"}]
                    },
                    "importedOnly":{
                      "uav:id":"nsu={{{sourceUri}}};s=MixedSource/importedOnly",
                      {{{schema}}}
                      "forms":[{"href":"https://example.test/imported-only"}]
                    }
                  }
                }
                """, thingModel).ConfigureAwait(false);
            await harness.RegisterTdAsync(projectionResource, $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/",
                     "tm":"https://www.w3.org/2019/wot/tm#"}],
                  "@type":["{{{rootType}}}","uav:projection"],
                  "id":"urn:projection:mixed","title":"Mixed projection",
                  "uav:scenario":"urn:scenario:mixed",
                  "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},
                  "security":"nosec_sc",
                  "uav:projects":[{
                    "uav:sourceName":"source",
                    "href":"{{{sourceUri}}}",
                    "type":"{{{contentType}}}",
                    "uav:selectAll":true
                  }],
                  "{{{affordanceMap}}}":{
                    "localOnly":{
                      "uav:id":"nsu={{{projectionUri}}};s=MixedProjection/localOnly",
                      {{{schema}}}
                      "forms":[{"href":"https://example.test/local-only"}]
                    },
                    "shared":{
                      "uav:id":"nsu={{{projectionUri}}};s=MixedProjection/shared",
                      {{{schema}}}
                      "forms":[{"href":"https://example.test/local-shared"}]
                    }
                  }
                }
                """, thingModel).ConfigureAwait(false);

            WotRefreshResult result = await harness.RefreshAsync().ConfigureAwait(false);

            NodeId resourceNode = harness.ResourceNodeId(projectionResource, thingModel);
            NodeId viewNode = new(resourceNode.IdentifierAsString + "/View", resourceNode.NamespaceIndex);
            WoTResourceLoadResultDataType load = result.Results.Single(
                row => row.ResourceId == projectionResource);
            string resultDetails = string.Join(
                Environment.NewLine,
                result.Results.Select(row => $"{row.ResourceId}: {row.Phase}/{row.Outcome}: {row.Message}"));
            Assert.That(load.LoadState, Is.EqualTo(WoTLoadStateEnum.Active), resultDetails);
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(
                viewNode, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Dictionary<string, NodeId> membersByName = members.ToArray()!
                .ToDictionary(
                    reference => reference.BrowseName.Name ??
                        throw new AssertionException("A projection member has no BrowseName."),
                    harness.Target,
                    StringComparer.Ordinal);
            Assert.That(
                string.Join(",", membersByName.Keys.OrderBy(name => name, StringComparer.Ordinal)),
                Is.EqualTo("importedOnly,localOnly,shared"),
                resultDetails + Environment.NewLine + load.Message);
            Assert.Multiple(() =>
            {
                Assert.That(membersByName, Has.Count.EqualTo(3));
                Assert.That(membersByName["importedOnly"], Is.EqualTo(importedOnly),
                    "Imported Nodes are organized by identity, not cloned.");
                Assert.That(membersByName["localOnly"], Is.EqualTo(localOnly));
                Assert.That(membersByName["shared"], Is.EqualTo(localShared),
                    "A local definition wins a same-kind name collision with an imported affordance.");
                Assert.That(membersByName["shared"], Is.Not.EqualTo(importedShared));
                Assert.That(load.RootNodeId, Is.EqualTo(viewNode));
                Assert.That(members.ToArray()!.Select(member => member.NodeClass),
                    Is.All.EqualTo(expectedNodeClass));
            });
            Assert.That(await ReadNodeClassAsync(harness, importedShared).ConfigureAwait(false),
                Is.EqualTo(expectedNodeClass));
            Assert.That(await ReadNodeClassAsync(harness, importedOnly).ConfigureAwait(false),
                Is.EqualTo(expectedNodeClass));
            Assert.That(await ReadNodeClassAsync(harness, localShared).ConfigureAwait(false),
                Is.EqualTo(expectedNodeClass));
            Assert.That(await ReadNodeClassAsync(harness, localOnly).ConfigureAwait(false),
                Is.EqualTo(expectedNodeClass));
            Assert.That(await ReadNodeClassAsync(harness, viewNode).ConfigureAwait(false),
                Is.EqualTo(NodeClass.View));
        }

        /// <summary>
        /// Verifies that canonical view cannot replace an existing node role.
        /// </summary>
        /// <param name="ownedProperty">The owned-property flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalViewCannotReplaceAnExistingNodeRoleAsync(bool ownedProperty)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var original = new NodeId("role-owner", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "original", "resource:original", NodeId.Null, original, LegacyPlan(101))).ConfigureAwait(false);
            NodeId occupied = ownedProperty
                ? await harness.ViewVersionIdAsync(original).ConfigureAwait(false)
                : Ua.VariableIds.Server_ServerStatus_StartTime;
            DataValue before = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResultException? rejection = null;
            try
            {
                await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                    "collision", "resource:collision", NodeId.Null, occupied, LegacyPlan(202)))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            DataValue after = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "An existing node's owner and role must not be overwritten.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(after.WrappedValue, Is.EqualTo(before.WrappedValue));
            });
        }

        /// <summary>
        /// Verifies that native view property paths resolve without inventing nodes.
        /// </summary>
        /// <param name="existing">The existing flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeViewPropertyPathsResolveWithoutInventingNodesAsync(bool existing)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("translated-view", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "translated", "resource:translated", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);
            NodeId propertyId = await harness.ViewVersionIdAsync(identity).ConfigureAwait(false);
            TranslateBrowsePathsToNodeIdsResponse translated = await harness.Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = identity,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                    IncludeSubtypes = false,
                                    TargetName = new QualifiedName(existing ? "ViewVersion" : "Missing")
                                }
                            ]
                        }
                    }
                ], CancellationToken.None).ConfigureAwait(false);

            Assert.That(translated.Results, Has.Count.EqualTo(1));
            BrowsePathResult result = translated.Results[0];
            if (!existing)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(result.Targets, Is.Empty);
                return;
            }
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets, Has.Count.EqualTo(1));
            Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            Assert.That(ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, harness.Session.NamespaceUris),
                Is.EqualTo(propertyId));
        }

        /// <summary>
        /// Verifies that canonical candidate renders shared child and typed wrappers over transport.
        /// </summary>
        [Test]
        public async Task CanonicalCandidateRendersSharedChildAndTypedWrappersOverTransportAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            NodeId right = harness.Identity("urn:c2:native-views", "Right");
            var context = harness.GraphContext(
                [new WotCanonicalViewSource(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], []),
                    harness.GraphRequest("left", left, [], [new WotCanonicalViewLink(harness.ResourceXid("child"), "Group")]),
                    harness.GraphRequest("right", right, [], [new WotCanonicalViewLink(harness.ResourceXid("child"), "Group")])
                ], []).State;

            await harness.PublishCanonicalAsync(state).ConfigureAwait(false);

            ArrayOf<ReferenceDescription> views = await harness.BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(views.ToArray()!.Count(reference => harness.Target(reference) == child), Is.EqualTo(1));
            ReferenceDescription leftGroup = (await harness.BrowseAsync(left, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false)).ToArray()!.Single();
            ReferenceDescription rightGroup = (await harness.BrowseAsync(right, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false)).ToArray()!.Single();
            Assert.That(leftGroup.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(leftGroup.TypeDefinition,
                Is.EqualTo(new ExpandedNodeId(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.WoTProjectionGroupType, harness.NamespaceUris))));
            NodeId leftWrapper = harness.Target(leftGroup);
            NodeId rightWrapper = harness.Target(rightGroup);
            Assert.That(leftWrapper, Is.Not.EqualTo(rightWrapper));
            ReferenceDescription rootProperty = (await harness.BrowseAsync(leftWrapper, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).ToArray()!.Single();
            Assert.That(rootProperty.BrowseName,
                Is.EqualTo(new QualifiedName("ProjectionRoot", harness.NamespaceIndex)));
            DataValue property = await harness.Session.ReadValueAsync(harness.Target(rootProperty)).ConfigureAwait(false);
            Assert.That(property.WrappedValue.TryGetValue(out NodeId projectionRoot), Is.True);
            Assert.That(projectionRoot, Is.EqualTo(child));
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(
                leftWrapper, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(members.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { Ua.VariableIds.Server_ServerStatus_StartTime }));
            NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
            ArrayOf<ReferenceDescription> correlation = await harness.BrowseAsync(
                child, hasProjection, BrowseDirection.Inverse).ConfigureAwait(false);
            Assert.That(correlation.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { harness.ResourceNodeId("child") }));
        }

        /// <summary>
        /// Verifies that canonical candidate reload updates ancestors and rejects the old token.
        /// </summary>
        [Test]
        public async Task CanonicalCandidateReloadUpdatesAncestorsAndRejectsTheOldTokenAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            NodeId right = harness.Identity("urn:c2:native-views", "Right");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId other = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            var context = harness.GraphContext(
                [new(reading, NodeClass.Variable), new(other, NodeClass.Variable)]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [
                    harness.GraphRequest("child", child, [reading], []),
                    harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")]),
                    harness.GraphRequest("right", right, [other], [])
                ], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            DataValue sourceBefore = await harness.Session.ReadValueAsync(reading).ConfigureAwait(false);
            WotCanonicalViewPreparation changed = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                [harness.GraphRequest("child", child, [reading, other], [])], []);
            Assert.That(changed.AffectedResourceXids.ToArray(),
                Is.EquivalentTo(new[] { harness.ResourceXid("child"), harness.ResourceXid("left") }));

            NodeManagerRegistration replacement = await harness.PublishCanonicalAsync(changed.State, registration)
                .ConfigureAwait(false);

            Assert.That(replacement.Id, Is.EqualTo(registration.Id));
            Assert.That(replacement.Generation, Is.GreaterThan(registration.Generation));
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(2u));
            Assert.That(await harness.ReadViewVersionAsync(left).ConfigureAwait(false), Is.EqualTo(2u));
            Assert.That(await harness.ReadViewVersionAsync(right).ConfigureAwait(false), Is.EqualTo(1u));
            BrowseResult current = await harness.BrowseInViewAsync(child, child, 2).ConfigureAwait(false);
            Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(current.References.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { reading, other }));
            BrowseResult stale = await harness.BrowseInViewAsync(child, child, 1).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadViewVersionInvalid));
            DataValue sourceAfter = await harness.Session.ReadValueAsync(reading).ConfigureAwait(false);
            Assert.That(sourceAfter.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sourceAfter.WrappedValue, Is.EqualTo(sourceBefore.WrappedValue));
            NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
            ArrayOf<ReferenceDescription> correlation = await harness.BrowseAsync(
                harness.ResourceNodeId("child"), hasProjection).ConfigureAwait(false);
            Assert.That(correlation.ToArray()!.Select(harness.Target), Is.EquivalentTo(new[] { child }));
        }

        /// <summary>
        /// Verifies that canonical batch retirement honors the captured browse image.
        /// </summary>
        /// <param name="immediate">The immediate flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalBatchRetirementHonorsTheCapturedBrowseImageAsync(bool immediate)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId number = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            NodeId product = Ua.VariableIds.Server_ServerStatus_BuildInfo_ProductName;
            var context = harness.GraphContext(
                [new(reading, NodeClass.Variable), new(number, NodeClass.Variable), new(product, NodeClass.Variable)]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [harness.GraphRequest("child", child, [reading, number, product], [])], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            BrowseResult first = await harness.BrowseInViewAsync(child, child, 1, 1).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.ContinuationPoint.IsNull, Is.False);
            var retained = new List<NodeId>(first.References.ToArray()!.Select(harness.Target));
            WotCanonicalViewState changed = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                [harness.GraphRequest("child", child, [number, product], [])], []).State;

            await harness.PublishCanonicalBatchAsync(changed, registration, immediate).ConfigureAwait(false);

            ByteString continuation = first.ContinuationPoint;
            do
            {
                BrowseNextResponse response = await harness.Session.BrowseNextAsync(
                    null, false, [continuation], CancellationToken.None).ConfigureAwait(false);
                BrowseResult next = response.Results[0];
                if (immediate)
                {
                    Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(next.References.IsEmpty, Is.True);
                    break;
                }
                Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
                retained.AddRange(next.References.ToArray()!.Select(harness.Target));
                continuation = next.ContinuationPoint;
            }
            while (!continuation.IsNull && continuation.Length != 0);
            if (!immediate)
            {
                Assert.That(retained, Is.EquivalentTo(new[] { reading, number, product }));
            }
            BrowseResult current = await harness.BrowseInViewAsync(child, child, 2).ConfigureAwait(false);
            Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(current.References.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { number, product }));
            Assert.That((await harness.BrowseInViewAsync(child, child, 1).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadViewVersionInvalid));
            Assert.That((await harness.Session.ReadValueAsync(reading).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that stock view participant stages the complete graph without live effects.
        /// </summary>
        /// <param name="publish">The publish flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StockViewParticipantStagesTheCompleteGraphWithoutLiveEffectsAsync(bool publish)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            IWotViewProjectionHost viewHost = harness.Views;
            Assert.That(viewHost, Is.AssignableTo<IWotPreparedViewProjectionHost>());
            var host = (IWotPreparedViewProjectionHost)viewHost;
            Assert.That(host.SupportsPreparedPublication, Is.True);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            WotRegistrySnapshot snapshot = harness.Registry.Current;
            int namespaceCount = harness.NamespaceUris.Count;
            var originalReferences = (await harness.BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false))
                .ToArray()!.Select(reference => (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                    reference.BrowseName, reference.NodeClass, reference.TypeDefinition)).ToArray();
            var expected = new WotCommittedPublicationState(snapshot);
            IWotPreparedViewPublication candidate = await host.PrepareAsync(
                [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
            await using (candidate.ConfigureAwait(false))
            {
                IWotPreparedProjectionPublication prepared = await harness.SourceHost.PrepareAsync([], candidate)
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    WotPreparedViewGraphState graph = prepared.ViewGraph!;
                    Assert.That(graph, Is.Not.Null);
                    Assert.That(graph.AffectedResourceXids.ToArray(),
                        Is.EqualTo(new[] { harness.ResourceXid("child") }));
                    Assert.That(graph.Views.ToList().Single().ViewNodeId, Is.EqualTo(child));
                    Assert.That(harness.Registry.Current, Is.SameAs(snapshot));
                    Assert.That(harness.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                    Assert.That((await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                        .ConfigureAwait(false)).ToArray()!.Select(reference =>
                            (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                                reference.BrowseName, reference.NodeClass, reference.TypeDefinition)),
                        Is.EqualTo(originalReferences));
                    ReadResponse hidden = await harness.Session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = child, AttributeId = Attributes.NodeClass }],
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(hidden.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(() => candidate.BindPreparedRegistrations([harness.RegistryRegistration]),
                        Throws.ArgumentException);
                    if (publish)
                    {
                        WotResource resource = snapshot.FindResourceByXid(harness.ResourceXid("child"))!;
                        uint generation = snapshot.RefreshGeneration + 1;
                        var projection = new WotResourceProjection(
                            resource.GroupId, resource.ResourceId, WoTLoadStateEnum.Active,
                            resource.DefaultVersionId, generation, 1, child,
                            resource.Validation, resource.Diagnostics, DateTime.UtcNow);
                        IWotPreparedRegistryPublication metadata = await harness.Registry.PreparePublicationAsync(
                            snapshot, [projection], generation, graph.CanonicalViewGraphState).ConfigureAwait(false);
                        await using (metadata.ConfigureAwait(false))
                        {
                            prepared.BindReadImages(metadata.ReadImages);
                            var committed = new WotCommittedPublicationState(metadata.IntendedSnapshot, graph.Views);
                            await prepared.CommitAsync(metadata.DecideAsync, () =>
                            {
                                candidate.OnPublished(committed);
                                metadata.Publish();
                            }).ConfigureAwait(false);
                        }
                        Assert.That(prepared.CleanupFailure, Is.Null);
                        Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
                        Assert.That(harness.Registry.Current.CanonicalViewGraphState,
                            Is.EqualTo(graph.CanonicalViewGraphState));
                        Assert.That((await harness.BrowseAsync(child, Ua.ReferenceTypeIds.Organizes)
                            .ConfigureAwait(false)).ToArray()!.Select(harness.Target),
                            Is.EqualTo(new[] { reading }));
                    }
                }
            }
            if (!publish)
            {
                Assert.That(harness.Registry.Current, Is.SameAs(snapshot));
                Assert.That((await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false)).ToArray()!.Select(reference =>
                        (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                            reference.BrowseName, reference.NodeClass, reference.TypeDefinition)),
                    Is.EqualTo(originalReferences));
                IWotPreparedViewPublication retry = await host.PrepareAsync(
                    [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
                await retry.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that stock view preparation freezes caller plans before waiting for admission.
        /// </summary>
        [Test]
        public async Task StockViewPreparationFreezesCallerPlansBeforeWaitingForAdmissionAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            LifecycleWotViewProjectionHost host = harness.Views;
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId other = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            var expected = new WotCommittedPublicationState(harness.Registry.Current);
            IWotPreparedViewPublication first = await host.PrepareAsync(
                [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
            NodeId[] members = [reading];
            Task<IWotPreparedViewPublication> waiting;
            try
            {
                waiting = host.PrepareAsync(
                    [harness.GraphRequest("child", child, members, [])], [], expected).AsTask();
                Assert.That(waiting.IsCompleted, Is.False);
                members[0] = other;
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(false);
            }
            IWotPreparedViewPublication candidate = await waiting.ConfigureAwait(false);
            await using (candidate.ConfigureAwait(false))
            {
                IWotPreparedProjectionPublication prepared = await harness.SourceHost.PrepareAsync([], candidate)
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    WotCanonicalViewState graph = WotCanonicalViewState.Restore(
                        prepared.ViewGraph!.CanonicalViewGraphState,
                        new WotCanonicalViewGraphContext(harness.LogicalServerUri, Namespaces.WotCon,
                            harness.NamespaceUris, [new(reading, NodeClass.Variable), new(other, NodeClass.Variable)]));
                    Assert.That(graph.Views.ToList().Single().Membership.ToArray(),
                        Is.EqualTo(new[] { new ExpandedNodeId(reading) }));
                }
            }
            Assert.That(harness.Registry.Current, Is.SameAs(expected.RegistrySnapshot));
        }

        /// <summary>
        /// Verifies that stock membership digest tracks replacement and retirement.
        /// </summary>
        [Test]
        public async Task StockMembershipDigestTracksReplacementAndRetirementAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId first = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId second = Ua.VariableIds.Server_ServerStatus_CurrentTime;
            AttributeSimpleReadResult absent = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(absent.Result.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            WotCommittedPublicationState initial = await harness.PublishPreparedGraphAsync(
                [harness.GraphRequest("child", child, [first], [])], [],
                new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
            AttributeSimpleReadResult original = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(original.Result, Is.EqualTo(ServiceResult.Good));
            Assert.That(original.Value.TryGetValue(out ByteString originalDigest), Is.True);
            Assert.That(originalDigest.Length, Is.EqualTo(32));
            IWotPreparedViewPublication aborted = await harness.Views.PrepareAsync(
                [harness.GraphRequest("child", child, [second], [])], [], initial).ConfigureAwait(false);
            await aborted.DisposeAsync().ConfigureAwait(false);
            AttributeSimpleReadResult unchanged = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(unchanged.Value, Is.EqualTo(original.Value));
            WotCommittedPublicationState replacement = await harness.PublishPreparedGraphAsync(
                [harness.GraphRequest("child", child, [second], [])], [], initial).ConfigureAwait(false);
            AttributeSimpleReadResult updated = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(updated.Result, Is.EqualTo(ServiceResult.Good));
            Assert.That(updated.Value.TryGetValue(out ByteString updatedDigest), Is.True);
            Assert.That(updatedDigest, Is.Not.EqualTo(originalDigest));
            Assert.That(updatedDigest, Is.EqualTo(WotCanonicalViewState.Parse(
                replacement.RegistrySnapshot.CanonicalViewGraphState).Views[0].MembershipDigest));
            await harness.PublishPreparedGraphAsync([], replacement.Views, replacement).ConfigureAwait(false);
            AttributeSimpleReadResult retired = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(retired.Result.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThatAsync(async () =>
                await harness.ReadMembershipDigestAsync("child", cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that stock membership digest polling does not reconstruct the graph.
        /// </summary>
        /// <param name="viewCount">The view count used by this test case.</param>
        [TestCase(1)]
        [TestCase(3)]
        public async Task StockMembershipDigestPollingDoesNotReconstructTheGraphAsync(int viewCount)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            string[] resources = ["child", "left", "right"];
            NodeId[] members =
            [
                Ua.VariableIds.Server_ServerStatus_StartTime,
                Ua.VariableIds.Server_ServerStatus_CurrentTime,
                Ua.VariableIds.Server_ServerStatus_State
            ];
            var requests = new List<WotViewProjectionRequest>();
            for (int i = 0; i < viewCount; i++)
            {
                requests.Add(harness.GraphRequest(
                    resources[i], harness.Identity("urn:c2:native-views", resources[i]), [members[i]], []));
            }
            WotCommittedPublicationState committed = await harness.PublishPreparedGraphAsync(
                requests.ToArrayOf(), [], new WotCommittedPublicationState(harness.Registry.Current))
                .ConfigureAwait(false);
            Dictionary<string, ByteString> digests = WotCanonicalViewState.Parse(
                committed.RegistrySnapshot.CanonicalViewGraphState).Views.ToList()
                .ToDictionary(view => view.ResourceXid, view => view.MembershipDigest, StringComparer.Ordinal);
            ByteString[] expected = resources.Take(viewCount).Select(resource => digests[harness.ResourceXid(resource)])
                .ToArray();
            await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            const int iterations = 32;
            int matched = 0;
#if NET8_0_OR_GREATER
            int thread = Environment.CurrentManagedThreadId;
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int i = 0; i < viewCount; i++)
                {
                    AttributeSimpleReadResult read = await harness.ReadMembershipDigestAsync(resources[i])
                        .ConfigureAwait(false);
                    if (ServiceResult.IsGood(read.Result) && read.Value.TryGetValue(out ByteString digest) &&
                        digest == expected[i])
                    {
                        matched++;
                    }
                }
            }
#if NET8_0_OR_GREATER
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread));
            TestContext.Out.WriteLine($"Digest polling: {iterations * viewCount} reads allocated {allocated} bytes.");
            Assert.That(allocated, Is.LessThanOrEqualTo(64 * 1024),
                "Warmed digest polling must not deserialize, rebuild or serialize the whole committed graph.");
#endif
            Assert.That(matched, Is.EqualTo(iterations * viewCount));
        }

        /// <summary>
        /// Verifies that stock membership digest does not hide an invalid committed image.
        /// </summary>
        /// <param name="unsupported">The unsupported flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StockMembershipDigestDoesNotHideAnInvalidCommittedImageAsync(bool unsupported)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            WotCommittedPublicationState committed = await harness.PublishPreparedGraphAsync(
                [harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], [])], [],
                new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
            AttributeSimpleReadResult valid = await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false);
            Assert.That(valid.Result, Is.EqualTo(ServiceResult.Good));
            ByteString invalid = ByteString.From(Encoding.UTF8.GetBytes(
                unsupported ? "{\"schemaVersion\":99}" : "{}"));
            IWotPreparedRegistryPublication metadata = await harness.Registry.PreparePublicationAsync(
                committed.RegistrySnapshot, [], committed.RefreshGeneration, invalid).ConfigureAwait(false);
            await using (metadata.ConfigureAwait(false))
            {
                await metadata.DecideAsync(CancellationToken.None).ConfigureAwait(false);
                metadata.Publish();
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (unsupported)
                {
                    await Assert.ThatAsync(async () =>
                        await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false),
                        Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(async () =>
                        await harness.ReadMembershipDigestAsync("child").ConfigureAwait(false),
                        Throws.TypeOf<FormatException>()).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verifies that stock membership digest concurrent first reads are coherent.
        /// </summary>
        [Test]
        public async Task StockMembershipDigestConcurrentFirstReadsAreCoherentAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            WotCommittedPublicationState committed = await harness.PublishPreparedGraphAsync(
                [
                    harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], []),
                    harness.GraphRequest("left", left, [Ua.VariableIds.Server_ServerStatus_CurrentTime], [])
                ], [], new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
            Dictionary<string, ByteString> digests = WotCanonicalViewState.Parse(
                committed.RegistrySnapshot.CanonicalViewGraphState).Views.ToList()
                .ToDictionary(view => view.ResourceXid, view => view.MembershipDigest, StringComparer.Ordinal);
            ByteString first = digests[harness.ResourceXid("child")];
            ByteString second = digests[harness.ResourceXid("left")];
            Assert.That(first, Is.Not.EqualTo(second));
            (int Index, AttributeSimpleReadResult Read)[] results = await Task.WhenAll(
                Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
                    (index, await harness.ReadMembershipDigestAsync(index % 2 == 0 ? "child" : "left")
                        .ConfigureAwait(false))))).ConfigureAwait(false);
            foreach ((int index, AttributeSimpleReadResult read) in results)
            {
                Assert.That(read.Result, Is.EqualTo(ServiceResult.Good));
                Assert.That(read.Value.TryGetValue(out ByteString digest), Is.True);
                Assert.That(digest, Is.EqualTo(index % 2 == 0 ? first : second));
            }
        }

        /// <summary>
        /// Verifies that prepared view without captured metadata is rejected before publication.
        /// </summary>
        [Test]
        public async Task PreparedViewWithoutCapturedMetadataIsRejectedBeforePublicationAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            ArrayOf<NodeManagerRegistration> registrations = harness.Registrations;
            WotRegistrySnapshot snapshot = harness.Registry.Current;
            var factory = new BlockingSourceFactory();
            var views = new Mock<IWotPreparedViewPublication>();
            ArrayOf<NodeManagerBatchChange> changes = [NodeManagerBatchChange.Add(factory)];
            views.SetupGet(value => value.Changes).Returns(changes);

            await Assert.ThatAsync(async () =>
            {
                IWotPreparedProjectionPublication prepared = await harness.SourceHost.PrepareAsync([], views.Object)
                    .ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
            }, Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);

            Assert.That(harness.Registrations, Is.EqualTo(registrations));
            Assert.That(harness.Registry.Current, Is.SameAs(snapshot));
            views.Verify(value => value.BindPreparedRegistrations(It.IsAny<ArrayOf<NodeManagerRegistration>>()),
                Times.Never);
            ReadResponse hidden = await harness.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = factory.Created.Identity("First"), AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(hidden.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Verifies that stock batched read keeps view token and digest in its captured generation.
        /// </summary>
        [Test]
        public async Task StockBatchedReadKeepsViewTokenAndDigestInItsCapturedGenerationAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            BlockingSource source = await harness.AddBlockingSourceAsync().ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId first = source.Identity("First");
            NodeId second = source.Identity("Second");
            WotCommittedPublicationState committed = await harness.PublishPreparedGraphAsync(
                [harness.GraphRequest("child", child, [first], [])], [],
                new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
            ByteString oldDigest = WotCanonicalViewState.Parse(
                committed.RegistrySnapshot.CanonicalViewGraphState).Views[0].MembershipDigest;
            NodeId token = await harness.ViewVersionIdAsync(child).ConfigureAwait(false);
            NodeId digest = harness.Target((await harness.BrowseAsync(
                harness.ResourceNodeId("child"), Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .ToList().Single(reference => reference.BrowseName ==
                    new QualifiedName("ProjectionMembershipDigest", harness.NamespaceIndex)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            async Task<ReadResponse> ReadImageAsync()
            {
                return await harness.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = first, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = token, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = digest, AttributeId = Attributes.Value }
                    ], timeout.Token).ConfigureAwait(false);
            }

            source.BlockNextValidation();
            Task<ReadResponse> pending = ReadImageAsync();
            Task<WotCommittedPublicationState>? publication = null;
            try
            {
                Task entered = await Task.WhenAny(source.Entered, pending).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(entered, Is.SameAs(source.Entered), "Read must pause after capturing generation one.");
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                publication = harness.PublishPreparedGraphAsync(
                    [harness.GraphRequest("child", child, [second], [])], [], committed,
                    () => published.TrySetResult(true));
                await Task.WhenAny(published.Task, publication).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (publication.IsCompleted)
                {
                    await publication.ConfigureAwait(false);
                }
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                ReadResponse current = await ReadImageAsync().ConfigureAwait(false);
                Assert.That(current.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
                Assert.That(current.Results[1].WrappedValue.TryGetValue(out uint newVersion), Is.True);
                Assert.That(newVersion, Is.EqualTo(2u));
                Assert.That(current.Results[2].WrappedValue.TryGetValue(out ByteString newDigest), Is.True);
                Assert.That(newDigest, Is.Not.EqualTo(oldDigest));
                Assert.That(newDigest, Is.EqualTo(WotCanonicalViewState.Parse(
                    harness.Registry.Current.CanonicalViewGraphState).Views[0].MembershipDigest));
            }
            finally
            {
                source.Resume();
                if (publication is not null)
                {
                    await publication.ConfigureAwait(false);
                }
            }
            ReadResponse captured = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(captured.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
            Assert.That(captured.Results[1].WrappedValue.TryGetValue(out uint oldVersion), Is.True);
            Assert.That(oldVersion, Is.EqualTo(1u));
            Assert.That(captured.Results[2].WrappedValue.TryGetValue(out ByteString capturedDigest), Is.True);
            Assert.That(capturedDigest, Is.EqualTo(oldDigest),
                "The retained Read must not combine generation one's token with generation two's membership digest.");
        }

        /// <summary>
        /// Verifies that native membership digest uses a custom captured read image.
        /// </summary>
        /// <param name="separateNamespace">The separate-namespace flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeMembershipDigestUsesACustomCapturedReadImageAsync(bool separateNamespace)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId resource = harness.ResourceNodeId("child");
            ByteString expected = ByteString.From(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
            var factory = new CapturedDigestFactory(
                resource, expected, separateNamespace ? "urn:c2:custom-views" : Namespaces.WotCon);
            await harness.Lifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            NodeId property = harness.Target((await harness.BrowseAsync(resource, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).ToList().Single(reference => reference.BrowseName ==
                    new QualifiedName("ProjectionMembershipDigest", harness.NamespaceIndex)));

            DataValue value = await harness.Session.ReadValueAsync(property).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ByteString actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that captured digest read does not cross initial publication or retirement.
        /// </summary>
        /// <param name="retire">The retire flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedDigestReadDoesNotCrossInitialPublicationOrRetirementAsync(bool retire)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            BlockingSource source = await harness.AddBlockingSourceAsync().ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId first = source.Identity("First");
            NodeId second = source.Identity("Second");
            WotCommittedPublicationState committed = new(harness.Registry.Current);
            ByteString oldDigest = default;
            if (retire)
            {
                committed = await harness.PublishPreparedGraphAsync(
                    [harness.GraphRequest("child", child, [first], [])], [], committed).ConfigureAwait(false);
                oldDigest = WotCanonicalViewState.Parse(
                    committed.RegistrySnapshot.CanonicalViewGraphState).Views[0].MembershipDigest;
            }
            NodeId property = harness.Target((await harness.BrowseAsync(
                harness.ResourceNodeId("child"), Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .ToList().Single(reference => reference.BrowseName ==
                    new QualifiedName("ProjectionMembershipDigest", harness.NamespaceIndex)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            async Task<ReadResponse> ReadImageAsync()
            {
                return await harness.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = first, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = property, AttributeId = Attributes.Value }
                    ], timeout.Token).ConfigureAwait(false);
            }

            source.BlockNextValidation();
            Task<ReadResponse> pending = ReadImageAsync();
            Task<WotCommittedPublicationState>? publication = null;
            try
            {
                Task entered = await Task.WhenAny(source.Entered, pending).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(entered, Is.SameAs(source.Entered));
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                publication = harness.PublishPreparedGraphAsync(
                    retire ? [] : [harness.GraphRequest("child", child, [second], [])],
                    retire ? committed.Views : [], committed, () => published.TrySetResult(true));
                await Task.WhenAny(published.Task, publication).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (publication.IsCompleted)
                {
                    await publication.ConfigureAwait(false);
                }
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                ReadResponse current = await ReadImageAsync().ConfigureAwait(false);
                Assert.That(current.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Results[1].StatusCode, Is.EqualTo(
                    retire ? StatusCodes.BadWaitingForInitialData : StatusCodes.Good));
                if (!retire)
                {
                    Assert.That(current.Results[1].WrappedValue.TryGetValue(out ByteString digest), Is.True);
                    Assert.That(digest, Is.EqualTo(WotCanonicalViewState.Parse(
                        harness.Registry.Current.CanonicalViewGraphState).Views[0].MembershipDigest));
                }
            }
            finally
            {
                source.Resume();
                if (publication is not null)
                {
                    await publication.ConfigureAwait(false);
                }
            }
            ReadResponse captured = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(captured.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(captured.Results[1].StatusCode, Is.EqualTo(
                retire ? StatusCodes.Good : StatusCodes.BadWaitingForInitialData));
            if (retire)
            {
                Assert.That(captured.Results[1].WrappedValue.TryGetValue(out ByteString digest), Is.True);
                Assert.That(digest, Is.EqualTo(oldDigest));
            }
        }

        /// <summary>
        /// Verifies that stock canonical switch keeps an in flight browse on its captured view.
        /// </summary>
        [Test]
        public async Task StockCanonicalSwitchKeepsAnInFlightBrowseOnItsCapturedViewAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            BlockingSource source = await harness.AddBlockingSourceAsync().ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId first = source.Identity("First");
            NodeId second = source.Identity("Second");
            NodeId third = source.Identity("Third");
            WotCommittedPublicationState committed = await harness.PublishPreparedGraphAsync(
                [harness.GraphRequest("child", child, [first, second], [])], [],
                new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            source.BlockNextValidation();
            Task<BrowseResult> pending = harness.BrowseInViewAsync(child, child, 1);
            Task<WotCommittedPublicationState>? publication = null;
            try
            {
                Task entered = await Task.WhenAny(source.Entered, pending).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(entered, Is.SameAs(source.Entered), "The native Browse must reach its blocked source owner.");
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                publication = harness.PublishPreparedGraphAsync(
                    [harness.GraphRequest("child", child, [second, third], [])], [], committed,
                    () => published.TrySetResult(true));
                await Task.WhenAny(published.Task, publication).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (publication.IsCompleted)
                {
                    await publication.ConfigureAwait(false);
                }
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                BrowseResult current = await harness.BrowseInViewAsync(child, child, 2).ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.References.ToList().Select(harness.Target),
                    Is.EquivalentTo(new[] { second, third }));
                Assert.That((await harness.BrowseInViewAsync(child, child, 1).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadViewVersionInvalid));
            }
            finally
            {
                source.Resume();
                if (publication is not null)
                {
                    await publication.ConfigureAwait(false);
                }
            }
            BrowseResult captured = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(captured.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(captured.References.ToList().Select(reference =>
                (harness.Target(reference), reference.NodeClass, reference.TypeDefinition)),
                Is.EquivalentTo(new[]
                {
                    (first, NodeClass.Variable, new ExpandedNodeId(Ua.VariableTypeIds.BaseDataVariableType)),
                    (second, NodeClass.Variable, new ExpandedNodeId(Ua.VariableTypeIds.BaseDataVariableType))
                }));
        }

        /// <summary>
        /// Verifies that stock retirement persists history for native cold materialization.
        /// </summary>
        /// <param name="immediate">The immediate flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StockRetirementPersistsHistoryForNativeColdMaterializationAsync(bool immediate)
        {
            ByteString payload;
            string logicalServer;
            ExpandedNodeId childIdentity;
            ExpandedNodeId wrapperIdentity;
            ushort oldNamespace;
            await using (NativeHarness harness = await NativeHarness.CreateAsync(
                withGraphResources: true, retirementPolicy: immediate
                    ? WotProjectionRetirementPolicy.Immediate : WotProjectionRetirementPolicy.Graceful)
                .ConfigureAwait(false))
            {
                NodeId child = harness.Identity("urn:c2:native-views", "Child");
                NodeId left = harness.Identity("urn:c2:native-views", "Left");
                NodeId right = harness.Identity("urn:c2:native-views", "Right");
                NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
                oldNamespace = child.NamespaceIndex;
                logicalServer = harness.LogicalServerUri;
                WotCommittedPublicationState initial = await harness.PublishPreparedGraphAsync(
                    [
                        harness.GraphRequest("child", child, [reading], []),
                        harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")]),
                        harness.GraphRequest("right", right, [], [new(harness.ResourceXid("child"), "Group")])
                    ], [], new WotCommittedPublicationState(harness.Registry.Current)).ConfigureAwait(false);
                var context = new WotCanonicalViewGraphContext(
                    logicalServer, Namespaces.WotCon, harness.NamespaceUris, [new(reading, NodeClass.Variable)]);
                WotCanonicalViewState graph = WotCanonicalViewState.Restore(
                    initial.RegistrySnapshot.CanonicalViewGraphState, context);
                childIdentity = graph.Views.ToList().Single(view =>
                    view.ResourceXid == harness.ResourceXid("child")).ViewNodeId;
                wrapperIdentity = graph.Nodes.ToList().Single(node =>
                    node.ResourceXid == harness.ResourceXid("right") && node.Role == WotCanonicalViewNodeRole.Group).NodeId;
                using var foreign = new LifecycleWotViewProjectionHost(harness.Lifecycle);
                await Assert.ThatAsync(async () => await foreign.PrepareAsync([], [], initial).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState))
                    .ConfigureAwait(false);
                WotViewProjectionHandle owned = initial.Views[0];
                var forged = new WotViewProjectionHandle(owned.ResourceXid, owned.ViewNodeId, owned.MaterializedNodeCount);
                await Assert.ThatAsync(async () => await harness.Views.PrepareAsync(
                    [], [forged], initial).ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);

                WotCommittedPublicationState remaining = await harness.PublishPreparedGraphAsync(
                    [], initial.Views.ToList().Where(handle => handle.ResourceXid != harness.ResourceXid("right"))
                        .ToArrayOf(), initial).ConfigureAwait(false);

                Assert.That(remaining.Views.ToList().Select(handle => handle.ResourceXid),
                    Is.EquivalentTo(new[] { harness.ResourceXid("child"), harness.ResourceXid("right") }));
                Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
                Assert.That((await harness.BrowseAsync(right, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false))
                    .ToList().Select(harness.Target),
                    Is.EqualTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, harness.NamespaceUris) }));
                Assert.That(await harness.BrowseAsync(harness.ResourceNodeId("left"),
                    ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris))
                    .ConfigureAwait(false), Is.Empty);
                WotCommittedPublicationState retired = await harness.PublishPreparedGraphAsync(
                    [], remaining.Views.ToList().Where(handle => handle.ResourceXid == harness.ResourceXid("right"))
                        .ToArrayOf(), remaining).ConfigureAwait(false);
                Assert.That(retired.Views.IsEmpty, Is.True);
                payload = retired.RegistrySnapshot.CanonicalViewGraphState;
                WotCanonicalViewState history = WotCanonicalViewState.Restore(payload, context);
                Assert.That(history.Views.ToList().Select(view =>
                    (view.ResourceXid, view.Active, view.Requested, view.ViewVersion)),
                    Is.EquivalentTo(new[]
                    {
                        (harness.ResourceXid("child"), false, false, 1u),
                        (harness.ResourceXid("left"), false, false, 1u),
                        (harness.ResourceXid("right"), false, false, 1u)
                    }));
                Assert.That(await harness.LoadDurableGraphAsync().ConfigureAwait(false), Is.EqualTo(payload));
                Assert.That((await harness.Session.ReadValueAsync(reading).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            await using NativeHarness restarted = await NativeHarness.CreateAsync(
                withGraphResources: true, rebaseNamespaces: true).ConfigureAwait(false);
            NodeId revivedChild = restarted.Identity("urn:c2:native-views", "Child");
            NodeId revivedRight = restarted.Identity("urn:c2:native-views", "Right");
            Assert.That(revivedChild.NamespaceIndex, Is.Not.EqualTo(oldNamespace));
            var restoredContext = new WotCanonicalViewGraphContext(logicalServer, Namespaces.WotCon,
                restarted.NamespaceUris, [new(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
            WotCanonicalViewState restored = WotCanonicalViewState.Restore(payload, restoredContext);
            WotCanonicalViewState revived = WotProjectionViewBuilder.PrepareCanonicalGraph(
                restoredContext, restored,
                [restarted.GraphRequest("right", revivedRight, [], [new(restarted.ResourceXid("child"), "Group")])],
                []).State;

            await restarted.PublishCanonicalAsync(revived).ConfigureAwait(false);

            Assert.That(revived.Views.ToList().Single(view =>
                view.ResourceXid == restarted.ResourceXid("child")).ViewNodeId, Is.EqualTo(childIdentity));
            Assert.That(await restarted.ReadViewVersionAsync(revivedChild).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That(await restarted.ReadViewVersionAsync(revivedRight).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That((await restarted.BrowseAsync(revivedRight, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false))
                .ToList().Select(restarted.Target),
                Is.EqualTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, restarted.NamespaceUris) }));
        }

        /// <summary>
        /// Verifies that canonical candidate retirement and restart retain shared identity.
        /// </summary>
        [Test]
        public async Task CanonicalCandidateRetirementAndRestartRetainSharedIdentityAsync()
        {
            ByteString payload;
            ExpandedNodeId childIdentity;
            ExpandedNodeId wrapperIdentity;
            ushort originalNamespace;
            await using (NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false))
            {
                NodeId child = harness.Identity("urn:c2:native-views", "Child");
                NodeId left = harness.Identity("urn:c2:native-views", "Left");
                NodeId right = harness.Identity("urn:c2:native-views", "Right");
                originalNamespace = child.NamespaceIndex;
                var context = harness.GraphContext(
                    [new(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
                WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                    [
                        harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], []),
                        harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")]),
                        harness.GraphRequest("right", right, [], [new(harness.ResourceXid("child"), "Group")])
                    ], []).State;
                childIdentity = initial.Views.ToArray()!.Single(view =>
                    view.ResourceXid == harness.ResourceXid("child")).ViewNodeId;
                wrapperIdentity = initial.Nodes.ToArray()!.Single(node =>
                    node.ResourceXid == harness.ResourceXid("right") &&
                    node.Role == WotCanonicalViewNodeRole.Group).NodeId;
                NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
                WotCanonicalViewState remaining = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                    [], [harness.ResourceXid("child"), harness.ResourceXid("left")]).State;

                registration = await harness.PublishCanonicalAsync(remaining, registration).ConfigureAwait(false);

                ArrayOf<ReferenceDescription> visible = await harness.BrowseAsync(
                    Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Contain(child).And.Contain(right));
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(left));
                Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
                Assert.That(await harness.ReadViewVersionAsync(right).ConfigureAwait(false), Is.EqualTo(1u));
                ArrayOf<ReferenceDescription> wrappers = await harness.BrowseAsync(right, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false);
                Assert.That(wrappers.ToArray()!.Select(harness.Target),
                    Is.EquivalentTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, harness.NamespaceUris) }));
                WotCanonicalViewState retired = WotProjectionViewBuilder.PrepareCanonicalGraph(
                    context, remaining, [], [harness.ResourceXid("right")]).State;
                await harness.PublishCanonicalAsync(retired, registration).ConfigureAwait(false);
                visible = await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false);
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(child));
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(right));
                Assert.That(retired.Views, Has.Count.EqualTo(3));
                Assert.That(retired.Views.ToArray()!.All(view => !view.Active && view.ViewVersion == 1), Is.True);
                NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
                Assert.That(await harness.BrowseAsync(harness.ResourceNodeId("child"), hasProjection)
                    .ConfigureAwait(false), Is.Empty);
                DataValue source = await harness.Session.ReadValueAsync(Ua.VariableIds.Server_ServerStatus_StartTime)
                    .ConfigureAwait(false);
                Assert.That(source.StatusCode, Is.EqualTo(StatusCodes.Good));
                payload = retired.ToByteString();
            }
            await using NativeHarness restarted = await NativeHarness.CreateAsync(
                withGraphResources: true, rebaseNamespaces: true).ConfigureAwait(false);
            NodeId restartedChild = restarted.Identity("urn:c2:native-views", "Child");
            NodeId restartedRight = restarted.Identity("urn:c2:native-views", "Right");
            Assert.That(restartedChild.NamespaceIndex, Is.Not.EqualTo(originalNamespace));
            var restoredContext = restarted.GraphContext(
                [new(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
            WotCanonicalViewState restored = WotCanonicalViewState.Restore(payload, restoredContext);
            WotCanonicalViewState revived = WotProjectionViewBuilder.PrepareCanonicalGraph(restoredContext, restored,
                [restarted.GraphRequest("right", restartedRight, [], [new(restarted.ResourceXid("child"), "Group")])],
                []).State;

            await restarted.PublishCanonicalAsync(revived).ConfigureAwait(false);

            Assert.That(revived.Views.ToArray()!.Single(view =>
                view.ResourceXid == restarted.ResourceXid("child")).ViewNodeId, Is.EqualTo(childIdentity));
            Assert.That(await restarted.ReadViewVersionAsync(restartedChild).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That(await restarted.ReadViewVersionAsync(restartedRight).ConfigureAwait(false), Is.EqualTo(1u));
            ArrayOf<ReferenceDescription> restoredWrappers = await restarted.BrowseAsync(
                restartedRight, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(restoredWrappers.ToArray()!.Select(restarted.Target),
                Is.EquivalentTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, restarted.NamespaceUris) }));
        }

        /// <summary>
        /// Verifies that canonical candidate rejects source mismatch without publishing.
        /// </summary>
        /// <param name="wrongClass">The wrong-class flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalCandidateRejectsSourceMismatchWithoutPublishingAsync(bool wrongClass)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            WotViewProjectionRequest request = harness.GraphRequest("child", child, [reading], []);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([new(reading, NodeClass.Variable)]), null, [request], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            WotCanonicalViewState candidate = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([new(reading, wrongClass ? NodeClass.Object : NodeClass.Variable, wrongClass)]),
                initial, [], []).State;
            ServiceResultException? rejection = null;

            try
            {
                await harness.PublishCanonicalAsync(candidate, registration).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.StatusCode,
                Is.EqualTo(wrongClass ? StatusCodes.BadNodeClassInvalid : StatusCodes.BadNodeIdUnknown));
            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(child, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            Assert.That(members.ToArray()!.Select(harness.Target), Is.EquivalentTo(new[] { reading }));
        }

        /// <summary>
        /// Verifies that canonical factory rejects an unrelated previous registration.
        /// </summary>
        [Test]
        public async Task CanonicalFactoryRejectsAnUnrelatedPreviousRegistrationAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null,
                [harness.GraphRequest("child", harness.ResourceNodeId("right"), [], [])], []).State;

            Assert.That(() => WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(
                state, harness.RegistryRegistration), Throws.ArgumentException);

            Assert.That(harness.IsCurrent(harness.RegistryRegistration), Is.True);
            ReadResponse read = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = harness.ResourceNodeId("right"), AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object));
        }

        /// <summary>
        /// Verifies that canonical predecessor cannot authorize an additional owner.
        /// </summary>
        /// <param name="batch">The batch flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalPredecessorCannotAuthorizeAnAdditionalOwnerAsync(bool batch)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null, [harness.GraphRequest("child", child, [], [])], []).State;
            NodeManagerRegistration previous = await harness.PublishCanonicalAsync(state).ConfigureAwait(false);
            ArrayOf<NodeManagerRegistration> owners = harness.Registrations;
            int namespaceCount = harness.NamespaceUris.Count;

            await Assert.ThatAsync(async () =>
            {
                if (batch)
                {
                    await harness.PrepareCanonicalAdditionAsync(state, previous).ConfigureAwait(false);
                }
                else
                {
                    await harness.AddCanonicalAsync(state, previous).ConfigureAwait(false);
                }
            }, Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(harness.Registrations.ToList(), Is.EqualTo(owners.ToList()));
            Assert.That(harness.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
        }

        /// <summary>
        /// Verifies that canonical factory rejects another allocation authority.
        /// </summary>
        /// <param name="differentServer">The different-server flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalFactoryRejectsAnotherAllocationAuthorityAsync(bool differentServer)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            WotViewProjectionRequest request = harness.GraphRequest("child", child, [], []);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null, [request], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            var foreignContext = new WotCanonicalViewGraphContext(
                differentServer ? "urn:c2:foreign-server" : "urn:c2:native-server",
                differentServer ? "urn:c2:native-allocation" : "urn:c2:foreign-allocation",
                harness.NamespaceUris, []);
            WotCanonicalViewState foreign = WotProjectionViewBuilder.PrepareCanonicalGraph(
                foreignContext, null, [request], []).State;
            int namespaceCount = harness.NamespaceUris.Count;

            Assert.That(() => WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(foreign, registration),
                Throws.ArgumentException);

            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(harness.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
        }

        /// <summary>
        /// Verifies that canonical factory rejects a previous owner from another server instance.
        /// </summary>
        [Test]
        public async Task CanonicalFactoryRejectsAPreviousOwnerFromAnotherServerInstanceAsync()
        {
            await using NativeHarness original = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = original.Identity("urn:c2:native-views", "Child");
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                original.GraphContext([]), null, [original.GraphRequest("child", child, [], [])], []).State;
            NodeManagerRegistration registration = await original.PublishCanonicalAsync(state).ConfigureAwait(false);
            await using NativeHarness foreign = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId foreignChild = foreign.Identity("urn:c2:native-views", "Child");
            int namespaceCount = foreign.NamespaceUris.Count;

            await Assert.ThatAsync(async () => await foreign.AddCanonicalAsync(
                state, registration).ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(original.IsCurrent(registration), Is.True);
            Assert.That(foreign.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            ReadResponse read = await foreign.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = foreignChild, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(await original.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
        }

        /// <summary>
        /// Verifies that canonical candidate cannot reassign a previous managers node.
        /// </summary>
        /// <param name="changeRole">The change-role flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalCandidateCannotReassignAPreviousManagersNodeAsync(bool changeRole)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            var context = harness.GraphContext([]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [
                    harness.GraphRequest("child", child, [], []),
                    harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")])
                ], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            NodeId occupied = changeRole
                ? ExpandedNodeId.ToNodeId(initial.Nodes.ToArray()!.Single(
                    node => node.Role == WotCanonicalViewNodeRole.Group).NodeId, harness.NamespaceUris)
                : child;
            WotCanonicalViewState candidate = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null, [harness.GraphRequest("right", occupied, [], [])], []).State;
            ServiceResultException? rejection = null;

            try
            {
                await harness.PublishCanonicalAsync(candidate, registration).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That(await harness.ReadViewVersionAsync(left).ConfigureAwait(false), Is.EqualTo(1u));
        }

        private static WotViewProjectionPlan LegacyPlan(uint version)
        {
            return new WotViewProjectionPlan(
                "urn:canonical-view-test", WotDocumentKind.ThingDescription, [], [], version, []);
        }

        private sealed class NativeHarness : IAsyncDisposable
        {
            private NativeHarness()
            {
                m_directory = Path.Combine(
                    Path.GetTempPath(), "UaWotC2", Guid.NewGuid().ToString("N"));
                m_storage = new PreparedWotTestStorage(Path.Combine(m_directory, "registry"));
                m_fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    AutoAccept = true,
                    SecurityNone = false
                };
            }

            /// <summary>
            /// Gets or sets the session for <c>NativeHarness</c>.
            /// </summary>
            public ISession Session { get; private set; } = null!;

            /// <summary>
            /// Gets or sets the views for <c>NativeHarness</c>.
            /// </summary>
            public LifecycleWotViewProjectionHost Views { get; private set; } = null!;

            /// <summary>
            /// Gets or sets the namespace index for <c>NativeHarness</c>.
            /// </summary>
            public ushort NamespaceIndex { get; private set; }

            /// <summary>
            /// Gets the namespace URIs for <c>NativeHarness</c>.
            /// </summary>
            public NamespaceTable NamespaceUris => m_server!.CurrentInstance.NamespaceUris;

            public string LogicalServerUri => m_server!.CurrentInstance.ServerUris.GetString(0)!;

            /// <summary>
            /// Gets the lifecycle for <c>NativeHarness</c>.
            /// </summary>
            public INodeManagerLifecycle Lifecycle => m_server!.NodeManagerLifecycle;

            public WotRegistryService Registry => m_registry!;

            public LifecycleWotProjectionHost SourceHost => new(m_server!.NodeManagerLifecycle);

            /// <summary>
            /// Gets or sets the registry registration for <c>NativeHarness</c>.
            /// </summary>
            public NodeManagerRegistration RegistryRegistration { get; private set; } = null!;

            /// <summary>
            /// Gets the registrations for <c>NativeHarness</c>.
            /// </summary>
            public ArrayOf<NodeManagerRegistration> Registrations => m_server!.NodeManagerLifecycle.Registrations;

            /// <summary>
            /// Creates a harness, starts the native server and registry, and disposes it again if startup fails.
            /// </summary>
            /// <param name="withGraphResources">The with-graph-resources flag supplied by the caller.</param>
            /// <param name="rebaseNamespaces">The rebase-namespaces flag supplied by the caller.</param>
            /// <param name="retirementPolicy">The retirement policy.</param>
            public static async Task<NativeHarness> CreateAsync(
                bool withGraphResources = false, bool rebaseNamespaces = false,
                WotProjectionRetirementPolicy retirementPolicy = WotProjectionRetirementPolicy.Graceful)
            {
                var harness = new NativeHarness();
                try
                {
                    await harness.StartAsync(withGraphResources, rebaseNamespaces, retirementPolicy).ConfigureAwait(false);
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Creates a node identifier in the given namespace, registering the namespace when needed.
            /// </summary>
            /// <param name="namespaceUri">The namespace URI.</param>
            /// <param name="identifier">The string identifier of the node.</param>
            public NodeId Identity(string namespaceUri, string identifier)
            {
                return new NodeId(
                    identifier, m_server!.CurrentInstance.NamespaceUris.GetIndexOrAppend(namespaceUri));
            }

            public string ResourceXid(string resource)
            {
                return m_registry!.Current.FindResource(WotRegistryGroups.ThingDescriptions, resource)!.Xid;
            }

            public NodeId ResourceNodeId(string resource, bool thingModel = false)
            {
                string group = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions;
                return new NodeId("WoTRegistry/groups/" + group + "/resources/" + resource, NamespaceIndex);
            }

            public WotCanonicalViewGraphContext GraphContext(ArrayOf<WotCanonicalViewSource> sources)
            {
                return new WotCanonicalViewGraphContext(
                    "urn:c2:native-server", "urn:c2:native-allocation", NamespaceUris, sources);
            }

            /// <summary>
            /// Builds a canonical view projection request for the resource with the given members and links.
            /// </summary>
            /// <param name="resource">The identifier of the registry resource.</param>
            /// <param name="view">The view.</param>
            /// <param name="members">The nodes that belong to the view.</param>
            /// <param name="links">The canonical view links.</param>
            public WotViewProjectionRequest GraphRequest(
                string resource, NodeId view, ArrayOf<NodeId> members, ArrayOf<WotCanonicalViewLink> links)
            {
                return new WotViewProjectionRequest(
                    "closure", ResourceXid(resource), ResourceNodeId(resource), view,
                    WotViewProjectionPlan.CreateCanonical("urn:c2:scenario", WotDocumentKind.ThingDescription,
                        members, links));
            }

            public async Task<BlockingSource> AddBlockingSourceAsync()
            {
                var factory = new BlockingSourceFactory();
                await Lifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
                return factory.Created;
            }

            /// <summary>
            /// Adds a Thing Description or Thing Model to the registry and publishes its registry node.
            /// </summary>
            /// <param name="resourceId">The registry resource identifier.</param>
            /// <param name="json">The document JSON.</param>
            /// <param name="thingModel">Whether to register a Thing Model rather than a Thing Description.</param>
            public async Task RegisterTdAsync(string resourceId, string json, bool thingModel = false)
            {
                WotRegistryMutationResult result = await Registry.UpsertResourceAsync(
                    new WotUpsertResourceRequest
                    {
                        GroupId = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                        ResourceId = resourceId,
                        Kind = thingModel ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                        Format = thingModel ? "WoT-TM/1.1" : "WoT-TD/1.1",
                        ContentType = thingModel ? "application/tm+json" : "application/td+json",
                        Content = ByteString.From(Encoding.UTF8.GetBytes(json))
                    }).ConfigureAwait(false);
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
                await ((WotRegistryNodeManager)RegistryRegistration.NodeManager)
                    .DispatchProjectionAsync(_ => default, CancellationToken.None).ConfigureAwait(false);
            }

            /// <summary>
            /// Refreshes the registered materialization closure.
            /// </summary>
            /// <param name="dryRun">Whether to prepare without publishing.</param>
            public ValueTask<WotRefreshResult> RefreshAsync(bool dryRun = false)
            {
                return m_coordinator!.RefreshAsync(new WotRefreshRequest
                {
                    Options = new WoTRefreshOptionsDataType
                    {
                        Atomicity = WoTAtomicityEnum.PerRegistry,
                        DryRun = dryRun
                    }
                });
            }

            /// <summary>
            /// Reads the projection membership digest property of the registry document for the resource.
            /// </summary>
            /// <param name="resource">The identifier of the registry resource.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<AttributeSimpleReadResult> ReadMembershipDigestAsync(
                string resource, CancellationToken cancellationToken = default)
            {
                var manager = (WotRegistryNodeManager)RegistryRegistration.NodeManager;
                WoTDocumentState document = manager.FindPredefinedNode<WoTDocumentState>(ResourceNodeId(resource));
                PropertyState<ByteString> property = document.ProjectionMembershipDigest!;
                return property.OnSimpleReadValueAsync!(manager.SystemContext, property, cancellationToken);
            }

            public async Task<ByteString> LoadDurableGraphAsync()
            {
                using FileWotRegistryStore observer = m_storage.OpenStore();
                WotRegistrySnapshot snapshot = await observer.LoadAsync().ConfigureAwait(false);
                return snapshot.CanonicalViewGraphState;
            }

            /// <summary>
            /// Prepares and commits a view graph publication together with matching registry metadata.
            /// </summary>
            /// <param name="updates">The view projections to add or update.</param>
            /// <param name="removals">The view projections to remove.</param>
            /// <param name="expected">The WoT committed publication state.</param>
            /// <param name="onPublished">The on published.</param>
            public async Task<WotCommittedPublicationState> PublishPreparedGraphAsync(
                ArrayOf<WotViewProjectionRequest> updates, ArrayOf<WotViewProjectionHandle> removals,
                WotCommittedPublicationState expected, Action? onPublished = null)
            {
                IWotPreparedViewPublication candidate = await Views.PrepareAsync(updates, removals, expected)
                    .ConfigureAwait(false);
                await using (candidate.ConfigureAwait(false))
                {
                    IWotPreparedProjectionPublication prepared = await SourceHost.PrepareAsync([], candidate)
                        .ConfigureAwait(false);
                    await using (prepared.ConfigureAwait(false))
                    {
                        WotPreparedViewGraphState graph = prepared.ViewGraph!;
                        uint generation = expected.RefreshGeneration + 1;
                        var projections = new List<WotResourceProjection>();
                        foreach (string xid in graph.AffectedResourceXids)
                        {
                            WotResource resource = Registry.Current.FindResourceByXid(xid)!;
                            WotViewProjectionHandle? handle = graph.Views.ToList()
                                .SingleOrDefault(view => view.ResourceXid == xid);
                            projections.Add(new WotResourceProjection(
                                resource.GroupId, resource.ResourceId,
                                handle is null ? WoTLoadStateEnum.Unloaded : WoTLoadStateEnum.Active,
                                handle is null ? null : resource.DefaultVersionId, generation,
                                handle?.MaterializedNodeCount ?? 0, handle is null ? NodeId.Null : handle.ViewNodeId,
                                resource.Validation, resource.Diagnostics, DateTime.UtcNow));
                        }
                        IWotPreparedRegistryPublication metadata = await Registry.PreparePublicationAsync(
                            expected.RegistrySnapshot, projections.ToArrayOf(), generation, graph.CanonicalViewGraphState)
                            .ConfigureAwait(false);
                        await using (metadata.ConfigureAwait(false))
                        {
                            prepared.BindReadImages(metadata.ReadImages);
                            var committed = new WotCommittedPublicationState(metadata.IntendedSnapshot, graph.Views);
                            await prepared.CommitAsync(metadata.DecideAsync, () =>
                            {
                                candidate.OnPublished(committed);
                                metadata.Publish();
                                onPublished?.Invoke();
                            }).ConfigureAwait(false);
                            Assert.That(prepared.CleanupFailure, Is.Null);
                            return committed;
                        }
                    }
                }
            }

            /// <summary>
            /// Publishes the canonical view node manager by adding it or shadow-reloading the previous one.
            /// </summary>
            /// <param name="state">The WoT canonical view state.</param>
            /// <param name="previous">The node manager registration.</param>
            public async Task<NodeManagerRegistration> PublishCanonicalAsync(
                WotCanonicalViewState state, NodeManagerRegistration? previous = null)
            {
                IAsyncNodeManagerFactory factory = WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous);
                return previous is null
                    ? await m_server!.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false)
                    : await m_server!.NodeManagerLifecycle.ShadowReloadAsync(previous, factory).ConfigureAwait(false);
            }

            /// <summary>
            /// Adds the canonical view node manager as an additional registration.
            /// </summary>
            /// <param name="state">The WoT canonical view state.</param>
            /// <param name="previous">The node manager registration.</param>
            public ValueTask<NodeManagerRegistration> AddCanonicalAsync(
                WotCanonicalViewState state, NodeManagerRegistration previous)
            {
                return m_server!.NodeManagerLifecycle.AddAsync(
                    WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous), callerContext: null);
            }

            /// <summary>
            /// Prepares a batch that adds the canonical view node manager and disposes it without committing.
            /// </summary>
            /// <param name="state">The WoT canonical view state.</param>
            /// <param name="previous">The node manager registration.</param>
            public async Task PrepareCanonicalAdditionAsync(
                WotCanonicalViewState state, NodeManagerRegistration previous)
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server!.NodeManagerLifecycle;
                IAsyncNodeManagerFactory factory = WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(
                    state, previous);
                IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)]).ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Replaces the previous canonical view node manager through a prepared batch and commits it.
            /// </summary>
            /// <param name="state">The WoT canonical view state.</param>
            /// <param name="previous">The node manager registration.</param>
            /// <param name="immediate">The immediate flag supplied by the caller.</param>
            public async Task PublishCanonicalBatchAsync(
                WotCanonicalViewState state, NodeManagerRegistration previous, bool immediate)
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server!.NodeManagerLifecycle;
                IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(previous,
                        WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous), immediate)])
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }

            /// <summary>
            /// Determines whether the registration is still registered with the node manager lifecycle.
            /// </summary>
            /// <param name="registration">The node manager registration.</param>
            public bool IsCurrent(NodeManagerRegistration registration)
            {
                return m_server!.NodeManagerLifecycle.Registrations.ToArray()!.Any(
                    current => ReferenceEquals(current, registration));
            }

            /// <summary>
            /// Browses the organized children of the node through the view at the given version.
            /// </summary>
            /// <param name="nodeId">The node identifier.</param>
            /// <param name="viewId">The view identifier.</param>
            /// <param name="version">The view version used for the browse.</param>
            /// <param name="maximumReferences">The maximum references.</param>
            public async Task<BrowseResult> BrowseInViewAsync(
                NodeId nodeId, NodeId viewId, uint version, uint maximumReferences = 0)
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, new ViewDescription { ViewId = viewId, ViewVersion = version }, maximumReferences,
                    [
                        new BrowseDescription
                        {
                            NodeId = nodeId,
                            ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                            BrowseDirection = BrowseDirection.Forward,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                return response.Results[0];
            }

            /// <summary>
            /// Resolves the target node identifier of a reference description.
            /// </summary>
            /// <param name="reference">The reference description.</param>
            public NodeId Target(ReferenceDescription reference)
            {
                return ExpandedNodeId.ToNodeId(reference.NodeId, NamespaceUris);
            }

            /// <summary>
            /// Browses the references of the given type from the node and returns them.
            /// </summary>
            /// <param name="nodeId">The node identifier.</param>
            /// <param name="referenceType">The reference type.</param>
            /// <param name="direction">The browse direction.</param>
            public async Task<ArrayOf<ReferenceDescription>> BrowseAsync(
                NodeId nodeId, NodeId referenceType, BrowseDirection direction = BrowseDirection.Forward)
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = nodeId,
                            ReferenceTypeId = referenceType,
                            IncludeSubtypes = false,
                            BrowseDirection = direction,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return response.Results[0].References;
            }

            /// <summary>
            /// Reads the view version property value of the view.
            /// </summary>
            /// <param name="viewId">The view identifier.</param>
            public async Task<uint> ReadViewVersionAsync(NodeId viewId)
            {
                NodeId propertyId = await ViewVersionIdAsync(viewId).ConfigureAwait(false);
                DataValue value = await Session.ReadValueAsync(propertyId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out uint version), Is.True);
                return version;
            }

            /// <summary>
            /// Finds the node identifier of the view version property of the view.
            /// </summary>
            /// <param name="viewId">The view identifier.</param>
            public async Task<NodeId> ViewVersionIdAsync(NodeId viewId)
            {
                BrowseResponse browse = await Session.BrowseAsync(
                    null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = viewId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(browse.Results, Has.Count.EqualTo(1));
                Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                ReferenceDescription[] references = browse.Results[0].References.ToArray()
                    ?? throw new InvalidOperationException("The native View returned no property references.");
                ReferenceDescription property = references.Single(
                    reference => reference.BrowseName.Name == "ViewVersion");
                return ExpandedNodeId.ToNodeId(property.NodeId, Session.NamespaceUris);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (Session is not null)
                    {
                        await Session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        Session?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (m_client is not null)
                            {
                                await m_client.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                Views?.Dispose();
                            }
                            finally
                            {
                                try
                                {
                                    await m_fixture.StopAsync().ConfigureAwait(false);
                                }
                                finally
                                {
                                    try
                                    {
                                        m_coordinator?.Dispose();
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            m_registry?.Dispose();
                                        }
                                        finally
                                        {
                                            m_store?.Dispose();
                                            m_storage.Dispose();
                                            try
                                            {
                                                m_server?.Dispose();
                                            }
                                            finally
                                            {
                                                if (Directory.Exists(m_directory))
                                                {
                                                    Directory.Delete(m_directory, recursive: true);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private async Task StartAsync(
                bool withGraphResources, bool rebaseNamespaces, WotProjectionRetirementPolicy retirementPolicy)
            {
                TestContext.Out.WriteLine(
                    $"C2_RUNTIME framework={RuntimeInformation.FrameworkDescription};clr={Environment.Version};" +
                    $"serverGC={GCSettings.IsServerGC};architecture={RuntimeInformation.ProcessArchitecture}");
                m_server = await m_fixture.StartAsync(m_directory).ConfigureAwait(false);
                if (rebaseNamespaces)
                {
                    m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:c2:restart-padding");
                }
                m_store = m_storage.OpenStore();
                Assert.That(m_store.SupportsPreparedCommits, Is.True);
                m_registry = new WotRegistryService(m_store);
                await m_registry.InitializeAsync().ConfigureAwait(false);
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                    documentConverter: withGraphResources ? new FakeWotDocumentConverter() : null,
                    viewProjectionHost: new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle));
                var options = new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                };
                RegistryRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator), callerContext: null)
                    .ConfigureAwait(false);
                if (withGraphResources)
                {
                    foreach (string resource in new[] { "child", "left", "right" })
                    {
                        string json = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                            "\"id\":\"urn:c2:resource:" + resource + "\",\"title\":\"" + resource + "\"}";
                        WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(
                            new WotUpsertResourceRequest
                            {
                                GroupId = WotRegistryGroups.ThingDescriptions,
                                ResourceId = resource,
                                Content = ByteString.From(Encoding.UTF8.GetBytes(json))
                            }).ConfigureAwait(false);
                        Assert.That(created.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                    }
                    var manager = (WotRegistryNodeManager)RegistryRegistration.NodeManager;
                    await manager.DispatchProjectionAsync(_ => default, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(m_registry.Current.RefreshGeneration, Is.Zero);
                    Assert.That(m_registry.Current.AllResources().All(resource => resource.ActiveVersionId is null),
                        Is.True, "Graph-only fixtures must not first activate unrelated ordinary source owners.");
                }
                Views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle, retirementPolicy);
                m_client = new ClientFixture(false, false, NUnitTelemetryContext.Create());
                await m_client.LoadClientConfigurationAsync(m_directory).ConfigureAwait(false);
                Session = await m_client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.That(Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                NamespaceIndex = (ushort)Session.NamespaceUris.GetIndex(Namespaces.WotCon);
                Assert.That(NamespaceIndex, Is.GreaterThan(0));
            }

            private readonly string m_directory;
            private readonly ServerFixture<ReferenceServer> m_fixture;
            private readonly PreparedWotTestStorage m_storage;
            private ReferenceServer? m_server;
            private ClientFixture? m_client;
            private WotRegistryService? m_registry;
            private FileWotRegistryStore? m_store;
            private WotMaterializationCoordinator? m_coordinator;
        }

        /// <summary>
        /// Reads the NodeClass attribute for an address-space Node.
        /// </summary>
        /// <param name="harness">The live server harness.</param>
        /// <param name="nodeId">The NodeId to read.</param>
        private static async Task<NodeClass> ReadNodeClassAsync(NativeHarness harness, NodeId nodeId)
        {
            ReadResponse response = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            return (NodeClass)nodeClass;
        }

        private sealed class CapturedDigestFactory(
            NodeId resource, ByteString digest, string namespaceUri) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [namespaceUri];

            /// <inheritdoc/>
            /// <param name="server">The server internal.</param>
            /// <param name="configuration">The application configuration.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var manager = new Mock<AsyncCustomNodeManager>(
                    server, configuration, server.Telemetry.CreateLogger<CapturedDigestFactory>(),
                    new[] { namespaceUri })
                {
                    CallBase = true
                };
                manager.As<IWotCanonicalViewReadImage>()
                    .Setup(image => image.TryGetMembershipDigest(resource, out digest)).Returns(true);
                return new ValueTask<IAsyncNodeManager>(manager.Object);
            }
        }

        /// <summary>
        /// Creates a source node manager whose validation can be paused to test publication boundaries.
        /// </summary>
        internal sealed class BlockingSourceFactory : IAsyncNodeManagerFactory
        {
            /// <inheritdoc cref="IAsyncNodeManagerFactory.NamespacesUris"/>
            public ArrayOf<string> NamespacesUris => ["urn:c2:blocked-source"];

            /// <summary>
            /// Gets or sets the created for <c>BlockingSourceFactory</c>.
            /// </summary>
            public BlockingSource Created { get; private set; } = null!;

            /// <inheritdoc/>
            /// <param name="server">The server internal.</param>
            /// <param name="configuration">The application configuration.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Created = new BlockingSource(server, configuration);
                return new ValueTask<IAsyncNodeManager>(Created);
            }
        }

        /// <summary>
        /// Provides a controllable node-validation barrier for canonical-view integration tests.
        /// </summary>
        internal sealed class BlockingSource : AsyncCustomNodeManager
        {
            /// <summary>
            /// Initializes a new <c>BlockingSource</c> instance.
            /// </summary>
            /// <param name="server">The server used to initialize the instance.</param>
            /// <param name="configuration">The configuration used to initialize the instance.</param>
            public BlockingSource(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, server.Telemetry.CreateLogger<BlockingSource>(), "urn:c2:blocked-source")
            {
            }

            /// <summary>
            /// Gets the entered for <c>BlockingSource</c>.
            /// </summary>
            public Task Entered => m_entered.Task;

            /// <summary>
            /// Creates a node identifier in the source node manager's namespace.
            /// </summary>
            /// <param name="name">The name assigned to the node or field used by the operation.</param>
            public NodeId Identity(string name)
            {
                return new NodeId(name, NamespaceIndex);
            }

            /// <summary>
            /// Arms the barrier for the next node validation call.
            /// </summary>
            public void BlockNextValidation()
            {
                Interlocked.Exchange(ref m_block, 1);
            }

            /// <summary>
            /// Releases the paused node validation call.
            /// </summary>
            public void Resume()
            {
                m_resume.TrySetResult(true);
            }

            /// <inheritdoc/>
            /// <param name="externalReferences">The external references.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                foreach (string name in new[] { "First", "Second", "Third" })
                {
                    var node = new BaseDataVariableState(null)
                    {
                        NodeId = Identity(name),
                        BrowseName = new QualifiedName(name, NamespaceIndex),
                        DisplayName = new LocalizedText(name),
                        TypeDefinitionId = Ua.VariableTypeIds.BaseDataVariableType,
                        DataType = Ua.DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Value = Variant.From(1),
                        StatusCode = StatusCodes.Good,
                        AccessLevel = AccessLevels.CurrentRead,
                        UserAccessLevel = AccessLevels.CurrentRead
                    };
                    await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
                }
            }

            /// <inheritdoc />
            protected override async ValueTask<NodeState> ValidateNodeAsync(
                ServerSystemContext context, NodeHandle handle, IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref m_block, 0) == 1)
                {
                    m_entered.TrySetResult(true);
                    await m_resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return await base.ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);
            }

            private readonly TaskCompletionSource<bool> m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_block;
        }
    }
}
