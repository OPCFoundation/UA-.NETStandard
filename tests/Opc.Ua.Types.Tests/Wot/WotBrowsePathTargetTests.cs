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

#nullable enable

using System;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    public sealed class WotBrowsePathTargetTests
    {
        [TestCase("/Objects/p:Pump&/One", "Pump/One", RelativePathFormatter.ElementType.AnyHierarchical)]
        [TestCase(".p:Value", "Value", RelativePathFormatter.ElementType.AnyComponent)]
        [TestCase("<#p:Contains>p:Value", "Value", RelativePathFormatter.ElementType.ForwardReference)]
        [TestCase("<!ua:HasComponent>p:Value", "Value", RelativePathFormatter.ElementType.InverseReference)]
        [TestCase("/{urn:form}Value", "Value", RelativePathFormatter.ElementType.AnyHierarchical)]
        [TestCase("/nsu=urn:form;Value", "Value", RelativePathFormatter.ElementType.AnyHierarchical)]
        [TestCase("/p:A&.B&:C&#D&!E&&F", "A.B:C#D!E&F", RelativePathFormatter.ElementType.AnyHierarchical)]
        public void CaptureReusesNativeSelectorsAndEscaping(
            string path, string name, RelativePathFormatter.ElementType kind)
        {
            JsonObject root = Document(path);
            using WotDocument document = Parse(root);

            var target = WotBrowsePathTarget.FromForm(document, FormPointer);

            Assert.That(target, Is.Not.Null);
            WotBrowsePathStep step = target!.Elements[^1];
            Assert.That(step.TargetName, Is.EqualTo(new WotBrowsePathElement("urn:form", name)));
            Assert.That(step.ReferenceKind, Is.EqualTo(kind));
            Assert.That(step.IncludeSubtypes, Is.EqualTo(!path.Contains("<#", StringComparison.Ordinal)));
            if (kind == RelativePathFormatter.ElementType.ForwardReference)
            {
                Assert.That(step.ReferenceName, Is.EqualTo(new WotBrowsePathElement("urn:form", "Contains")));
            }
            else if (kind == RelativePathFormatter.ElementType.InverseReference)
            {
                Assert.That(step.ReferenceName,
                    Is.EqualTo(new WotBrowsePathElement(WotVocabulary.OpcUaNamespace, "HasComponent")));
            }
            Assert.That(WotPortableIdentity.IsResolvableBrowsePath(path, anchored: true), Is.True);
        }

        [TestCase("root", "urn:root", "/uav:browsePath")]
        [TestCase("affordance", "urn:affordance", "/properties/Value/uav:browsePath")]
        [TestCase("form", "urn:form", "/properties/Value/forms/0/uav:browsePath")]
        public void InheritedPathRetainsItsOriginalCarryingContext(
            string scope, string expectedNamespace, string pointer)
        {
            JsonObject root = Document("/Objects/p:Value");
            var affordance = (JsonObject)root["properties"]!["Value"]!;
            var form = (JsonObject)affordance["forms"]![0]!;
            root["@context"] = new JsonObject { ["p"] = "urn:root" };
            affordance["@context"] = new JsonObject { ["p"] = "urn:affordance" };
            if (scope != "form")
            {
                form.Remove("uav:browsePath");
                (scope == "root" ? root : affordance)["uav:browsePath"] = "/Objects/p:Value";
            }
            WotBrowsePathTarget? target;
            using (WotDocument document = Parse(root))
            {
                target = WotBrowsePathTarget.FromForm(document, FormPointer);
            }

            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Elements[^1].TargetName.NamespaceUri, Is.EqualTo(expectedNamespace));
            Assert.That(target.JsonPointer, Is.EqualTo(pointer));
            Assert.That(target.AnchorId, Is.EqualTo("i=84"));
            Assert.That(form.ContainsKey("uav:browsePath"), Is.EqualTo(scope == "form"));
        }

        [Test]
        public void OrderedResetAndObjectPrefixDefinitionsSurviveCapture()
        {
            JsonObject root = Document("p:Value");
            root["properties"]!["Value"]!["forms"]![0]!["@context"] = new JsonArray(
                new JsonObject { ["p"] = "urn:discard" },
                null,
                new JsonObject
                {
                    ["p"] = new JsonObject { ["@id"] = "urn:after-reset", ["@prefix"] = true }
                });
            using WotDocument document = Parse(root);

            var target = WotBrowsePathTarget.FromForm(document, FormPointer);

            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Elements[0].TargetName.NamespaceUri, Is.EqualTo("urn:after-reset"));
        }

        [Test]
        public void ResetDoesNotRecoverAnOuterPrefix()
        {
            JsonObject root = Document("p:Value");
            root["properties"]!["Value"]!["forms"]![0]!["@context"] = null;
            using WotDocument document = Parse(root);

            ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [TestCase(true, false, "nsu=urn:source;s=RootAnchor")]
        [TestCase(true, true, "nsu=urn:source;s=FormAnchor")]
        [TestCase(false, false, "nsu=urn:source;s=Affordance")]
        public void ExplicitAnchorsPrecedeNearerFallbackIdentities(bool rootAnchor, bool formAnchor, string expected)
        {
            JsonObject root = Document("p:Value");
            JsonNode affordance = root["properties"]!["Value"]!;
            affordance["uav:id"] = "nsu=urn:source;s=Affordance";
            if (rootAnchor)
            {
                root["uav:browsePathAnchor"] = "nsu=urn:source;s=RootAnchor";
            }
            if (formAnchor)
            {
                affordance["forms"]![0]!["uav:browsePathAnchor"] = "nsu=urn:source;s=FormAnchor";
            }
            using WotDocument document = Parse(root);

            var target = WotBrowsePathTarget.FromForm(document, FormPointer);

            Assert.That(target, Is.Not.Null);
            Assert.That(target!.AnchorId, Is.EqualTo(expected));
        }

        [TestCase("uav:browsePathAnchor")]
        [TestCase("uav:id")]
        public void InvalidNearerAnchorDeclarationsCannotFallBack(string term)
        {
            JsonObject root = Document("p:Value");
            root["properties"]!["Value"]!["forms"]![0]![term] = 42;
            using WotDocument document = Parse(root);

            ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [TestCase("")]
        [TestCase("/")]
        [TestCase("/Objects/")]
        [TestCase("/0:Objects")]
        [TestCase("/1:Value")]
        [TestCase("/:Value")]
        [TestCase("/unknown:Value")]
        [TestCase("/{urn:unclosed")]
        [TestCase("/nsu=urn:unclosed")]
        [TestCase("/p:Value&")]
        [TestCase("/p:Value&Z")]
        [TestCase("<p:Contains")]
        public void MalformedPathsFailWithAnExplicitNativeStatus(string path)
        {
            using WotDocument document = Parse(Document(path));

            ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer));

            Assert.That(StatusCode.IsBad(exception!.StatusCode), Is.True);
        }

        [TestCase("/{https://example.org/model/}Value", "https://example.org/model/")]
        [TestCase("/nsu=https://example.org/model/;Value", "https://example.org/model/")]
        [TestCase("/nsu=urn:vendor%3Bpart%253B;Value", "urn:vendor;part%3B")]
        public void NamespaceUriDelimitersDoNotSplitPathSteps(string path, string expectedNamespace)
        {
            using WotDocument document = Parse(Document(path));

            var target = WotBrowsePathTarget.FromForm(document, FormPointer);

            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Elements.Count, Is.EqualTo(1));
            Assert.That(target.Elements[0].TargetName,
                Is.EqualTo(new WotBrowsePathElement(expectedNamespace, "Value")));
        }

        [Test]
        public void CharacterAndElementBoundsAcceptTheBoundaryAndRejectTheNextValue()
        {
            const string path = "/Objects/p:Value";
            using WotDocument document = Parse(Document(path));

            Assert.That(WotBrowsePathTarget.FromForm(document, FormPointer, path.Length, 2), Is.Not.Null);
            ServiceResultException? length = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer, path.Length - 1, 2));
            ServiceResultException? elements = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer, path.Length, 1));

            Assert.That(length!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(elements!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void UndeclaredPathsAreDistinctFromInvalidPaths()
        {
            JsonObject root = Document("/Objects/p:Value");
            ((JsonObject)root["properties"]!["Value"]!["forms"]![0]!).Remove("uav:browsePath");
            using WotDocument document = Parse(root);

            Assert.That(WotBrowsePathTarget.FromForm(document, FormPointer), Is.Null);
        }

        [TestCase("/")]
        [TestCase("/Objects/Pump/")]
        public void PortableAnnotationsAreNotAssumedToBeCompleteNativeTargets(string path)
        {
            using WotDocument document = Parse(Document(path));

            Assert.That(WotPortableIdentity.IsResolvableBrowsePath(path, anchored: false), Is.True);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [TestCase("0")]
        [TestCase("4294967295")]
        [TestCase("4294967296")]
        [TestCase("999999999999999999999999")]
        public void NumericNamespacePrefixesCannotBecomePortableAfterOverflow(string prefix)
        {
            string path = "/" + prefix + ":Value";
            JsonObject root = Document(path);
            root["properties"]!["Value"]!["forms"]![0]!["@context"] =
                new JsonObject { [prefix] = "urn:form" };
            using WotDocument document = Parse(root);

            Assert.That(WotPortableIdentity.IsResolvableBrowsePath(path, anchored: true), Is.False);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => WotBrowsePathTarget.FromForm(document, FormPointer));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        private static JsonObject Document(string path)
        {
            return new JsonObject
            {
                ["uav:id"] = "nsu=urn:source;s=Root",
                ["properties"] = new JsonObject
                {
                    ["Value"] = new JsonObject
                    {
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["@context"] = new JsonObject { ["p"] = "urn:form" },
                            ["uav:browsePath"] = path
                        })
                    }
                }
            };
        }

        private static WotDocument Parse(JsonObject document)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
        }

        private const string FormPointer = "/properties/Value/forms/0";
    }
}
