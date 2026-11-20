/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
