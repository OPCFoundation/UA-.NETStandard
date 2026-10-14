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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies offline preparation, exact source identity and root-confined input composition.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimePlanningTests
    {
        [Test]
        public async Task OpenAndInspectDoNotActivateChannelsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["readproperty", "writeproperty"])
                .ConfigureAwait(false);
            JsonObject inspected = WotRuntimeTestContext.Payload(
                await fixture.Tools.InspectAsync(handle).ConfigureAwait(false));

            Assert.That(inspected["sha256"]!.GetValue<string>(),
                Is.EqualTo(WotRuntimeTestContext.Selection().Sha256));
            Assert.That(inspected["operations"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(inspected["operations"]!.AsArray()
                .All(item => !item!["channelOpen"]!.GetValue<bool>()), Is.True);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SelectionPinsOneFormAndRequestedOperationsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            WotRuntimeSelection selection = WotRuntimeTestContext.Selection() with { FormIndex = 1 };
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("second",
                new WotDocumentSource { Text = WotRuntimeTestContext.PropertyDocument },
                selection, ["readproperty"]).ConfigureAwait(false));
            string handle = opened["runtime"]!["handle"]!.GetValue<string>();

            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            JsonObject refused = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.Int32, "1")).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms.Single().JsonPointer,
                Is.EqualTo("/properties/temperature/forms/1"));
            Assert.That(fixture.Factory.OpenedForms.Single().Addressing.Target,
                Is.EqualTo("http://device.example/other"));
            Assert.That(refused["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported.Code));
            Assert.That(fixture.Factory.Writes, Is.Empty);
        }

        [TestCase("digest")]
        [TestCase("affordance")]
        [TestCase("formIndex")]
        [TestCase("bindingId")]
        [TestCase("bindingVersion")]
        [TestCase("operation")]
        public async Task AlteredDigestOrSelectionIsRejectedAsync(string changed)
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            WotRuntimeSelection original = WotRuntimeTestContext.Selection();
            WotRuntimeSelection selection = changed switch
            {
                "digest" => original with { Sha256 = new string('0', 64) },
                "affordance" => original with { Affordance = "Temperature" },
                "formIndex" => original with { FormIndex = 2 },
                "bindingId" => original with { BindingId = "w3c.coap" },
                "bindingVersion" => original with { BindingVersion = "unsupported-version" },
                _ => original
            };
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("pin",
                new WotDocumentSource { Text = WotRuntimeTestContext.PropertyDocument }, selection,
                changed == "operation" ? ["invokeaction"] : ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [TestCase(65_536, false)]
        [TestCase(65_537, true)]
        public async Task InlineDocumentUsesExactUtf8ByteBoundaryAsync(int bytes, bool rejected)
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string document = WotRuntimeTestContext.PropertyDocument;
            document += new string(' ', bytes - Encoding.UTF8.GetByteCount(document));
            Assert.That(Encoding.UTF8.GetByteCount(document), Is.EqualTo(bytes));

            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("inline",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document),
                ["readproperty"]).ConfigureAwait(false), error: rejected);

            Assert.That(fixture.Registry.List().Count, Is.EqualTo(rejected ? 0 : 1));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task TransferRootFilePreparesWithoutEndpointIoAsync()
        {
            await using var fixture = new WotRuntimeTestContext(files: true);
            using IDisposable caller = fixture.Enter();
            string path = Path.Combine(fixture.Root!, "device.json");
            await File.WriteAllTextAsync(path, WotRuntimeTestContext.PropertyDocument, new UTF8Encoding(false))
                .ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("file",
                new WotDocumentSource { FilePath = path }, WotRuntimeTestContext.Selection(),
                ["readproperty"]).ConfigureAwait(false));

            Assert.That(result["sha256"]!.GetValue<string>(), Is.EqualTo(WotRuntimeTestContext.Selection().Sha256));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [Test]
        public async Task TransferRootTraversalIsRejectedBeforeActivationAsync()
        {
            await using var fixture = new WotRuntimeTestContext(files: true);
            using IDisposable caller = fixture.Enter();
            string path = Path.Combine(fixture.Root!, "..", "outside.json");
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("outside",
                new WotDocumentSource { FilePath = path }, WotRuntimeTestContext.Selection(),
                ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task RepeatedRuntimeNameDoesNotReplaceTheExistingPlanAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string first = await fixture.OpenAsync().ConfigureAwait(false);
            JsonObject duplicate = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("property",
                new WotDocumentSource { Text = WotRuntimeTestContext.PropertyDocument },
                WotRuntimeTestContext.Selection() with { FormIndex = 1 }, ["readproperty"]).ConfigureAwait(false),
                error: true);

            Assert.That(duplicate["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadEntryExists.Code));
            Assert.That(fixture.Registry.List().ToList().Single().Handle, Is.EqualTo(first));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(first).ConfigureAwait(false));
            Assert.That(fixture.Factory.OpenedForms.Single().Addressing.Target,
                Is.EqualTo("http://device.example/value"));
        }

        [Test]
        public async Task OversizedOpenMetadataDoesNotConsumeRuntimeCapacityAsync()
        {
            await using var fixture = new WotRuntimeTestContext(
                options: new WotRuntimeOptions { MaxResultBytes = 1024 });
            using IDisposable caller = fixture.Enter();
            JsonObject rejected = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("long-reference",
                new WotDocumentSource
                {
                    Text = WotRuntimeTestContext.PropertyDocument,
                    Reference = "https://documents.example/" + new string('a', 1500)
                }, WotRuntimeTestContext.Selection(), ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(rejected["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded.Code));
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }
    }
}
#endif
