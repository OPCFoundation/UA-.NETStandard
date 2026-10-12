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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryDocumentStoreTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task InlinedBlobDocumentsRetainBytesAndPageOffsetsAcrossOpaqueCursorsAsync(
            bool documentView, bool referenceView)
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                using var endpoint = new XRegistryTransactionalEndpoint(
                    Options() with { DocumentStore = documents, PageSize = 1 },
                    new InMemoryXRegistryTransactionStore());
                for (int index = 0; index < 2; index++)
                {
                    string path = "/groups/" + (index == 0 ? "a" : "b") + "/schemas/r/versions/v1";
                    XRegistryResponse created = await endpoint.ExecuteAsync(
                        Request(XRegistryAction.Replace, path, "{}") with
                        { Document = ByteString.From(new byte[] { (byte)(index + 1), 0x42 }) }).ConfigureAwait(false);
                    Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                }
                if (referenceView)
                {
                    XRegistryResponse alias = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace,
                        "/groups/a/schemas/alias", /*lang=json,strict*/ """{"meta":{"xref":"/groups/a/schemas/r"}}"""))
                        .ConfigureAwait(false);
                    Assert.That(alias.IsSuccess, Is.True, alias.Error?.Detail);
                }
                XRegistryRequest request = Request(XRegistryAction.Read, "/groups") with
                {
                    Parameters = documentView
                        ? [new("inline", "*"), new("binary", null), new("doc", null)]
                        : [new("inline", "*"), new("binary", null)]
                };
                for (int index = 0; index < 2; index++)
                {
                    XRegistryResponse page = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                    string id = index == 0 ? "a" : "b";
                    Assert.Multiple(() =>
                    {
                        Assert.That(page.StatusCode, Is.EqualTo(200), page.Error?.Detail);
                        Assert.That(
                            page.Metadata.EnumerateObject().Select(item => item.Name), Is.EqualTo(new[] { id }));
                        Assert.That(page.Metadata.GetProperty(id).GetProperty("schemas").GetProperty("r")
                            .GetProperty("versions").GetProperty("v1").GetProperty("schemabase64").GetString(),
                            Is.EqualTo(index == 0 ? "AUI=" : "AkI="));
                        Assert.That(page.Links.Count, Is.EqualTo(index == 0 ? 1 : 0));
                    });
                    if (index == 0)
                    {
                        string target = page.Links[0].Target;
                        int query = target.IndexOf('?', StringComparison.Ordinal);
                        request = request with
                        {
                            Parameters = [.. target[(query + 1)..].Split('&').Select(pair =>
                            {
                                string[] parts = pair.Split(['='], 2);
                                return new XRegistryParameter(Uri.UnescapeDataString(parts[0]),
                                    parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : null);
                            })]
                        };
                    }
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task StoredDocumentsUseDeduplicatedReferencesAndRoundTripAfterRestartAsync()
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                var state = new InMemoryXRegistryTransactionStore();
                XRegistryTransactionalOptions options = Options() with { DocumentStore = documents };
                byte[] bytes = new byte[512 * 1024];
                for (int index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = (byte)(index % 251);
                }
                using (var endpoint = new XRegistryTransactionalEndpoint(options, state))
                {
                    foreach (string id in s_versions)
                    {
                        XRegistryResponse created = await endpoint.ExecuteAsync(
                            Request(XRegistryAction.Replace, k_resource + "/versions/" + id, "{}") with
                            { Document = ByteString.From(bytes) }).ConfigureAwait(false);
                        Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                    }
                    XRegistryResponse touched = await endpoint.ExecuteAsync(
                        Request(XRegistryAction.Merge, k_resource + "/versions/v1", "{}")).ConfigureAwait(false);
                    Assert.That(touched.StatusCode, Is.EqualTo(200));
                }
                ByteString encoded = await state.LoadAsync().ConfigureAwait(false);
                Assert.That(
                    encoded.Length, Is.LessThan(10_000), "A generation must not duplicate the domain documents.");
                Assert.That(Directory.GetFiles(root, "*.blob"), Has.Length.EqualTo(1));
                using var reopened = new XRegistryTransactionalEndpoint(options, state);
                XRegistryResponse read =
                    await reopened.ExecuteAsync(Request(XRegistryAction.Read, k_resource + "/versions/v1")
                    with
                    { View = XRegistryView.Default }).ConfigureAwait(false);
                Assert.That(read.Document.ToArray(), Is.EqualTo(bytes));
                XRegistryResponse inline = await reopened.ExecuteAsync(Request(XRegistryAction.Read, "/") with
                { Parameters = [new("inline", "groups.schemas.versions.schema")] }).ConfigureAwait(false);
                Assert.That(
                    inline.Metadata.GetProperty("groups").GetProperty("g").GetProperty("schemas").GetProperty("r")
                    .GetProperty("versions").GetProperty("v1").GetProperty("schemabase64").GetString(),
                    Is.EqualTo(Convert.ToBase64String(bytes)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task AbortedPreparationLeavesOnlyCollectableUnreferencedContentAsync()
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                var state = new InMemoryXRegistryTransactionStore();
                using var endpoint =
                    new XRegistryTransactionalEndpoint(Options() with { DocumentStore = documents }, state);
                IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(
                    Request(XRegistryAction.Replace, k_resource + "/versions/v1", "{}") with
                    { Document = ByteString.From(Encoding.UTF8.GetBytes("candidate")) }).ConfigureAwait(false);
                await using (operation.ConfigureAwait(false))
                {
                    var snapshot = (IXRegistryPreparedSnapshot)operation;
                    XRegistryResponse preview = await snapshot.ReadCandidateAsync(
                        Request(XRegistryAction.Read, k_resource + "/versions/v1") with
                        {
                            View = XRegistryView.Default
                        })
                        .ConfigureAwait(false);
                    Assert.That(Encoding.UTF8.GetString(preview.Document.ToArray()), Is.EqualTo("candidate"));
                }
                Assert.That((await state.LoadAsync().ConfigureAwait(false)).IsNull, Is.True);
                Assert.That(await documents.CollectAsync([]).ConfigureAwait(false), Is.EqualTo(1));
                Assert.That(Directory.GetFiles(root, "*.blob"), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingOrCorruptCommittedContentCannotBecomeAnEmptyDocumentAsync(bool missing)
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                var state = new InMemoryXRegistryTransactionStore();
                XRegistryTransactionalOptions options = Options() with { DocumentStore = documents };
                using (var endpoint = new XRegistryTransactionalEndpoint(options, state))
                {
                    XRegistryResponse created = await endpoint.ExecuteAsync(
                        Request(XRegistryAction.Replace, k_resource + "/versions/v1", "{}") with
                        { Document = ByteString.From(Encoding.UTF8.GetBytes("original")) }).ConfigureAwait(false);
                    Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                }
                string blob = Directory.GetFiles(root, "*.blob").Single();
                if (missing)
                {
                    File.Delete(blob);
                }
                else
                {
                    using var stream = new FileStream(blob, FileMode.Truncate, FileAccess.Write);
                    byte[] changed = Encoding.UTF8.GetBytes("replaced");
#if NET
                    await stream.WriteAsync(changed.AsMemory()).ConfigureAwait(false);
#else
                    await stream.WriteAsync(changed, 0, changed.Length).ConfigureAwait(false);
#endif
                }
                using var reopened = new XRegistryTransactionalEndpoint(options, state);
                if (missing)
                {
                    Assert.ThrowsAsync<FileNotFoundException>(async () =>
                        await reopened.InspectAsync(Writer).ConfigureAwait(false));
                }
                else
                {
                    Assert.ThrowsAsync<InvalidDataException>(async () =>
                        await reopened.InspectAsync(Writer).ConfigureAwait(false));
                }
                Assert.That((await state.LoadAsync().ConfigureAwait(false)).IsNull, Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task CollectionKeepsVerifiedLiveBlobsAndRejectsTruncatedRetainedReferencesAsync()
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                using var first = new MemoryStream(Encoding.UTF8.GetBytes("keep"));
                using var second = new MemoryStream(Encoding.UTF8.GetBytes("discard"));
                XRegistryBlobReference keep = await documents.StoreAsync(first, 4).ConfigureAwait(false);
                XRegistryBlobReference discard = await documents.StoreAsync(second, 7).ConfigureAwait(false);
                Assert.ThrowsAsync<InvalidDataException>(async () => await documents.CollectAsync(
                    [new XRegistryBlobReference(keep.Sha256, keep.Length + 1)]).ConfigureAwait(false));
                Assert.That(Directory.GetFiles(root, "*.blob"), Has.Length.EqualTo(2));
                Assert.That(await documents.CollectAsync([keep]).ConfigureAwait(false), Is.EqualTo(1));
                using Stream retained = await documents.OpenReadAsync(keep).ConfigureAwait(false);
                Assert.That(retained.Length, Is.EqualTo(4));
                Assert.That(File.Exists(Path.Combine(root, discard.Sha256 + ".blob")), Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task StreamingLimitRejectsAdjacentByteWithoutLeavingAStagingFileAsync()
        {
            string root = DirectoryPath();
            try
            {
                await using var documents = new FileXRegistryDocumentStore(root);
                using var input = new MemoryStream(new byte[65_537]);
                Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await documents.StoreAsync(input, 65_536).ConfigureAwait(false));
                Assert.That(Directory.GetFiles(root, "*.pending"), Is.Empty);
                Assert.That(Directory.GetFiles(root, "*.blob"), Is.Empty);
                input.Position = 0;
                using var canceled = new CancellationTokenSource();
                await canceled.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(async () =>
                    await documents.StoreAsync(input, 65_537, canceled.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string DirectoryPath()
        {
            string path = Path.Combine(Path.GetTempPath(), "xregistry-documents-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private const string k_resource = "/groups/g/schemas/r";
        private static readonly string[] s_versions = ["v1", "v2"];
    }
}
