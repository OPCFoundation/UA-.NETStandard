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
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Regression tests for the Utils / Schema / Polyfill defects reported by
    /// the read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsAuditRegressionTests
    {
        [Test]
        public void OpenReadAllowsConcurrentReadersAndReadOnlyFiles()
        {
            // File.Open(path, FileMode.Open) requests ReadWrite access with
            // FileShare.None, which fails on a read-only file and locks out
            // concurrent readers.
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            File.WriteAllText(path, "content");

            try
            {
                File.SetAttributes(path, FileAttributes.ReadOnly);

                var fileSystem = new LocalFileSystem();

                using Stream first = fileSystem.OpenRead(path);
                using Stream second = fileSystem.OpenRead(path);

                Assert.Multiple(() =>
                {
                    Assert.That(first.CanRead, Is.True);
                    Assert.That(second.CanRead, Is.True);
                });
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        [Test]
        public void MatchDoesNotThrowForAnUnterminatedCharacterSet()
        {
            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => CoreUtils.Match("ab", "a[", true));
                Assert.DoesNotThrow(() => CoreUtils.Match("abc", "a[b-", true));
                Assert.DoesNotThrow(() => CoreUtils.Match("abc", "a[!", true));
            });
        }

        [Test]
        public void MatchRejectsAnUnterminatedCharacterSet()
        {
            // Only an empty "[" was rejected. A non-empty set with no closing
            // ']' ran the scanner off the end of the pattern, which then left
            // the switch as though the set had matched.
            Assert.Multiple(() =>
            {
                Assert.That(CoreUtils.Match("a", "[a", true), Is.False);
                Assert.That(CoreUtils.Match("b", "[!a", true), Is.False);
                Assert.That(CoreUtils.Match("a", "[a-c", true), Is.False);
                Assert.That(CoreUtils.Match("a", "[", true), Is.False);

                // a closed set still works, in both directions.
                Assert.That(CoreUtils.Match("a", "[a]", true), Is.True);
                Assert.That(CoreUtils.Match("b", "[a]", true), Is.False);
                Assert.That(CoreUtils.Match("b", "[!a]", true), Is.True);
                Assert.That(CoreUtils.Match("b", "[a-c]", true), Is.True);
            });
        }

        [Test]
        public void MatchRejectsAnUnmatchedTrailingCharacter()
        {
            // The trailing bound was target.Length - 1, which let exactly one
            // unmatched character through.
            Assert.Multiple(() =>
            {
                Assert.That(CoreUtils.Match("abc", "ab", true), Is.False);
                Assert.That(CoreUtils.Match("ab", "ab", true), Is.True);
                Assert.That(CoreUtils.Match("12", "#", true), Is.False);
            });
        }

        [Test]
        public void ReplaceThrowsForAnInvalidOldValueUnderEveryComparison()
        {
            // The non-ordinal path returned the target instead of throwing, so
            // the polyfill's contract differed from the framework overload and
            // from its own ordinal branch.
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => "abc".Replace(null!, "x", StringComparison.CurrentCulture));
                Assert.Throws<ArgumentException>(
                    () => "abc".Replace(string.Empty, "x", StringComparison.CurrentCulture));
                Assert.Throws<ArgumentNullException>(
                    () => "abc".Replace(null!, "x", StringComparison.OrdinalIgnoreCase));
                Assert.Throws<ArgumentException>(
                    () => "abc".Replace(string.Empty, "x", StringComparison.OrdinalIgnoreCase));
            });
        }

        [Test]
        public void MatchAcceptsAPatternEndingInASingleCharacterWildcard()
        {
            // The unmatched-trailing-character guard was placed before the '?'
            // consumed its character, so it fired for every pattern whose last
            // character is '?' and nothing matched.
            Assert.Multiple(() =>
            {
                Assert.That(CoreUtils.Match("a", "?", true), Is.True);
                Assert.That(CoreUtils.Match("ab", "a?", true), Is.True);
                Assert.That(CoreUtils.Match("abc", "a??", true), Is.True);
                Assert.That(CoreUtils.Match("abcd", "ab??", true), Is.True);

                // and the guard still rejects a genuinely unmatched tail.
                Assert.That(CoreUtils.Match("abc", "a?", true), Is.False);
                Assert.That(CoreUtils.Match(string.Empty, "?", true), Is.False);
            });
        }

        [Test]
        public void MatchAcceptsATrailingWildcardOverAnEmptyRemainder()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CoreUtils.Match("a", "a*", true), Is.True);
                Assert.That(CoreUtils.Match("a", "a**", true), Is.True);
                Assert.That(CoreUtils.Match("abc", "a*", true), Is.True);
            });
        }

        [Test]
        public void ServiceResultExceptionToleratesANullResult()
        {
            // The constructor dereferenced the null it explicitly tolerates.
            var ex = new ServiceResultException((ServiceResult)null);

            Assert.That(ex.StatusCode, Is.EqualTo(ServiceResult.Bad.StatusCode));
        }

        [Test]
        public void VirtualFileSystemKeepsBytesPastTheFinalPosition()
        {
            // The write stream truncated the file to its final position, so a
            // writer that seeked back and wrote a shorter tail lost data.
            var fileSystem = new VirtualFileSystem();
            const string path = "audit/regression.bin";

            using (Stream stream = fileSystem.OpenWrite(path))
            {
                stream.Write([1, 2, 3, 4, 5, 6], 0, 6);
                stream.Seek(2, SeekOrigin.Begin);
                stream.Write([9], 0, 1);
            }

            using (Stream stream = fileSystem.OpenRead(path))
            {
                byte[] buffer = new byte[8];
                int read = stream.Read(buffer, 0, buffer.Length);

                // A range indexer over an array needs RuntimeHelpers.GetSubArray,
                // which .NET Framework does not have.
                byte[] head = new byte[6];
                Array.Copy(buffer, head, head.Length);

                Assert.That(read, Is.EqualTo(6));
                Assert.That(head, Is.EqualTo(new byte[] { 1, 2, 9, 4, 5, 6 }));
            }
        }

        [Test]
        public void VirtualFileSystemStillHonoursAnExplicitSetLength()
        {
            var fileSystem = new VirtualFileSystem();
            const string path = "audit/truncated.bin";

            using (Stream stream = fileSystem.OpenWrite(path))
            {
                stream.Write([1, 2, 3, 4], 0, 4);
                stream.SetLength(2);
            }

            Assert.That(fileSystem.GetLength(path), Is.EqualTo(2));
        }

        [Test]
        public void VirtualFileSystemHonoursSetLengthToTheCurrentLength()
        {
            // SetLength returned early when asked for the length the file
            // already had, so the high water mark stayed at zero and the
            // truncation on dispose emptied the file despite the explicit
            // length request.
            var fileSystem = new VirtualFileSystem();
            const string path = "audit/unchanged.bin";

            using (Stream stream = fileSystem.OpenWrite(path))
            {
                stream.Write([1, 2, 3, 4, 5, 6], 0, 6);
            }

            using (Stream stream = fileSystem.OpenWrite(path))
            {
                stream.SetLength(6);
            }

            Assert.That(fileSystem.GetLength(path), Is.EqualTo(6));
        }

        [Test]
        public void BinarySchemaValidatorImportsTheBuiltInTypeDictionary()
        {
            // The fallback imported Namespaces.OpcUa, for which no embedded
            // resource exists, so validating a dictionary without explicit
            // imports always failed.
            const string bsd =
                "<opc:TypeDictionary xmlns:opc=\"http://opcfoundation.org/BinarySchema/\" " +
                "xmlns:ua=\"http://opcfoundation.org/UA/\" " +
                "DefaultByteOrder=\"LittleEndian\" " +
                "TargetNamespace=\"http://test.org/Types/\">" +
                "<opc:StructuredType Name=\"Sample\">" +
                "<opc:Field Name=\"Value\" TypeName=\"opc:Int32\" />" +
                "</opc:StructuredType>" +
                "</opc:TypeDictionary>";

            var validator = new Opc.Ua.Schema.Binary.BinarySchemaValidator();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bsd));
            Assert.DoesNotThrow(() => validator.Validate(stream));
        }

        [Test]
        public void PolyfillIndexOfHonoursTheComparison()
        {
            // The polyfills ignored the comparison argument entirely. On modern
            // target frameworks the framework method is used, so this asserts
            // the behaviour both implementations must agree on.
            const string target = "ABC";

            Assert.Multiple(() =>
            {
                Assert.That(
                    target.IndexOf('b', StringComparison.OrdinalIgnoreCase),
                    Is.EqualTo(1));
                Assert.That(
                    target.Replace("b", "x", StringComparison.OrdinalIgnoreCase),
                    Is.EqualTo("AxC"));
            });
        }
    }
}
