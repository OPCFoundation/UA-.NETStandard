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

namespace Opc.Ua.Types.Buffers.Tests
{
    /// <summary>
    /// Tests for the read only memory extensions.
    /// </summary>
    [TestFixture]
    [Category("Buffers")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReadOnlyMemoryTests
    {
        [Test]
        public void ConstructorInitializesFieldsCorrectly()
        {
            var memory = new ReadOnlyMemory("test", 4, 1);
            Assert.That(memory.Object, Is.EqualTo("test"));
            Assert.That(memory.Length, Is.EqualTo(4));
            Assert.That(memory.Index, Is.EqualTo(1));
        }

#if NET8_0_OR_GREATER
        [Test]
        public void ReinterpretAsReturnsExpectedType()
        {
            var memory = new ReadOnlyMemory("test", 4, 1);
            ref string str = ref ReadOnlyMemory.ReinterpretAs<string>(ref memory);
            Assert.That(str, Is.EqualTo("test"));
        }

        [Test]
        public void FromReturnsExpectedReadOnlyMemory()
        {
            string str = "test";
            ref var memory = ref ReadOnlyMemory.From(ref str);
            Assert.That(memory.Object, Is.EqualTo("test"));
        }
#endif
    }
}
