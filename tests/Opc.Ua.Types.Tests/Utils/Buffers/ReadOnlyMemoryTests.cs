// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

        // ReadOnlyMemoryHelper.ReinterpretAs/From are net8.0+-only library helpers
        // (unsafe ref reinterpretation).
#if NET8_0_OR_GREATER
        [Test]
        public void ReinterpretAsReturnsExpectedType()
        {
            var memory = new ReadOnlyMemory("test", 4, 1);
            ref string str = ref ReadOnlyMemoryHelper.ReinterpretAs<string>(ref memory);
            Assert.That(str, Is.EqualTo("test"));
        }

        [Test]
        public void FromReturnsExpectedReadOnlyMemory()
        {
            string str = "test";
            ref ReadOnlyMemory memory = ref ReadOnlyMemoryHelper.From(ref str);
            Assert.That(memory.Object, Is.EqualTo("test"));
        }
#endif
    }
}
