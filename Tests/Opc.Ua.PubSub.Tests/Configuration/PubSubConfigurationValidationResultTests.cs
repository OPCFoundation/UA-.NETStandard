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
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Coverage for <see cref="PubSubConfigurationValidationResult"/>:
    /// <c>IsValid</c> aggregation across severities and the
    /// <c>ThrowIfInvalid</c> throw-or-pass behaviour.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.4", Summary = "PubSub configuration validation result")]
    public class PubSubConfigurationValidationResultTests
    {
        private static PubSubConfigurationIssue NewIssue(
            PubSubConfigurationIssueSeverity severity,
            string code = "PSC0099")
        {
            return new PubSubConfigurationIssue(
                severity,
                code,
                "test",
                "Root");
        }

        [Test]
        public void EmptyIssues_IsValidTrue()
        {
            var result = new PubSubConfigurationValidationResult(
                Array.Empty<PubSubConfigurationIssue>());
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }

        [Test]
        public void OnlyInfoIssues_IsValidTrue()
        {
            var result = new PubSubConfigurationValidationResult(
                new[] { NewIssue(PubSubConfigurationIssueSeverity.Info) });
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void OnlyWarningIssues_IsValidTrue()
        {
            var result = new PubSubConfigurationValidationResult(
                new[] { NewIssue(PubSubConfigurationIssueSeverity.Warning) });
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void AnyErrorIssue_IsValidFalse()
        {
            var result = new PubSubConfigurationValidationResult(
                new[]
                {
                    NewIssue(PubSubConfigurationIssueSeverity.Info),
                    NewIssue(PubSubConfigurationIssueSeverity.Warning),
                    NewIssue(PubSubConfigurationIssueSeverity.Error)
                });
            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void ThrowIfInvalid_OnInvalid_ThrowsWithErrors()
        {
            var result = new PubSubConfigurationValidationResult(
                new[]
                {
                    NewIssue(PubSubConfigurationIssueSeverity.Warning, "PSC0900"),
                    NewIssue(PubSubConfigurationIssueSeverity.Error, "PSC0901"),
                    NewIssue(PubSubConfigurationIssueSeverity.Error, "PSC0902")
                });
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(result.ThrowIfInvalid)!;
            Assert.That(
                ex.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0901"));
            Assert.That(
                ex.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0902"));
        }

        [Test]
        public void ThrowIfInvalid_OnValid_DoesNotThrow()
        {
            var result = new PubSubConfigurationValidationResult(
                new[]
                {
                    NewIssue(PubSubConfigurationIssueSeverity.Warning),
                    NewIssue(PubSubConfigurationIssueSeverity.Info)
                });
            Assert.DoesNotThrow(result.ThrowIfInvalid);
        }

        [Test]
        public void Constructor_NullIssues_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationValidationResult(null!));
        }

        [Test]
        public void Exception_NullIssues_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationException(null!));
        }

        [Test]
        public void Exception_MessageSummarisesFirstErrors()
        {
            var issues = new[]
            {
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCAAA"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCBBB"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCCCC"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCDDD")
            };
            var ex = new PubSubConfigurationException(issues);
            Assert.That(ex.Message, Does.Contain("PSCAAA"));
            Assert.That(ex.Message, Does.Contain("PSCBBB"));
            Assert.That(ex.Message, Does.Contain("PSCCCC"));
            Assert.That(ex.Issues, Has.Count.EqualTo(4));
        }

        [Test]
        public void Exception_NoIssues_StillProducesMessage()
        {
            var ex = new PubSubConfigurationException(
                Array.Empty<PubSubConfigurationIssue>());
            Assert.That(ex.Message, Is.Not.Null);
            Assert.That(ex.Issues, Is.Empty);
        }

        [Test]
        public void Issue_NullCode_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    null!,
                    "m",
                    "p"));
        }

        [Test]
        public void Issue_NullMessage_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    "c",
                    null!,
                    "p"));
        }

        [Test]
        public void Issue_NullPath_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    "c",
                    "m",
                    null!));
        }
    }
}
