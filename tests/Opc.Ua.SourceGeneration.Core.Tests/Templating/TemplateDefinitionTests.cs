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

using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="TemplateDefinition"/> class.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateDefinitionTests
    {
        /// <summary>
        /// Tests that Render calls OnTemplateWrite delegate and returns true when
        /// OnTemplateWrite is set and returns true.
        /// </summary>
        [Test]
        public void Render_OnTemplateWriteSetReturnsTrue_ReturnsTrue()
        {
            // Arrange
            var templateDefinition = new TemplateDefinition();
            var mockContext = new Mock<IWriteContext>();
            bool delegateCalled = false;

            templateDefinition.OnTemplateWrite = (ctx) =>
            {
                delegateCalled = true;
                return true;
            };

            // Act
            bool result = templateDefinition.Render(mockContext.Object);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(delegateCalled, Is.True);
        }

        /// <summary>
        /// Tests that Render calls OnTemplateWrite delegate and returns false
        /// when OnTemplateWrite is set and returns false.
        /// </summary>
        [Test]
        public void Render_OnTemplateWriteSetReturnsFalse_ReturnsFalse()
        {
            // Arrange
            var templateDefinition = new TemplateDefinition();
            var mockContext = new Mock<IWriteContext>();
            bool delegateCalled = false;

            templateDefinition.OnTemplateWrite = (ctx) =>
            {
                delegateCalled = true;
                return false;
            };

            // Act
            bool result = templateDefinition.Render(mockContext.Object);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(delegateCalled, Is.True);
        }

        /// <summary>
        /// Tests that Render passes the correct context to OnTemplateWrite delegate.
        /// </summary>
        [Test]
        public void Render_OnTemplateWriteSet_PassesCorrectContext()
        {
            // Arrange
            var templateDefinition = new TemplateDefinition();
            var mockContext = new Mock<IWriteContext>();
            IWriteContext receivedContext = null;

            templateDefinition.OnTemplateWrite = (ctx) =>
            {
                receivedContext = ctx;
                return true;
            };

            // Act
            templateDefinition.Render(mockContext.Object);

            // Assert
            Assert.That(receivedContext, Is.SameAs(mockContext.Object));
        }

        /// <summary>
        /// Tests that Load returns context.TemplateString when OnTemplateLoad is null.
        /// </summary>
        [Test]
        public void Load_OnTemplateLoadIsNull_ReturnsContextTemplateString()
        {
            // Arrange
            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = null
            };

            var expectedTemplateString = (TemplateString)"test template content";
            var mockContext = new Mock<ILoadContext>();
            mockContext.Setup(c => c.TemplateString).Returns(expectedTemplateString);

            // Act
            TemplateString result = templateDefinition.Load(mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedTemplateString));
        }

        /// <summary>
        /// Tests that Load returns context.TemplateString when context.TemplateString is null and OnTemplateLoad is null.
        /// Input: OnTemplateLoad is null, context.TemplateString is null.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void Load_OnTemplateLoadIsNullAndContextTemplateStringIsNull_ReturnsNull()
        {
            // Arrange
            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = null
            };

            var mockContext = new Mock<ILoadContext>();
            mockContext.Setup(c => c.TemplateString).Returns((TemplateString)null);

            // Act
            TemplateString result = templateDefinition.Load(mockContext.Object);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that Load invokes OnTemplateLoad delegate when it is not null and returns its result.
        /// Input: OnTemplateLoad is set to a delegate that returns a specific TemplateString.
        /// Expected: Returns the TemplateString from the delegate invocation.
        /// </summary>
        [Test]
        public void Load_OnTemplateLoadIsNotNull_InvokesDelegateAndReturnsResult()
        {
            // Arrange
            var expectedTemplateString = (TemplateString)"delegate result template";
            var mockContext = new Mock<ILoadContext>();

            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = (ctx) => expectedTemplateString
            };

            // Act
            TemplateString result = templateDefinition.Load(mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedTemplateString));
        }

        /// <summary>
        /// Tests that Load passes the correct context parameter to the OnTemplateLoad delegate.
        /// Input: OnTemplateLoad is set, valid context provided.
        /// Expected: The delegate receives the same context instance.
        /// </summary>
        [Test]
        public void Load_OnTemplateLoadIsNotNull_PassesCorrectContextToDelegate()
        {
            // Arrange
            var mockContext = new Mock<ILoadContext>();
            ILoadContext capturedContext = null;
            var returnTemplateString = (TemplateString)"result";

            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = (ctx) =>
                {
                    capturedContext = ctx;
                    return returnTemplateString;
                }
            };

            // Act
            templateDefinition.Load(mockContext.Object);

            // Assert
            Assert.That(capturedContext, Is.SameAs(mockContext.Object));
        }

        /// <summary>
        /// Tests that Load returns null when OnTemplateLoad delegate returns null.
        /// Input: OnTemplateLoad returns null.
        /// Expected: Returns null without throwing exception.
        /// </summary>
        [Test]
        public void Load_OnTemplateLoadReturnsNull_ReturnsNull()
        {
            // Arrange
            var mockContext = new Mock<ILoadContext>();

            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = (ctx) => null
            };

            // Act
            TemplateString result = templateDefinition.Load(mockContext.Object);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that Load invokes OnTemplateLoad with null context when context is null and OnTemplateLoad is not null.
        /// Input: Context is null, OnTemplateLoad is set.
        /// Expected: Delegate is invoked with null parameter and returns its result.
        /// </summary>
        [Test]
        public void Load_ContextIsNullAndOnTemplateLoadIsNotNull_InvokesDelegateWithNull()
        {
            // Arrange
            ILoadContext capturedContext = new Mock<ILoadContext>().Object;
            var expectedTemplateString = (TemplateString)"result from null context";

            var templateDefinition = new TemplateDefinition
            {
                OnTemplateLoad = (ctx) =>
                {
                    capturedContext = ctx;
                    return expectedTemplateString;
                }
            };

            // Act
            TemplateString result = templateDefinition.Load(null);

            // Assert
            Assert.That(result, Is.EqualTo(expectedTemplateString));
            Assert.That(capturedContext, Is.Null);
        }
    }
}
