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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class CompanionSnapshotExporterTests
    {
        [Test]
        public void SnapshotPreservesPortableSourceQualityUnitsAndDisplayOnlyRepresentation()
        {
            var namespaces = new NamespaceTable();
            ushort index = namespaces.GetIndexOrAppend("urn:ualens:pumps");
            var target = new CompanionTarget("pumps", new NodeId("pump", index), "Cooling pump", "Pump");
            var timestamp = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            ByteString encoded = CompanionSnapshotExporter.Render(target,
            [
                new CompanionValue("Mass flow", Variant.From(12.5))
                {
                    SourceNodeId = new NodeId("flow", index),
                    Quality = StatusCodes.Uncertain,
                    SourceTimestamp = timestamp,
                    EngineeringMetadata = "kg/s; range 0 .. 20"
                }
            ], namespaces);
            using JsonDocument document = JsonDocument.Parse(encoded.Memory);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("representation").GetString(), Is.EqualTo("display-snapshot"));
            Assert.That(root.GetProperty("targetId").GetString(), Is.EqualTo("nsu=urn:ualens:pumps;s=pump"));
            JsonElement value = root.GetProperty("values")[0];
            Assert.That(value.GetProperty("sourceNodeId").GetString(), Is.EqualTo("nsu=urn:ualens:pumps;s=flow"));
            Assert.That(value.GetProperty("statusCode").GetUInt32(), Is.EqualTo((uint)StatusCodes.Uncertain));
            Assert.That(value.GetProperty("sourceTimestamp").GetDateTime(), Is.EqualTo(timestamp));
            Assert.That(value.GetProperty("engineeringMetadata").GetString(), Is.EqualTo("kg/s; range 0 .. 20"));
            Assert.That(value.GetProperty("displayValue").GetString(), Does.Contain("Uncertain"));
            Assert.That(root.TryGetProperty("PreparedOperation", out _), Is.False);
            Assert.That(root.TryGetProperty("ConfirmDeployment", out _), Is.False);
        }

        [TestCase(0)]
        [TestCase(1025)]
        public void SnapshotRejectsInvalidFieldCounts(int count)
        {
            CompanionValue[] values = Enumerable.Range(0, count)
                .Select(index => new CompanionValue("Value" + index, Variant.From(index))).ToArray();
            Assert.That(() => CompanionSnapshotExporter.Render(Target(), values, new NamespaceTable()),
                Throws.TypeOf<ArgumentException>());
        }

        [TestCase(4096, true)]
        [TestCase(4097, false)]
        public void SnapshotEnforcesIndividualTextBounds(int length, bool accepted)
        {
            ArrayOf<CompanionValue> values = [new("Text", Variant.From(new string('x', length)))];
            if (accepted)
            {
                ByteString result = CompanionSnapshotExporter.Render(Target(), values, new NamespaceTable());
                using JsonDocument document = JsonDocument.Parse(result.Memory);
                Assert.That(document.RootElement.GetProperty("values")[0].GetProperty("displayValue").GetString(),
                    Has.Length.EqualTo(length));
            }
            else
            {
                Assert.That(() => CompanionSnapshotExporter.Render(Target(), values, new NamespaceTable()),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [Test]
        public void SnapshotEnforcesTotalByteLimitWhileSerializing()
        {
            ArrayOf<CompanionValue> values = Enumerable.Range(0, 300)
                .Select(index => new CompanionValue("Value" + index, Variant.From(new string('x', 4096))))
                .ToArrayOf();
            Assert.That(() => CompanionSnapshotExporter.Render(Target(), values, new NamespaceTable()),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public async Task ExportCreatesOnlyTheRequestedFileAndNeverOverwritesItAsync()
        {
            string root = Directory.CreateDirectory(OpenUsdTestPaths.NewDestination()).FullName;
            string path = Path.Combine(root, "snapshot.json");
            try
            {
                ArrayOf<CompanionValue> values = [new("Name", Variant.From("original"))];
                await CompanionSnapshotExporter.WriteAsync(
                    path, Target(), values, new NamespaceTable(), static () => { }, CancellationToken.None)
                    .ConfigureAwait(false);
                string first = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
                await Assert.ThatAsync(() => CompanionSnapshotExporter.WriteAsync(
                    path, Target(), [new("Name", Variant.From("replacement"))], new NamespaceTable(),
                    static () => { }, CancellationToken.None), Throws.TypeOf<IOException>()).ConfigureAwait(false);
                Assert.That(await File.ReadAllTextAsync(path).ConfigureAwait(false), Is.EqualTo(first));
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(root);
            }
        }

        [Test]
        public async Task StaleSelectionBeforePublicationRemovesThePendingFileAsync()
        {
            string root = Directory.CreateDirectory(OpenUsdTestPaths.NewDestination()).FullName;
            string path = Path.Combine(root, "snapshot.json");
            int checks = 0;
            try
            {
                await Assert.ThatAsync(() => CompanionLocalExport.WriteAsync(
                    path, ByteString.From("bounded"u8), () =>
                    {
                        if (++checks == 2)
                        {
                            throw new InvalidOperationException("The selected target changed.");
                        }
                    }, CancellationToken.None), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(checks, Is.EqualTo(2));
                Assert.That(File.Exists(path), Is.False);
                Assert.That(Directory.GetFiles(root), Is.Empty);
            }
            finally
            {
                Directory.Delete(root);
            }
        }

        private static CompanionTarget Target()
        {
            return new CompanionTarget("pumps", new NodeId(1u), "Pump", "Pump");
        }
    }
}
