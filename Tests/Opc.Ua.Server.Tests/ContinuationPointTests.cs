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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ContinuationPoint")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ContinuationPointTests
    {
        [Test]
        public void ReferenceTypeIdRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.ReferenceTypeId
            };

            Assert.That(cp.ReferenceTypeIdRequired, Is.True);
        }

        [Test]
        public void ReferenceTypeIdRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.ReferenceTypeIdRequired, Is.False);
        }

        [Test]
        public void IsForwardRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.IsForward
            };

            Assert.That(cp.IsForwardRequired, Is.True);
        }

        [Test]
        public void IsForwardRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.IsForwardRequired, Is.False);
        }

        [Test]
        public void NodeClassRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.NodeClass
            };

            Assert.That(cp.NodeClassRequired, Is.True);
        }

        [Test]
        public void NodeClassRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.NodeClassRequired, Is.False);
        }

        [Test]
        public void BrowseNameRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.BrowseName
            };

            Assert.That(cp.BrowseNameRequired, Is.True);
        }

        [Test]
        public void BrowseNameRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.BrowseNameRequired, Is.False);
        }

        [Test]
        public void DisplayNameRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.DisplayName
            };

            Assert.That(cp.DisplayNameRequired, Is.True);
        }

        [Test]
        public void DisplayNameRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.DisplayNameRequired, Is.False);
        }

        [Test]
        public void TypeDefinitionRequiredReturnsTrueWhenMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.TypeDefinition
            };

            Assert.That(cp.TypeDefinitionRequired, Is.True);
        }

        [Test]
        public void TypeDefinitionRequiredReturnsFalseWhenMaskNotSet()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.TypeDefinitionRequired, Is.False);
        }

        [Test]
        public void TargetAttributesRequiredReturnsTrueWhenNodeClassMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredReturnsTrueWhenNodeClassInResultMask()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.NodeClass
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredReturnsTrueWhenBrowseNameInResultMask()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.BrowseName
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredReturnsTrueWhenDisplayNameInResultMask()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.DisplayName
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredReturnsTrueWhenTypeDefinitionInResultMask()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.TypeDefinition
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredReturnsFalseWhenOnlyReferenceTypeIdInMask()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.ReferenceTypeId
            };

            Assert.That(cp.TargetAttributesRequired, Is.False);
        }

        [Test]
        public void TargetAttributesRequiredReturnsFalseWhenNothingSet()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.TargetAttributesRequired, Is.False);
        }

        [Test]
        public void DisposeDisposesDataWhenDisposable()
        {
            var disposableMock = new Mock<IDisposable>();
            var cp = new ContinuationPoint
            {
                Data = disposableMock.Object
            };

            cp.Dispose();

            disposableMock.Verify(d => d.Dispose(), Times.Once);
        }

        [Test]
        public void DisposeDoesNotThrowWhenDataIsNotDisposable()
        {
            var cp = new ContinuationPoint
            {
                Data = "non-disposable data"
            };

            Assert.DoesNotThrow(() => cp.Dispose());
        }

        [Test]
        public void PropertiesCanBeSetAndRetrieved()
        {
            var id = Guid.NewGuid();
            using var cp = new ContinuationPoint
            {
                Id = id,
                MaxResultsToReturn = 100,
                BrowseDirection = BrowseDirection.Both,
                ReferenceTypeId = new NodeId(33),
                IncludeSubtypes = true,
                NodeClassMask = 0xFF,
                ResultMask = BrowseResultMask.All,
                Index = 5,
                Data = "test"
            };

            Assert.That(cp.Id, Is.EqualTo(id));
            Assert.That(cp.MaxResultsToReturn, Is.EqualTo(100));
            Assert.That(cp.BrowseDirection, Is.EqualTo(BrowseDirection.Both));
            Assert.That(cp.ReferenceTypeId, Is.EqualTo(new NodeId(33)));
            Assert.That(cp.IncludeSubtypes, Is.True);
            Assert.That(cp.NodeClassMask, Is.EqualTo(0xFF));
            Assert.That(cp.ResultMask, Is.EqualTo(BrowseResultMask.All));
            Assert.That(cp.Index, Is.EqualTo(5));
            Assert.That(cp.Data, Is.EqualTo("test"));
        }
    }
}
