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

#nullable enable
#pragma warning disable CA2007

using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable]
    public class ContinuationPointTests
    {
        [Test]
        public void ResultMaskPropertiesReflectRequestedFields()
        {
            var continuationPoint = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.ReferenceTypeId |
                    BrowseResultMask.IsForward |
                    BrowseResultMask.NodeClass |
                    BrowseResultMask.BrowseName |
                    BrowseResultMask.DisplayName |
                    BrowseResultMask.TypeDefinition
            };

            Assert.Multiple(() =>
            {
                Assert.That(continuationPoint.ReferenceTypeIdRequired, Is.True);
                Assert.That(continuationPoint.IsForwardRequired, Is.True);
                Assert.That(continuationPoint.NodeClassRequired, Is.True);
                Assert.That(continuationPoint.BrowseNameRequired, Is.True);
                Assert.That(continuationPoint.DisplayNameRequired, Is.True);
                Assert.That(continuationPoint.TypeDefinitionRequired, Is.True);
                Assert.That(continuationPoint.TargetAttributesRequired, Is.True);
            });
        }

        [Test]
        public void TargetAttributesRequiredUsesNodeClassMaskWhenResultMaskIsEmpty()
        {
            var continuationPoint = new ContinuationPoint
            {
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = BrowseResultMask.None
            };

            Assert.That(continuationPoint.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void EmptyResultMaskDoesNotRequireOptionalFields()
        {
            var continuationPoint = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.Multiple(() =>
            {
                Assert.That(continuationPoint.ReferenceTypeIdRequired, Is.False);
                Assert.That(continuationPoint.IsForwardRequired, Is.False);
                Assert.That(continuationPoint.NodeClassRequired, Is.False);
                Assert.That(continuationPoint.BrowseNameRequired, Is.False);
                Assert.That(continuationPoint.DisplayNameRequired, Is.False);
                Assert.That(continuationPoint.TypeDefinitionRequired, Is.False);
                Assert.That(continuationPoint.TargetAttributesRequired, Is.False);
            });
        }
    }
}
