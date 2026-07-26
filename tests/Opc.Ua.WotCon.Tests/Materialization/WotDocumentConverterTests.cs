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

using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests for <see cref="WotConversionOutput"/> factory methods and the
    /// <see cref="WotNodeSetDocumentConverter"/> production implementation.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotDocumentConverterTests
    {
        [Test]
        public void FailureOutputSucceededIsFalse()
        {
            WotConversionOutput output = WotConversionOutput.Failure("Something went wrong.");

            Assert.That(output.Succeeded, Is.False);
            Assert.That(output.NodeSet, Is.Null);
            Assert.That(output.Errors, Has.Length.EqualTo(1));
            Assert.That(output.Errors[0], Does.Contain("Something went wrong."));
        }

        [Test]
        public void FailureOutputWithMultipleErrors()
        {
            WotConversionOutput output = WotConversionOutput.Failure("error1", "error2");

            Assert.That(output.Errors, Has.Length.EqualTo(2));
        }

        [Test]
        public void SuccessOutputSucceededIsTrue()
        {
            var nodeSet = new UANodeSet();
            WotConversionOutput output = WotConversionOutput.Success(nodeSet);

            Assert.That(output.Succeeded, Is.True);
            Assert.That(output.NodeSet, Is.SameAs(nodeSet));
            Assert.That(output.Errors, Is.Empty);
        }

        [Test]
        public void ConstructorWithDefaultErrorsIsEmpty()
        {
            var output = new WotConversionOutput(null, default);

            Assert.That(output.Errors, Is.Empty);
            Assert.That(output.Succeeded, Is.False);
        }

        [Test]
        public void NodeSetDocumentConverterConvertsValidThingModel()
        {
            var converter = new WotNodeSetDocumentConverter();
            byte[] content = TestMaterialization.Tm("urn:test-tm");

            var version = new WotResourceVersion(
                versionId: "v1",
                content: content,
                contentType: "application/tm+json",
                format: "WoT-TM/1.0",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingModels,
                resourceId: "test-tm",
                kind: WoTDocumentKindEnum.ThingModel,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();
            WotRegistrySnapshot snapshot = service.Current;

            WotConversionOutput output = converter.Convert(resource, content, snapshot);

            Assert.That(output, Is.Not.Null);
        }

        [Test]
        public void NodeSetDocumentConverterFailsOnInvalidJson()
        {
            var converter = new WotNodeSetDocumentConverter();
            byte[] invalidContent = TestMaterialization.InvalidJson();

            var version = new WotResourceVersion(
                versionId: "v1",
                content: invalidContent,
                contentType: "application/td+json",
                format: "WoT-TD/1.1",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingDescriptions,
                resourceId: "bad",
                kind: WoTDocumentKindEnum.ThingDescription,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();
            WotRegistrySnapshot snapshot = service.Current;

            WotConversionOutput output = converter.Convert(resource, invalidContent, snapshot);

            Assert.That(output.Succeeded, Is.False);
            Assert.That(output.Errors, Is.Not.Empty);
        }

        [Test]
        public void NodeSetDocumentConverterCanBeInstantiatedWithoutOptions()
        {
            var converter = new WotNodeSetDocumentConverter();
            Assert.That(converter, Is.Not.Null);
        }

        [Test]
        public void NodeSetDocumentConverterCanBeInstantiatedWithOptions()
        {
            var options = new WotNodeSetConverterOptions();
            var converter = new WotNodeSetDocumentConverter(options);
            Assert.That(converter, Is.Not.Null);
        }

        [Test]
        public void SuccessOutputFromConstructorWithNonDefaultErrors()
        {
            var nodeSet = new UANodeSet();
            var errors = ImmutableArray<string>.Empty;
            var output = new WotConversionOutput(nodeSet, errors);

            Assert.That(output.Succeeded, Is.True);
            Assert.That(output.Errors, Is.Empty);
        }

        [Test]
        public void FailureOutputRootNodeIdIsNull()
        {
            WotConversionOutput output = WotConversionOutput.Failure("fail");

            Assert.That(output.RootNodeId, Is.Null);
        }
    }
}
