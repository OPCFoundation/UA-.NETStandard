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
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    [TestFixture]
    [Category("AliasNames")]
    public sealed class AliasPatternRegressionTests
    {
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

        [TestCase("[]")]
        [TestCase("[^]")]
        [TestCase("[!]")]
        [TestCase("[z-a]")]
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

        [Test]
        public void AdversarialPatternEvaluationHasAnExplicitFiniteBudget()
        {
            FieldInfo budget = typeof(AliasNameWildcardMatcher).GetField(
                "s_matchTimeout", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(budget, Is.Not.Null, "Do not execute an unbounded adversarial regular expression.");
            Assert.That((TimeSpan)budget.GetValue(null), Is.EqualTo(TimeSpan.FromMilliseconds(100)));
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
            }
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }
    }
}
