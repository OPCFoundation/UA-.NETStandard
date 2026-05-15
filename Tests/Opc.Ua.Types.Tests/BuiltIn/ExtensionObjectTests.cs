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
            Assert.That(extensionObject_Default.IsNull, Is.True);
            Assert.That(extensionObject_Default.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(extensionObject_Default.Encoding, Is.EqualTo(ExtensionObjectEncoding.None));
            Assert.That(extensionObject_Default.IsNull, Is.True);
            // Constructor by ExtensionObject
            var extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.That(extensionObject.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(extensionObject.Encoding, Is.EqualTo(ExtensionObjectEncoding.None));
            Assert.That(extensionObject.TryGetValue(out IEncodeable enc), Is.False);
            Assert.That(extensionObject.TryGetAsBinary(out ByteString _), Is.False);
            Assert.That(extensionObject.TryGetAsXml(out XmlElement _), Is.False);
            Assert.That(extensionObject.TryGetAsJson(out string _), Is.False);
            Assert.That(extensionObject.IsNull, Is.True);
            // static extensions
            Assert.That(ExtensionObject.ToEncodeable(default), Is.Null);
            // constructor by ExpandedNodeId
            extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.That(extensionObject.GetHashCode(), Is.Zero);
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(ExtensionObject.ToArray(null, typeof(object)), Is.Null);
            Assert.That(ExtensionObject.ToList<object>(null), Is.Null);
            Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new object()));
            Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new byte[] { 1, 2, 3 }));
#pragma warning restore CS0618 // Type or member is obsolete
            // constructor by object
            ByteString bytes = [1, 2, 3];
            extensionObject = new ExtensionObject(default, bytes);
            Assert.That(extensionObject.IsNull, Is.False);
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(extensionObject.Equals(extensionObject), Is.True);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            // string extension
            string extensionObjectString = extensionObject.ToString();
            Assert
                .Throws<FormatException>(() => extensionObject.ToString("123", null));
            Assert.That(extensionObjectString, Is.Not.Null);
            // IsEqual operator
            ExtensionObject clonedExtensionObject = extensionObject.WithTypeId(new ExpandedNodeId(333));
            Assert.That(extensionObject, Is.Not.EqualTo(clonedExtensionObject));
            Assert.That(extensionObject, Is.Not.EqualTo(extensionObject_Default));
            Assert.That(extensionObject, Is.Not.EqualTo(new object()));
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(clonedExtensionObject.Equals(clonedExtensionObject), Is.True);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(extensionObject.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(
                extensionObject.TypeId.GetHashCode(),
                Is.EqualTo(ExpandedNodeId.Null.GetHashCode()));
            Assert.That(extensionObject.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
            Assert.That(extensionObject.TryGetAsBinary(out ByteString bs) ? bs : default, Is.EqualTo(bytes));
            // default value is null
            Assert.That(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject), Is.EqualTo(ExtensionObject.Null));
        }
    }
}
