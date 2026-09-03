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

using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Encoders;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Session-mock unit tests for <see cref="WotRegistryClient"/>,
    /// <see cref="WotRegistryGroupClient"/> and
    /// <see cref="WotRegistryResourceClient"/>: browse resolution, method
    /// argument/result shapes and chunked upload/commit semantics.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotRegistryClientTests
    {
        private static ITelemetryContext CreateTelemetry()
        {
            return Mock.Of<ITelemetryContext>();
        }

        [Test]
        public async Task ForServerAsyncResolvesTheWellKnownRegistryObjectAsync()
        {
            var mock = new WotRegistrySessionMock();

            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            Assert.That(client.RegistryNodeId, Is.EqualTo(mock.RegistryNodeId));
        }

        [Test]
        public void ForServerAsyncThrowsWhenRegistryIsMissing()
        {
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            var messageContext = ServiceMessageContext.Create(CreateTelemetry());
            sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);
            sessionMock.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new[]
                    {
                        new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] }
                    }.ToArrayOf(),
                    DiagnosticInfos = default
                });

            Assert.That(
                () => WotRegistryClient.ForServerAsync(sessionMock.Object, CreateTelemetry()).AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task CreateThingDescriptionGroupCreatesATdGroupAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync()
                .ConfigureAwait(false);

            Assert.That(group.GroupId, Is.EqualTo(WotRegistryClient.ThingDescriptionsGroupId));
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
        }

        [Test]
        public async Task CreateThingModelGroupCreatesATmGroupAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            WotRegistryGroupClient group = await client
                .CreateThingModelGroupAsync()
                .ConfigureAwait(false);

            Assert.That(group.GroupId, Is.EqualTo(WotRegistryClient.ThingModelsGroupId));
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
        }

        [Test]
        public async Task GetOrCreateGroupReportsCreatedOnlyOnceAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            (WotRegistryGroupClient first, bool firstCreated) = await client
                .GetOrCreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryGroupClient second, bool secondCreated) = await client
                .GetOrCreateThingDescriptionGroupAsync().ConfigureAwait(false);

            Assert.That(firstCreated, Is.True);
            Assert.That(secondCreated, Is.False);
            Assert.That(second.GroupNodeId, Is.EqualTo(first.GroupNodeId));
        }

        [Test]
        public async Task OpenGroupResolvesAnExistingGroupAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient created = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            WotRegistryGroupClient opened = await client
                .OpenGroupAsync(WotRegistryClient.ThingDescriptionsGroupId)
                .ConfigureAwait(false);

            Assert.That(opened.GroupNodeId, Is.EqualTo(created.GroupNodeId));
        }

        [Test]
        public void OpenGroupThrowsForUnknownGroup()
        {
            var mock = new WotRegistrySessionMock();

            Assert.That(
                async () =>
                {
                    WotRegistryClient client = await WotRegistryClient
                        .ForServerAsync(mock.Session, CreateTelemetry())
                        .ConfigureAwait(false);
                    await client.OpenGroupAsync("does-not-exist").ConfigureAwait(false);
                },
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task GetOrCreateResourceReportsCreatedAndVersionIdAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            (WotRegistryResourceClient resource, string versionId, bool created) = await group
                .GetOrCreateResourceAsync("sensor", "1.0.0").ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(versionId, Is.EqualTo("1.0.0"));
            Assert.That(resource.ResourceId, Is.EqualTo("sensor"));
            Assert.That(resource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));

            (_, _, bool createdAgain) = await group
                .GetOrCreateResourceAsync("sensor", "1.0.0").ConfigureAwait(false);
            Assert.That(createdAgain, Is.False);
        }

        [Test]
        public async Task OpenResourceThrowsForUnknownResourceAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            Assert.That(
                () => group.OpenResourceAsync("does-not-exist").AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task OpenResourceRejectsEmptyResourceIdAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            Assert.That(
                () => group.OpenResourceAsync(string.Empty).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("resourceId"));
        }

        [Test]
        public async Task ThingModelGroupCreatesThingModelResourceClientAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingModelGroupAsync().ConfigureAwait(false);

            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("model").ConfigureAwait(false);

            Assert.That(resource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
            Assert.That(resource.Proxy, Is.InstanceOf<ThingModelFileTypeClient>());
        }

        [Test]
        public async Task UploadNewVersionRoundTripsChunkedContentAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("sensor").ConfigureAwait(false);

            byte[] content = Encoding.UTF8.GetBytes(new string('x', 10_000));
            await resource.UploadNewVersionAsync(ByteString.From(content), chunkSize: 4096)
                .ConfigureAwait(false);

            ByteString downloaded = await resource.DownloadAsync(chunkSize: 4096).ConfigureAwait(false);
            Assert.That(downloaded.ToArray(), Is.EqualTo(content));
        }

        [Test]
        public async Task RetryingContentlessPlaceholderUploadsWithoutAllocatingAnotherVersionAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient first, string firstVersionId, bool firstCreated) =
                await group.GetOrCreateResourceAsync("retry").ConfigureAwait(false);
            (WotRegistryResourceClient retry, string retryVersionId, bool retryCreated) =
                await group.GetOrCreateResourceAsync("retry").ConfigureAwait(false);

            await retry.UploadNewVersionAsync(ByteString.From(Encoding.UTF8.GetBytes("retry")))
                .ConfigureAwait(false);

            NodeId createResourceMethodId = mock.ResolveMethodId(
                XRegistry.MethodIds.GroupType_CreateResource);
            Assert.Multiple(() =>
            {
                Assert.That(firstCreated, Is.True);
                Assert.That(retryCreated, Is.False);
                Assert.That(retryVersionId, Is.EqualTo(firstVersionId));
                Assert.That(
                    mock.Capture.Count(request => request.MethodId == createResourceMethodId),
                    Is.Zero,
                    "The retry must fill the returned placeholder rather than allocate a Version.");
                Assert.That(first.ResourceNodeId, Is.EqualTo(retry.ResourceNodeId));
                Assert.That(retry.HasContent, Is.True);
            });
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public async Task UnavailableContentStatePreservesLegacyUploadBehaviorAsync(
            bool exposeContentDigest,
            bool returnNullContentDigest)
        {
            var mock = new WotRegistrySessionMock
            {
                ExposeContentDigest = exposeContentDigest,
                ReturnNullContentDigest = returnNullContentDigest
            };
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            _ = await group.GetOrCreateResourceAsync("legacy").ConfigureAwait(false);
            (WotRegistryResourceClient retry, _, bool created) =
                await group.GetOrCreateResourceAsync("legacy").ConfigureAwait(false);

            await retry.UploadNewVersionAsync(ByteString.From(Encoding.UTF8.GetBytes("legacy")))
                .ConfigureAwait(false);

            NodeId createResourceMethodId = mock.ResolveMethodId(
                XRegistry.MethodIds.GroupType_CreateResource);
            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(retry.HasContent, Is.Null);
                Assert.That(
                    mock.Capture.Count(request => request.MethodId == createResourceMethodId),
                    Is.EqualTo(1),
                    "Without content state the client must retain its prior allocation behavior.");
            });
        }

        [Test]
        public async Task ValidateSetEnabledSetDefaultVersionAndDeleteInvokeExpectedMethodsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("sensor").ConfigureAwait(false);

            WoTValidationOutcomeDataType outcome = await resource.ValidateAsync().ConfigureAwait(false);
            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));

            await resource.SetEnabledAsync(false, expectedEpoch: 0).ConfigureAwait(false);
            await resource.SetDefaultVersionAsync("2.0.0", expectedEpoch: 0).ConfigureAwait(false);
            await resource.DeleteAsync(expectedEpoch: 0).ConfigureAwait(false);

            Assert.That(
                () => group.OpenResourceAsync("sensor").AsTask(),
                Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task ValidateThrowsServiceResultExceptionForEmptyCallResultsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("sensor").ConfigureAwait(false);
            mock.ReturnEmptyCallResultsOnce = true;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await resource.ValidateAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshAllReportsFailuresWithoutThrowingAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            await group.GetOrCreateResourceAsync("good").ConfigureAwait(false);
            await group.GetOrCreateResourceAsync("bad").ConfigureAwait(false);
            mock.InvalidResourceIds.Add("bad");

            WotRegistryRefreshResult result = await client
                .RefreshAllAsync(requestId: "req-1")
                .ConfigureAwait(false);

            Assert.That(result.Summary.RequestId, Is.EqualTo("req-1"));
            Assert.That(result.HasFailures, Is.True);
            Assert.That(result.EnsureSuccess, Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task RefreshAllSucceedsWhenNothingFailedAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            await group.GetOrCreateResourceAsync("good").ConfigureAwait(false);

            WotRegistryRefreshResult result = await client.RefreshAllAsync().ConfigureAwait(false);

            Assert.That(result.HasFailures, Is.False);
            Assert.That(result.EnsureSuccess, Throws.Nothing);
        }

        [Test]
        public async Task RefreshAllThrowsServiceResultExceptionForEmptyCallResultsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            await group.GetOrCreateResourceAsync("good").ConfigureAwait(false);
            mock.ReturnEmptyCallResultsOnce = true;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void ConstructorRejectsANullSession()
        {
            Assert.That(
                () => new WotRegistryClient(null!, new NodeId("WoTRegistry", 1), CreateTelemetry()),
                Throws.ArgumentNullException
                    .With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void ConstructorRejectsANullRegistryObjectId()
        {
            var mock = new WotRegistrySessionMock();

            Assert.That(
                () => new WotRegistryClient(mock.Session, NodeId.Null, CreateTelemetry()),
                Throws.ArgumentException
                    .With.Property("ParamName").EqualTo("registryObjectId"));
        }

        [Test]
        public void ForServerAsyncRejectsNullArguments()
        {
            var mock = new WotRegistrySessionMock();

            Assert.That(
                () => WotRegistryClient.ForServerAsync(null!, CreateTelemetry()).AsTask(),
                Throws.ArgumentNullException
                    .With.Property("ParamName").EqualTo("session"));
            Assert.That(
                () => WotRegistryClient.ForServerAsync(mock.Session, null!).AsTask(),
                Throws.ArgumentNullException
                    .With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task GroupMethodsRejectAnEmptyGroupIdAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            Assert.That(
                () => client.CreateGroupAsync("   ").AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("groupId"));
            Assert.That(
                () => client.GetOrCreateGroupAsync(string.Empty).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("groupId"));
            Assert.That(
                () => client.OpenGroupAsync(null!).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("groupId"));
        }

        [Test]
        public async Task GetOrCreateThingModelGroupReportsCreatedOnlyOnceAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            (WotRegistryGroupClient group, bool created) = await client
                .GetOrCreateThingModelGroupAsync().ConfigureAwait(false);
            (_, bool createdAgain) = await client
                .GetOrCreateThingModelGroupAsync().ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False);
            Assert.That(group.GroupId, Is.EqualTo(WotRegistryClient.ThingModelsGroupId));
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
        }

        [Test]
        public async Task CreateGroupThrowsWhenTheServerReportsNoTypeDefinitionAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.ReturnNoTypeDefinitionOnce = true;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CreateGroupAsync("orphan").ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(ex.Message, Does.Contain("TypeDefinition"));
        }

        [Test]
        public async Task CreateGroupThrowsWhenTheTypeDefinitionIsUnrecognisedAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.TypeDefinitionOverride = new NodeId("SomeOtherGroupType", mock.WotConNs);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CreateGroupAsync("weird").ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(ex.Message, Does.Contain("unrecognised"));
        }

        [Test]
        public async Task RefreshThrowsWhenTheServerReportsABadStatusAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.FailNextCallOn[mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh)] =
                StatusCodes.BadNotSupported;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task RefreshThrowsWhenTheServerReturnsTooFewOutputArgumentsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ => [new Variant(1u)]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshThrowsWhenTheOutputArgumentsAreNotStructuresAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ => [new Variant(1), new Variant(2), new Variant("three")]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshDecodesBinaryEncodedOutputArgumentsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            IServiceMessageContext context = mock.Session.MessageContext;
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = "binary",
                Generation = 9,
                Outcome = WoTOutcomeEnum.Success,
                Total = 1,
                Succeeded = 1
            };
            var loadResult = new WoTResourceLoadResultDataType
            {
                GroupId = WotRegistryClient.ThingDescriptionsGroupId,
                ResourceId = "sensor",
                VersionId = "1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Outcome = WoTOutcomeEnum.Success,
                LoadState = WoTLoadStateEnum.Active
            };
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ =>
                [
                    new Variant(OpaqueBinaryExtension(summary, context)),
                    new Variant(new[] { OpaqueBinaryExtension(loadResult, context) }.ToArrayOf()),
                    new Variant(9u)
                ]);

            WotRegistryRefreshResult result = await client.RefreshAllAsync().ConfigureAwait(false);

            Assert.That(result.Summary.RequestId, Is.EqualTo("binary"));
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].ResourceId, Is.EqualTo("sensor"));
            Assert.That(result.NewGeneration, Is.EqualTo(9u));
            Assert.That(result.HasFailures, Is.False);
        }

        [Test]
        public async Task RefreshThrowsWhenTheSummaryExtensionObjectHasNoBodyAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ =>
                [
                    new Variant(new ExtensionObject(s_opaqueTypeId)),
                    new Variant(new[] { new ExtensionObject(s_opaqueTypeId) }.ToArrayOf()),
                    new Variant(1u)
                ]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshThrowsWhenTheSummaryBinaryBodyIsMalformedAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            var truncated = new ExtensionObject(
                new WoTRefreshSummaryDataType().TypeId,
                ByteString.From([0x40, 0x00, 0x00, 0x00]));
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ => [new Variant(truncated), new Variant(2), new Variant(1u)]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshThrowsWhenAPerResourceResultCannotBeDecodedAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            IServiceMessageContext context = mock.Session.MessageContext;
            var summary = new WoTRefreshSummaryDataType { RequestId = "partial" };
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ =>
                [
                    new Variant(OpaqueBinaryExtension(summary, context)),
                    new Variant(new[] { new ExtensionObject(s_opaqueTypeId) }.ToArrayOf()),
                    new Variant(1u)
                ]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshAllAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task RefreshResultReportsFailureFromAPerResourceOutcomeAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = "mixed",
                Outcome = WoTOutcomeEnum.Success,
                Total = 1,
                Skipped = 1
            };
            ArrayOf<WoTResourceLoadResultDataType> results = new[]
            {
                new WoTResourceLoadResultDataType
                {
                    GroupId = WotRegistryClient.ThingDescriptionsGroupId,
                    ResourceId = "rejected",
                    Outcome = WoTOutcomeEnum.Rejected,
                    LoadState = WoTLoadStateEnum.Failed
                }
            }.ToArrayOf();
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTRegistryType_Refresh),
                _ =>
                [
                    Variant.FromStructure(summary),
                    Variant.FromStructure(results),
                    new Variant(4u)
                ]);

            WotRegistryRefreshResult result = await client.RefreshAllAsync().ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(4u));
            Assert.That(result.HasFailures, Is.True);
            Assert.That(() => result.EnsureSuccess(), Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task ResourceMethodsRejectAnEmptyResourceIdAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            Assert.That(
                () => group.CreateResourceAsync("   ").AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("resourceId"));
            Assert.That(
                () => group.GetOrCreateResourceAsync(string.Empty).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("resourceId"));
        }

        [Test]
        public async Task CreateResourceReturnsAWrapperAndTheAssignedVersionAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingModelGroupAsync().ConfigureAwait(false);

            (WotRegistryResourceClient resource, string versionId) = await group
                .CreateResourceAsync("model", "3.1.4").ConfigureAwait(false);

            Assert.That(versionId, Is.EqualTo("3.1.4"));
            Assert.That(resource.ResourceId, Is.EqualTo("model"));
            Assert.That(resource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
            Assert.That(resource.Proxy, Is.InstanceOf<ThingModelFileTypeClient>());
        }

        [Test]
        public async Task OpenResourceResolvesAnExistingResourceAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient created, _) = await group
                .CreateResourceAsync("sensor").ConfigureAwait(false);

            WotRegistryResourceClient opened = await group
                .OpenResourceAsync("sensor").ConfigureAwait(false);

            Assert.That(opened.ResourceNodeId, Is.EqualTo(created.ResourceNodeId));
            Assert.That(opened.GroupId, Is.EqualTo(WotRegistryClient.ThingDescriptionsGroupId));
        }

        [Test]
        public async Task DeleteGroupRemovesItFromTheRegistryAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            await group.DeleteAsync(expectedEpoch: 0).ConfigureAwait(false);

            Assert.That(
                () => client.OpenGroupAsync(WotRegistryClient.ThingDescriptionsGroupId).AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task ResourceClientExposesItsGroupSessionAndTelemetryAsync()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, telemetry)
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);

            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("sensor").ConfigureAwait(false);

            Assert.That(resource.GroupId, Is.EqualTo(WotRegistryClient.ThingDescriptionsGroupId));
            Assert.That(resource.Session, Is.SameAs(mock.Session));
            Assert.That(resource.Telemetry, Is.SameAs(telemetry));
        }

        [Test]
        public async Task ValidateThrowsWhenTheServerReportsABadStatusAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryResourceClient resource = await CreateResourceAsync(mock).ConfigureAwait(false);
            mock.FailNextCallOn[mock.ResolveMethodId(MethodIds.WoTDocumentType_Validate)] =
                StatusCodes.BadUserAccessDenied;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await resource.ValidateAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ValidateThrowsWhenTheOutcomeIsNotAStructureAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryResourceClient resource = await CreateResourceAsync(mock).ConfigureAwait(false);
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTDocumentType_Validate),
                _ => [new Variant(42)]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await resource.ValidateAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task ValidateDecodesABinaryEncodedOutcomeAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryResourceClient resource = await CreateResourceAsync(mock).ConfigureAwait(false);
            IServiceMessageContext context = mock.Session.MessageContext;
            var outcome = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Failed
            };
            mock.OverrideMethod(
                mock.ResolveMethodId(MethodIds.WoTDocumentType_Validate),
                _ => [new Variant(OpaqueBinaryExtension(outcome, context))]);

            WoTValidationOutcomeDataType decoded = await resource
                .ValidateAsync().ConfigureAwait(false);

            Assert.That(decoded.FormatValidated, Is.True);
            Assert.That(decoded.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task UploadAndDownloadStreamRoundTripsContentAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryResourceClient resource = await CreateResourceAsync(mock).ConfigureAwait(false);
            byte[] content = Encoding.UTF8.GetBytes(new string('s', 5000));

            using var source = new MemoryStream(content);
            await resource.UploadNewVersionAsync(source, chunkSize: 1024).ConfigureAwait(false);

            using var destination = new MemoryStream();
            await resource.DownloadToAsync(destination, chunkSize: 1024).ConfigureAwait(false);

            Assert.That(destination.ToArray(), Is.EqualTo(content));
        }

        private static async Task<WotRegistryResourceClient> CreateResourceAsync(
            WotRegistrySessionMock mock)
        {
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            WotRegistryGroupClient group = await client
                .CreateThingDescriptionGroupAsync().ConfigureAwait(false);
            (WotRegistryResourceClient resource, _, _) = await group
                .GetOrCreateResourceAsync("sensor").ConfigureAwait(false);
            return resource;
        }

        /// <summary>
        /// Wraps <paramref name="value"/> as a binary-encoded
        /// <see cref="ExtensionObject"/> whose type id the message context
        /// cannot resolve, which is how a server that publishes its own
        /// encoding ids appears to the client.
        /// </summary>
        private static ExtensionObject OpaqueBinaryExtension(
            IEncodeable value,
            IServiceMessageContext context)
        {
            using var encoder = new BinaryEncoder(context);
            value.Encode(encoder);
            return new ExtensionObject(s_opaqueTypeId, ByteString.From(encoder.CloseAndReturnBuffer()!));
        }

        private static readonly ExpandedNodeId s_opaqueTypeId = new NodeId("OpaqueEncoding", 0);
    }
}
