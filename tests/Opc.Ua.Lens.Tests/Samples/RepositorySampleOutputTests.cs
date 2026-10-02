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

using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleOutputTests
    {
        [Test]
        public void OverlongPemMarkersStillRedactFollowingKeyLines()
        {
            var output = new RepositorySampleOutput([]);
            string prefix = new('x', RepositorySampleOutput.MaximumLineLength + 1);
            output.Append(RepositorySampleOutputStream.StandardOutput, prefix + "-----BEGIN PRIVATE KEY-----\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "ZmFrZS10ZXN0LWJvZHk=\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, prefix + "-----END PRIVATE KEY-----\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "Safe status\n");

            Assert.That(output.Snapshot.Count, Is.EqualTo(4));
            Assert.That(output.Snapshot[0].Text, Is.EqualTo("[oversized sample output omitted]"));
            Assert.That(output.Snapshot[1].Text, Is.EqualTo("[sensitive sample output omitted]"));
            Assert.That(output.Snapshot[2].Text, Is.EqualTo("[oversized sample output omitted]"));
            Assert.That(output.Snapshot[3].Text, Is.EqualTo("Safe status"));
        }

        [Test]
        public void OutputBoundsLongLinesBeforeNewlineAndRetainsOnlyTheTail()
        {
            var output = new RepositorySampleOutput([]);
            output.Append(
                RepositorySampleOutputStream.StandardOutput,
                new string('x', RepositorySampleOutput.MaximumLineLength * 100));
            Assert.That(output.Snapshot.Count, Is.Zero);
            output.Complete(RepositorySampleOutputStream.StandardOutput);
            Assert.That(output.Snapshot[0].Text, Is.EqualTo("[oversized sample output omitted]"));

            for (int index = 0; index < RepositorySampleOutput.MaximumLines + 4; index++)
            {
                output.Append(
                    RepositorySampleOutputStream.StandardOutput,
                    "line-" + index.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            ArrayOf<RepositorySampleOutputLine> tail = output.Snapshot;
            Assert.That(tail.Count, Is.EqualTo(128));
            Assert.That(tail[0].Text, Is.EqualTo("line-4"));
            Assert.That(tail[127].Text, Is.EqualTo("line-131"));
        }

        [Test]
        public void ExactLineLimitIsRetainedAndOneExtraCharacterIsWithheld()
        {
            var output = new RepositorySampleOutput([]);
            string exact = new('x', RepositorySampleOutput.MaximumLineLength);
            output.Append(RepositorySampleOutputStream.StandardOutput, exact + "\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, exact + "x\n");

            Assert.That(output.Snapshot[0].Text, Is.EqualTo(exact));
            Assert.That(output.Snapshot[1].Text, Is.EqualTo("[oversized sample output omitted]"));
        }

        [TestCase("PASSWORD=not-a-real-password")]
        [TestCase("Authorization: Bearer fixture")]
        [TestCase("private key: fixture")]
        [TestCase("opc.tcp://operator:fixture@localhost:58123/Server")]
        public void SensitiveOutputIsRedactedAcrossReadChunks(string line)
        {
            var output = new RepositorySampleOutput([]);
            int middle = line.Length / 2;
            output.Append(RepositorySampleOutputStream.StandardError, line.AsSpan(0, middle));
            output.Append(RepositorySampleOutputStream.StandardError, line.AsSpan(middle));
            output.Complete(RepositorySampleOutputStream.StandardError);

            Assert.That(output.Snapshot.Count, Is.EqualTo(1));
            Assert.That(output.Snapshot[0].Stream, Is.EqualTo(RepositorySampleOutputStream.StandardError));
            Assert.That(output.Snapshot[0].Text, Is.EqualTo("[sensitive sample output omitted]"));
        }

        [Test]
        public void PrivatePathsAndPemBodiesAreNeverRetained()
        {
            string root = Path.GetFullPath(Path.Combine("private-fixture", "source"));
            var output = new RepositorySampleOutput([root]);
            output.Append(RepositorySampleOutputStream.StandardOutput, "Loading " + root + "\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "Loading " + root.Replace('\\', '/') + "\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "-----BEGIN PRIVATE KEY-----\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "ZmFrZS10ZXN0LWJvZHk=\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "-----END PRIVATE KEY-----\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "Safe status\n");

            Assert.That(output.Snapshot.Count, Is.EqualTo(6));
            for (int index = 0; index < 5; index++)
            {
                Assert.That(output.Snapshot[index].Text, Is.EqualTo("[sensitive sample output omitted]"));
            }
            Assert.That(output.Snapshot[5].Text, Is.EqualTo("Safe status"));
        }

        [Test]
        public void StreamFramingIsIndependentAndControlCharactersAreRemoved()
        {
            var output = new RepositorySampleOutput([]);
            output.Append(RepositorySampleOutputStream.StandardOutput, "out");
            output.Append(RepositorySampleOutputStream.StandardError, "err\u001b\u0000\n");
            output.Append(RepositorySampleOutputStream.StandardOutput, "put\r\n");

            Assert.That(output.Snapshot.Count, Is.EqualTo(2));
            Assert.That(output.Snapshot[0].Stream, Is.EqualTo(RepositorySampleOutputStream.StandardError));
            Assert.That(output.Snapshot[0].Text, Is.EqualTo("err  "));
            Assert.That(output.Snapshot[1].Stream, Is.EqualTo(RepositorySampleOutputStream.StandardOutput));
            Assert.That(output.Snapshot[1].Text, Is.EqualTo("output"));
        }
    }
}
