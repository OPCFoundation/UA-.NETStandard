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

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.WotCon.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Wot = Opc.Ua.WotCon;
using XRegistry = Opc.Ua.XRegistry;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

[TestFixture]
[Category("IndustrialCompanions")]
public sealed class IndustrialWotCompanionProviderTests
{
    [Test]
    public async Task ManagementInspectionDoesNotDiscoverOrContactExternalAssetsAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Management(session);
        session.AddProperty(target.NodeId, Wot.Namespaces.WotCon, "SupportedWoTBindings",
            Variant.From(ArrayOf.Wrapped(["urn:test:binding-a", "urn:test:binding-b"])));
        var provider = new WotCompanionProvider();
        CompanionInspection inspection = await provider.InspectAsync(session.Context(), target, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(Field(inspection.Values, "Supported binding count").TryGetValue(out int count), Is.True);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(session.Calls.IsEmpty, Is.True);
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "create-sample-asset" &&
            operation.Safety == CompanionOperationSafety.SampleMutation), Is.True);
        Assert.That(provider.Descriptor.Maturity,
            Does.Contain("v1.02").And.Contain("1.1 draft").And.Contain("2026-09-05"));
    }

    [Test]
    public void BindingArrayIsBoundedAndItsReadErrorsAreNotHidden()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Management(session);
        NodeId bindings = session.AddProperty(target.NodeId, Wot.Namespaces.WotCon, "SupportedWoTBindings",
            Variant.From(ArrayOf.Wrapped(["urn:test:a", "urn:test:b"])));
        var provider = new WotCompanionProvider();
        Assert.That(
            async () => await provider.InspectAsync(
                session.Context(maxFields: 1), target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        session.SetValue(bindings, Variant.Null, status: StatusCodes.BadUserAccessDenied);
        Assert.That(
            async () => await provider.InspectAsync(session.Context(), target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadUserAccessDenied));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task CreateSampleUsesConnectivityAndAssetFileHelpersWithOnlyFixedDisconnectedContentAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Management(session);
        NodeId asset = session.Id("new-asset");
        NodeId file = session.Id("asset-file");
        session.AddObject(asset, ObjectTypeIds.BaseObjectType, "Asset");
        session.AddObject(file, Wot.ObjectTypeIds.WoTAssetFileType);
        session.AddChild(asset, file, Wot.Namespaces.WotCon, "WoTFile");
        ByteString uploaded = default(ByteString);
        using var cancellation = new CancellationTokenSource();
        session.CallHandler = (request, token) =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            if (request.MethodId == session.Resolve(Wot.MethodIds.WoTAssetConnectionManagementType_CreateAsset))
            {
                Assert.That(request.ObjectId, Is.EqualTo(target.NodeId));
                Assert.That(request.InputArguments[0].TryGetValue(out string? name), Is.True);
                Assert.That(name, Is.EqualTo("ualens-sample-demo"));
                return Good([Variant.From(asset)]);
            }
            Assert.That(request.ObjectId, Is.EqualTo(file));
            if (request.MethodId == MethodIds.FileType_Open)
            {
                Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                Assert.That(mode, Is.EqualTo(6));
                return Good([Variant.From(7u)]);
            }
            Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(7u));
            if (request.MethodId == MethodIds.FileType_Write)
            {
                Assert.That(request.InputArguments[1].TryGetValue(out uploaded), Is.True);
                return Good();
            }
            Assert.That(request.MethodId, Is.EqualTo(session.Resolve(Wot.MethodIds.WoTAssetFileType_CloseAndUpdate)));
            return Good();
        };
        CompanionOperationResult result = await new WotCompanionProvider()
            .ExecuteAsync(session.Context(), target, "create-sample-asset", "demo", cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(uploaded.IsNull, Is.False);
        Assert.That(uploaded.Length, Is.InRange(1, 1024));
        using JsonDocument document = JsonDocument.Parse(uploaded.Span.ToArray());
        Assert.That(document.RootElement.GetProperty("id").GetString(), Is.EqualTo("urn:ualens:sample:asset"));
        Assert.That(document.RootElement.TryGetProperty("base", out _), Is.False);
        Assert.That(document.RootElement.TryGetProperty("forms", out _), Is.False);
        Assert.That(document.RootElement.GetProperty("properties").EnumerateObject().MoveNext(), Is.False);
        Assert.That(Field(result.Values, "Asset NodeId").TryGetValue(out NodeId actualAsset), Is.True);
        Assert.That(actualAsset, Is.EqualTo(asset));
        Assert.That(Field(result.Values, "WoT file NodeId").TryGetValue(out NodeId actualFile), Is.True);
        Assert.That(actualFile, Is.EqualTo(file));
        Assert.That(session.Calls, Has.Count.EqualTo(4));
    }

    [Test]
    public void FailedAssetOpenCompensatesOnlyTheNewlyCreatedAssetAndPreservesTheFailure()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Management(session);
        NodeId asset = session.Id("failed-new-asset");
        var failure = new ServiceResultException(StatusCodes.BadConnectionClosed);
        bool deleted = false;
        session.TranslateHandler = (_, _) => throw failure;
        session.CallHandler = (request, token) =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(target.NodeId));
            if (request.MethodId == session.Resolve(Wot.MethodIds.WoTAssetConnectionManagementType_CreateAsset))
            {
                return Good([Variant.From(asset)]);
            }
            Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                Wot.MethodIds.WoTAssetConnectionManagementType_DeleteAsset)));
            Assert.That(request.InputArguments[0].TryGetValue(out NodeId deleteId), Is.True);
            Assert.That(deleteId, Is.EqualTo(asset));
            Assert.That(token.IsCancellationRequested, Is.False);
            deleted = true;
            return Good();
        };
        Assert.That(
            async () => await new WotCompanionProvider()
                .ExecuteAsync(session.Context(), target, "create-sample-asset", "demo", CancellationToken.None)
                .ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
        Assert.That(deleted, Is.True);
        Assert.That(session.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RegistryInspectionIsReadOnlyAndPreservesGenerationAsync()
    {
        var scenario = new RegistryScenario();
        CompanionInspection inspection = await scenario.Provider
            .InspectAsync(scenario.Session.Context(), scenario.Target, CancellationToken.None).ConfigureAwait(false);
        Assert.That(Field(inspection.Values, "RefreshGeneration").TryGetValue(out uint generation), Is.True);
        Assert.That(generation, Is.EqualTo(42u));
        Assert.That(inspection.Operations, Has.Count.EqualTo(3));
        Assert.That(inspection.Operations.Contains(operation => operation.Id != "refresh" &&
            operation.Safety != CompanionOperationSafety.SampleMutation), Is.False);
        Assert.That(scenario.Session.Calls.IsEmpty, Is.True);
        Assert.That(inspection.Summary, Does.Contain("AutoRefresh"));
    }

    [Test]
    public async Task RegistrationAndRefreshUseExactSampleVersionAndBoundedSelectionAsync()
    {
        var scenario = new RegistryScenario();
        CompanionOperationResult registration = await scenario.RegisterAsync().ConfigureAwait(false);
        Assert.That(Field(registration.Values, "Version NodeId").TryGetValue(out NodeId version), Is.True);
        Assert.That(version, Is.EqualTo(scenario.Version));
        Assert.That(scenario.StoredDocument.IsNull, Is.False);
        CompanionOperationResult refresh = await scenario.RefreshAsync().ConfigureAwait(false);
        Assert.That(Field(refresh.Values, "Generation").TryGetValue(out uint generation), Is.True);
        Assert.That(generation, Is.EqualTo(43u));
        Assert.That(Field(refresh.Values, "Succeeded").TryGetValue(out uint succeeded), Is.True);
        Assert.That(succeeded, Is.EqualTo(1u));
        Assert.That(scenario.RefreshCalls, Is.EqualTo(1));
        Assert.That(scenario.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task RefreshRejectsDifferentContentBeforeIssuingTheRefreshMethodAsync()
    {
        var scenario = new RegistryScenario();
        await scenario.RegisterAsync().ConfigureAwait(false);
        scenario.StoredDocument = ByteString.From("""{"title":"different model"}"""u8);
        Assert.That(
            async () => await scenario.RefreshAsync().ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(scenario.RefreshCalls, Is.Zero);
    }

    [TestCase(Wot.WoTOutcomeEnum.Failed, 1u)]
    [TestCase(Wot.WoTOutcomeEnum.Rejected, 0u)]
    [TestCase(Wot.WoTOutcomeEnum.Success, 1u)]
    public async Task RefreshDomainFailureIsNotReportedAsSuccessAsync(Wot.WoTOutcomeEnum outcome, uint failed)
    {
        var scenario = new RegistryScenario { Outcome = outcome, Failed = failed };
        await scenario.RegisterAsync().ConfigureAwait(false);
        Assert.That(
            async () => await scenario.RefreshAsync().ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
        Assert.That(scenario.RefreshCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task ServerExpandingTheRefreshSelectionIsNotReportedAsBoundedSuccessAsync()
    {
        var scenario = new RegistryScenario { Total = 2 };
        await scenario.RegisterAsync().ConfigureAwait(false);
        Assert.That(
            async () => await scenario.RefreshAsync().ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void RefreshMetadataTransportErrorIsNotConvertedToLegacyHierarchy()
    {
        var scenario = new RegistryScenario();
        scenario.Session.SetValue(scenario.Generation, Variant.Null, status: StatusCodes.BadCommunicationError);
        Assert.That(
            async () => await scenario.RefreshAsync().ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadCommunicationError));
        Assert.That(scenario.Session.Calls.IsEmpty, Is.True);
    }

    [TestCase("register-sample-model", "../other")]
    [TestCase("refresh-sample-model", "urn:another-resource")]
    [TestCase("register-sample-model", "abcdefghijklmnopqrstuvwxyz0123456789")]
    public void RegistrySampleInputIsBoundedAndCannotSelectArbitraryResources(string operation, string input)
    {
        var scenario = new RegistryScenario();
        Assert.That(
            async () => await scenario.Provider.ExecuteAsync(
                scenario.Session.Context(), scenario.Target, operation, input, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.ArgumentException);
        Assert.That(scenario.Session.Calls.IsEmpty, Is.True);
    }

    private static CompanionTarget Management(IndustrialCompanionTestSession session)
    {
        NodeId management = session.Id("management");
        session.AddObject(management, Wot.ObjectTypeIds.WoTAssetConnectionManagementType);
        return new CompanionTarget("wot", management, "Management", "WoT asset management");
    }

    private sealed class RegistryScenario
    {
        public RegistryScenario()
        {
            Session = new IndustrialCompanionTestSession();
            Provider = new WotCompanionProvider();
            NodeId registry = Session.Id("wot-registry");
            NodeId group = Session.Id("thing-models");
            NodeId resource = Session.Id("logical-resource");
            NodeId versions = Session.Id("versions");
            Version = Session.Id("version-one");
            Target = new CompanionTarget("wot", registry, "Registry", "WoT registry");
            Session.AddObject(registry, Wot.ObjectTypeIds.WoTRegistryType);
            Session.AddObject(group, Wot.ObjectTypeIds.ThingModelGroupType);
            Session.AddObject(resource, ObjectTypeIds.BaseObjectType);
            Session.AddObject(versions, XRegistry.ObjectTypeIds.ResourceVersionsType);
            Session.AddObject(Version, Wot.ObjectTypeIds.ThingModelFileType, "1");
            Session.AddChild(registry, group, Wot.Namespaces.WotCon, WotRegistryClient.ThingModelsGroupId);
            Session.AddChild(group, resource, Wot.Namespaces.WotCon, "ualens-sample-one");
            Session.AddChild(resource, versions, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Versions");
            Session.AddChild(versions, Version);
            Generation = Session.AddProperty(registry, Wot.Namespaces.WotCon, "RefreshGeneration", Variant.From(42u));
            Session.AddProperty(registry, Wot.Namespaces.WotCon, "AutoRefresh", Variant.From(false));
            Session.AddProperty(registry, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "SpecVersion",
                Variant.From("0.6.0"));
            Session.CallHandler = (request, _) =>
            {
                if (request.MethodId == Session.Resolve(XRegistry.MethodIds.RegistryType_GetOrCreateGroup))
                {
                    Assert.That(request.ObjectId, Is.EqualTo(registry));
                    Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                    Assert.That(id, Is.EqualTo(WotRegistryClient.ThingModelsGroupId));
                    return Good([Variant.From(group), Variant.From(false)]);
                }
                if (request.MethodId == Session.Resolve(XRegistry.MethodIds.GroupType_GetOrCreateResource))
                {
                    Assert.That(request.ObjectId, Is.EqualTo(group));
                    Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                    Assert.That(id, Is.EqualTo("ualens-sample-one"));
                    Assert.That(request.InputArguments[1].TryGetValue(out string? requestedVersion), Is.True);
                    Assert.That(requestedVersion, Is.EqualTo("1"));
                    return Good([Variant.From(Version), Variant.From("1"), Variant.From(12u), Variant.From(true)]);
                }
                if (request.MethodId == MethodIds.FileType_Write)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(Version));
                    Assert.That(request.InputArguments[1].TryGetValue(out ByteString document), Is.True);
                    Assert.That(document.Length, Is.InRange(1, 1024));
                    StoredDocument = document;
                    return Good();
                }
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(Version));
                    Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(mode, Is.EqualTo(1));
                    return Good([Variant.From(12u)]);
                }
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(Version));
                    Reads++;
                    return Good([Variant.From(Reads == 1 ? StoredDocument : ByteString.Empty)]);
                }
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(Version));
                    return Good();
                }
                Assert.That(request.MethodId, Is.EqualTo(Session.Resolve(Wot.MethodIds.WoTRegistryType_Refresh)));
                Assert.That(request.ObjectId, Is.EqualTo(registry));
                Assert.That(request.InputArguments[0].TryGetStructure(
                    Session.MessageContext, out ArrayOf<Wot.WoTResourceSelectorDataType> selection), Is.True);
                Assert.That(selection, Has.Count.EqualTo(1));
                Assert.That(selection[0].GroupId, Is.EqualTo(WotRegistryClient.ThingModelsGroupId));
                Assert.That(selection[0].ResourceId, Is.EqualTo("ualens-sample-one"));
                Assert.That(selection[0].VersionId, Is.EqualTo("1"));
                Assert.That(selection[0].Kind, Is.EqualTo(Wot.WoTDocumentKindEnum.ThingModel));
                Assert.That(request.InputArguments[1].TryGetStructure<Wot.WoTRefreshOptionsDataType>(
                    Session.MessageContext, out Wot.WoTRefreshOptionsDataType? options), Is.True);
                Assert.That(options!.IncludeDependents, Is.False);
                Assert.That(options.Force, Is.False);
                Assert.That(options.DryRun, Is.False);
                Assert.That(options.MaxParallelism, Is.EqualTo(1u));
                Assert.That(options.Timeout, Is.EqualTo(5000d));
                Assert.That(options.Atomicity, Is.EqualTo(Wot.WoTAtomicityEnum.PerResource));
                Assert.That(options.DeletePolicy, Is.EqualTo(Wot.WoTDeletePolicyEnum.Reject));
                Assert.That(request.InputArguments[2].TryGetValue(out uint expectedGeneration), Is.True);
                Assert.That(expectedGeneration, Is.EqualTo(42u));
                RefreshCalls++;
                return Good(
                [
                    Variant.FromStructure(new Wot.WoTRefreshSummaryDataType
                    {
                        Outcome = Outcome,
                        Total = Total,
                        Succeeded = Outcome == Wot.WoTOutcomeEnum.Success ? 1u : 0u,
                        Failed = Failed
                    }),
                    Variant.FromStructure(ArrayOf.Wrapped(
                        [new Wot.WoTResourceLoadResultDataType { Outcome = Outcome }])),
                    Variant.From(43u)
                ]);
            };
        }

        public IndustrialCompanionTestSession Session { get; }

        public WotCompanionProvider Provider { get; }

        public CompanionTarget Target { get; }

        public NodeId Version { get; }

        public NodeId Generation { get; }

        public ByteString StoredDocument { get; set; }

        public Wot.WoTOutcomeEnum Outcome { get; set; } = Wot.WoTOutcomeEnum.Success;

        public uint Total { get; set; } = 1;

        public uint Failed { get; set; }

        public int Reads { get; private set; }

        public int RefreshCalls { get; private set; }

        public ValueTask<CompanionOperationResult> RegisterAsync()
        {
            return Provider.ExecuteAsync(
                Session.Context(), Target, "register-sample-model", "one", CancellationToken.None);
        }

        public ValueTask<CompanionOperationResult> RefreshAsync()
        {
            return Provider.ExecuteAsync(
                Session.Context(), Target, "refresh-sample-model", "one", CancellationToken.None);
        }
    }
}
