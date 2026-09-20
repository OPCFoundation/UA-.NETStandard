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
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

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
