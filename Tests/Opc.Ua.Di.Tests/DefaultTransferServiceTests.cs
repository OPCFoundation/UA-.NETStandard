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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Transfer;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="DefaultTransferService"/>: transfer-from
    /// (export), transfer-to (import), chunked fetch, error
    /// propagation, omit-good-results filtering, and timeout
    /// discard.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Transfer")]
    public sealed class DefaultTransferServiceTests
    {
        private static SystemContext CreateContext()
        {
            return new SystemContext(telemetry: null!);
        }

        private static NodeId ElementId => new("Device", 2);

        [Test]
        public async Task TransferFromDeviceWithExporterReturnsEntries()
        {
            var service = new DefaultTransferService();
            var set = new ParameterSet(ElementId)
            {
                Entries =
                {
                    new ParameterEntry(
                        [new QualifiedName("Manufacturer", 2)],
                        new Variant("Acme"),
                        StatusCodes.Good)
                }
            };
            service.RegisterExporter(ElementId, (_, _) => new ValueTask<ParameterSet>(set));

            int transferId = await service
                .TransferFromDeviceAsync(CreateContext(), ElementId)
                .ConfigureAwait(false);
            Assert.That(transferId, Is.GreaterThan(0));

            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId, sequenceNumber: 0,
                maxResults: 0, omitGoodResults: false).ConfigureAwait(false);

            Assert.That(chunk.Entries, Has.Length.EqualTo(1));
            Assert.That(chunk.Entries[0].NodePath[0].Name, Is.EqualTo("Manufacturer"));
            Assert.That(chunk.EndOfResults, Is.True);
            Assert.That(StatusCode.IsGood(chunk.TransferError), Is.True);
        }

        [Test]
        public async Task TransferFromDeviceWithoutExporterReturnsBadNotSupported()
        {
            var service = new DefaultTransferService();
            int transferId = await service
                .TransferFromDeviceAsync(CreateContext(), ElementId)
                .ConfigureAwait(false);
            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, false).ConfigureAwait(false);

            Assert.That(chunk.TransferError, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(chunk.EndOfResults, Is.True);
            Assert.That(chunk.Entries, Is.Empty);
        }

        [Test]
        public async Task FetchWithUnknownTransferIdReturnsBadNotFound()
        {
            var service = new DefaultTransferService();
            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId: 9999, sequenceNumber: 0,
                maxResults: 0, omitGoodResults: false).ConfigureAwait(false);
            Assert.That(chunk.TransferError, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(chunk.EndOfResults, Is.True);
        }

        [Test]
        public async Task FetchPagesThroughEntriesViaMaxResults()
        {
            var service = new DefaultTransferService();
            var set = new ParameterSet(ElementId);
            for (int i = 0; i < 5; i++)
            {
                set.Entries.Add(new ParameterEntry(
                    [new QualifiedName($"P{i}", 2)],
                    new Variant(i),
                    StatusCodes.Good));
            }
            service.RegisterExporter(ElementId, (_, _) => new ValueTask<ParameterSet>(set));

            int transferId = await service
                .TransferFromDeviceAsync(CreateContext(), ElementId)
                .ConfigureAwait(false);

            FetchResult first = await service.FetchAsync(
                CreateContext(), transferId, sequenceNumber: 0,
                maxResults: 2, omitGoodResults: false).ConfigureAwait(false);
            Assert.That(first.Entries, Has.Length.EqualTo(2));
            Assert.That(first.EndOfResults, Is.False);
            Assert.That(first.SequenceNumber, Is.EqualTo(2));

            FetchResult second = await service.FetchAsync(
                CreateContext(), transferId, sequenceNumber: 2,
                maxResults: 2, omitGoodResults: false).ConfigureAwait(false);
            Assert.That(second.Entries, Has.Length.EqualTo(2));
            Assert.That(second.EndOfResults, Is.False);

            FetchResult third = await service.FetchAsync(
                CreateContext(), transferId, sequenceNumber: 4,
                maxResults: 2, omitGoodResults: false).ConfigureAwait(false);
            Assert.That(third.Entries, Has.Length.EqualTo(1));
            Assert.That(third.EndOfResults, Is.True);
        }

        [Test]
        public async Task FetchOmitGoodResultsFiltersGoodEntries()
        {
            var service = new DefaultTransferService();
            var set = new ParameterSet(ElementId)
            {
                Entries =
                {
                    new ParameterEntry(
                        [new QualifiedName("Ok", 2)],
                        new Variant(1), StatusCodes.Good),
                    new ParameterEntry(
                        [new QualifiedName("Bad", 2)],
                        new Variant(2), StatusCodes.BadInternalError)
                }
            };
            service.RegisterExporter(ElementId, (_, _) => new ValueTask<ParameterSet>(set));

            int transferId = await service
                .TransferFromDeviceAsync(CreateContext(), ElementId)
                .ConfigureAwait(false);
            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, omitGoodResults: true).ConfigureAwait(false);

            Assert.That(chunk.Entries, Has.Length.EqualTo(1));
            Assert.That(chunk.Entries[0].NodePath[0].Name, Is.EqualTo("Bad"));
        }

        [Test]
        public async Task TransferToDeviceInvokesImporterAndCarriesStatuses()
        {
            var service = new DefaultTransferService();
            ParameterSet? capturedInput = null;
            service.RegisterImporter(ElementId,
                (ctx, parameters, ct) =>
                {
                    capturedInput = parameters;
                    return new ValueTask<StatusCode[]>(
                    [
                        StatusCodes.Good,
                        StatusCodes.BadOutOfRange
                    ]);
                });

            var input = new ParameterSet(ElementId)
            {
                Entries =
                {
                    new ParameterEntry(
                        [new QualifiedName("A", 2)],
                        new Variant(10), StatusCodes.Good),
                    new ParameterEntry(
                        [new QualifiedName("B", 2)],
                        new Variant(200), StatusCodes.Good)
                }
            };

            int transferId = await service.TransferToDeviceAsync(
                CreateContext(), ElementId, input).ConfigureAwait(false);

            Assert.That(capturedInput, Is.SameAs(input));

            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, false).ConfigureAwait(false);
            Assert.That(chunk.Entries, Has.Length.EqualTo(2));
            Assert.That(chunk.Entries[0].StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(chunk.Entries[1].StatusCode,
                Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public async Task TransferToDeviceWithoutImporterReturnsBadNotSupported()
        {
            var service = new DefaultTransferService();
            var input = new ParameterSet(ElementId);

            int transferId = await service.TransferToDeviceAsync(
                CreateContext(), ElementId, input).ConfigureAwait(false);
            FetchResult chunk = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, false).ConfigureAwait(false);

            Assert.That(chunk.TransferError, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task TransferIdsAreUnique()
        {
            var service = new DefaultTransferService();
            service.RegisterExporter(ElementId,
                (_, _) => new ValueTask<ParameterSet>(new ParameterSet(ElementId)));

            int a = await service.TransferFromDeviceAsync(CreateContext(), ElementId).ConfigureAwait(false);
            int b = await service.TransferFromDeviceAsync(CreateContext(), ElementId).ConfigureAwait(false);
            int c = await service.TransferFromDeviceAsync(CreateContext(), ElementId).ConfigureAwait(false);

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(b, Is.Not.EqualTo(c));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public async Task FetchAfterEndOfResultsReturnsBadNotFound()
        {
            var service = new DefaultTransferService();
            service.RegisterExporter(ElementId,
                (_, _) => new ValueTask<ParameterSet>(new ParameterSet(ElementId)
                {
                    Entries =
                    {
                        new ParameterEntry(
                            [new QualifiedName("X", 2)],
                            new Variant(1), StatusCodes.Good)
                    }
                }));

            int transferId = await service.TransferFromDeviceAsync(
                CreateContext(), ElementId).ConfigureAwait(false);

            FetchResult first = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, false).ConfigureAwait(false);
            Assert.That(first.EndOfResults, Is.True);

            // After end-of-results, server discards the transfer; a
            // re-fetch must surface BadNotFound.
            FetchResult second = await service.FetchAsync(
                CreateContext(), transferId, 0, 0, false).ConfigureAwait(false);
            Assert.That(second.TransferError,
                Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void TransferToDeviceRejectsNullArgs()
        {
            var service = new DefaultTransferService();
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await service.TransferToDeviceAsync(
                    null!, ElementId, new ParameterSet(ElementId)).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await service.TransferToDeviceAsync(
                    CreateContext(), ElementId, null!).ConfigureAwait(false));
        }
    }
}
