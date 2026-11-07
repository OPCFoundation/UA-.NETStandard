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

#if XREGISTRY_HTTP_MODERN
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpAddressResolutionTests
    {
        [TestCase("malformed", HttpStatusCode.BadGateway)]
        [TestCase("network", HttpStatusCode.BadGateway)]
        [TestCase("not-found", HttpStatusCode.NotFound)]
        [TestCase("canonical-forbidden", HttpStatusCode.Forbidden)]
        public async Task AliasResolutionDistinguishesBackendFailureFromExplicitRejectionAsync(
            string failure, HttpStatusCode expected)
        {
            var endpoint = new Mock<IXRegistryEndpoint>();
            endpoint.Setup(value => value.InspectAsync(
                It.IsAny<XRegistryCallContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FakeRegistryEndpoint().Description);
            endpoint.As<IXRegistryAddressResolver>().Setup(value => value.ResolveAddressAsync(
                It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .Returns<XRegistryRequest, CancellationToken>((request, _) => failure switch
                {
                    "malformed" => throw new InvalidDataException("private upstream malformed xid"),
                    "network" => throw new HttpRequestException("private upstream transport"),
                    "not-found" => new ValueTask<XRegistryAddressResolution>(new XRegistryAddressResolution(
                        request, new XRegistryResponse(404)
                        {
                            Error = new XRegistryError("not_found", "Missing alias.")
                        })),
                    _ => new ValueTask<XRegistryAddressResolution>(
                        new XRegistryAddressResolution(request.AtResolvedPath("/schemagroups/g")))
                });
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint.Object,
                HttpHostTestData.OpenOptions with
                {
                    AuthorizeAsync = (_, request, _) => new ValueTask<bool>(request.Path != "/schemagroups/g")
                }).ConfigureAwait(false);
            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry/_s/1", UriKind.Relative)).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(expected), body);
                Assert.That(body, Does.Not.Contain("private upstream"));
            });
            endpoint.Verify(value => value.ExecuteAsync(
                It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
#endif
