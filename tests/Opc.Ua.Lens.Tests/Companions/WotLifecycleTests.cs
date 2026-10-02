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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;
using Wot = Opc.Ua.WotCon;
using XRegistry = Opc.Ua.XRegistry;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class WotLifecycleTests
    {
        [TestCase(false, "set-enabled")]
        [TestCase(true, "set-enabled")]
        [TestCase(false, "select-default")]
        [TestCase(true, "select-default")]
        [TestCase(false, "delete-document")]
        [TestCase(true, "delete-document")]
        public async Task DocumentMutationsPreserveLogicalVersusVersionEpochs(bool version, string operation)
        {
            using var fixture = new WotFixture(version ? "version" : "resource");
            uint epoch = operation == "delete-document" && version ? 23u : 7u;
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, operation, fixture.Inputs(operation, epoch), CancellationToken.None)
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, operation, input, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(fixture.Calls, Has.Count.EqualTo(1));
            CallMethodRequest request = fixture.Server.Calls[0];
            Assert.That(request.ObjectId, Is.EqualTo(fixture.Target.NodeId));
            Assert.That(request.InputArguments[^1].TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(epoch));
            Assert.That(input.Review, Does.Contain(
                operation == "delete-document" && version ? "selected Version" : "MetaEpoch"));
            if (operation == "set-enabled")
            {
                Assert.That(request.InputArguments[0].TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.True);
            }
            if (operation == "select-default")
            {
                Assert.That(request.InputArguments[0].TryGetValue(out string? selected), Is.True);
                Assert.That(selected, Is.EqualTo("v2"));
            }
            Assert.That(result.Summary, Does.Contain("accepted"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrationCreatesOnlyANewVersionAndHonorsAutoRefreshConsent(bool autoRefresh)
        {
            using var fixture = new WotFixture("registry");
            fixture.Server.SetValue(fixture.AutoRefresh, Variant.From(autoRefresh));
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "register-version",
                fixture.Inputs("register-version", accepted: autoRefresh), CancellationToken.None)
                    .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "register-version", input, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sRegistration));
            Assert.That(fixture.Document.ToArray(), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(ModelDefinition)));
            Assert.That(Field(result.Values, "Version ID").TryGetValue(out string? version), Is.True);
            Assert.That(version, Is.EqualTo("v2"));
            Assert.That(result.Summary, Does.Contain("AutoRefresh"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnacceptedAutoRefreshIsRejectedBeforeUploadEvenWhenPolicyChanges(bool afterPreparation)
        {
            using var fixture = new WotFixture("registry");
            CompanionTaskInput? prepared = afterPreparation ? await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "register-version",
                fixture.Inputs("register-version"), CancellationToken.None).ConfigureAwait(false) : null;
            fixture.Server.SetValue(fixture.AutoRefresh, Variant.From(true));

            if (afterPreparation)
            {
                await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                    fixture.Context, fixture.Target, "register-version", prepared!, null, CancellationToken.None)
                    .AsTask(),
                    Throws.InvalidOperationException).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => fixture.Provider.PrepareInputAsync(
                    fixture.Context, fixture.Target, "register-version",
                    fixture.Inputs("register-version"), CancellationToken.None).AsTask(),
                    Throws.InvalidOperationException).ConfigureAwait(false);
            }
            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task AReturnedGroupMustHaveTheRequestedWotDomainKind()
        {
            using var fixture = new WotFixture("registry");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "register-version",
                fixture.Inputs("register-version"), CancellationToken.None).ConfigureAwait(false);
            fixture.Server.AddObject(fixture.Group, Wot.ObjectTypeIds.ThingDescriptionGroupType);

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "register-version", input, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sGetGroup));
            Assert.That(fixture.Document.Length, Is.Zero);
        }

        [Test]
        public async Task CreatingAnAssetUploadsTheReviewedBytesWithoutImplicitDiscovery()
        {
            using var fixture = new WotFixture("management");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "create-asset", fixture.Inputs("create-asset"), CancellationToken.None)
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "create-asset", input, null, CancellationToken.None)
                    .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sCreateAsset));
            Assert.That(fixture.Document.ToArray(), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(Definition)));
            Assert.That(Field(result.Values, "Asset NodeId").TryGetValue(out NodeId asset), Is.True);
            Assert.That(asset, Is.EqualTo(fixture.Asset));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOpeningOfANewAssetCompensatesOnlyThatAssetAndPreservesBothFailures(bool cleanupFails)
        {
            using var fixture = new WotFixture("management");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "create-asset", fixture.Inputs("create-asset"), CancellationToken.None)
                .ConfigureAwait(false);
            var openFailure = new ServiceResultException(StatusCodes.BadConnectionClosed);
            fixture.Server.TranslateHandler = (path, token) =>
                path.StartingNode == fixture.Asset ? throw openFailure : fixture.Server.Translate(path);
            fixture.FailCleanup = cleanupFails;

            if (cleanupFails)
            {
                await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                    fixture.Context, fixture.Target, "create-asset", input, null, CancellationToken.None).AsTask(),
                    Throws.TypeOf<AggregateException>().With.InnerException.SameAs(openFailure)
                        .And.Property(nameof(AggregateException.InnerExceptions)).Some.InstanceOf<IOException>())
                    .ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                    fixture.Context, fixture.Target, "create-asset", input, null, CancellationToken.None).AsTask(),
                    Throws.Exception.SameAs(openFailure)).ConfigureAwait(false);
            }
            Assert.That(fixture.Calls, Is.EqualTo(sFailedAsset));
        }

        [Test]
        public async Task ExplicitRefreshPinsOneVersionAndReturnsCorrelatedMaterializationEvidence()
        {
            using var fixture = new WotFixture("registry");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "refresh-resource", fixture.Inputs("refresh-resource"),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Calls, Is.Empty);

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "refresh-resource", input, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sRefresh));
            Assert.That(Field(result.Values, "Generation").TryGetValue(out uint generation), Is.True);
            Assert.That(generation, Is.EqualTo(43));
            Assert.That(Field(result.Values, "Materialized nodes").TryGetValue(out uint nodes), Is.True);
            Assert.That(nodes, Is.EqualTo(5));
            Assert.That(Field(result.Values, "Root NodeId").TryGetValue(out NodeId root), Is.True);
            Assert.That(root, Is.EqualTo(fixture.ProjectedRoot));
            Assert.That(input.Review, Does.Contain("exactly one").And.Contain("No Force"));
            Assert.That(result.Summary, Does.Contain("not proof of reachability"));
        }

        [TestCase("generation")]
        [TestCase("content")]
        public async Task ChangedVersionOrGenerationCannotExecuteARefresh(string change)
        {
            using var fixture = new WotFixture("registry");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "refresh-resource", fixture.Inputs("refresh-resource"),
                CancellationToken.None).ConfigureAwait(false);
            if (change == "generation")
            {
                fixture.Server.SetValue(fixture.GenerationNode, Variant.From(43u));
            }
            else
            {
                fixture.Server.SetValue(fixture.DigestNode, Variant.From(ByteString.From(new byte[32])));
            }

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "refresh-resource", input, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("correlation")]
        [TestCase("count")]
        [TestCase("version")]
        [TestCase("digest")]
        [TestCase("generation")]
        [TestCase("failure")]
        [TestCase("unknown-outcome")]
        public async Task InvalidOrExpandedRefreshEvidenceNeverBecomesSuccess(string fault)
        {
            using var fixture = new WotFixture("registry") { RefreshFault = fault };
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "refresh-resource", fixture.Inputs("refresh-resource"),
                CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "refresh-resource", input, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sRefresh));
        }

        [Test]
        public async Task AssetDeletionRechecksManagerMembershipAndContentBeforeTheSingleDelete()
        {
            using var fixture = new WotFixture("management");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-asset", fixture.Inputs("delete-asset"),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Calls, Is.EqualTo(sAssetRead));
            fixture.Calls.Clear();

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "delete-asset", input, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sReadAndDelete));
            Assert.That(input.Review, Does.Contain(fixture.Asset.ToString()).And.Contain("SHA-256"));
            Assert.That(Field(result.Values, "Requested asset deletion").TryGetValue(out NodeId asset), Is.True);
            Assert.That(asset, Is.EqualTo(fixture.Asset));
        }

        [Test]
        public async Task AnAssetOutsideTheSelectedManagerCannotBePreparedForDeletion()
        {
            using var fixture = new WotFixture("management");

            await Assert.ThatAsync(() => fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-asset",
                [new("asset", Variant.From(fixture.Server.Id("unrelated"))),
                    new("includeChildren", Variant.From(true))], CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotFound))
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ChangedAssetContentInvalidatesThePreparedDeletion()
        {
            using var fixture = new WotFixture("management");
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-asset", fixture.Inputs("delete-asset"),
                CancellationToken.None).ConfigureAwait(false);
            fixture.StoredAsset = ByteString.From("""{"changed":true}"""u8);
            fixture.Calls.Clear();

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "delete-asset", input, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(sAssetRead));
        }

        private const string Definition =
            """{"@context":"https://www.w3.org/2022/wot/td/v1.1","title":"Reviewed fixture","properties":{}}""";

        private const string ModelDefinition =
            """{"@context":"https://www.w3.org/2022/wot/td/v1.1","@type":"tm:ThingModel","title":"Reviewed model"}""";

        private static readonly string[] sRegistration = ["GetOrCreateGroup", "CreateResource", "Write", "Close"];
        private static readonly string[] sGetGroup = ["GetOrCreateGroup"];
        private static readonly string[] sCreateAsset = ["CreateAsset", "Open", "Write", "CloseAndUpdate"];
        private static readonly string[] sFailedAsset = ["CreateAsset", "DeleteAsset"];
        private static readonly string[] sRefresh = ["Refresh"];
        private static readonly string[] sAssetRead = ["Open", "Read", "Read", "Close"];
        private static readonly string[] sReadAndDelete = ["Open", "Read", "Read", "Close", "DeleteAsset"];

        private sealed class WotFixture : IDisposable
        {
            public WotFixture(string role)
            {
                NodeId node = Server.Id(role);
                Server.AddObject(node, role switch
                {
                    "management" => Wot.ObjectTypeIds.WoTAssetConnectionManagementType,
                    "registry" => Wot.ObjectTypeIds.WoTRegistryType,
                    _ => Wot.ObjectTypeIds.ThingModelFileType
                });
                Target = new CompanionTarget("wot", node, role, role switch
                {
                    "management" => "WoT asset management",
                    "registry" => "WoT registry",
                    _ => "Thing Model"
                });
                Server.AddProperty(node, XNamespace, "Epoch", Variant.From(role is "resource" or "version" ? 23u : 7u));
                Server.AddProperty(node, XNamespace, "MetaEpoch", Variant.From(7u));
                Server.AddProperty(node, XNamespace, "Xid",
                    Variant.From(role == "version" ? "/thingmodels/item/versions/v1" : "/thingmodels/item"));
                AutoRefresh = Server.AddProperty(node, Wot.Namespaces.WotCon, "AutoRefresh", Variant.From(false));
                AddMethod(node, "GetOrCreateGroup", XNamespace);
                AddMethod(node, "Delete", XNamespace);
                AddMethod(node, "SetEnabled", Wot.Namespaces.WotCon);
                AddMethod(node, "SetDefaultVersion", Wot.Namespaces.WotCon);
                AddMethod(node, "CreateAsset", Wot.Namespaces.WotCon);
                AddMethod(node, "DeleteAsset", Wot.Namespaces.WotCon);
                Asset = Server.Id("created-asset");
                File = Server.Id("asset-file");
                Group = Server.Id("group");
                Version = Server.Id("new-version");
                Server.AddObject(Asset, ObjectTypeIds.BaseObjectType, "Created asset");
                Server.AddObject(File, Wot.ObjectTypeIds.WoTAssetFileType);
                Server.AddChild(Asset, File, Wot.Namespaces.WotCon, "WoTFile");
                Server.AddChild(Target.NodeId, Asset);
                Server.AddObject(Group, Wot.ObjectTypeIds.ThingModelGroupType);
                Server.AddChild(Target.NodeId, Group, Wot.Namespaces.WotCon, "thingmodels");
                NodeId resource = Server.Id("logical-resource");
                NodeId versions = Server.Id("versions");
                Server.AddObject(resource, Wot.ObjectTypeIds.ThingModelFileType);
                Server.AddObject(versions, ObjectTypeIds.FolderType);
                Server.AddObject(Version, Wot.ObjectTypeIds.ThingModelFileType);
                Server.AddChild(Group, resource, Wot.Namespaces.WotCon, "item");
                Server.AddChild(resource, versions, XNamespace, "Versions");
                Server.AddChild(versions, Version, Wot.Namespaces.WotCon, "v2");
                Digest = ByteString.From(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ModelDefinition)));
                DigestNode = Server.AddProperty(Version, Wot.Namespaces.WotCon, "ContentDigest", Variant.From(Digest));
                GenerationNode = Server.AddProperty(Target.NodeId, Wot.Namespaces.WotCon,
                    "RefreshGeneration", Variant.From(42u));
                ProjectedRoot = Server.Id("materialized-root");
                AddMethod(Target.NodeId, "Refresh", Wot.Namespaces.WotCon);
                Server.CallHandler = (request, token) =>
                {
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTRegistryType_Refresh))
                    {
                        Calls.Add("Refresh");
                        Assert.That(request.InputArguments[0].TryGetStructure(
                            Server.MessageContext, out ArrayOf<Wot.WoTResourceSelectorDataType> selection), Is.True);
                        Assert.That(selection, Has.Count.EqualTo(1));
                        Assert.That(selection[0].GroupId, Is.EqualTo("thingmodels"));
                        Assert.That(selection[0].ResourceId, Is.EqualTo("item"));
                        Assert.That(selection[0].VersionId, Is.EqualTo("v2"));
                        Assert.That(selection[0].Kind, Is.EqualTo(Wot.WoTDocumentKindEnum.ThingModel));
                        Assert.That(string.IsNullOrEmpty(selection[0].Xid), Is.True);
                        Assert.That(request.InputArguments[1].TryGetValue<Wot.WoTRefreshOptionsDataType>(
                            out Wot.WoTRefreshOptionsDataType? options, Server.MessageContext), Is.True);
                        Assert.That(options!.Force, Is.False);
                        Assert.That(options.IncludeDependents, Is.False);
                        Assert.That(options.DryRun, Is.False);
                        Assert.That(options.DeletePolicy, Is.EqualTo(Wot.WoTDeletePolicyEnum.Reject));
                        Assert.That(options.Atomicity, Is.EqualTo(Wot.WoTAtomicityEnum.PerResource));
                        Assert.That(options.MaxParallelism, Is.EqualTo(1));
                        Assert.That(options.Timeout, Is.EqualTo(5000));
                        Assert.That(request.InputArguments[2].TryGetValue(out uint generation), Is.True);
                        Assert.That(generation, Is.EqualTo(42));
                        Assert.That(request.InputArguments[3].TryGetValue(out string? requestId), Is.True);
                        Assert.That(requestId, Is.Not.Null.And.Not.Empty);
                        return Good(
                        [
                            Variant.FromStructure(new Wot.WoTRefreshSummaryDataType
                            {
                                RequestId = RefreshFault == "correlation" ? "other" : requestId,
                                Generation = 43, Total = RefreshFault == "count" ? 2u : 1u,
                                Succeeded = RefreshFault == "failure" ? 0u : 1u,
                                Failed = RefreshFault == "failure" ? 1u : 0u,
                                Outcome = RefreshFault == "failure"
                                    ? Wot.WoTOutcomeEnum.Failed : Wot.WoTOutcomeEnum.Success,
                                Atomicity = Wot.WoTAtomicityEnum.PerResource
                            }),
                            Variant.FromStructure<Wot.WoTResourceLoadResultDataType>(
                            [
                                new()
                                {
                                    GroupId = "thingmodels", ResourceId = "item",
                                    VersionId = RefreshFault == "version" ? "v3" : "v2",
                                    Kind = Wot.WoTDocumentKindEnum.ThingModel,
                                    ContentDigest = RefreshFault == "digest" ? ByteString.Empty : Digest,
                                    Generation = RefreshFault == "generation" ? 41u : 43u,
                                    Outcome = RefreshFault == "unknown-outcome" ? (Wot.WoTOutcomeEnum)99 :
                                        Wot.WoTOutcomeEnum.Success,
                                    RootNodeId = ProjectedRoot, MaterializedNodeCount = 5,
                                    LoadState = Wot.WoTLoadStateEnum.Active
                                }
                            ]),
                            Variant.From(43u)
                        ]);
                    }
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTAssetConnectionManagementType_CreateAsset))
                    {
                        Calls.Add("CreateAsset");
                        Assert.That(request.ObjectId, Is.EqualTo(Target.NodeId));
                        Assert.That(request.InputArguments[0].TryGetValue(out string? name), Is.True);
                        Assert.That(name, Is.EqualTo("asset"));
                        return Good([Variant.From(Asset)]);
                    }
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTAssetConnectionManagementType_DeleteAsset))
                    {
                        Calls.Add("DeleteAsset");
                        Assert.That(request.InputArguments[0].TryGetValue(out NodeId asset), Is.True);
                        Assert.That(asset, Is.EqualTo(Asset));
                        Assert.That(token.IsCancellationRequested, Is.False);
                        return FailCleanup ? throw new IOException("cleanup failed") : Good();
                    }
                    if (request.MethodId == Server.Resolve(XRegistry.MethodIds.RegistryType_GetOrCreateGroup))
                    {
                        Calls.Add("GetOrCreateGroup");
                        Assert.That(request.InputArguments[0].TryGetValue(out string? group), Is.True);
                        Assert.That(group, Is.EqualTo("thingmodels"));
                        return Good([Variant.From(Group), Variant.From(false)]);
                    }
                    if (request.MethodId == Server.Resolve(XRegistry.MethodIds.GroupType_CreateResource))
                    {
                        Calls.Add("CreateResource");
                        Assert.That(request.ObjectId, Is.EqualTo(Group));
                        Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                        Assert.That(id, Is.EqualTo("item"));
                        Assert.That(request.InputArguments[1].TryGetValue(out string? version), Is.True);
                        Assert.That(version, Is.EqualTo("v2"));
                        return Good([Variant.From(Version), Variant.From("v2"), Variant.From(42u)]);
                    }
                    if (request.MethodId == MethodIds.FileType_Open)
                    {
                        Calls.Add("Open");
                        Assert.That(request.ObjectId, Is.EqualTo(File));
                        m_readOffset = 0;
                        return Good([Variant.From(42u)]);
                    }
                    if (request.MethodId == MethodIds.FileType_Read)
                    {
                        Calls.Add("Read");
                        Assert.That(request.ObjectId, Is.EqualTo(File));
                        Assert.That(request.InputArguments[1].TryGetValue(out int maximum), Is.True);
                        int count = Math.Min(maximum, StoredAsset.Length - m_readOffset);
                        var chunk = ByteString.From(StoredAsset.Span.Slice(m_readOffset, count));
                        m_readOffset += count;
                        return Good([Variant.From(chunk)]);
                    }
                    if (request.MethodId == MethodIds.FileType_Write)
                    {
                        Calls.Add("Write");
                        Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
                        Assert.That(handle, Is.EqualTo(42));
                        Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                        Document.Write(bytes.Span);
                        return Good();
                    }
                    if (request.MethodId == MethodIds.FileType_Close)
                    {
                        Calls.Add("Close");
                        return Good();
                    }
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTAssetFileType_CloseAndUpdate))
                    {
                        Calls.Add("CloseAndUpdate");
                        return Good();
                    }
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTDocumentType_SetEnabled))
                    {
                        Calls.Add("SetEnabled");
                        return Good();
                    }
                    if (request.MethodId == Server.Resolve(Wot.MethodIds.WoTDocumentType_SetDefaultVersion))
                    {
                        Calls.Add("SetDefaultVersion");
                        return Good();
                    }
                    Assert.That(request.MethodId, Is.EqualTo(Server.Resolve(XRegistry.MethodIds.ResourceType_Delete)));
                    Calls.Add("Delete");
                    return Good();
                };
            }

            public IndustrialCompanionTestSession Server { get; } = new();
            public WotCompanionProvider Provider { get; } = new();
            public CompanionContext Context => Server.Context();
            public CompanionTarget Target { get; }
            public NodeId AutoRefresh { get; }
            public NodeId Asset { get; }
            public NodeId File { get; }
            public NodeId Group { get; }
            public NodeId Version { get; }
            public NodeId DigestNode { get; }
            public NodeId GenerationNode { get; }
            public NodeId ProjectedRoot { get; }
            public ByteString Digest { get; }
            public string? RefreshFault { get; set; }
            public List<string> Calls { get; } = [];
            public MemoryStream Document { get; } = new();
            public bool FailCleanup { get; set; }

            public ByteString StoredAsset { get; set; } =
                ByteString.From(System.Text.Encoding.UTF8.GetBytes(Definition));

            public ArrayOf<CompanionValue> Inputs(string operation, uint epoch = 7, bool accepted = false)
            {
                return operation switch
                {
                    "set-enabled" => [new("epoch", Variant.From(epoch)), new("enabled", Variant.From(true))],
                    "select-default" => [new("epoch", Variant.From(epoch)), new("version", Variant.From("v2"))],
                    "delete-document" =>
                        [new("epoch", Variant.From(epoch)), new("includeChildren", Variant.From(true))],
                    "create-asset" => [new("id", Variant.From("asset")), new("document", Variant.From(Definition))],
                    "delete-asset" => [new("asset", Variant.From(Asset)), new("includeChildren", Variant.From(true))],
                    "register-version" =>
                    [
                        new("epoch", Variant.From(epoch)), new("group", Variant.From("thingmodels")),
                        new("id", Variant.From("item")), new("version", Variant.From("v2")),
                        new("document", Variant.From(ModelDefinition)), new("acceptAutoRefresh", Variant.From(accepted))
                    ],
                    "refresh-resource" =>
                    [
                        new("epoch", Variant.From(epoch)), new("generation", Variant.From(42u)),
                        new("group", Variant.From("thingmodels")), new("id", Variant.From("item")),
                        new("version", Variant.From("v2")), new("acceptRefresh", Variant.From(true))
                    ],
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
            }

            public void Dispose()
            {
                Document.Dispose();
            }

            private void AddMethod(NodeId parent, string name, string namespaceUri)
            {
                NodeId method = Server.AddProperty(parent, namespaceUri, name, default);
                Server.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
                Server.SetValue(method, Variant.From(true), Attributes.Executable);
                Server.SetValue(method, Variant.From(true), Attributes.UserExecutable);
            }

            private const string XNamespace = XRegistry.XRegistryWellKnown.XRegistryNamespaceUri;
            private int m_readOffset;
        }
    }
}
