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
using System.Text;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class RegistryTaskFormsTests
    {
        [TestCase("entry")]
        [TestCase("urn:example:item")]
        [TestCase("Upper_Case-1.2")]
        public void ExplicitIdentifiersAreNotNormalizedOrDerivedFromDocumentBytes(string id)
        {
            Assert.That(RegistryTaskForms.Id([new("id", Variant.From(id))], "id"), Is.EqualTo(id));
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a\nb")]
        public void InvalidIdentifiersCannotBePrepared(string id)
        {
            Assert.That(() => RegistryTaskForms.Id([new("id", Variant.From(id))], "id"), Throws.ArgumentException);
        }

        [TestCase(128)]
        [TestCase(129)]
        public void IdentifierLengthIsBoundedWithoutTruncation(int length)
        {
            string id = new('x', length);
            if (length == 128)
            {
                Assert.That(RegistryTaskForms.Id([new("id", Variant.From(id))], "id"), Is.EqualTo(id));
            }
            else
            {
                Assert.That(() => RegistryTaskForms.Id([new("id", Variant.From(id))], "id"), Throws.ArgumentException);
            }
        }

        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void NonzeroEpochsPreserveTheirFullUnsignedWidth(uint epoch)
        {
            Assert.That(RegistryTaskForms.ExpectedEpoch([new("epoch", Variant.From(epoch))]), Is.EqualTo(epoch));
        }

        [Test]
        public void ZeroEpochCannotDisableServerSideConcurrencyChecks()
        {
            Assert.That(() => RegistryTaskForms.ExpectedEpoch([new("epoch", Variant.From(0u))]),
                Throws.ArgumentException);
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("wrong-type")]
        [TestCase("null-field")]
        public void InvalidTypedFieldsFailExplicitly(string fault)
        {
            ArrayOf<CompanionValue> values = fault switch
            {
                "missing" => [],
                "duplicate" => [new("epoch", Variant.From(1u)), new("epoch", Variant.From(2u))],
                "wrong-type" => [new("epoch", Variant.From(1))],
                "null-field" => [null!],
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            Assert.That(() => RegistryTaskForms.ExpectedEpoch(values), Throws.ArgumentException);
        }

        [TestCase(false, 65536)]
        [TestCase(false, 65537)]
        [TestCase(true, 65536)]
        [TestCase(true, 65537)]
        public void JsonUsesAnExactUtf8ByteLimit(bool multibyte, int bytes)
        {
            string json = multibyte ? "{\"v\":\"" + new string('\u00E9', 32764) + "\"}" :
                "{}" + new string(' ', 65534);
            if (bytes == 65537)
            {
                json += " ";
            }
            Assert.That(Encoding.UTF8.GetByteCount(json), Is.EqualTo(bytes));
            ArrayOf<CompanionValue> inputs = [new("document", Variant.From(json))];
            if (bytes == 65536)
            {
                ByteString document = RegistryTaskForms.Json(inputs);
                Assert.That(document.Length, Is.EqualTo(65536));
                Assert.That(Encoding.UTF8.GetString(document.Span), Is.EqualTo(json));
            }
            else
            {
                Assert.That(() => RegistryTaskForms.Json(inputs), Throws.ArgumentException);
            }
        }

        [Test]
        public void InvalidUtf16IsNotSilentlyReplacedDuringEncoding()
        {
            Assert.That(() => RegistryTaskForms.Json([new("document", Variant.From("{\"v\":\"\uD800\"}"))]),
                Throws.TypeOf<EncoderFallbackException>());
        }

        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("""{"a":1,"a":2}""")]
        [TestCase("""{"nested":{"a":1,"a":2}}""")]
        [TestCase("""{"items":[{"a":1,"a":2}]}""")]
        public void NonObjectsAndDuplicateMembersAtAnyDepthAreRejected(string json)
        {
            Assert.That(() => RegistryTaskForms.Json([new("document", Variant.From(json))]), Throws.ArgumentException);
        }

        [TestCase("""{"title":"Missing context"}""")]
        [TestCase("""{"@context":"https://www.w3.org/2022/wot/td/v1.1"}""")]
        public void WotRegistrationRequiresItsContextAndTitle(string json)
        {
            Assert.That(() => RegistryTaskForms.Json([new("document", Variant.From(json))], wot: true),
                Throws.ArgumentException);
        }

        [Test]
        public void PreparedDocumentsHaveIndependentBytesAndReviewIdentifiesTheCreatedVersion()
        {
            byte[] bytes = """{"sensitive":"value-not-for-review"}"""u8.ToArray();
            var task = new RegistryLifecycleTask(
                new CompanionTarget("xregistry", new NodeId("group", 2), "group", "Registry group"),
                "create-version", 9, "resource-id", "version-id", new ByteString(bytes), group: "group-id");
            bytes[0] = 0;

            Assert.That(task.Document.Span[0], Is.EqualTo((byte)'{'));
            Assert.That(task.Review, Does.Contain("resource-id").And.Contain("version-id").And.Contain("group-id"));
            Assert.That(task.Review, Does.Not.Contain("value-not-for-review"));
            Assert.That(task.Review, Does.Contain("SHA-256"));
        }
    }
}
