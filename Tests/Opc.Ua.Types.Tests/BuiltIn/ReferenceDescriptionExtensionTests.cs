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

using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceDescriptionExtensionTests
    {
        [Test]
        public void SetReferenceTypeHonorsResultMask()
        {
            var reference = new ReferenceDescription();
            var referenceTypeId = new NodeId(123, 2);

            reference.SetReferenceType(
                BrowseResultMask.ReferenceTypeId | BrowseResultMask.IsForward,
                referenceTypeId,
                true);

            Assert.That(reference.ReferenceTypeId, Is.EqualTo(referenceTypeId));
            Assert.That(reference.IsForward, Is.True);

            reference.SetReferenceType(BrowseResultMask.None, referenceTypeId, true);

            Assert.That(reference.ReferenceTypeId.IsNull, Is.True);
            Assert.That(reference.IsForward, Is.False);
        }

        [Test]
        public void SetTargetAttributesHonorsResultMask()
        {
            var reference = new ReferenceDescription();
            var browseName = new QualifiedName("Target", 2);
            var displayName = new LocalizedText("Target Display");
            var typeDefinition = new ExpandedNodeId(new NodeId(321, 2));

            reference.SetTargetAttributes(
                BrowseResultMask.NodeClass |
                    BrowseResultMask.BrowseName |
                    BrowseResultMask.DisplayName |
                    BrowseResultMask.TypeDefinition,
                NodeClass.Variable,
                browseName,
                displayName,
                typeDefinition);

            Assert.That(reference.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(reference.BrowseName, Is.EqualTo(browseName));
            Assert.That(reference.DisplayName, Is.EqualTo(displayName));
            Assert.That(reference.TypeDefinition, Is.EqualTo(typeDefinition));

            reference.SetTargetAttributes(
                BrowseResultMask.None,
                NodeClass.Variable,
                browseName,
                displayName,
                typeDefinition);

            Assert.That(reference.NodeClass, Is.EqualTo(NodeClass.Unspecified));
            Assert.That(reference.BrowseName.IsNull, Is.True);
            Assert.That(reference.DisplayName.IsNull, Is.True);
            Assert.That(reference.TypeDefinition.IsNull, Is.True);
        }
    }
}
