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
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotAssetNameValidator"/>. The
    /// validator is the gate that prevents user-supplied asset names
    /// from escaping the configured Thing-Description storage folder
    /// when persisted to disk.
    /// </summary>
    [TestFixture]
    [Category("Assets")]
    [Parallelizable(ParallelScope.All)]
    public class WotAssetNameValidatorTests
    {
        [TestCase("valid_asset-name.123", Description = "ASCII letters, digits, dashes and dots in the middle.")]
        [TestCase("a", Description = "Single character.")]
        [TestCase("device42", Description = "Letters + digits.")]
        [TestCase("Temperature-Sensor_01", Description = "Mixed case + separators.")]
        public void ValidateAcceptsReasonableNames(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"expected '{name}' to be accepted but got {result}");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ValidateRejectsEmptyOrWhitespace(string? name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That((uint)result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("..", Description = "Pure traversal.")]
        [TestCase("../escape", Description = "Forward slash traversal.")]
        [TestCase("..\\escape", Description = "Backslash traversal.")]
        [TestCase("foo/../bar", Description = "Middle traversal.")]
        [TestCase("foo..bar", Description = "Embedded '..' substring.")]
        public void ValidateRejectsParentDirectoryTokens(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That((uint)result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("/leading-slash")]
        [TestCase("\\leading-backslash")]
        [TestCase("trailing-slash/")]
        [TestCase("nested/path/asset")]
        public void ValidateRejectsPathSeparators(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [TestCase(".hidden", Description = "Leading dot (Unix hidden file).")]
        [TestCase(" leadingSpace", Description = "Leading space.")]
        [TestCase("~tilde", Description = "Leading tilde (shell home expansion).")]
        public void ValidateRejectsRiskyLeadingCharacters(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [TestCase("trailingDot.")]
        [TestCase("trailingSpace ")]
        public void ValidateRejectsRiskyTrailingCharacters(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateRejectsNulByte()
        {
            ServiceResult result = WotAssetNameValidator.Validate("with\0null");
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [TestCase("C:asset")]
        [TestCase("drive:with-colon")]
        public void ValidateRejectsColon(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [TestCase("CON")]
        [TestCase("con")]
        [TestCase("PRN")]
        [TestCase("AUX")]
        [TestCase("NUL")]
        [TestCase("COM1")]
        [TestCase("com9")]
        [TestCase("LPT1")]
        [TestCase("lpt9")]
        public void ValidateRejectsWindowsReservedNames(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [TestCase("CON1", Description = "Trailing digit moves it out of the reserved set.")]
        [TestCase("COM", Description = "Without trailing digit.")]
        [TestCase("CONsole", Description = "Longer name starting with reserved prefix.")]
        public void ValidateAcceptsNamesThatAreNotExactlyReservedDeviceNames(string name)
        {
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"'{name}' should not be flagged as a Windows reserved device name.");
        }

        [Test]
        public void ValidateRejectsTooLongNames()
        {
            string name = new('a', WotAssetNameValidator.MaxNameLength + 1);
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateAcceptsMaximumLengthName()
        {
            string name = new('a', WotAssetNameValidator.MaxNameLength);
            ServiceResult result = WotAssetNameValidator.Validate(name);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void TryGetSafeFileNameComposesPathUnderBaseFolderForValidName()
        {
            string baseFolder = Path.Combine(Path.GetTempPath(),
                "WotConValidatorTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(baseFolder);

                bool ok = WotAssetNameValidator.TryGetSafeFileName(
                    "asset-001", baseFolder, out string? path);

                Assert.That(ok, Is.True);
                Assert.That(path, Is.Not.Null);
                string expected = Path.Combine(Path.GetFullPath(baseFolder), "asset-001.jsonld");
                Assert.That(path, Is.EqualTo(expected).IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(baseFolder))
                {
                    Directory.Delete(baseFolder, recursive: true);
                }
            }
        }

        [Test]
        public void TryGetSafeFileNameRejectsInvalidName()
        {
            string baseFolder = Path.GetTempPath();
            bool ok = WotAssetNameValidator.TryGetSafeFileName(
                "../escape", baseFolder, out string? path);
            Assert.That(ok, Is.False);
            Assert.That(path, Is.Null);
        }

        [Test]
        public void TryGetSafeFileNameRejectsEmptyBaseFolder()
        {
            bool ok = WotAssetNameValidator.TryGetSafeFileName(
                "asset", "", out string? path);
            Assert.That(ok, Is.False);
            Assert.That(path, Is.Null);
        }

        [Test]
        public void TryGetSafeFileNameKeepsAbsoluteResolvedPathInsideBaseFolder()
        {
            // Hand-craft a name that has already passed the cheap
            // character checks (it's a plain ASCII letter), then point
            // the base folder at one that contains a similarly-named
            // sibling, to make sure the StartsWith canonical check
            // anchors on the directory separator and not on a prefix.
            string root = Path.Combine(Path.GetTempPath(),
                "WotConValidatorTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string baseFolder = Path.Combine(root, "store");
                Directory.CreateDirectory(baseFolder);
                Directory.CreateDirectory(Path.Combine(root, "storeOther"));

                bool ok = WotAssetNameValidator.TryGetSafeFileName(
                    "asset", baseFolder, out string? path);

                Assert.That(ok, Is.True);
                string normalized = Path.GetFullPath(baseFolder) +
                    Path.DirectorySeparatorChar;
                Assert.That(path!.StartsWith(normalized,
                    StringComparison.OrdinalIgnoreCase), Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
