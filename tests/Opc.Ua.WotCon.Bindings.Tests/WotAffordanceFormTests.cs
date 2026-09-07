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
using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotAffordanceForm"/> helpers: JSON Pointer
    /// construction, <c>TryGet</c> accessors and escape token logic.
    /// </summary>
    [TestFixture]
    public sealed class WotAffordanceFormTests
    {
        private static WotAffordanceForm MakeForm(
            WotAffordanceKind kind,
            string name,
            string formJson,
            string affordanceJson = "{}")
        {
            using var formDoc = JsonDocument.Parse(formJson);
            using var affordanceDoc = JsonDocument.Parse(affordanceJson);
            JsonElement formElement = formDoc.RootElement.Clone();
            JsonElement affordanceElement = affordanceDoc.RootElement.Clone();

            string collection = kind switch
            {
                WotAffordanceKind.Action => "actions",
                WotAffordanceKind.Event => "events",
                _ => "properties"
            };
            string pointer = "/" + collection + "/" + WotAffordanceForm.EscapePointerToken(name) + "/forms/0";

            return new WotAffordanceForm(
                kind,
                name,
                ["readproperty"],
                null,
                null,
                null,
                [],
                pointer,
                formElement,
                affordanceElement);
        }

        [Test]
        public void EscapePointerTokenLeavesSafeCharsUnchanged()
        {
            Assert.That(WotAffordanceForm.EscapePointerToken("temperature"), Is.EqualTo("temperature"));
        }

        [Test]
        public void EscapePointerTokenEscapesTilde()
        {
            Assert.That(WotAffordanceForm.EscapePointerToken("a~b"), Is.EqualTo("a~0b"));
        }

        [Test]
        public void EscapePointerTokenEscapesForwardSlash()
        {
            Assert.That(WotAffordanceForm.EscapePointerToken("a/b"), Is.EqualTo("a~1b"));
        }

        [Test]
        public void EscapePointerTokenEscapesBothSpecialChars()
        {
            Assert.That(WotAffordanceForm.EscapePointerToken("a~/b"), Is.EqualTo("a~0~1b"));
        }

        [Test]
        public void EscapePointerTokenHandlesEmptyString()
        {
            Assert.That(WotAffordanceForm.EscapePointerToken(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void PointerAppendsChildToken()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "temp",
                """{"href":"http://example.com"}""");

            string pointer = form.Pointer("href");

            Assert.That(pointer, Is.EqualTo("/properties/temp/forms/0/href"));
        }

        [Test]
        public void PointerWithEmptyChildTokenReturnsFormPointer()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "temp",
                """{"href":"http://example.com"}""");

            string pointer = form.Pointer(string.Empty);

            Assert.That(pointer, Is.EqualTo("/properties/temp/forms/0"));
        }

        [Test]
        public void AffordancePointerBuildsPropertyPath()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "temp", "{}");

            Assert.That(form.AffordancePointer(), Is.EqualTo("/properties/temp"));
        }

        [Test]
        public void AffordancePointerBuildsActionPath()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Action, "reset", "{}");

            Assert.That(form.AffordancePointer(), Is.EqualTo("/actions/reset"));
        }

        [Test]
        public void AffordancePointerBuildsEventPath()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Event, "alarm", "{}");

            Assert.That(form.AffordancePointer(), Is.EqualTo("/events/alarm"));
        }

        [Test]
        public void AffordancePointerWithChildTokenAppendsToken()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "temp", "{}");

            Assert.That(form.AffordancePointer("uav:mapToNodeId"),
                Is.EqualTo("/properties/temp/uav:mapToNodeId"));
        }

        [Test]
        public void TryGetStringReturnsTrueForExistingStringProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"modv:entity":"holdingRegister"}""");

            bool found = form.TryGetString("modv:entity", out string value);

            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("holdingRegister"));
        }

        [Test]
        public void TryGetStringReturnsFalseForMissingProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            bool found = form.TryGetString("nonexistent", out string value);

            Assert.That(found, Is.False);
            Assert.That(value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void TryGetStringReturnsFalseForNonStringProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"num":42}""");

            bool found = form.TryGetString("num", out string value);

            Assert.That(found, Is.False);
            Assert.That(value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void TryGetBooleanReturnsTrueForTrueProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"mqv:retain":true}""");

            bool found = form.TryGetBoolean("mqv:retain", out bool value);

            Assert.That(found, Is.True);
            Assert.That(value, Is.True);
        }

        [Test]
        public void TryGetBooleanReturnsTrueForFalseProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"mqv:retain":false}""");

            bool found = form.TryGetBoolean("mqv:retain", out bool value);

            Assert.That(found, Is.True);
            Assert.That(value, Is.False);
        }

        [Test]
        public void TryGetBooleanReturnsFalseForMissingProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            bool found = form.TryGetBoolean("missing", out bool value);

            Assert.That(found, Is.False);
            Assert.That(value, Is.False);
        }

        [Test]
        public void TryGetInt32ReturnsTrueForNumberProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"modv:address":42}""");

            bool found = form.TryGetInt32("modv:address", out int value);

            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void TryGetInt32ReturnsTrueForStringNumericProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p",
                """{"modv:address":"100"}""");

            bool found = form.TryGetInt32("modv:address", out int value);

            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(100));
        }

        [Test]
        public void TryGetInt32ReturnsFalseForMissingProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            bool found = form.TryGetInt32("missing", out int value);

            Assert.That(found, Is.False);
            Assert.That(value, Is.Zero);
        }

        [Test]
        public void TryGetStringArrayReturnsTrueForArrayProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Event, "ev",
                """{"uav:eventFields":["Message","Severity"]}""");

            bool found = form.TryGetStringArray("uav:eventFields", out ImmutableArray<string> values);

            Assert.That(found, Is.True);
            Assert.That(values, Does.Contain("Message"));
            Assert.That(values, Does.Contain("Severity"));
        }

        [Test]
        public void TryGetStringArrayReturnsFalseForMissingProperty()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Event, "ev", "{}");

            bool found = form.TryGetStringArray("missing", out ImmutableArray<string> values);

            Assert.That(found, Is.False);
            Assert.That(values.IsEmpty, Is.True);
        }

        [Test]
        public void HasOperationReturnsTrueForMatchingOp()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            Assert.That(form.HasOperation("readproperty"), Is.True);
        }

        [Test]
        public void HasOperationIsCaseInsensitive()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            Assert.That(form.HasOperation("READPROPERTY"), Is.True);
        }

        [Test]
        public void HasOperationReturnsFalseForNonMatchingOp()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            Assert.That(form.HasOperation("writeproperty"), Is.False);
        }

        [Test]
        public void WotAffordanceFormConstructorDefaultsNullArraysToEmpty()
        {
            using var formDoc = JsonDocument.Parse("{}");
            using var affordanceDoc = JsonDocument.Parse("{}");

            var form = new WotAffordanceForm(
                WotAffordanceKind.Property, "p",
                default,
                null, null, null,
                default,
                "/properties/p/forms/0",
                formDoc.RootElement.Clone(),
                affordanceDoc.RootElement.Clone());

            Assert.That(form.Operations.IsEmpty, Is.True);
            Assert.That(form.SecuritySchemes.IsEmpty, Is.True);
        }

        [Test]
        public void TargetMappingIsEmptyWhenNoTermsPresent()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}");

            Assert.That(form.TargetMapping.IsEmpty, Is.True);
        }

        [Test]
        public void TargetMappingIsParsedFromAffordanceElement()
        {
            WotAffordanceForm form = MakeForm(WotAffordanceKind.Property, "p", "{}",
                """{"uav:mapToNodeId":"ns=2;i=1000"}""");

            Assert.That(form.TargetMapping.IsEmpty, Is.False);
            Assert.That(form.TargetMapping.TargetNodeId, Is.EqualTo("ns=2;i=1000"));
        }
    }
}
