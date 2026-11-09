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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class MachineryResultActionTests
    {
        [TestCase(1, false)]
        [TestCase(8193, true)]
        [TestCase(1024 * 1024, false)]
        public async Task ExactSelectedResultUsesBoundedTransferAndNewLocalPublicationAsync(int size, bool directOwner)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            MachineryActionSelection original = test.SelectResult();
            MachineryActionSelection selection = directOwner
                ? test.Select(MachinerySection.Results, original.Item, original.Values,
                    new MachineryMachine(ObjectIds.ObjectsFolder, "Direct management objects", "Management owner"))
                : original;
            var source = new ResultSource { Size = size };
            long written = await MachineryResultExport.WriteAsync(
                files.Destination, selection, source, CancellationToken.None).ConfigureAwait(false);
            Assert.That(written, Is.EqualTo(size));
            Assert.That(new FileInfo(files.Destination).Length, Is.EqualTo(size));
            Assert.That((await File.ReadAllBytesAsync(files.Destination).ConfigureAwait(false))
                .All(value => value == 0x5a), Is.True);
            string[] expectedIds = ["result-exact"];
            Assert.That(source.Ids, Is.EqualTo(expectedIds));
            Assert.That(source.Owner, Is.EqualTo(selection.Machine.NodeId));
            Assert.That(source.Reads, Is.EqualTo(2));
            Assert.That(source.Variable, Is.EqualTo(selection.Item.NodeId));
            Assert.That(source.Maximum, Is.EqualTo(CompanionLocalExport.MaximumBytes));
            Assert.That(source.Stream!.CanWrite, Is.False);
            Assert.That(Directory.EnumerateFiles(files.Root), Has.Exactly(1).Items);
            test.Services.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [TestCase("good-null")]
        [TestCase("wrong-type")]
        [TestCase("missing-id")]
        [TestCase("wrong-id")]
        [TestCase("blank")]
        [TestCase("overlong")]
        [TestCase("uncertain")]
        [TestCase("wrong-source")]
        [TestCase("duplicate")]
        public async Task OnlyARealTypedCurrentResultCanBeDownloadedAsync(string partition)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            MachineryActionSelection original = test.SelectResult();
            CompanionValue published = original.Values[0];
            CompanionValue id = original.Values[1];
            ArrayOf<CompanionValue> values = partition switch
            {
                "good-null" => [published with { Value = Variant.Null }, id],
                "wrong-type" => [published with { Value = Variant.From("not a result") }, id],
                "missing-id" => [published],
                "wrong-id" => [published, id with { Value = Variant.From("other") }],
                "blank" => MachineryActionTestContext.ResultValues(original.Item.NodeId, " "),
                "overlong" => MachineryActionTestContext.ResultValues(original.Item.NodeId, new string('x', 257)),
                "uncertain" => [published with { Quality = StatusCodes.Uncertain }, id],
                "wrong-source" => [published with { SourceNodeId = new NodeId(999u) }, id],
                _ => [published, published, id]
            };
            MachineryActionSelection selection = test.Select(MachinerySection.Results, original.Item, values);
            var source = new Mock<IMachineryResultSource>(MockBehavior.Strict);
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                files.Destination, selection, source.Object, CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            source.VerifyNoOtherCalls();
            Assert.That(Directory.EnumerateFiles(files.Root), Is.Empty);
        }

        [TestCase(256)]
        [TestCase(1)]
        public void ResultIdentifierUsesExactUnnormalizedBoundaryText(int length)
        {
            var test = new MachineryActionTestContext();
            string id = length == 1 ? "x" : " " + new string('x', length - 2) + " ";
            Assert.That(test.SelectResult(id).RequireResultId(), Is.EqualTo(id));
        }

        [TestCase("blank")]
        [TestCase("relative")]
        [TestCase("uri")]
        [TestCase("unc")]
        [TestCase("existing")]
        [TestCase("directory")]
        [TestCase("missing-parent")]
        public async Task InvalidOrExistingDestinationIsRejectedBeforeNetworkActivityAsync(string partition)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            string path = partition switch
            {
                "blank" => string.Empty,
                "relative" => "selected.bin",
                "uri" => "https://example.invalid/result",
                "unc" => @"\\example.invalid\share\selected.bin",
                "directory" => files.Root,
                "missing-parent" => Path.Combine(files.Root, "absent", "selected.bin"),
                _ => files.Destination
            };
            if (partition == "existing")
            {
                await File.WriteAllTextAsync(path, "keep me").ConfigureAwait(false);
            }
            var source = new Mock<IMachineryResultSource>(MockBehavior.Strict);
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                path, test.SelectResult(), source.Object, CancellationToken.None),
                Throws.Exception.TypeOf<ArgumentException>().Or.TypeOf<IOException>()).ConfigureAwait(false);
            source.VerifyNoOtherCalls();
            if (partition == "existing")
            {
                Assert.That(await File.ReadAllTextAsync(path).ConfigureAwait(false), Is.EqualTo("keep me"));
            }
            Assert.That(Directory.EnumerateFiles(files.Root, "*.pending"), Is.Empty);
        }

        [TestCase("before")]
        [TestCase("after")]
        [TestCase("selection")]
        [TestCase("peer")]
        [TestCase("final")]
        public async Task ChangedResultOrSelectionNeverPublishesDownloadedBytesAsync(string change)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            MachineryActionSelection selection = test.SelectResult();
            var source = new ResultSource();
            if (change == "before")
            {
                source.CurrentId = "other";
            }
            source.AfterDownload = () =>
            {
                if (change == "after")
                {
                    source.CurrentId = "other";
                }
                if (change == "selection")
                {
                    test.Current = false;
                }
                if (change == "peer")
                {
                    test.Endpoint.ServerCertificate = ByteString.From("changed-peer"u8);
                }
            };
            if (change == "final")
            {
                test.OnRequireCurrent = () =>
                {
                    if (Directory.EnumerateFiles(files.Root, "*.pending").Any())
                    {
                        throw new InvalidOperationException("Changed at final publication.");
                    }
                };
            }
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                files.Destination, selection, source, CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(Directory.EnumerateFiles(files.Root), Is.Empty);
            Assert.That(source.Ids, Has.Count.EqualTo(change == "before" ? 0 : 1));
        }

        [TestCase(0)]
        [TestCase((1024 * 1024) + 1)]
        [TestCase(-1)]
        public async Task EmptyOversizedOrIncompleteTransferCannotBecomeASuccessfulFileAsync(int size)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            var source = new ResultSource { Size = size < 0 ? 10 : size, ReportedSize = size < 0 ? 9 : null };
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                files.Destination, test.SelectResult(), source, CancellationToken.None),
                Throws.Exception.TypeOf<ServiceResultException>().Or.TypeOf<NotSupportedException>())
                .ConfigureAwait(false);
            Assert.That(Directory.EnumerateFiles(files.Root), Is.Empty);
            Assert.That(source.Stream!.CanWrite, Is.False);
        }

        [Test]
        public async Task ReadAndCloseFailureEvidenceIsPreservedAndPartialBuffersAreDiscardedAsync()
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            var failure = new AggregateException("Read and Close failed.",
                new IOException("Read failed."), new IOException("Close failed."));
            var source = new ResultSource { Failure = failure };
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                files.Destination, test.SelectResult(), source, CancellationToken.None),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Directory.EnumerateFiles(files.Root), Is.Empty);
            Assert.That(source.Stream!.CanWrite, Is.False);
            Assert.That(source.Reads, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanceledResultTransferNeverPublishesOrReplaysAsync(bool before)
        {
            var test = new MachineryActionTestContext();
            using var files = new MachineryActionFiles();
            using var cancellation = new CancellationTokenSource();
            var source = new ResultSource { AfterDownload = cancellation.Cancel };
            if (before)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            await Assert.ThatAsync(() => MachineryResultExport.WriteAsync(
                files.Destination, test.SelectResult(), source, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(Directory.EnumerateFiles(files.Root), Is.Empty);
            Assert.That(source.Ids, Has.Count.EqualTo(before ? 0 : 1));
        }

        private sealed class ResultSource : IMachineryResultSource
        {
            public int Size { get; init; } = 16;
            public int? ReportedSize { get; init; }
            public int Reads { get; private set; }
            public string? CurrentId { get; set; } = "result-exact";
            public System.Collections.Generic.List<string> Ids { get; } = [];
            public NodeId Owner { get; private set; }
            public NodeId Variable { get; private set; }
            public long Maximum { get; private set; }
            public Stream? Stream { get; private set; }
            public Action? AfterDownload { get; set; }
            public Exception? Failure { get; init; }

            public ValueTask<string?> ReadResultIdAsync(
                CompanionContext context, NodeId variable, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Reads++;
                Variable = variable;
                return ValueTask.FromResult(CurrentId);
            }

            public async ValueTask<long> DownloadAsync(
                CompanionContext context, NodeId owner, string resultId, Stream destination,
                long maximumBytes, CancellationToken cancellationToken)
            {
                Ids.Add(resultId);
                Owner = owner;
                Maximum = maximumBytes;
                Stream = destination;
                byte[] bytes = new byte[Size];
                Array.Fill(bytes, (byte)0x5a);
                await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (Failure is not null)
                {
                    throw Failure;
                }
                AfterDownload?.Invoke();
                return ReportedSize ?? Size;
            }
        }
    }
}
