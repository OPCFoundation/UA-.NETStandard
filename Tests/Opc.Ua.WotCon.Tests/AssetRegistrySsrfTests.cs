/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// SSRF integration tests against the public AssetRegistry
    /// surface: <c>CreateAssetForEndpointAsync</c> and
    /// <c>ConnectionTestAsync</c> must reject hostile endpoint
    /// strings *before* any <see cref="IWotAssetDiscoveryProvider"/>
    /// call is made.
    /// </summary>
    /// <remarks>
    /// These tests construct the registry with a null node manager —
    /// only the two methods under test plus the cancellation token
    /// they accept are exercised; neither path dereferences
    /// <c>m_manager</c> for the rejection cases.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AssetRegistrySsrfTests
    {
        [TestCase("file:///etc/passwd")]
        [TestCase("javascript:alert(1)")]
        [TestCase("http://127.0.0.1/")]
        [TestCase("http://[::1]/")]
        [TestCase("http://localhost/")]
        [TestCase("http://10.0.0.1/")]
        [TestCase("http://192.168.1.1/")]
        // AWS / Azure Instance Metadata Service — the classic SSRF
        // target that motivates this entire hardening.
        [TestCase("http://169.254.169.254/latest/meta-data/")]
        public async Task CreateAssetForEndpoint_RejectsHostilePolicyViolation_WithoutInvokingDiscovery(
            string hostile)
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>(MockBehavior.Strict);
            await using AssetRegistry registry = MakeRegistry(mockDiscovery.Object);

            (ServiceResult status, NodeId assetId) = await registry
                .CreateAssetForEndpointAsync("asset-001", hostile, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.AnyOf(
                StatusCodes.BadSecurityChecksFailed, StatusCodes.BadInvalidArgument),
                $"'{hostile}' must be rejected, got {status}");
            Assert.That(assetId.IsNull, Is.True);
            mockDiscovery.Verify(
                p => p.CreateThingDescriptionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never(),
                "Discovery provider must NOT be invoked for a hostile endpoint.");
        }

        [TestCase("file:///etc/passwd")]
        [TestCase("http://127.0.0.1/")]
        [TestCase("http://10.0.0.1/")]
        [TestCase("http://169.254.169.254/")]
        public async Task ConnectionTest_RejectsHostilePolicyViolation_WithoutInvokingDiscovery(
            string hostile)
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>(MockBehavior.Strict);
            await using AssetRegistry registry = MakeRegistry(mockDiscovery.Object);

            (ServiceResult status, bool success, string text) = await registry
                .ConnectionTestAsync(hostile, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.AnyOf(
                StatusCodes.BadSecurityChecksFailed, StatusCodes.BadInvalidArgument),
                $"'{hostile}' must be rejected, got {status}");
            Assert.That(success, Is.False);
            Assert.That(text, Is.EqualTo(string.Empty));
            mockDiscovery.Verify(
                p => p.TestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never(),
                "Discovery provider must NOT be invoked for a hostile endpoint.");
        }

        [Test]
        public async Task ConnectionTest_TimesOutWhenProviderHangsPastPolicyTimeout()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(p => p.TestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken inner) =>
                {
                    // Hang past the policy timeout. Honor inner-token so the
                    // policy can actually cancel us instead of the test
                    // blocking forever.
                    await Task.Delay(TimeSpan.FromSeconds(30), inner).ConfigureAwait(false);
                    return (true, "Healthy");
                });
            var options = new WotConnectivityServerOptions
            {
                Discovery = mockDiscovery.Object,
                AssetEndpointPolicy = new AssetEndpointPolicy
                {
                    MaxOperationTimeout = TimeSpan.FromMilliseconds(100)
                }
            };

            await using AssetRegistry registry = MakeRegistry(options);

            (ServiceResult status, bool success, _) = await registry
                .ConnectionTestAsync("https://example.com/td", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(success, Is.False);
        }

        [Test]
        public async Task ConnectionTest_CallerCancellation_Propagates_NotMappedToBadTimeout()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(p => p.TestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken inner) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), inner).ConfigureAwait(false);
                    return (true, "ok");
                });
            await using AssetRegistry registry = MakeRegistry(mockDiscovery.Object);

            using var cts = new CancellationTokenSource();
            Task<(ServiceResult, bool, string)> call = registry
                .ConnectionTestAsync("https://example.com/td", cts.Token)
                .AsTask();
            cts.Cancel();

            Assert.ThrowsAsync(Is.AssignableTo<OperationCanceledException>(), async () =>
                await call.ConfigureAwait(false));
        }

        [Test]
        public async Task ConnectionTest_ZeroTimeoutDisablesPolicyTimeout()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(p => p.TestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(bool, string)>((true, "ok")));
            var options = new WotConnectivityServerOptions
            {
                Discovery = mockDiscovery.Object,
                AssetEndpointPolicy = new AssetEndpointPolicy
                {
                    MaxOperationTimeout = TimeSpan.Zero
                }
            };
            await using AssetRegistry registry = MakeRegistry(options);

            (ServiceResult status, bool success, string text) = await registry
                .ConnectionTestAsync("https://example.com/td", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(success, Is.True);
            Assert.That(text, Is.EqualTo("ok"));
        }

        // ----------------------------------------------------------------
        // Harness — only the two SSRF-relevant methods need a registry;
        // neither dereferences the node manager when the endpoint is
        // rejected up front, so we pass null! for the manager.
        // ----------------------------------------------------------------

        private static AssetRegistry MakeRegistry(IWotAssetDiscoveryProvider discovery)
            => MakeRegistry(new WotConnectivityServerOptions { Discovery = discovery });

        private static AssetRegistry MakeRegistry(WotConnectivityServerOptions options)
            => new(manager: null!, options, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }
}
