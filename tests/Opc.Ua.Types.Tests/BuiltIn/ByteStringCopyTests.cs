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

using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ByteStringCopyTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CopyPreservesNullVersusEmpty(bool isNull)
        {
            ByteString source = isNull ? default : ByteString.Empty;
            ByteString copy = source.Copy();

            Assert.That(copy.IsNull, Is.EqualTo(isNull));
            Assert.That(copy.IsEmpty, Is.True);
            Assert.That(copy.Length, Is.Zero);
            Assert.That(copy.Copy().IsNull, Is.EqualTo(isNull));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void CopyRetainsContentAndIsolatesBackingStorage(int length)
        {
            var bytes = new byte[length];
            bytes[0] = 17;
            var source = new ByteString(bytes);
            ByteString copy = source.Copy();
            bytes[0] = 42;

            Assert.That(copy.IsNull, Is.False);
            Assert.That(copy.Length, Is.EqualTo(length));
            Assert.That(copy[0], Is.EqualTo(17));
            Assert.That(source[0], Is.EqualTo(42));
            Assert.That(copy.Span[1..].ToArray(), Is.All.Zero);
        }
    }
}
