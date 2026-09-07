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

using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotFormExtractor"/>.
    /// </summary>
    [TestFixture]
    public sealed class WotFormExtractorTests
    {
        private static byte[] Utf8(string json)
        {
            return Encoding.UTF8.GetBytes(json);
        }

        [Test]
        public void ExtractReturnsEmptyForNonObject()
        {
            var forms = WotFormExtractor.Extract(Utf8("[]"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractReturnsEmptyForEmptyDocument()
        {
            var forms = WotFormExtractor.Extract(Utf8("{}"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractReturnsEmptyForMalformedJson()
        {
            var forms = WotFormExtractor.Extract(Utf8("{not valid json}"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractPropertyFormWithDefaultOps()
        {
            string td =
                "{\"properties\":{\"temp\":{\"type\":\"number\"," +
                "\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            var form = forms[0];
            Assert.That(form.Kind, Is.EqualTo(WotAffordanceKind.Property));
            Assert.That(form.AffordanceName, Is.EqualTo("temp"));
            Assert.That(form.Href, Is.EqualTo("http://example.com/temp"));
            Assert.That(form.Operations, Does.Contain("readproperty"));
            Assert.That(form.Operations, Does.Contain("writeproperty"));
        }

        [Test]
        public void ExtractReadOnlyPropertyDefaultOpsContainOnlyReadProperty()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"readOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/sensor\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Not.Contain("writeproperty"));
        }

        [Test]
        public void ExtractWriteOnlyPropertyDefaultOpsContainOnlyWriteProperty()
        {
            string td =
                "{\"properties\":{\"actuator\":{\"writeOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/actuator\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Not.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Contain("writeproperty"));
        }

        [Test]
        public void ExtractObservablePropertyDefaultOpsIncludeObserve()
        {
            string td =
                "{\"properties\":{\"power\":{\"observable\":true," +
                "\"forms\":[{\"href\":\"http://example.com/power\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("observeproperty"));
            Assert.That(forms[0].Operations, Does.Contain("unobserveproperty"));
        }

        [Test]
        public void ExtractActionFormWithDefaultOps()
        {
            string td =
                "{\"actions\":{\"reset\":{\"forms\":[{\"href\":\"http://example.com/reset\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Kind, Is.EqualTo(WotAffordanceKind.Action));
            Assert.That(forms[0].Operations, Does.Contain("invokeaction"));
            Assert.That(forms[0].Operations, Has.Length.EqualTo(1));
        }

        [Test]
        public void ExtractEventFormWithDefaultOps()
        {
            string td =
                "{\"events\":{\"alarm\":{\"forms\":[{\"href\":\"http://example.com/alarm\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Kind, Is.EqualTo(WotAffordanceKind.Event));
            Assert.That(forms[0].Operations, Does.Contain("subscribeevent"));
            Assert.That(forms[0].Operations, Does.Contain("unsubscribeevent"));
        }

        [Test]
        public void ExtractFormWithExplicitSingleOpOverride()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"forms\":[{\"href\":\"http://example.com/sensor\"," +
                "\"op\":\"readproperty\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Has.Length.EqualTo(1));
        }

        [Test]
        public void ExtractFormWithExplicitArrayOpOverride()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"forms\":[{\"href\":\"http://example.com/sensor\"," +
                "\"op\":[\"readproperty\",\"observeproperty\"]}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Contain("observeproperty"));
        }

        [Test]
        public void ExtractFormLevelSecurityOverridesThingSecurity()
        {
            string td =
                "{\"security\":\"nosec_sc\",\"properties\":{\"sensor\":{\"forms\":[{" +
                "\"href\":\"http://example.com/sensor\",\"security\":\"basic_sc\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("basic_sc"));
            Assert.That(forms[0].SecuritySchemes, Does.Not.Contain("nosec_sc"));
        }

        [Test]
        public void ExtractFormWithoutSecurityFallsBackToThingSecurity()
        {
            string td =
                "{\"security\":\"nosec_sc\",\"properties\":{\"sensor\":{\"forms\":[{" +
                "\"href\":\"http://example.com/sensor\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("nosec_sc"));
        }

        [Test]
        public void ExtractThingLevelSecurityArray()
        {
            string td =
                "{\"security\":[\"auth1_sc\",\"auth2_sc\"],\"properties\":{\"p\":{\"forms\":[{" +
                "\"href\":\"http://example.com/p\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].SecuritySchemes, Does.Contain("auth1_sc"));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("auth2_sc"));
        }

        [Test]
        public void ExtractFormlessAffordanceProducesFormlessDescriptor()
        {
            string td = "{\"properties\":{\"nada\":{\"type\":\"string\"}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].AffordanceName, Is.EqualTo("nada"));
            Assert.That(forms[0].Href, Is.Null);
        }

        [Test]
        public void ExtractMultipleFormsProducesOneEntryPerForm()
        {
            string td =
                "{\"properties\":{\"p\":{\"forms\":[" +
                "{\"href\":\"http://example.com/p\"}," +
                "{\"href\":\"http://backup.com/p\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(2));
        }

        [Test]
        public void ExtractMultipleAffordancesAcrossKinds()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}," +
                "\"actions\":{\"reset\":{\"forms\":[{\"href\":\"http://example.com/reset\"}]}}," +
                "\"events\":{\"alarm\":{\"forms\":[{\"href\":\"http://example.com/alarm\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(3));
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Property), Is.True);
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Action), Is.True);
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Event), Is.True);
        }

        [Test]
        public void ExtractJsonPointerForFirstForm()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Is.EqualTo("/properties/temp/forms/0"));
        }

        [Test]
        public void ExtractJsonPointerForSecondForm()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[" +
                "{\"href\":\"http://a.com/t\"},{\"href\":\"http://b.com/t\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Is.EqualTo("/properties/temp/forms/0"));
            Assert.That(forms[1].JsonPointer, Is.EqualTo("/properties/temp/forms/1"));
        }

        [Test]
        public void ExtractEscapesSpecialCharactersInAffordanceName()
        {
            string td =
                "{\"properties\":{\"te~mp/val\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Does.Contain("te~0mp~1val"));
        }

        [Test]
        public void ExtractFormWithContentType()
        {
            string td =
                "{\"properties\":{\"data\":{\"forms\":[{\"href\":\"http://example.com/data\"," +
                "\"contentType\":\"application/octet-stream\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].ContentType, Is.EqualTo("application/octet-stream"));
        }

        [Test]
        public void ExtractBothReadOnlyAndWriteOnlyYieldsReadProperty()
        {
            string td =
                "{\"properties\":{\"odd\":{\"readOnly\":true,\"writeOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/odd\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
        }
    }
}
