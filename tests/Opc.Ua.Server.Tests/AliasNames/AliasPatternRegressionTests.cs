/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Verifies OPC UA alias wildcard syntax, invalid-pattern handling, and bounded evaluation.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    public sealed class AliasPatternRegressionTests
    {
        /// <summary>
        /// Verifies that character sets honor OPC UA escapes and patterns match the complete target string.
        /// </summary>
        [TestCase("d", @"[\d]", true)]
        [TestCase("5", @"[\d]", false)]
        [TestCase("]", @"[\]]", true)]
        [TestCase("\\", @"[\\]", true)]
        [TestCase("-", @"[a\-z]", true)]
        [TestCase("m", @"[a\-z]", false)]
        [TestCase("c", "[a-d]", true)]
        [TestCase("c", "[^a-d]", false)]
        [TestCase("z", "[^a-d]", true)]
        [TestCase("z", "[!a-d]", true)]
        [TestCase("a\n", "a", false)]
        [TestCase("a\n", "a_", true)]
        [TestCase("a\nb", "a%b", true)]
        public void CharacterSetsUseUaEscapesAndWholeStringSemantics(string target, string pattern, bool expected)
        {
            Assert.That(AliasNameWildcardMatcher.IsMatch(target, pattern), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that both alias lookup methods reject malformed patterns before searching an empty store.
        /// </summary>
        [TestCase("[]")]
        [TestCase("[^]")]
        [TestCase("[!]")]
        [TestCase("[z-a]")]
        [TestCase("[a^b]")]
        [TestCase("[a-^]")]
        public async Task InvalidPatternIsRejectedEvenWhenTheAliasStoreIsEmptyAsync(string pattern)
        {
            var categoryId = new NodeId("pattern-category", 1);
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId, new QualifiedName("Patterns", 1), AliasNameCapabilities.All);
            using var store = new InMemoryAliasNameStore([descriptor]);
            using var registry = new AliasNameStoreRegistry();
            registry.Register(store);
            var types = new TypeTable(new NamespaceTable());
            FindAliasMethodStateResult result = await AliasNameMethodDispatcher.FindAliasAsync(
                registry, types, categoryId, pattern, NodeId.Null, CancellationToken.None).ConfigureAwait(false);
            FindAliasVerboseMethodStateResult verbose = await AliasNameMethodDispatcher.FindAliasVerboseAsync(
                registry, types, categoryId, pattern, NodeId.Null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(verbose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.AliasNodeList.IsEmpty, Is.True);
            Assert.That(verbose.AliasNodeList.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that adversarial wildcard matching has a finite timeout and cannot run unbounded.
        /// </summary>
        [Test]
        public void AdversarialPatternEvaluationHasAnExplicitFiniteBudget()
        {
            string target = new('a', 256);
            string pattern = string.Concat(System.Linq.Enumerable.Repeat("%a", 32)) + "%b";
            var timer = Stopwatch.StartNew();
            try
            {
                Assert.That(AliasNameWildcardMatcher.IsMatch(target, pattern), Is.False);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(ex.InnerException, Is.InstanceOf<TimeoutException>());
            }
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// Verifies the shared matcher retains the alias deadline and maps expiry to the existing service error.
        /// </summary>
        [Test]
        public void SharedPatternTimeoutRemainsAnAliasServiceError()
        {
            string text = "%" + new string('a', 16384) + "b";
            LikePattern pattern = AliasNameWildcardMatcher.CreatePattern(text);
            var elapsed = Stopwatch.StartNew();

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                AliasNameWildcardMatcher.Matches(new string('a', 65536), pattern));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(error.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(AliasNameWildcardMatcher.Matches(new string('a', 16384) + "b", pattern), Is.True);
        }

        /// <summary>
        /// Verifies that each alias lookup shares one deadline rather than resetting the budget per alias.
        /// </summary>
        [Test]
        public void ExpiredSharedPatternDeadlineReturnsBadTimeout()
        {
            LikePattern pattern = AliasNameWildcardMatcher.CreatePattern("%");

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                AliasNameWildcardMatcher.Matches(
                    "alias",
                    pattern,
                    Stopwatch.GetTimestamp() - 1));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void SubMillisecondDeadlinesNeverBecomeInvalidTimeoutArguments()
        {
            LikePattern pattern = AliasNameWildcardMatcher.CreatePattern("alias");
            long budget = Math.Max(1, Stopwatch.Frequency / 10000);
            for (int ii = 0; ii < 256; ii++)
            {
                long deadline = Stopwatch.GetTimestamp() + budget;
                try
                {
                    Assert.That(AliasNameWildcardMatcher.Matches("alias", pattern, deadline), Is.True);
                }
                catch (ServiceResultException ex)
                {
                    Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
                    Assert.That(ex.InnerException, Is.TypeOf<TimeoutException>());
                }
            }
        }
    }
}
