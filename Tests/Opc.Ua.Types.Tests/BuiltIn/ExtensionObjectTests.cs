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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#pragma warning disable IDE0028 // Simplify collection initialization
#pragma warning disable IDE0305 // Simplify collection initialization

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ExtensionObjectTests
    {
        /// <summary>
        /// Validate ExtensionObject special cases and constructors.
        /// </summary>
        [Test]
        public void TestExtensionObject()
        {
            // Validate the default constructor
            var extensionObject_Default = new ExtensionObject();
            Assert.NotNull(extensionObject_Default);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject_Default.TypeId);
            Assert.AreEqual(ExtensionObjectEncoding.None, extensionObject_Default.Encoding);
            Assert.IsTrue(extensionObject_Default.IsNull);
            // Constructor by ExtensionObject
            var extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.NotNull(extensionObject);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject.TypeId);
            Assert.AreEqual(ExtensionObjectEncoding.None, extensionObject.Encoding);
            Assert.IsFalse(extensionObject.TryGetEncodeable(out IEncodeable enc));
            Assert.IsFalse(extensionObject.TryGetAsBinary(out var _));
            Assert.IsFalse(extensionObject.TryGetAsXml(out var _));
            Assert.IsFalse(extensionObject.TryGetAsJson(out var _));
            Assert.IsTrue(extensionObject.IsNull);
            // static extensions
            Assert.Null(Ua.ExtensionObject.ToEncodeable(default));
            Assert.Null(Ua.ExtensionObject.ToArray(null, typeof(object)));
            Assert.Null(Ua.ExtensionObject.ToList<object>(null));
            // constructor by ExpandedNodeId
            extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.AreEqual(0, extensionObject.GetHashCode());
#pragma warning disable CS0618 // Type or member is obsolete
            NUnit.Framework.Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new object()));
            NUnit.Framework.Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new byte[] { 1, 2, 3 }));
#pragma warning restore CS0618 // Type or member is obsolete
            // constructor by object
            ByteString bytes = [1, 2, 3];
            extensionObject = new ExtensionObject(default, bytes);
            Assert.NotNull(extensionObject);
            Assert.AreEqual(extensionObject, extensionObject);
            // string extension
            string extensionObjectString = extensionObject.ToString();
            NUnit.Framework.Assert
                .Throws<FormatException>(() => extensionObject.ToString("123", null));
            Assert.NotNull(extensionObjectString);
            // IsEqual operator
            ExtensionObject clonedExtensionObject = extensionObject.WithTypeId(new ExpandedNodeId(333));
            Assert.AreNotEqual(extensionObject, clonedExtensionObject);
            Assert.AreNotEqual(extensionObject, extensionObject_Default);
            Assert.AreNotEqual(extensionObject, new object());
            Assert.AreEqual(clonedExtensionObject, clonedExtensionObject);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject.TypeId);
            Assert.AreEqual(
                ExpandedNodeId.Null.GetHashCode(),
                extensionObject.TypeId.GetHashCode());
            Assert.AreEqual(ExtensionObjectEncoding.Binary, extensionObject.Encoding);
            Assert.AreEqual(bytes, extensionObject.TryGetAsBinary(out ByteString bs) ? bs : default);
            // collection
            var collection = new ExtensionObjectCollection();
            Assert.NotNull(collection);
            collection = new ExtensionObjectCollection(100);
            Assert.NotNull(collection);
            collection = [.. collection];
            Assert.NotNull(collection);
            collection = CoreUtils.Clone(collection);
            // default value is null
            Assert.That(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject), Is.EqualTo(ExtensionObject.Null));
        }
    }
}
