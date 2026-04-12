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

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ExtensionsTests
    {
        [Test]
        public void GetBoolReturnsTrueWhenValueIsTrue()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyFlag".ToLowerInvariant()] = "true"
            });

            bool result = options.GetBool("MyFlag");

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetBoolReturnsTrueWhenValueIsTrueCaseInsensitive()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyFlag".ToLowerInvariant()] = "True"
            });

            bool result = options.GetBool("MyFlag");

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetBoolReturnsFalseWhenValueIsFalse()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyFlag".ToLowerInvariant()] = "false"
            });

            bool result = options.GetBool("MyFlag");

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetBoolReturnsFalseWhenKeyMissing()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            bool result = options.GetBool("NonExistent");

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetStringReturnsValueWhenPresent()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyString".ToLowerInvariant()] = "hello"
            });

            string result = options.GetString("MyString");

            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void GetStringReturnsEmptyStringWhenKeyMissing()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            string result = options.GetString("NonExistent");

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetIntegerReturnsValueWhenPresent()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyInt".ToLowerInvariant()] = "42"
            });

            int result = options.GetInteger("MyInt");

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetIntegerReturnsZeroWhenKeyMissing()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            int result = options.GetInteger("NonExistent");

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetIntegerReturnsZeroWhenValueIsNotANumber()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyInt".ToLowerInvariant()] = "notanumber"
            });

            int result = options.GetInteger("MyInt");

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetStringsSplitsSemicolonDelimited()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyList".ToLowerInvariant()] = "a;b;c"
            });

            List<string> result = options.GetStrings("MyList");

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Does.Contain("a"));
            Assert.That(result, Does.Contain("b"));
            Assert.That(result, Does.Contain("c"));
        }

        [Test]
        public void GetStringsSplitsCommaDelimited()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyList".ToLowerInvariant()] = "x,y,z"
            });

            List<string> result = options.GetStrings("MyList");

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Does.Contain("x"));
            Assert.That(result, Does.Contain("y"));
            Assert.That(result, Does.Contain("z"));
        }

        [Test]
        public void GetStringsSplitsPlusDelimited()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyList".ToLowerInvariant()] = "a+b"
            });

            List<string> result = options.GetStrings("MyList");

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetStringsReturnsEmptyListWhenKeyMissing()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            List<string> result = options.GetStrings("NonExistent");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void GetStringsTrimsWhitespace()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyList".ToLowerInvariant()] = " a ; b ; c "
            });

            List<string> result = options.GetStrings("MyList");

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Does.Contain("a"));
            Assert.That(result, Does.Contain("b"));
            Assert.That(result, Does.Contain("c"));
        }

        [Test]
        public void GetStringsFiltersEmptyEntries()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyList".ToLowerInvariant()] = "a;;b"
            });

            List<string> result = options.GetStrings("MyList");

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetValueWithBuildMetadataPrefix()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_metadata.AdditionalFiles.{SourceGenerator.Name}MyProp".ToLowerInvariant()] = "val"
            });

            string result = options.GetString("MyProp", buildProperty: false);

            Assert.That(result, Is.EqualTo("val"));
        }

        [Test]
        public void GetBoolWithBuildMetadataPrefix()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_metadata.AdditionalFiles.{SourceGenerator.Name}IsEnabled".ToLowerInvariant()] = "true"
            });

            bool result = options.GetBool("IsEnabled", buildProperty: false);

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetIntegerWithBuildMetadataPrefix()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_metadata.AdditionalFiles.{SourceGenerator.Name}Count".ToLowerInvariant()] = "7"
            });

            int result = options.GetInteger("Count", buildProperty: false);

            Assert.That(result, Is.EqualTo(7));
        }
    }
}
