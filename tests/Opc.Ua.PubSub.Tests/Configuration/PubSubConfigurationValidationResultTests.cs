// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                []);
            Assert.That(result.IsValid, Is.True);
            Assert.That(((PubSubConfigurationIssue[]?)result.Issues) ?? [], Is.Empty);
        }

        [Test]
        public void OnlyInfoIssues_IsValidTrue()
        {
            var result = new PubSubConfigurationValidationResult(
                [NewIssue(PubSubConfigurationIssueSeverity.Info)]);
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void OnlyWarningIssues_IsValidTrue()
        {
            var result = new PubSubConfigurationValidationResult(
                [NewIssue(PubSubConfigurationIssueSeverity.Warning)]);
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void AnyErrorIssue_IsValidFalse()
        {
            var result = new PubSubConfigurationValidationResult(
                [
                    NewIssue(PubSubConfigurationIssueSeverity.Info),
                    NewIssue(PubSubConfigurationIssueSeverity.Warning),
                    NewIssue(PubSubConfigurationIssueSeverity.Error)
                ]);
            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void ThrowIfInvalid_OnInvalid_ThrowsWithErrors()
        {
            var result = new PubSubConfigurationValidationResult(
                [
                    NewIssue(PubSubConfigurationIssueSeverity.Warning, "PSC0900"),
                    NewIssue(PubSubConfigurationIssueSeverity.Error, "PSC0901"),
                    NewIssue(PubSubConfigurationIssueSeverity.Error, "PSC0902")
                ]);
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(result.ThrowIfInvalid)!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i is not null && i.Code == "PSC0901"));
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i is not null && i.Code == "PSC0902"));
        }

        [Test]
        public void ThrowIfInvalid_OnValid_DoesNotThrow()
        {
            var result = new PubSubConfigurationValidationResult(
                [
                    NewIssue(PubSubConfigurationIssueSeverity.Warning),
                    NewIssue(PubSubConfigurationIssueSeverity.Info)
                ]);
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
            PubSubConfigurationIssue[] issues =
            [
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCAAA"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCBBB"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCCCC"),
                NewIssue(PubSubConfigurationIssueSeverity.Error, "PSCDDD")
            ];
            var ex = new PubSubConfigurationException(issues);
            Assert.That(ex.Message, Does.Contain("PSCAAA"));
            Assert.That(ex.Message, Does.Contain("PSCBBB"));
            Assert.That(ex.Message, Does.Contain("PSCCCC"));
            Assert.That(((PubSubConfigurationIssue[]?)ex.Issues) ?? [], Has.Length.EqualTo(4));
        }

        [Test]
        public void Exception_NoIssues_StillProducesMessage()
        {
            var ex = new PubSubConfigurationException(
                []);
            Assert.That(ex.Message, Is.Not.Null);
            Assert.That(((PubSubConfigurationIssue[]?)ex.Issues) ?? [], Is.Empty);
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
