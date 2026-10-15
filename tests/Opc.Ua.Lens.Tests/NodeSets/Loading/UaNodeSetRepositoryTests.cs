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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Export;
using UaLens.NodeSets.Loading;

namespace UaLens.Tests.NodeSets.Loading
{
    [TestFixture]
    internal sealed class UaNodeSetRepositoryTests
    {
        [Test]
        public async Task MapsExactMetadataIncludingPackageMetadataAndCachesCatalogAndDocuments()
        {
            var requests = new List<string>();
            using HttpClient client = Client((request, _) =>
            {
                string url = request.RequestUri!.AbsoluteUri;
                requests.Add(url);
                Assert.That(request.Headers.UserAgent.ToString(), Does.Contain("UaLens"));
                Assert.That(request.RequestUri.Host, Is.AnyOf("api.github.com", "raw.githubusercontent.com"));
                if (url == UaNodeSetRepository.TreeUrl)
                {
                    Assert.That(request.Headers.Contains("X-GitHub-Api-Version"), Is.True);
                    return Response(Tree("DI/Device.NodeSet2.xml", "DI/Device.PackageMetadata.NodeSet2.xml"));
                }
                Assert.That(url, Does.Contain("/" + Revision + "/"));
                if (url.EndsWith("Device.NodeSet2.xml", StringComparison.Ordinal))
                {
                    Assert.That(request.Headers.Range, Is.Not.Null);
                    return Response(NodeSetLoaderTests.Model("http://opcfoundation.org/UA/DI/"));
                }
                return Response(NodeSetLoaderTests.Model("http://opcfoundation.org/UA/DI/PackageMetadata/"));
            });
            using var repository = new UaNodeSetRepository(client);
            var requirement = new NodeSetRequirement("http://opcfoundation.org/UA/DI/PackageMetadata/");
            NodeSetDocument? first = await repository.FindAsync(requirement).ConfigureAwait(false);
            int firstCount = requests.Count;
            NodeSetDocument? second = await repository.FindAsync(requirement).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Source, Does.EndWith("Device.PackageMetadata.NodeSet2.xml"));
            Assert.That(second, Is.SameAs(first));
            Assert.That(requests, Has.Count.EqualTo(firstCount));
            Assert.That(firstCount, Is.EqualTo(4));
        }

        [Test]
        public async Task NonstandardModelUrisAreDiscoveredButNeverRequestedAsUrls()
        {
            var hosts = new List<string>();
            using HttpClient client = Client((request, _) =>
            {
                hosts.Add(request.RequestUri!.Host);
                return Response(request.RequestUri.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Tree("Unexpected/Actual.NodeSet2.xml") :
                    NodeSetLoaderTests.Model("https://vendor.invalid/custom/model"));
            });
            using var repository = new UaNodeSetRepository(client);
            NodeSetDocument? document = await repository.FindAsync(
                new NodeSetRequirement("https://vendor.invalid/custom/model")).ConfigureAwait(false);
            Assert.That(document, Is.Not.Null);
            Assert.That(hosts, Does.Not.Contain("vendor.invalid"));
        }

        [Test]
        public async Task NonstandardNotFoundRequiresACompletedCatalogAndIsCached()
        {
            int calls = 0;
            using HttpClient client = Client((request, _) =>
            {
                calls++;
                return Response(request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Tree("Anything/Model.NodeSet2.xml") : NodeSetLoaderTests.Model("urn:published"));
            });
            using var repository = new UaNodeSetRepository(client);
            var missing = new NodeSetRequirement("https://nonstandard.invalid/model");
            Assert.That(await repository.FindAsync(missing).ConfigureAwait(false), Is.Null);
            Assert.That(await repository.FindAsync(missing).ConfigureAwait(false), Is.Null);
            Assert.That(calls, Is.EqualTo(2));
        }

        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.Redirect)]
        public void HttpFailuresAreNotReportedAsNotFound(HttpStatusCode status)
        {
            using HttpClient client = Client((_, _) => new HttpResponseMessage(status));
            using var repository = new UaNodeSetRepository(client);
            HttpRequestException? exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false));
            Assert.That(exception!.StatusCode, Is.EqualTo(status));
        }

        [Test]
        public async Task MissingCandidateIsSkippedButMissingRepositoryTreeIsAnError()
        {
            using HttpClient client = Client((request, _) =>
                request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Response(Tree("Gone/Model.NodeSet2.xml")) : new HttpResponseMessage(HttpStatusCode.NotFound));
            using var repository = new UaNodeSetRepository(client);
            Assert.That(await repository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false),
                Is.Null);
            using HttpClient missing = Client((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var missingRepository = new UaNodeSetRepository(missing);
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await missingRepository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false));
        }

        [Test]
        public void TruncatedTreeCannotProduceNotFound()
        {
            using HttpClient client = Client((_, _) => Response(Tree().Replace(
                "\"truncated\":false", "\"truncated\":true", StringComparison.Ordinal)));
            using var repository = new UaNodeSetRepository(client);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("truncated"));
        }

        [Test]
        public void DownloadedModelMustMatchItsDiscoveryHeader()
        {
            using HttpClient client = Client((request, _) =>
                Response(request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Tree("Model.NodeSet2.xml") :
                    NodeSetLoaderTests.Model(request.Headers.Range is null ? "urn:wrong" : "urn:expected")));
            using var repository = new UaNodeSetRepository(client);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:expected")).ConfigureAwait(false));
        }

        [Test]
        public void DownloadedRevisionMustMeetTheMinimum()
        {
            using HttpClient client = Client((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl)
                {
                    return Response(Tree("Model.NodeSet2.xml"));
                }
                int year = request.Headers.Range is null ? 2020 : 2025;
                return Response(NodeSetLoaderTests.Xml(
                    $"""<Model ModelUri="urn:model" PublicationDate="{year}-01-01T00:00:00Z"/>"""));
            });
            using var repository = new UaNodeSetRepository(client);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement(
                    "urn:model", PublicationDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task HeaderReadsStayBoundedEvenWhenServerIgnoresRange()
        {
            CountingStream? body = null;
            using HttpClient client = Client((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl)
                {
                    return Response(Tree("Large.NodeSet2.xml"));
                }
                Assert.That(request.Headers.Range, Is.Not.Null);
                byte[] prefix = Encoding.UTF8.GetBytes(NodeSetLoaderTests.Model("urn:other"));
                byte[] bytes = new byte[NodeSetDocumentReader.MaxHeaderBytes * 4];
                prefix.CopyTo(bytes, 0);
                body = new CountingStream(bytes);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            });
            using var repository = new UaNodeSetRepository(client);
            Assert.That(await repository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false),
                Is.Null);
            Assert.That(body!.BytesRead, Is.EqualTo(NodeSetDocumentReader.MaxHeaderBytes));
            Assert.That(body.IsDisposed, Is.True);
        }

        [Test]
        public async Task CancellationDuringRequestPropagatesAndReleasesTheCatalogGate()
        {
            using var cancellation = new CancellationTokenSource();
            using HttpClient client = Client((_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Response(Tree());
            });
            using var repository = new UaNodeSetRepository(client);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:model"), cancellation.Token)
                    .ConfigureAwait(false));
            Assert.That(await repository.FindAsync(new NodeSetRequirement("urn:model")).ConfigureAwait(false), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransportTimeoutIsAnHttpErrorRatherThanUserCancellation(bool duringBodyRead)
        {
            using HttpClient client = Client((request, _) =>
            {
                if (!duringBodyRead)
                {
                    throw new TaskCanceledException("Simulated HTTP request timeout.");
                }
                if (request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl)
                {
                    return Response(Tree("Model.NodeSet2.xml"));
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new TimeoutStream())
                };
            });
            using var repository = new UaNodeSetRepository(client);
            HttpRequestException? exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:model")).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("timed out"));
            Assert.That(exception.InnerException, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task DisposingRepositoryDoesNotDisposeInjectedClient()
        {
            using HttpClient client = Client((_, _) => Response(Tree()));
            var repository = new UaNodeSetRepository(client);
            repository.Dispose();
            using HttpResponseMessage response = await client.GetAsync(new Uri(UaNodeSetRepository.TreeUrl))
                .ConfigureAwait(false);
            Assert.That(response.IsSuccessStatusCode, Is.True);
        }

        [TestCase("1.5.0", "1.4.0", true)]
        [TestCase("1.4.0", "1.5.0", false)]
        [TestCase("2.0.0", "1.5.0", true)]
        [TestCase("1.5.0-rc.1", "1.5.0", false)]
        [TestCase("1.5.0", "1.5.0-rc.1", true)]
        [TestCase("1.5.0-rc.10", "1.5.0-rc.2", true)]
        [TestCase("1.5.0+build", "1.5.0", true)]
        [TestCase("99999999999999999999999.0.0", "2.0.0", true)]
        public void SemanticModelVersionRequiresCompatibleMinimum(string actual, string required, bool expected)
        {
            var model = new ModelTableEntry { ModelUri = "urn:model", ModelVersion = actual };
            Assert.That(NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", ModelVersion: required)),
                Is.EqualTo(expected));
        }

        [Test]
        public void PublicationDateTakesPrecedenceOverLegacyVersion()
        {
            var date = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = new ModelTableEntry
            {
                ModelUri = "urn:model",
                Version = "Vendor release label",
                PublicationDate = date,
                PublicationDateSpecified = true
            };
            Assert.That(NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", "999.0", date)), Is.True);
            Assert.That(NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", "999.0", date.AddDays(1))),
                Is.False);
            model.PublicationDateSpecified = false;
            Assert.That(NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", PublicationDate: date)),
                Is.False);
        }

        [TestCase("1.10", "1.9", true)]
        [TestCase("1.9", "1.10", true)]
        [TestCase("label", "label", true)]
        [TestCase("label2", "label1", true)]
        public void LegacyVersionIsAHumanReadableLabelNotAnOrderedRevision(
            string actual, string required, bool expected)
        {
            var model = new ModelTableEntry { ModelUri = "urn:model", Version = actual };
            Assert.That(NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", required)),
                Is.EqualTo(expected));
        }

        [TestCase("2.0.0", "1.0.0", true)]
        [TestCase("1.0.0", "1.0.0", false)]
        [TestCase("1.0.0", null, true)]
        [TestCase(null, "1.0.0", false)]
        [TestCase(null, null, false)]
        public void ModelVersionPrecedesPublicationDateAccordingToAnnexF(
            string? actual, string? required, bool expected)
        {
            var date = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = new ModelTableEntry
            {
                ModelUri = "urn:model",
                ModelVersion = actual,
                PublicationDate = date.AddDays(-1),
                PublicationDateSpecified = true
            };
            Assert.That(NodeSetMetadata.Satisfies(
                model, new NodeSetRequirement("urn:model", PublicationDate: date, ModelVersion: required)),
                Is.EqualTo(expected));
        }

        [TestCase("01.0.0")]
        [TestCase("1.0")]
        [TestCase("1.0.0-01")]
        [TestCase("1.0.0+")]
        public void InvalidSemanticVersionsFailExplicitly(string version)
        {
            var model = new ModelTableEntry { ModelUri = "urn:model", ModelVersion = version };
            Assert.Throws<InvalidDataException>(() =>
                NodeSetMetadata.Satisfies(model, new NodeSetRequirement("urn:model", ModelVersion: "1.0.0")));
        }

        [Test]
        public void TruncatedMetadataHasAnExplicitHeaderLimitError()
        {
            using HttpClient client = Client((request, _) => Response(
                request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Tree("Large.NodeSet2.xml") :
                    "<?xml version=\"1.0\"?><!--" + new string('x', NodeSetDocumentReader.MaxHeaderBytes)));
            using var repository = new UaNodeSetRepository(client);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:missing")).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("header limit"));
        }

        [Test]
        public void OversizedDocumentIsRejectedBeforeReadingTheBody()
        {
            using HttpClient client = Client((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl)
                {
                    return Response(Tree("Large.NodeSet2.xml"));
                }
                HttpResponseMessage response = Response(NodeSetLoaderTests.Model("urn:model"));
                if (request.Headers.Range is null)
                {
                    response.Content.Headers.ContentLength = NodeSetDocumentReader.MaxDocumentBytes + 1;
                }
                return response;
            });
            using var repository = new UaNodeSetRepository(client);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:model")).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("byte input limit"));
        }

        [Test]
        public void RepositoryPathsCannotEscapeTheApprovedTree()
        {
            using HttpClient client = Client((_, _) => Response(Tree("../other/Model.NodeSet2.xml")));
            using var repository = new UaNodeSetRepository(client);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:model")).ConfigureAwait(false));
        }

        [Test]
        public void SuccessfulHttpResponseContainingHtmlIsNotTreatedAsModelAbsence()
        {
            using HttpClient client = Client((request, _) => Response(
                request.RequestUri!.AbsoluteUri == UaNodeSetRepository.TreeUrl ?
                    Tree("Model.NodeSet2.xml") : "<html><body>Server error</body></html>"));
            using var repository = new UaNodeSetRepository(client);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.FindAsync(new NodeSetRequirement("urn:model")).ConfigureAwait(false));
        }

        private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        {
            var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
                    Task.FromResult(respond(request, token)));
            handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
            return new HttpClient(handler.Object);
        }

        private static HttpResponseMessage Response(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        }

        private static string Tree(params string[] paths)
        {
            string entries = string.Join(',', paths.Select(static path => $$"""{"path":"{{path}}","type":"blob"}"""));
            return $$"""{"sha":"{{Revision}}","truncated":false,"tree":[{{entries}}]}""";
        }

        private sealed class TimeoutStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                return ValueTask.FromException<int>(new TaskCanceledException("Simulated HTTP body timeout."));
            }
        }

        private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
        {
            public int BytesRead { get; private set; }
            public bool IsDisposed { get; private set; }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                int count = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesRead += count;
                return count;
            }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }

        private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    }
}
