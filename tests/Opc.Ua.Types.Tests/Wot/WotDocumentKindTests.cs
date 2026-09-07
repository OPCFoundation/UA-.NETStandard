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
 *
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
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Additional tests for WotDocument covering ThingDescription kind,
    /// Unknown kind, Forms property, and null-guard paths.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotDocumentKindTests
    {
        [Test]
        public void KindIsThingDescriptionForUavObjectToken()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"uav:object\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
        }

        [Test]
        public void KindIsThingDescriptionForUavVariableToken()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"uav:variable\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
        }

        [Test]
        public void KindIsThingDescriptionForUavMethodToken()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"uav:method\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
        }

        [Test]
        public void KindIsThingModelForUavVariableTypeToken()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"uav:variableType\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingModel));
        }

        [Test]
        public void KindIsUnknownWhenTypeTokenIsUnrecognized()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"vendor:SomeThing\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.Unknown));
        }

        [Test]
        public void KindIsUnknownWhenTypeMemberIsAbsent()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"no-type-at-all\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.Unknown));
        }

        [Test]
        public void FormsPropertyReturnsArrayElements()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@type\":\"tm:ThingModel\"," +
                "\"forms\":[{\"href\":\"coap://example.com/sensor\"},{\"href\":\"http://example.com/s\"}]}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Forms, Has.Count.EqualTo(2));
            Assert.That(
                document.Forms[0].GetProperty("href").GetString(),
                Is.EqualTo("coap://example.com/sensor"));
        }

        [Test]
        public void FormsPropertyReturnsEmptyListWhenAbsent()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"@type\":\"tm:ThingModel\",\"title\":\"T\"}");

            using var document = WotDocument.Parse(json);

            Assert.That(document.Forms, Is.Empty);
        }

        [Test]
        public void TryGetUavNullArgThrows()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            Assert.That(
                () => document.TryGetUav(null!, out _),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryEvaluatePointerInstanceNullArgThrows()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            Assert.That(
                () => document.TryEvaluatePointer(null!, out _),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryEvaluatePointerStaticNullArgThrows()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(
                () => WotDocument.TryEvaluatePointer(root, null!, out _),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryEvaluatePointerEmptyStringAddressesRoot()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer(string.Empty, out JsonElement value);

            Assert.That(found, Is.True);
            Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Object));
        }

        [Test]
        public void TryEvaluatePointerReturnsFalseForLeadingNonSlash()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer("title", out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void WriteCanonicalWritesToStream()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"b\":2,\"a\":1}");
            using var document = WotDocument.Parse(json);
            using var stream = new MemoryStream();

            document.WriteCanonical(stream);

            Assert.That(stream.Length, Is.GreaterThan(0));
            string canonical = Encoding.UTF8.GetString(stream.ToArray());
            Assert.That(canonical, Does.StartWith("{\"a\""));
        }

        [Test]
        public void WriteCanonicalNullStreamThrows()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            Assert.That(
                () => document.WriteCanonical(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void WriteNullStreamThrows()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            using var document = WotDocument.Parse(json);

            Assert.That(
                () => document.Write(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void ParseThrowsFormatExceptionWhenJsonExceedsMaxJsonDocumentSize()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            var options = new WotNodeSetConverterOptions { MaxJsonDocumentSize = 1 };

            Assert.That(
                () => WotDocument.Parse(json, options),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void TryEvaluatePointerFollowsArrayByNumericIndex()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"items\":[\"alpha\",\"beta\",\"gamma\"]}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer("/items/1", out JsonElement element);

            Assert.That(found, Is.True);
            Assert.That(element.GetString(), Is.EqualTo("beta"));
        }

        [Test]
        public void TryEvaluatePointerReturnsFalseWhenIntermediateNodeIsScalar()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"a\":42}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer("/a/nested", out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void TryEvaluatePointerUnescapesTilde1AsSlashInPropertyName()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"a/b\":99}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer("/a~1b", out JsonElement element);

            Assert.That(found, Is.True);
            Assert.That(element.GetInt32(), Is.EqualTo(99));
        }

        [Test]
        public void TryEvaluatePointerReturnsFalseForArrayIndexWithLeadingZero()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"items\":[\"a\",\"b\"]}");
            using var document = WotDocument.Parse(json);

            bool found = document.TryEvaluatePointer("/items/01", out _);

            Assert.That(found, Is.False);
        }
    }
}
