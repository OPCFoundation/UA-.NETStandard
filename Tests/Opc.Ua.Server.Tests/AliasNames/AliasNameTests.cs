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
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("AliasNames")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AliasStoreChangedEventArgsTests
    {
        [Test]
        public void ConstructorSetsProperties()
        {
            var categoryId = new NodeId(100);
            uint lastChange = 42;

            var args = new AliasStoreChangedEventArgs(categoryId, lastChange);

            Assert.That(args.CategoryId, Is.EqualTo(categoryId));
            Assert.That(args.LastChange, Is.EqualTo(lastChange));
        }

        [Test]
        public void ConstructorWithNullCategoryIdThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new AliasStoreChangedEventArgs(NodeId.Null, 42));
        }

        [Test]
        public void ConstructorWithZeroLastChangeIsValid()
        {
            var categoryId = new NodeId(100);

            var args = new AliasStoreChangedEventArgs(categoryId, 0);

            Assert.That(args.LastChange, Is.Zero);
        }

        [Test]
        public void ConstructorWithMaxLastChangeIsValid()
        {
            var categoryId = new NodeId(100);

            var args = new AliasStoreChangedEventArgs(categoryId, uint.MaxValue);

            Assert.That(args.LastChange, Is.EqualTo(uint.MaxValue));
        }
    }

    [TestFixture]
    [Category("AliasNames")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AliasNameCategoryDescriptorTests
    {
        [Test]
        public void ConstructorSetsProperties()
        {
            var nodeId = new NodeId(200);
            var browseName = new QualifiedName("TestCategory");

            var descriptor = new AliasNameCategoryDescriptor(nodeId, browseName);

            Assert.That(descriptor.NodeId, Is.EqualTo(nodeId));
            Assert.That(descriptor.BrowseName, Is.EqualTo(browseName));
            Assert.That(descriptor.Capabilities, Is.EqualTo(AliasNameCapabilities.None));
            Assert.That(descriptor.SubCategories, Is.Empty);
        }

        [Test]
        public void ConstructorWithNullNodeIdThrowsArgumentException()
        {
            var browseName = new QualifiedName("TestCategory");

            Assert.Throws<ArgumentException>(() =>
                new AliasNameCategoryDescriptor(NodeId.Null, browseName));
        }

        [Test]
        public void ConstructorWithNullBrowseNameThrowsArgumentException()
        {
            var nodeId = new NodeId(200);

            Assert.Throws<ArgumentException>(() =>
                new AliasNameCategoryDescriptor(nodeId, default));
        }

        [Test]
        public void ConstructorWithCapabilitiesSetsCapabilities()
        {
            var nodeId = new NodeId(200);
            var browseName = new QualifiedName("TestCategory");
            var capabilities = AliasNameCapabilities.FindAliasVerbose | AliasNameCapabilities.AddAliasesToCategory;

            var descriptor = new AliasNameCategoryDescriptor(nodeId, browseName, capabilities);

            Assert.That(descriptor.Capabilities, Is.EqualTo(capabilities));
        }

        [Test]
        public void ConstructorWithSubCategoriesSetsSubCategories()
        {
            var nodeId = new NodeId(200);
            var browseName = new QualifiedName("ParentCategory");
            var subCategory = new AliasNameCategoryDescriptor(
                new NodeId(201), new QualifiedName("SubCategory"));
            var subCategories = new[] { subCategory };

            var descriptor = new AliasNameCategoryDescriptor(
                nodeId, browseName, AliasNameCapabilities.None, subCategories);

            Assert.That(descriptor.SubCategories, Has.Count.EqualTo(1));
        }
    }
}
