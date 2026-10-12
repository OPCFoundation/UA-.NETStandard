/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using RegistryMethods = Opc.Ua.XRegistry.MethodIds;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises public registry tools through registered sessions and generated method proxies.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class XRegistryRemoteToolsTests
    {
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CreateGroupSelectsExplicitMethodAndReturnsExactAssignedIdentityAsync(
            bool ifNotExists, bool created)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.GoodResultsMayBeIncomplete,
                Variant.From(fixture.Id("assigned-group")), Variant.From(created));

            CallToolResult result = await fixture.Registry.CreateGroupAsync(
                fixture.Id("registry").ToString(), "things", ifNotExists, "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("groupNodeId").GetString(),
                Is.EqualTo(fixture.Id("assigned-group").ToString()));
            Assert.That(json.GetProperty("created").GetBoolean(), Is.EqualTo(created));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            fixture.AssertCall(fixture.Id("registry"), ifNotExists
                ? RegistryMethods.RegistryType_GetOrCreateGroup : RegistryMethods.RegistryType_CreateGroup,
                Variant.From("things"));
            Assert.That(fixture.Reads, Is.Empty);
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"), "Creation must not trigger follow-up reads.");
        }

        [TestCase(false, "")]
        [TestCase(false, "requested-v7")]
        [TestCase(true, "")]
        [TestCase(true, "requested-v7")]
        public async Task CreateResourcePreservesVersionSelectionWithoutOpeningFileAsync(
            bool ifNotExists, string version)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good, Variant.From(fixture.Id("assigned-version")),
                Variant.From("server-v19"), Variant.From(0u), Variant.From(false));

            CallToolResult result = await fixture.Registry.CreateResourceAsync(
                fixture.Id("group").ToString(), "pump", version, ifNotExists, "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("versionNodeId").GetString(),
                Is.EqualTo(fixture.Id("assigned-version").ToString()));
            Assert.That(json.GetProperty("versionId").GetString(), Is.EqualTo("server-v19"));
            Assert.That(json.GetProperty("created").GetBoolean(), Is.EqualTo(!ifNotExists));
            fixture.AssertCall(fixture.Id("group"), ifNotExists
                ? RegistryMethods.GroupType_GetOrCreateResource : RegistryMethods.GroupType_CreateResource,
                Variant.From("pump"), Variant.From(version), Variant.From(false));
            Assert.That(fixture.Reads, Is.Empty);
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"));
        }

        [TestCase("group")]
        [TestCase("resource")]
        public async Task CreateRefusalRetainsMethodAndInputStatusesWithoutInventingIdentityAsync(string kind)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.OnCall = _ => new CallMethodResult
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                InputArgumentResults = [StatusCodes.BadOutOfRange],
                OutputArguments = []
            };
            CallToolResult result = kind == "group"
                ? await fixture.Registry.CreateGroupAsync(
                    fixture.Id("registry").ToString(), "things", sessionName: "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.CreateResourceAsync(
                    fixture.Id("group").ToString(), "pump", sessionName: "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidArgument);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("inputArgumentResults")[0].GetUInt32(),
                Is.EqualTo(StatusCodes.BadOutOfRange.Code));
            Assert.That(json.TryGetProperty("groupNodeId", out _), Is.False);
            Assert.That(json.TryGetProperty("versionNodeId", out _), Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        [TestCase("missing-node")]
        [TestCase("null-node")]
        [TestCase("wrong-node-type")]
        [TestCase("missing-created")]
        public async Task GetOrCreateGroupRejectsMalformedSuccessOutputsAsync(string shape)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            Variant[] output = shape switch
            {
                "missing-node" => [],
                "null-node" => [Variant.From(NodeId.Null), Variant.From(true)],
                "wrong-node-type" => [Variant.From("ns=1;s=fake"), Variant.From(true)],
                _ => [Variant.From(fixture.Id("group"))]
            };
            fixture.Reply(StatusCodes.Good, output);

            CallToolResult result = await fixture.Registry.CreateGroupAsync(
                fixture.Id("registry").ToString(), "things", true, "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        [TestCase("missing-version")]
        [TestCase("wrong-version")]
        [TestCase("wrong-handle")]
        [TestCase("missing-created")]
        public async Task GetOrCreateResourceRejectsMalformedSuccessOutputsAsync(string shape)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            var node = Variant.From(fixture.Id("assigned-version"));
            Variant[] output = shape switch
            {
                "missing-version" => [node],
                "wrong-version" => [node, Variant.From(7u), Variant.From(0u), Variant.From(true)],
                "wrong-handle" => [node, Variant.From("v7"), Variant.From("0"), Variant.From(true)],
                _ => [node, Variant.From("v7"), Variant.From(0u)]
            };
            fixture.Reply(StatusCodes.Good, output);

            CallToolResult result = await fixture.Registry.CreateResourceAsync(
                fixture.Id("group").ToString(), "pump", ifNotExists: true, sessionName: "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Reads, Is.Empty);
        }

        [Test]
        public async Task CreateResourceClosesUnexpectedServerHandleAndReportsProtocolViolationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.OnCall = request =>
            {
                if (request.MethodId == fixture.Model(RegistryMethods.GroupType_CreateResource))
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments =
                        [
                            Variant.From(fixture.Id("unexpectedly-opened")), Variant.From("v19"), Variant.From(71u)
                        ]
                    };
                }
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Id("unexpectedly-opened")));
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From(71u)]));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };

            CallToolResult result = await fixture.Registry.CreateResourceAsync(
                fixture.Id("group").ToString(), "pump", sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUnexpectedError);
            Assert.That(fixture.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Calls[0].InputArguments[2], Is.EqualTo(Variant.From(false)));
            Assert.That(fixture.CallTokens[1], Is.EqualTo(CancellationToken.None));
            Assert.That(fixture.Reads, Is.Empty);
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("versionNodeId", out _), Is.False);
        }

        [TestCase("group", "registry")]
        [TestCase("resource", "group")]
        public async Task CreationRequiresCorrectOwnerRoleBeforeCallingServerAsync(string operation, string actualRole)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            string wrong = operation == "group" ? fixture.Id("group").ToString() : fixture.Id("registry").ToString();
            CallToolResult result = operation == "group"
                ? await fixture.Registry.CreateGroupAsync(wrong, "things", sessionName: "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.CreateResourceAsync(wrong, "pump", sessionName: "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("message").GetString(),
                Does.Contain(actualRole == "registry" ? "Group" : "Registry"));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("group", "group", "Group")]
        [TestCase("resource", "resource", "Resource")]
        [TestCase("version", "version-v7", "Version")]
        public async Task DeleteChecksHierarchyAndPassesApplicableCounterExactlyOnceAsync(
            string operation, string node, string scope)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good);
            uint counter = operation == "resource" ? 37u : 11u;

            CallToolResult result = await DeleteAsync(fixture, operation, node, counter).ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Id(node).ToString()));
            Assert.That(json.GetProperty("scope").GetString(), Is.EqualTo(scope));
            fixture.AssertCall(fixture.Id(node), operation == "group"
                ? RegistryMethods.GroupType_Delete : RegistryMethods.ResourceType_Delete, Variant.From(counter));
            Assert.That(fixture.Reads, Is.Empty, "No stale pre-read can replace the explicit Epoch/MetaEpoch.");
        }

        [TestCase("resource", "version-v7")]
        [TestCase("version", "resource")]
        [TestCase("group", "version-v7")]
        [TestCase("resource", "legacy")]
        [TestCase("version", "legacy")]
        public async Task DeleteCannotConfuseWholeResourceConcreteVersionOrLegacyFlatRoleAsync(
            string operation, string node)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Type(fixture.Id("legacy"), RegistryTypes.ResourceType);
            fixture.Browse(fixture.Id("legacy"), ReferenceTypeIds.Organizes,
                [fixture.Reference(fixture.Id("group"), RegistryTypes.GroupType)], BrowseDirection.Inverse);

            CallToolResult result = await DeleteAsync(fixture, operation, node, 7).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("none")]
        [TestCase("multiple")]
        [TestCase("untyped")]
        public async Task ResourceRoleRejectsMissingMultipleOrInvalidOrganizingParentsAsync(string parents)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Type(fixture.Id("ambiguous"), RegistryTypes.ResourceType);
            if (parents != "none")
            {
                ExpandedNodeId parentType = parents == "multiple" ? RegistryTypes.GroupType : ObjectTypeIds.FolderType;
                fixture.Type(fixture.Id("parent"), parentType);
                ReferenceDescription first = fixture.Reference(fixture.Id("parent"), parentType);
                fixture.Type(fixture.Id("other-parent"), RegistryTypes.GroupType);
                ReferenceDescription second = fixture.Reference(fixture.Id("other-parent"), RegistryTypes.GroupType);
                fixture.Browse(fixture.Id("ambiguous"), ReferenceTypeIds.Organizes,
                    parents == "multiple" ? [first, second] : [first], BrowseDirection.Inverse);
            }
            CallToolResult result = await fixture.Registry.ReadAsync(
                fixture.Id("ambiguous").ToString(), "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeDefinitionInvalid);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase("resource", "group", "Resource", 31u)]
        [TestCase("version-v7", "versions", "Version", 5u)]
        public async Task ComponentMembershipIgnoresParallelNotifierWhenResolvingDeletionScopeAsync(
            string node, string parent, string scope, uint epoch)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.BrowseResults.Remove((fixture.Id(node), ReferenceTypeIds.Organizes, BrowseDirection.Inverse));
            ExpandedNodeId parentType = scope == "Resource"
                ? RegistryTypes.GroupType : RegistryTypes.ResourceVersionsType;
            fixture.Browse(fixture.Id(node), ReferenceTypeIds.HasComponent,
                [fixture.Reference(fixture.Id(parent), parentType)], BrowseDirection.Inverse);
            fixture.Browse(fixture.Id(node), ReferenceTypeIds.HasNotifier,
                [fixture.Reference(fixture.Id("notifier-only"), RegistryTypes.RegistryType)], BrowseDirection.Inverse);
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await DeleteAsync(
                fixture, scope == "Resource" ? "resource" : "version", node, epoch).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("scope").GetString(), Is.EqualTo(scope));
            fixture.AssertCall(fixture.Id(node), RegistryMethods.ResourceType_Delete, Variant.From(epoch));
            BrowseDescription membership = fixture.Browses.Single(browse =>
                browse.NodeId == fixture.Id(node) && browse.BrowseDirection == BrowseDirection.Inverse);
            Assert.That(membership.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(membership.NodeClassMask, Is.EqualTo((uint)NodeClass.Object));
            Assert.That(membership.IncludeSubtypes, Is.True);
            Assert.That(fixture.Browses.Any(browse => browse.NodeId == fixture.Id("notifier-only")), Is.False);
            Assert.That(fixture.Paths.Any(path => path.RelativePath.Elements[0].TargetName ==
                fixture.Name(kRegistryNamespace, "Versions")), Is.True);
        }

        [TestCase("resource", "group", "Resource")]
        [TestCase("version-v7", "versions", "Version")]
        public async Task DuplicateOrganizingAndComponentLinksToSameOwnerDoNotCreateAmbiguityAsync(
            string node, string parent, string scope)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            ExpandedNodeId parentType = scope == "Resource"
                ? RegistryTypes.GroupType : RegistryTypes.ResourceVersionsType;
            fixture.Browse(fixture.Id(node), ReferenceTypeIds.HasComponent,
                [fixture.Reference(fixture.Id(parent), parentType),
                    fixture.Reference(fixture.Id(parent), parentType)], BrowseDirection.Inverse);
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await DeleteAsync(
                fixture, scope == "Resource" ? "resource" : "version", node, 19).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("scope").GetString(), Is.EqualTo(scope));
            fixture.AssertCall(fixture.Id(node), RegistryMethods.ResourceType_Delete, Variant.From(19u));
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"));
        }

        [TestCase("resource", "resource")]
        [TestCase("version-v7", "version")]
        public async Task DistinctOrganizingAndComponentOwnersAreRefusedBeforeMutationAsync(
            string node, string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            ExpandedNodeId parentType = operation == "resource"
                ? RegistryTypes.GroupType : RegistryTypes.ResourceVersionsType;
            fixture.Type(fixture.Id("other-owner"), parentType);
            fixture.Browse(fixture.Id(node), ReferenceTypeIds.HasComponent,
                [fixture.Reference(fixture.Id("other-owner"), parentType)], BrowseDirection.Inverse);
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await DeleteAsync(fixture, operation, node, 19).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeDefinitionInvalid);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("message").GetString(),
                Does.Contain("ambiguous"));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Browses.Any(browse => browse.NodeId == fixture.Id("other-owner")), Is.False);
        }

        [Test]
        public async Task DeleteResourceRejectsUntypedVersionsChildBeforeWholeResourceDeletionAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Type(fixture.Id("versions"), ObjectTypeIds.FolderType);
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Registry.DeleteResourceAsync(
                fixture.Id("resource").ToString(), 31, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(fixture.Calls, Is.Empty, "The mere name Versions cannot authorize whole-resource deletion.");
        }

        [Test]
        public async Task DeleteVersionRejectsOrphanVersionsFolderBeforeMutationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.BrowseResults.Remove(
                (fixture.Id("versions"), ReferenceTypeIds.HasComponent, BrowseDirection.Inverse));
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Registry.DeleteVersionAsync(
                fixture.Id("version-v7").ToString(), 5, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeDefinitionInvalid);
            Assert.That(fixture.Calls, Is.Empty, "A typed folder alone does not establish a logical-resource owner.");
        }

        [TestCase("group")]
        [TestCase("resource")]
        [TestCase("version")]
        public async Task DeletePreservesServerRefusalAndDoesNotRetryAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.BadInvalidState);
            string node = operation == "version" ? "version-v7" : operation;

            CallToolResult result = await DeleteAsync(fixture, operation, node, 5).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidState);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id(node).ToString()));
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        [TestCase("group")]
        [TestCase("resource")]
        [TestCase("version")]
        [TestCase("set-label")]
        [TestCase("remove-label")]
        public async Task ZeroEpochRequiresForceAndIsForwardedOnlyAfterExplicitOptInAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good);
            string node = operation == "version"
                ? "version-v7" : operation is "set-label" or "remove-label" ? "resource" : operation;

            CallToolResult denied = await MutationAsync(fixture, operation, node, 0, false).ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(denied);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Browses, Is.Empty);

            CallToolResult accepted = await MutationAsync(fixture, operation, node, 0, true).ConfigureAwait(false);

            Assert.That(accepted.IsError, Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            ArrayOf<Variant> arguments = fixture.Calls[0].InputArguments;
            Assert.That(arguments[^1], Is.EqualTo(Variant.From(0u)));
        }

        [TestCase("create-group")]
        [TestCase("create-resource")]
        [TestCase("group")]
        [TestCase("resource")]
        [TestCase("version")]
        [TestCase("set-label")]
        [TestCase("remove-label")]
        public async Task MutationsRefuseUnencryptedRegisteredSessionBeforeUaWorkAsync(string operation)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            CallToolResult result = operation switch
            {
                "create-group" => await fixture.Registry.CreateGroupAsync(
                    fixture.Id("registry").ToString(), "things", sessionName: "selected").ConfigureAwait(false),
                "create-resource" => await fixture.Registry.CreateResourceAsync(
                    fixture.Id("group").ToString(), "pump", sessionName: "selected").ConfigureAwait(false),
                _ => await MutationAsync(fixture, operation, operation, 0, true).ConfigureAwait(false)
            };

            RemoteModelTestSession.Error(result, StatusCodes.BadSecurityModeInsufficient);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(false, "registry", "registry-labels")]
        [TestCase(false, "group", "group-labels")]
        [TestCase(false, "resource", "meta-labels")]
        [TestCase(false, "version-v7", "version-labels")]
        [TestCase(true, "resource", "meta-labels")]
        [TestCase(true, "version-v7", "version-labels")]
        public async Task LabelsUseRoleSpecificContainerAndExactValueCounterOrderAsync(
            bool remove, string node, string container)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good);
            uint epoch = node == "resource" ? 31u : 5u;

            CallToolResult result = remove
                ? await fixture.Registry.RemoveLabelAsync(
                    fixture.Id(node).ToString(), "owner", epoch, sessionName: "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.SetLabelAsync(
                    fixture.Id(node).ToString(), "owner", "operations", epoch, sessionName: "selected")
                    .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.AssertCall(fixture.Id(container), remove
                ? RegistryMethods.AttributesType_RemoveAttribute : RegistryMethods.AttributesType_AddAttribute,
                remove ? [Variant.From("owner"), Variant.From(epoch)]
                    : [Variant.From("owner"), Variant.From("operations"), Variant.From(epoch)]);
            Assert.That(fixture.Reads, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingLabelContainerIsUnsupportedNotSuccessAsync(bool remove)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Translations.Remove((fixture.Id("resource"), fixture.Name(kRegistryNamespace, "MetaLabels")));
            CallToolResult result = remove
                ? await fixture.Registry.RemoveLabelAsync(
                    fixture.Id("resource").ToString(), "owner", 31, sessionName: "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.SetLabelAsync(
                    fixture.Id("resource").ToString(), "owner", "value", 31, sessionName: "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LabelMethodFailureRetainsStatusWithoutChangingDocumentAsync(bool remove)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.BadInvalidState);
            CallToolResult result = await MutationAsync(
                fixture, remove ? "remove-label" : "set-label", "resource", 31, false).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidState);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls[0].ObjectId, Is.EqualTo(fixture.Id("meta-labels")));
        }

        [Test]
        public async Task SetLabelRejectsNullValueInsteadOfDispatchingRemoveLabelAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Registry.SetLabelAsync(
                fixture.Id("resource").ToString(), "owner", null!, 31, sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Calls, Is.Empty, "Null set-label input is not permission to remove that label.");
        }

        [Test]
        public async Task SetLabelAcceptsEmptyValueWithoutRemovingLabelAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Registry.SetLabelAsync(
                fixture.Id("resource").ToString(), "owner", string.Empty, 31, sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.AssertCall(fixture.Id("meta-labels"), RegistryMethods.AttributesType_AddAttribute,
                Variant.From("owner"), Variant.From(string.Empty), Variant.From(31u));
        }

        [TestCase("registry", "Registry", "registry-labels")]
        [TestCase("group", "Group", "group-labels")]
        [TestCase("resource", "Resource", "meta-labels")]
        [TestCase("version-v7", "Version", "version-labels")]
        [TestCase("legacy", "LegacyResource", null)]
        public async Task ReadPreservesStructuralRoleEpochMetadataAndOptionalAbsenceAsync(
            string node, string role, string? labels)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Type(fixture.Id("legacy"), RegistryTypes.ResourceType);
            fixture.Browse(fixture.Id("legacy"), ReferenceTypeIds.Organizes,
                [fixture.Reference(fixture.Id("group"), RegistryTypes.GroupType)], BrowseDirection.Inverse);
            fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "Epoch"), Variant.From(5u));
            fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "Xid"), Variant.From("/things/pump"));
            if (role is not "Registry" and not "Group")
            {
                fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "MetaEpoch"), Variant.From(31u));
                fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "VersionId"), Variant.From("v7"));
                fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "Documentation"),
                    Variant.From("urn:docs:pump"));
                fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "ExternalReference"),
                    Variant.From("urn:external:pump"));
                fixture.Property(fixture.Id(node), fixture.Name(kRegistryNamespace, "ResourceUrl"),
                    Variant.From("https://federation.invalid/not-followed"));
            }
            CallToolResult result = await fixture.Registry.ReadAsync(fixture.Id(node).ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            JsonElement properties = json.GetProperty("properties");

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("role").GetString(), Is.EqualTo(role));
            Assert.That(json.GetProperty("labelsNodeId").GetString(),
                Is.EqualTo(labels is null ? null : fixture.Id(labels).ToString()));
            Assert.That(properties.GetProperty("Epoch").GetProperty("dataValue").GetProperty("Value").GetUInt32(),
                Is.EqualTo(5));
            Assert.That(properties.GetProperty("Xid").GetProperty("dataValue").GetProperty("Value").GetString(),
                Is.EqualTo("/things/pump"));
            Assert.That(properties.GetProperty("Description").GetProperty("supported").GetBoolean(), Is.False);
            if (role is not "Registry" and not "Group")
            {
                Assert.That(properties.GetProperty("MetaEpoch").GetProperty("dataValue")
                    .GetProperty("Value").GetUInt32(), Is.EqualTo(31));
                Assert.That(properties.GetProperty("VersionId").GetProperty("dataValue")
                    .GetProperty("Value").GetString(), Is.EqualTo("v7"));
                Assert.That(properties.GetProperty("Documentation").GetProperty("dataValue")
                    .GetProperty("Value").GetString(),
                    Is.EqualTo("urn:docs:pump"));
                Assert.That(properties.GetProperty("ExternalReference").GetProperty("dataValue")
                    .GetProperty("Value").GetString(),
                    Is.EqualTo("urn:external:pump"));
                Assert.That(properties.GetProperty("MetaCreatedAt").GetProperty("supported").GetBoolean(), Is.False);
                Assert.That(properties.GetProperty("MetaModifiedAt").GetProperty("supported").GetBoolean(), Is.False);
                Assert.That(properties.TryGetProperty("IsDefault", out _), Is.False);
                Assert.That(properties.TryGetProperty("ContentId", out _), Is.False);
                Assert.That(properties.GetProperty("ResourceUrl").GetProperty("dataValue")
                    .GetProperty("Value").GetString(),
                    Is.EqualTo("https://federation.invalid/not-followed"));
            }
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Reads, Has.Count.EqualTo(role is "Registry" or "Group" ? 2 : 7));
        }

        [Test]
        public async Task MetadataBadQualityIsSupportedButErrorRatherThanMissingAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Property(fixture.Id("registry"), fixture.Name(kRegistryNamespace, "Epoch"),
                new DataValue(Variant.From(8u), StatusCodes.BadUserAccessDenied));

            CallToolResult result = await fixture.Registry.ReadAsync(fixture.Id("registry").ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement epoch = RemoteModelTestSession.Payload(result).GetProperty("properties").GetProperty("Epoch");

            Assert.That(result.IsError, Is.True);
            Assert.That(epoch.GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(epoch.GetProperty("dataValue").GetProperty("Value").GetUInt32(), Is.EqualTo(8));
            Assert.That(epoch.GetProperty("dataValue").GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ModelReadServiceFailureIsNotConvertedToUnsupportedOptionalPropertyAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            NodeId parent = fixture.Id(wot ? "document" : "registry");
            fixture.Property(parent, fixture.Name(wot ? WotCon.Namespaces.WotCon : kRegistryNamespace,
                wot ? "Enabled" : "Epoch"), Variant.From(1u));
            fixture.OnRead = _ => throw new ServiceResultException(
                StatusCodes.BadSessionClosed, "remote session closed");

            CallToolResult result = wot
                ? await fixture.Wot.ReadResourceStateAsync(parent.ToString(), "selected").ConfigureAwait(false)
                : await fixture.Registry.ReadAsync(parent.ToString(), "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadSessionClosed);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("message").GetString(),
                Does.Contain("remote session closed"));
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("properties", out _), Is.False);
            Assert.That(fixture.Reads, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ReadCapabilitiesRegistersGeneratedTypeBeforeDecodingBinaryServiceValueAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(emptyFactory: true).ConfigureAwait(false);
            fixture.RegistryHierarchy();
            Assert.That(
                fixture.Context.Factory.ContainsEncodeableType(XRegistry.DataTypeIds.RegistryCapabilitiesDataType),
                Is.False);
            var capabilities = new XRegistry.RegistryCapabilitiesDataType
            {
                Flags = ["epoch", "filter"],
                Mutable = ["entities"],
                Pagination = true,
                StickyVersions = false,
                SpecVersions = ["1.0"]
            };
            byte[] encoded;
            using (var encoder = new BinaryEncoder(fixture.Context))
            {
                encoder.WriteDataValue(null, new DataValue(Variant.FromStructure(capabilities), StatusCodes.Uncertain));
                encoded = encoder.CloseAndReturnBuffer()!;
            }
            NodeId valueNode = fixture.Property(fixture.Id("registry"),
                fixture.Name(kRegistryNamespace, "CapabilitiesInfo"), Variant.Null);
            fixture.OnRead = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(valueNode));
                Assert.That(
                    fixture.Context.Factory.ContainsEncodeableType(XRegistry.DataTypeIds.RegistryCapabilitiesDataType),
                    Is.True,
                    "Registration must precede decoding the incoming service value.");
                using var decoder = new BinaryDecoder(encoded, fixture.Context);
                DataValue decoded = decoder.ReadDataValue(null);
                Assert.That(decoded.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
                Assert.That(extension.Encoding, Is.EqualTo(ExtensionObjectEncoding.EncodeableObject),
                    "An opaque Binary extension object is not a decoded capability document.");
                return decoded;
            };

            CallToolResult result = await fixture.Registry.ReadAsync(
                fixture.Id("registry").ToString(), "selected").ConfigureAwait(false);
            CallToolResult repeated = await fixture.Registry.ReadAsync(
                fixture.Id("registry").ToString(), "selected").ConfigureAwait(false);
            JsonElement data = RemoteModelTestSession.Payload(result).GetProperty("properties")
                .GetProperty("CapabilitiesInfo").GetProperty("dataValue");
            JsonElement body = data.GetProperty("Value");

            Assert.That(result.IsError, Is.False);
            Assert.That(repeated.IsError, Is.False, "Generated registration must be idempotent per session.");
            Assert.That(data.GetProperty("UaType").GetByte(), Is.EqualTo((byte)BuiltInType.ExtensionObject));
            Assert.That(data.GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(body.GetProperty("Flags").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(s_capabilityFlags));
            Assert.That(body.GetProperty("Mutable")[0].GetString(), Is.EqualTo("entities"));
            Assert.That(body.GetProperty("Pagination").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("StickyVersions").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("SpecVersions")[0].GetString(), Is.EqualTo("1.0"));
            Assert.That(body.TryGetProperty("UaBody", out _), Is.False);
            Assert.That(fixture.Reads, Has.Count.EqualTo(2));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(RegistryCollection.Groups, "registry", "group")]
        [TestCase(RegistryCollection.Resources, "group", "resource")]
        [TestCase(RegistryCollection.Versions, "resource", "version-v7")]
        public async Task ListUsesCollectionHierarchyAndReturnsExactEntityIdentityAsync(
            RegistryCollection collection, string parent, string child)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id(parent).ToString(), collection, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id(child).ToString()));
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("nextOffset").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(fixture.Browses[^1].NodeId,
                Is.EqualTo(fixture.Id(collection == RegistryCollection.Versions ? "versions" : parent)));
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(fixture.Browses[^1].NodeClassMask, Is.EqualTo((uint)NodeClass.Object));
            Assert.That(fixture.Browses[^1].IncludeSubtypes, Is.True);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(RegistryCollection.Groups, "component")]
        [TestCase(RegistryCollection.Resources, "component")]
        [TestCase(RegistryCollection.Versions, "component")]
        [TestCase(RegistryCollection.Groups, "derived-component")]
        [TestCase(RegistryCollection.Resources, "derived-component")]
        [TestCase(RegistryCollection.Versions, "derived-component")]
        [TestCase(RegistryCollection.Groups, "derived-organizes")]
        [TestCase(RegistryCollection.Resources, "derived-organizes")]
        [TestCase(RegistryCollection.Versions, "derived-organizes")]
        public async Task ListsAcceptNativeAndVendorMembershipButIgnoreNotifierAndOtherReferencesAsync(
            RegistryCollection collection, string referenceKind)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            string input = collection switch
            {
                RegistryCollection.Groups => "registry",
                RegistryCollection.Resources => "group",
                _ => "resource"
            };
            NodeId parent = fixture.Id(collection == RegistryCollection.Versions ? "versions" : input);
            NodeId member = fixture.Id(collection == RegistryCollection.Groups ? "group" :
                collection == RegistryCollection.Resources ? "resource" : "version-v7");
            ExpandedNodeId memberType = collection == RegistryCollection.Groups
                ? RegistryTypes.GroupType : RegistryTypes.ResourceType;
            fixture.BrowseResults.Remove((parent, ReferenceTypeIds.Organizes, BrowseDirection.Forward));
            NodeId referenceType = referenceKind == "component"
                ? ReferenceTypeIds.HasComponent : fixture.Id(referenceKind);
            if (referenceKind != "component")
            {
                fixture.Subtype(referenceType, referenceKind == "derived-organizes"
                    ? ReferenceTypeIds.Organizes : ReferenceTypeIds.HasComponent);
            }
            fixture.Browse(parent, referenceType, [fixture.Reference(member, memberType, "member")]);
            fixture.Browse(parent, ReferenceTypeIds.HasNotifier,
                [fixture.Reference(member, memberType, "parallel-notifier"),
                    fixture.Reference(fixture.Id("notifier-only"), memberType)]);
            fixture.Browse(parent, ReferenceTypeIds.HasEventSource,
                [fixture.Reference(fixture.Id("event-source-only"), memberType)]);

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id(input).ToString(), collection, sessionName: "selected").ConfigureAwait(false);
            JsonElement page = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(), Is.EqualTo(member.ToString()));
            Assert.That(page.GetProperty("items")[0].GetProperty("displayName").GetString(), Is.EqualTo("member"));
            Assert.That(page.GetProperty("items")[0].GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(fixture.Model(memberType).ToString()));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(fixture.Browses[^1].NodeId, Is.EqualTo(parent));
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(fixture.Browses[^1].IncludeSubtypes, Is.True);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListDeduplicatesMembershipAcrossPagesAndReleasesCurrentContinuationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            var firstCursor = ByteString.From([61]);
            var lastCursor = ByteString.From([62]);
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.Organizes,
                [fixture.Reference(fixture.Id("one"), RegistryTypes.GroupType)], continuation: firstCursor);
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.HasComponent,
                [fixture.Reference(fixture.Id("one"), RegistryTypes.GroupType)]);
            ReferenceDescription duplicate = fixture.Reference(fixture.Id("one"), RegistryTypes.GroupType);
            duplicate.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            ReferenceDescription second = fixture.Reference(fixture.Id("two"), RegistryTypes.GroupType);
            second.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            fixture.NextPages.Enqueue(new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = [duplicate, second],
                ContinuationPoint = lastCursor
            });

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, maxResults: 1, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement page = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("one").ToString()));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(page.GetProperty("nextOffset").GetInt32(), Is.EqualTo(1));
            Assert.That(fixture.Continuations, Has.Count.EqualTo(2));
            Assert.That(fixture.Continuations[0], Is.EqualTo((false, firstCursor, CancellationToken.None)));
            Assert.That(fixture.Continuations[1], Is.EqualTo((true, lastCursor, CancellationToken.None)));
            Assert.That(fixture.NextPages, Is.Empty);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(10000, false)]
        [TestCase(10001, false)]
        [TestCase(10000, true)]
        [TestCase(10001, true)]
        public async Task CollectionBudgetCountsIgnoredNotifierAndDuplicateMembershipLinksAsync(
            int count, bool duplicateMembership)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            ReferenceDescription reference = fixture.Reference(fixture.Id("group"), RegistryTypes.GroupType);
            reference.ReferenceTypeId = duplicateMembership
                ? ReferenceTypeIds.HasComponent : ReferenceTypeIds.HasNotifier;
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.HierarchicalReferences,
                Enumerable.Repeat(reference, count).ToArrayOf());

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, sessionName: "selected")
                .ConfigureAwait(false);

            if (count == 10001)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            else
            {
                Assert.That(result.IsError, Is.False);
                JsonElement page = RemoteModelTestSession.Payload(result);
                Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(duplicateMembership ? 1 : 0));
                Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
                if (duplicateMembership)
                {
                    Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                        Is.EqualTo(fixture.Id("group").ToString()));
                }
            }
            Assert.That(fixture.Browses, Has.Count.EqualTo(2));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ExplicitEmptyHierarchicalPageOverridesTypedMembershipFixturesAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.HierarchicalReferences, []);

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement page = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HierarchicalOrTypedBrowseFailureCannotBeHiddenByOtherMembershipFixturesAsync(
            bool explicitHierarchy)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.BrowseResults[(fixture.Id("registry"),
                explicitHierarchy ? ReferenceTypeIds.HierarchicalReferences : ReferenceTypeIds.HasComponent,
                BrowseDirection.Forward)] = new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied };

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, sessionName: "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("items", out _), Is.False);
            Assert.That(fixture.Browses[^1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListAcceptsVendorSubtypeFiltersUnrelatedObjectsAndReleasesPagedCursorAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            NodeId vendorType = fixture.Id("vendor-group-type");
            fixture.Subtype(vendorType, RegistryTypes.GroupType);
            var cursor = ByteString.From([71]);
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.Organizes,
                [fixture.Reference(fixture.Id("unrelated"), ObjectTypeIds.FolderType),
                    fixture.Reference(fixture.Id("first"), vendorType),
                    fixture.Reference(fixture.Id("second"), RegistryTypes.GroupType),
                    fixture.Reference(fixture.Id("third"), vendorType)], continuation: cursor);

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, 1, 1, "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("second").ToString()));
            Assert.That(json.GetProperty("nextOffset").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(fixture.Continuations, Is.EqualTo([(true, cursor, CancellationToken.None)]));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListRemapsUriQualifiedLocalIdsAndLeavesRemoteGroupsInformationalAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(includeRegistry: false).ConfigureAwait(false);
            ushort ns = fixture.Context.NamespaceUris.GetIndexOrAppend(kRegistryNamespace);
            fixture.RegistryHierarchy();
            var local = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("local-group", kRegistryNamespace),
                TypeDefinition = RegistryTypes.GroupType,
                BrowseName = new QualifiedName("things", ns),
                NodeClass = NodeClass.Object
            };
            var remote = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(new NodeId("foreign-group", ns), null, 11),
                TypeDefinition = RegistryTypes.GroupType,
                BrowseName = new QualifiedName("foreign", ns),
                NodeClass = NodeClass.Object
            };
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.Organizes, [local, remote]);

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement items = RemoteModelTestSession.Payload(result).GetProperty("items");

            Assert.That(result.IsError, Is.False);
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(items[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(new NodeId("local-group", ns).ToString()));
            Assert.That(items[0].GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(fixture.Model(RegistryTypes.GroupType).ToString()));
            Assert.That(items[0].GetProperty("expandedNodeId").GetString(), Is.EqualTo(local.NodeId.ToString()));
            Assert.That(items[0].GetProperty("local").GetBoolean(), Is.True);
            Assert.That(items[1].GetProperty("nodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[1].GetProperty("expandedNodeId").GetString(), Is.EqualTo(remote.NodeId.ToString()));
            Assert.That(items[1].GetProperty("local").GetBoolean(), Is.False);
            Assert.That(items[1].GetProperty("remote").GetBoolean(), Is.True);
            Assert.That(fixture.Browses.Select(browse => browse.NodeId), Is.All.EqualTo(fixture.Id("registry")));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("resource", "meta-labels")]
        [TestCase("version-v7", "version-labels")]
        public async Task ListLabelsUsesProperContainerAndPreservesTypedFalseAsync(string node, string container)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            ReferenceDescription label = fixture.Reference(
                fixture.Id("approved"), ObjectTypeIds.BaseObjectType, "approved");
            label.NodeClass = NodeClass.Variable;
            fixture.Browse(fixture.Id(container), ReferenceTypeIds.HasProperty, [label]);
            fixture.Values[(fixture.Id("approved"), Attributes.Value)] = new DataValue(Variant.From(false));

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id(node).ToString(), RegistryCollection.Labels, sessionName: "selected").ConfigureAwait(false);
            JsonElement entry = RemoteModelTestSession.Payload(result).GetProperty("items")[0];

            Assert.That(result.IsError, Is.False);
            Assert.That(entry.GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Id("approved").ToString()));
            Assert.That(entry.GetProperty("dataValue").GetProperty("UaType").GetByte(),
                Is.EqualTo((byte)BuiltInType.Boolean));
            Assert.That(entry.GetProperty("dataValue").GetProperty("Value").GetBoolean(), Is.False);
            Assert.That(fixture.Browses[^1].NodeId, Is.EqualTo(fixture.Id(container)));
            Assert.That(fixture.Browses[^1].NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(-1, 1)]
        [TestCase(0, 0)]
        [TestCase(0, 501)]
        [TestCase(1000001, 1)]
        public async Task ListRejectsInvalidPageWithoutBrowsingAsync(int offset, int size)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, offset, size, "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(10000)]
        [TestCase(10001)]
        public async Task ListBoundsFilteredTraversalAtTenThousandReferencesAsync(int references)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Browse(fixture.Id("registry"), ReferenceTypeIds.Organizes,
                Enumerable.Range(0, references).Select(index =>
                    fixture.Reference(fixture.Id($"unrelated-{index}"), ObjectTypeIds.FolderType)).ToArrayOf());

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("registry").ToString(), RegistryCollection.Groups, sessionName: "selected")
                .ConfigureAwait(false);

            if (references == 10001)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            else
            {
                Assert.That(result.IsError, Is.False);
                JsonElement page = RemoteModelTestSession.Payload(result);
                Assert.That(page.GetProperty("items").GetArrayLength(), Is.Zero);
                Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            }
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Browses, Has.Count.EqualTo(2));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public async Task ListErrorsReflectOnlyReturnedLabelPageNotOffsetOrLookaheadAsync(int offset, bool error)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Browse(fixture.Id("meta-labels"), ReferenceTypeIds.HasProperty,
                [fixture.Reference(fixture.Id("good"), ObjectTypeIds.BaseObjectType, "good"),
                    fixture.Reference(fixture.Id("bad"), ObjectTypeIds.BaseObjectType, "bad")]);
            fixture.Values[(fixture.Id("good"), Attributes.Value)] = new DataValue(Variant.From("visible"));
            fixture.Values[(fixture.Id("bad"), Attributes.Value)] =
                new DataValue(Variant.From("stale"), StatusCodes.BadOutOfRange);

            CallToolResult result = await fixture.Registry.ListAsync(
                fixture.Id("resource").ToString(), RegistryCollection.Labels, offset, 1, "selected")
                .ConfigureAwait(false);
            JsonElement page = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.EqualTo(error));
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("dataValue").GetProperty("Value").GetString(),
                Is.EqualTo(error ? "stale" : "visible"));
            Assert.That(page.GetProperty("items")[0].GetProperty("error").GetBoolean(), Is.EqualTo(error));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.EqualTo(offset == 0));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ListRejectsUnknownCollectionAndPropagatesFailedLabelReadAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            CallToolResult invalid = await fixture.Registry.ListAsync(
                fixture.Id("resource").ToString(), (RegistryCollection)99, sessionName: "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(invalid);
            fixture.Browse(fixture.Id("meta-labels"), ReferenceTypeIds.HasProperty,
                [fixture.Reference(fixture.Id("label"), ObjectTypeIds.BaseObjectType)]);
            fixture.Values[(fixture.Id("label"), Attributes.Value)] =
                DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);

            CallToolResult denied = await fixture.Registry.ListAsync(
                fixture.Id("resource").ToString(), RegistryCollection.Labels, sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(denied.IsError, Is.True);
            JsonElement label = RemoteModelTestSession.Payload(denied).GetProperty("items")[0];
            Assert.That(label.GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Id("label").ToString()));
            Assert.That(label.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(label.GetProperty("dataValue").GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task DiscoverFindsActualVendorRegistryRootAndDoesNotFollowRemoteReferencesAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            NodeId type = fixture.Id("vendor-registry-type");
            fixture.Subtype(type, RegistryTypes.RegistryType);
            ReferenceDescription local = fixture.Reference(fixture.Id("renamed-root"), type, "VendorRegistry");
            local.NodeId = new ExpandedNodeId("renamed-root", "urn:test:remote-model");
            local.TypeDefinition = new ExpandedNodeId("vendor-registry-type", "urn:test:remote-model");
            ReferenceDescription remote = fixture.Reference(fixture.Id("foreign"), type);
            remote.NodeId = new ExpandedNodeId(fixture.Id("foreign"), null, 2);
            fixture.Browse(ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences, [remote, local]);

            CallToolResult result = await fixture.Registry.DiscoverAsync(sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Id("renamed-root").ToString()));
            Assert.That(json.GetProperty("items")[0].GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(type.ToString()));
            Assert.That(json.GetProperty("items")[0].GetProperty("expandedNodeId").GetString(),
                Is.EqualTo(local.NodeId.ToString()));
            Assert.That(json.GetProperty("items")[0].GetProperty("local").GetBoolean(), Is.True);
            Assert.That(fixture.Browses.Select(value => value.NodeId), Is.EqualTo([ObjectIds.ObjectsFolder]));
        }

        [Test]
        public async Task DiscoverBrowseFailureIsNotAnEmptySuccessPageAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.BrowseResults[
                (ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward)] =
                new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied };

            CallToolResult result = await fixture.Registry.DiscoverAsync(sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("items", out _), Is.False);
            Assert.That(fixture.Browses, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MissingRegistryNamespaceIsUnsupportedWithoutAppendingOrUaWorkAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(includeRegistry: false).ConfigureAwait(false);
            int count = fixture.Context.NamespaceUris.Count;
            CallToolResult result = await fixture.Registry.DiscoverAsync(sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task ToolsResolveCurrentNamedSessionOnEveryInvocationAsync()
        {
            await using RemoteModelTestSession first = await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            await using RemoteModelTestSession replacement =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            first.RegistryHierarchy();
            replacement.RegistryHierarchy();
            first.Reply(StatusCodes.Good, Variant.From(first.Id("first-assigned")));
            replacement.Reply(StatusCodes.BadUserAccessDenied);
            Ua.Mcp.Tools.XRegistryTools tools = first.Registry;
            await first.Manager.RegisterExistingSessionAsync("other", replacement.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult accepted = await tools.CreateGroupAsync(
                first.Id("registry").ToString(), "one", sessionName: "selected").ConfigureAwait(false);
            await first.Manager.RegisterExistingSessionAsync("selected", replacement.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            CallToolResult refused = await tools.CreateGroupAsync(
                replacement.Id("registry").ToString(), "two", sessionName: "selected").ConfigureAwait(false);

            Assert.That(RemoteModelTestSession.Payload(accepted).GetProperty("groupNodeId").GetString(),
                Is.EqualTo(first.Id("first-assigned").ToString()));
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            first.AssertCall(first.Id("registry"), RegistryMethods.RegistryType_CreateGroup, Variant.From("one"));
            replacement.AssertCall(replacement.Id("registry"), RegistryMethods.RegistryType_CreateGroup,
                Variant.From("two"));
        }

        [Test]
        public async Task BadServiceStatusCannotBecomeSuccessfulCreationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.Reply(StatusCodes.Good, Variant.From(fixture.Id("not-accepted")));
            fixture.CallServiceStatus = StatusCodes.BadSessionClosed;

            CallToolResult result = await fixture.Registry.CreateGroupAsync(
                fixture.Id("registry").ToString(), "things", sessionName: "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadSessionClosed);
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("groupNodeId", out _), Is.False);
            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
        }

        private static Task<CallToolResult> DeleteAsync(
            RemoteModelTestSession fixture, string operation, string node, uint epoch, bool force = false)
        {
            return operation switch
            {
                "group" => fixture.Registry.DeleteGroupAsync(fixture.Id(node).ToString(), epoch, force, "selected"),
                "resource" => fixture.Registry.DeleteResourceAsync(
                    fixture.Id(node).ToString(), epoch, force, "selected"),
                "version" => fixture.Registry.DeleteVersionAsync(fixture.Id(node).ToString(), epoch, force, "selected"),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        private static Task<CallToolResult> MutationAsync(
            RemoteModelTestSession fixture, string operation, string node, uint epoch, bool force)
        {
            return operation switch
            {
                "set-label" => fixture.Registry.SetLabelAsync(
                    fixture.Id(node).ToString(), "owner", "operations", epoch, force, "selected"),
                "remove-label" => fixture.Registry.RemoveLabelAsync(
                    fixture.Id(node).ToString(), "owner", epoch, force, "selected"),
                _ => DeleteAsync(fixture, operation, node, epoch, force)
            };
        }

        private const string kRegistryNamespace = XRegistry.XRegistryWellKnown.XRegistryNamespaceUri;
        private static readonly string[] s_capabilityFlags = ["epoch", "filter"];
    }
}
#endif
