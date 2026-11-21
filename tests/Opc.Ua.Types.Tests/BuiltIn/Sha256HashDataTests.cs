// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class Sha256HashDataTests
    {
        [TestCase("", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
        [TestCase("abc", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
        public void HashesKnownVectors(string text, string expected)
        {
            byte[] input = Encoding.ASCII.GetBytes(text);
            byte[] result = SHA256.HashData(input);

            Assert.That(CoreUtils.ToHexString(result), Is.EqualTo(expected));
            Assert.That(Encoding.ASCII.GetString(input), Is.EqualTo(text));
            Assert.That(result, Has.Length.EqualTo(32));
        }

        [Test]
        public void StreamHashUsesCurrentPositionAndLeavesTheStreamOwnedByTheCaller()
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("xxabc"));
            stream.Position = 2;
            byte[] result = SHA256.HashData(stream);

            Assert.That(CoreUtils.ToHexString(result),
                Is.EqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
            Assert.That(stream.Position, Is.EqualTo(5));
            Assert.That(stream.CanRead, Is.True);
        }

        [Test]
        public void NullInputIsRejectedByBothOverloads()
        {
            Assert.That(() => SHA256.HashData((byte[])null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => SHA256.HashData((Stream)null!), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
