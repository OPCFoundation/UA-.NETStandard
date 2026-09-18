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
using System.Runtime;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotEventIdentityModeTests
    {
        [SetUp]
        public void RecordRuntime()
        {
            TestContext.Out.WriteLine($"D3 runtime={Environment.Version}; serverGC={GCSettings.IsServerGC}");
        }

        [Test]
        public void EventDeclarationsDefaultToLocalReEmission()
        {
            WotProjectedAffordance direct = CreateDeclaration(WotAffordanceKind.Event);
            using JsonDocument document = JsonDocument.Parse("{}");
            WotProjectedAffordance converted = ConvertDeclaration(document.RootElement);
            Assert.That(direct.IdentityMode, Is.EqualTo(WoTEventIdentityModeEnum.LocalReEmission));
            Assert.That(converted.IdentityMode, Is.EqualTo(WoTEventIdentityModeEnum.LocalReEmission));
        }

        [Test]
        public void TypedSelectionPreservesTheDeclarationWithoutMutatingItsDefault()
        {
            WotProjectedAffordance original = CreateDeclaration(WotAffordanceKind.Event);
            WotProjectedAffordance selected = original.WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(original.IdentityMode, Is.EqualTo(WoTEventIdentityModeEnum.LocalReEmission));
                Assert.That(selected.IdentityMode, Is.EqualTo(WoTEventIdentityModeEnum.TransparentForwarding));
                Assert.That(selected.Kind, Is.EqualTo(WotAffordanceKind.Event));
                Assert.That(selected.Name, Is.EqualTo("event"));
                Assert.That(selected.JsonPointer, Is.EqualTo("/events/event"));
                Assert.That(selected.NodeId, Is.EqualTo("nsu=urn:wot:d3:local;s=EventType"));
                Assert.That(selected.OwnerNodeId, Is.EqualTo("nsu=urn:wot:d3:local;s=Owner"));
            }
        }

        [TestCase("local-re-emission", WoTEventIdentityModeEnum.LocalReEmission)]
        [TestCase("transparent-forwarding", WoTEventIdentityModeEnum.TransparentForwarding)]
        public void ConvertedDeclarationRetainsItsExplicitMode(string mode, WoTEventIdentityModeEnum expected)
        {
            using JsonDocument document = JsonDocument.Parse($$"""{"uav:eventIdentityMode":"{{mode}}"}""");
            WotProjectedAffordance declaration = ConvertDeclaration(document.RootElement);
            Assert.That(declaration.IdentityMode, Is.EqualTo(expected));
        }

        [TestCase("null")]
        [TestCase("0")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("\"unknown\"")]
        public void InvalidModeDeclarationsFailWithoutFallback(string value)
        {
            using JsonDocument document = JsonDocument.Parse($$"""{"uav:eventIdentityMode":{{value}}}""");
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => ConvertDeclaration(document.RootElement));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [TestCase(WotAffordanceKind.Property)]
        [TestCase(WotAffordanceKind.Action)]
        public void NonEventDeclarationsRejectEventIdentityModes(WotAffordanceKind kind)
        {
            WotProjectedAffordance declaration = CreateDeclaration(kind);
            Assert.Throws<InvalidOperationException>(
                () => declaration.WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding));
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void UnknownTypedModesAreRejected(int value)
        {
            WotProjectedAffordance declaration = CreateDeclaration(WotAffordanceKind.Event);
            ArgumentOutOfRangeException? error = Assert.Throws<ArgumentOutOfRangeException>(
                () => declaration.WithIdentityMode((WoTEventIdentityModeEnum)value));
            Assert.That(error!.ParamName, Is.EqualTo("identityMode"));
        }

        private static WotProjectedAffordance CreateDeclaration(WotAffordanceKind kind)
        {
            return new WotProjectedAffordance(kind, "event", "/events/event",
                "nsu=urn:wot:d3:local;s=EventType", "nsu=urn:wot:d3:local;s=Owner");
        }

        private static WotProjectedAffordance ConvertDeclaration(JsonElement definition)
        {
            return WotProjectedAffordance.FromConverted(new Wot.WotConvertedAffordance(
                Wot.WotAffordanceKind.Event, "event", "/events/event",
                new ExpandedNodeId("EventType", "urn:wot:d3:local"),
                new ExpandedNodeId("Owner", "urn:wot:d3:local"), definition));
        }
    }
}
