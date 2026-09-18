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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using WotNodeSetConverterOptions = Opc.Ua.Wot.WotNodeSetConverterOptions;
using WotProjectionCompatibilityMode = Opc.Ua.Wot.WotProjectionCompatibilityMode;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotRefreshCaptureContractTests
    {
        [Test]
        public async Task RefreshFreezesRequestBeforePreparation()
        {
            using var registry = new WotRegistryService();
            WotResource resource = await RegisterAsync(registry, "selected");
            var request = new WotRefreshRequest
            {
                RequestId = "admitted",
                Selection = [Selector(resource)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            };
            var converter = new Mock<IWotDocumentConverter>(MockBehavior.Strict);
            var inner = new FakeWotDocumentConverter();
            converter.Setup(instance => instance.ConvertAsync(
                It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((WotResource selected, ByteString content, WotRegistrySnapshot snapshot,
                    IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                {
                    request.RequestId = "mutated";
                    request.Options.Atomicity = WoTAtomicityEnum.PerRegistry;
                    request.Selection[0].ResourceId = "not-selected";
                    return inner.ConvertAsync(selected, content, snapshot, contents, token);
                });
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, documentConverter: converter.Object)
            {
                RegistryOrigin = new WotRegistryOrigin("urn:registry:captured-contract")
            };

            WotRefreshResult result = await coordinator.RefreshAsync(request);
            WotResourceVersion version = registry.Current.FindResource(resource.GroupId, resource.ResourceId)!
                .FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Summary.RequestId, Is.EqualTo("admitted"));
                Assert.That(result.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
                Assert.That(result.Results.Single().ResourceId, Is.EqualTo("selected"));
                Assert.That(result.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(version.LastDependencyAttempt!.RequestId, Is.EqualTo("admitted"));
                Assert.That(version.DependencySnapshot!.RequestId, Is.EqualTo("admitted"));
                Assert.That(host.AddCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void CapturedRequestDoesNotExposeCallerOrReturnedSelectorMutation()
        {
            var selector = new WoTResourceSelectorDataType
            {
                GroupId = "things",
                ResourceId = "selected",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription
            };
            var request = new WotRefreshRequest
            {
                RequestId = "admitted",
                Selection = [selector],
                Options = new WoTRefreshOptionsDataType { Force = true }
            };
            WotCapturedRefreshRequest captured = WotCapturedRefreshRequest.Capture(request);
            selector.ResourceId = "caller-change";
            request.RequestId = "caller-change";
            request.Options.Force = false;
            captured.Selection[0].VersionId = "returned-change";

            Assert.That(captured.RequestId, Is.EqualTo("admitted"));
            Assert.That(captured.Selection[0].ResourceId, Is.EqualTo("selected"));
            Assert.That(captured.Selection[0].VersionId, Is.EqualTo("v1"));
            Assert.That(captured.Force, Is.True);
            Assert.That(captured.SelectsAll, Is.False);
        }

        [Test]
        public async Task PublicCaptureIsSideEffectFreeAndProducesIndependentPlanValues()
        {
            using var registry = new WotRegistryService();
            WotResource resource = await RegisterAsync(registry, "selected");
            long before = registry.Current.Generation;
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, documentConverter: new FakeWotDocumentConverter())
            {
                RegistryOrigin = new WotRegistryOrigin("urn:registry:original")
            };
            var request = new WotRefreshRequest
            {
                RequestId = "capture",
                Selection = [Selector(resource)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            };
            using WotRefreshCapture capture = await coordinator.CaptureAsync(request);
            request.RequestId = "later";
            coordinator.RegistryOrigin = new WotRegistryOrigin("urn:registry:later");
            WoTRefreshPlanDataType plan = capture.CreateRefreshPlan(WoTAtomicityEnum.PerGroup, 1);
            Assert.That(plan.RequestId, Is.EqualTo("capture"));
            Assert.That(plan.PreparationGeneration, Is.Zero);
            Assert.That(plan.RequestedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerGroup));
            Assert.That(plan.UnitCount, Is.EqualTo(1u));
            plan.RequestId = "returned-change";
            Assert.That(capture.CreateRefreshPlan(WoTAtomicityEnum.PerResource, 1).RequestId, Is.EqualTo("capture"));
            Assert.That(capture.RegistryOrigin!.OriginUri, Is.EqualTo("urn:registry:original"));
            Assert.That(capture.Inputs.Selection.Count, Is.EqualTo(1));
            Assert.That(capture.GetRegistryInputDigest(capture.Inputs.Closures[0]).Length, Is.EqualTo(32));
            Assert.That(registry.Current.Generation, Is.EqualTo(before));
            Assert.That(coordinator.Generation, Is.Zero);
            Assert.That(host.AddCount, Is.Zero);
            Assert.That(registry.Current.FindResource(resource.GroupId, resource.ResourceId)!
                .DefaultVersion!.DependencySnapshot, Is.Null);
        }

        [Test]
        public async Task PublicCaptureRejectsStaleGenerationAndDryRunPlanPublication()
        {
            using var registry = new WotRegistryService();
            WotResource resource = await RegisterAsync(registry, "selected");
            using var coordinator = new WotMaterializationCoordinator(
                registry, new FakeWotProjectionHost(), documentConverter: new FakeWotDocumentConverter());
            await Assert.ThatAsync(() => coordinator.CaptureAsync(
                    new WotRefreshRequest { ExpectedGeneration = 1 }).AsTask(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState));
            using WotRefreshCapture capture = await coordinator.CaptureAsync(new WotRefreshRequest
            {
                Selection = [Selector(resource)],
                Options = new WoTRefreshOptionsDataType { DryRun = true }
            });
            Assert.That(() => capture.CreateRefreshPlan(WoTAtomicityEnum.PerClosure, 1),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState));
            Assert.That(coordinator.Generation, Is.Zero);
        }

        [Test]
        public async Task PublicCaptureRejectsAnotherCapturesClosure()
        {
            using var registry = new WotRegistryService();
            WotResource resource = await RegisterAsync(registry, "selected");
            using var coordinator = new WotMaterializationCoordinator(
                registry, new FakeWotProjectionHost(), documentConverter: new FakeWotDocumentConverter());
            var request = new WotRefreshRequest { Selection = [Selector(resource)] };
            using WotRefreshCapture first = await coordinator.CaptureAsync(request);
            using WotRefreshCapture second = await coordinator.CaptureAsync(request);
            Assert.That(() => first.GetRegistryInputDigest(second.Inputs.Closures[0]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => first.CreateRefreshPlan((WoTAtomicityEnum)(-1), 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(WotProjectionCompatibilityMode.None, WotProjectionCompatibilityMode.DraftProjection11)]
        [TestCase(WotProjectionCompatibilityMode.DraftProjection11, WotProjectionCompatibilityMode.None)]
        public async Task CaptureKeepsProjectionCompatibilityInItsImmutableInputDigest(
            WotProjectionCompatibilityMode initial,
            WotProjectionCompatibilityMode changed)
        {
            using var registry = new WotRegistryService();
            WotResource resource = await RegisterAsync(registry, "selected");
            var options = new WotNodeSetConverterOptions { ProjectionCompatibilityMode = initial };
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, converterOptions: options, documentConverter: new FakeWotDocumentConverter());
            var request = new WotRefreshRequest { Selection = [Selector(resource)] };
            using WotRefreshCapture first = await coordinator.CaptureAsync(request);
            ByteString firstDigest = first.GetRegistryInputDigest(first.Inputs.Closures[0]);

            options.ProjectionCompatibilityMode = changed;
            using WotRefreshCapture second = await coordinator.CaptureAsync(request);
            ByteString secondDigest = second.GetRegistryInputDigest(second.Inputs.Closures[0]);

            Assert.That(first.GetRegistryInputDigest(first.Inputs.Closures[0]), Is.EqualTo(firstDigest));
            Assert.That(secondDigest, Is.Not.EqualTo(firstDigest));
            options.ProjectionCompatibilityMode = initial;
            using WotRefreshCapture restored = await coordinator.CaptureAsync(request);
            Assert.That(restored.GetRegistryInputDigest(restored.Inputs.Closures[0]), Is.EqualTo(firstDigest));
            Assert.That(second.GetRegistryInputDigest(second.Inputs.Closures[0]), Is.EqualTo(secondDigest));
            Assert.That(coordinator.Generation, Is.Zero);
            Assert.That(host.AddCount, Is.Zero);
        }

        [Test]
        public void CaptureProviderUsesTheRegisteredCoordinator()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IWotProjectionHost>(new FakeWotProjectionHost());
            services.AddSingleton(Mock.Of<Opc.Ua.Server.INodeManagerLifecycle>());
            services.AddOpcUa().AddWotRegistryServer();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IWotRefreshCaptureProvider>(),
                Is.SameAs(provider.GetRequiredService<WotMaterializationCoordinator>()));
        }

        [Test]
        public void CaptureContractRunsOnRequestedRuntime()
        {
            string? expected = Environment.GetEnvironmentVariable("WOT_B2_EXPECTED_RUNTIME");
            Assert.That(expected, Is.Not.Null.And.Not.Empty, "The guarded proof runner must pin the actual runtime.");
            Assert.That(Environment.Version.ToString(), Is.EqualTo(expected));
        }

        private static async Task<WotResource> RegisterAsync(WotRegistryService registry, string resourceId)
        {
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things",
                ResourceId = resourceId,
                VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    $$"""{"id":"urn:{{resourceId}}","title":"{{resourceId}}"}"""))
            });
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
            return result.Resource!;
        }

        private static WoTResourceSelectorDataType Selector(WotResource resource)
        {
            return new WoTResourceSelectorDataType
            {
                Kind = resource.Kind,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId
            };
        }
    }
}
