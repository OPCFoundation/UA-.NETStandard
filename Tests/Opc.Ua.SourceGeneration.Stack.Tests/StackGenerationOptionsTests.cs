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
    [Category("SourceGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class StackGenerationOptionsTests
    {
        [Test]
        public void FromProviderWithNoOptionsReturnsEmptyExclude()
        {
            var provider = new AnalyzerOptionsProvider([]);

            StackGenerationOptions options = StackGenerationOptions.FromProvider(provider);

            Assert.That(options.Exclude, Is.Not.Null);
            Assert.That(options.Exclude, Is.Empty);
        }

        [Test]
        public void FromProviderWithExclusionsParsesCorrectly()
        {
            string key = $"build_property.{SourceGenerator.Name}Exclude".ToLowerInvariant();
            var provider = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                [key] = "TypeA;TypeB;TypeC"
            });

            StackGenerationOptions options = StackGenerationOptions.FromProvider(provider);

            Assert.That(options.Exclude, Has.Count.EqualTo(3));
            Assert.That(options.Exclude, Does.Contain("TypeA"));
            Assert.That(options.Exclude, Does.Contain("TypeB"));
            Assert.That(options.Exclude, Does.Contain("TypeC"));
        }

        [Test]
        public void FromProviderWithCommaDelimitedExclusions()
        {
            string key = $"build_property.{SourceGenerator.Name}Exclude".ToLowerInvariant();
            var provider = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                [key] = "A,B,C"
            });

            StackGenerationOptions options = StackGenerationOptions.FromProvider(provider);

            Assert.That(options.Exclude, Has.Count.EqualTo(3));
        }

        [Test]
        public void StackGenerationOptionsRecordEquality()
        {
            var options1 = new StackGenerationOptions
            {
                Exclude = new List<string> { "A", "B" }
            };
            var options2 = new StackGenerationOptions
            {
                Exclude = options1.Exclude
            };

            Assert.That(options1, Is.EqualTo(options2));
        }
    }
}
