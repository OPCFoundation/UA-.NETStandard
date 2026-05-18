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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;

namespace Opc.Ua.Client.Tests.AliasNames
{
    /// <summary>
    /// Mocked-session unit tests for <see cref="AliasNameClient"/>
    /// — covers <c>FindAlias</c> / <c>AddAliasesToCategory</c> /
    /// <c>DeleteAliasesFromCategory</c> / <c>ReadLastChange</c> and the
    /// hardcoded standard-method-id fast-path used when the client is
    /// rooted at the well-known categories.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameClientTests
    {
        [Test]
        public async Task FindAliasAsyncTargetsAliasesFindAliasMethodAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            // Server returns a single AliasNameDataType.
            harness.CallHandler = _ =>
            {
                var entry = new AliasNameDataType
                {
                    AliasName = new QualifiedName("Tag1"),
                    ReferencedNodes = new[] { new ExpandedNodeId("Target", 2) }
                        .ToArrayOf()
                };
                ArrayOf<AliasNameDataType> arr = new[] { entry }.ToArrayOf();
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments =
                        new[] { Variant.FromStructure(arr) }.ToArrayOf()
                };
            };

            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            IReadOnlyList<AliasNameDataType> result = await client
                .FindAliasAsync("%", referenceTypeFilter: null).ConfigureAwait(false);

            Assert.That(harness.CallRequests, Has.Count.EqualTo(1));
            Assert.That(harness.CallRequests[0].ObjectId, Is.EqualTo(ObjectIds.Aliases));
            Assert.That(harness.CallRequests[0].MethodId,
                Is.EqualTo(MethodIds.Aliases_FindAlias),
                "Standard Aliases.FindAlias method-id should be used.");
            Assert.That(harness.CallRequests[0].InputArguments.Count, Is.EqualTo(2));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].AliasName.Name, Is.EqualTo("Tag1"));
        }

        [Test]
        public async Task FindAliasAsyncPassesNullFilterAsNodeIdNullAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            CallMethodRequest captured = null;
            harness.CallHandler = req =>
            {
                captured = req;
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments = new[]
                    {
                        Variant.FromStructure(
                            System.Array.Empty<AliasNameDataType>().ToArrayOf())
                    }.ToArrayOf()
                };
            };

            AliasNameClient client = AliasNameClient.OpenStandardTopics(harness.Session);
            await client.FindAliasAsync("Foo", null).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.MethodId, Is.EqualTo(MethodIds.Topics_FindAlias));
            captured.InputArguments[1].TryGetValue(out NodeId filter);
            Assert.That(filter.IsNull, Is.True);
        }

        [Test]
        public async Task ReadLastChangeAsyncTargetsStandardAliasesLastChangeAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.ReadHandler = read =>
            {
                Assert.That(read.NodeId,
                    Is.EqualTo(VariableIds.Aliases_LastChange));
                return new DataValue
                {
                    WrappedValue = new Variant((uint)42),
                    StatusCode = StatusCodes.Good
                };
            };
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            uint? lastChange = await client.ReadLastChangeAsync().ConfigureAwait(false);
            Assert.That(lastChange, Is.EqualTo((uint?)42));
        }

        [Test]
        public async Task AddAliasesToCategoryAsyncReturnsPerEntryStatusCodesAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ =>
            {
                ArrayOf<StatusCode> codes = new StatusCode[]
                {
                    StatusCodes.Good,
                    StatusCodes.BadBrowseNameDuplicated
                }.ToArrayOf();
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments = new[] { Variant.From(codes) }.ToArrayOf()
                };
            };

            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            client.Options.AllowVerboseProbe = false;
            StatusCode[] result = await client.AddAliasesToCategoryAsync(
                new[]
                {
                    new AliasNameAddRequest("A",
                        new ExpandedNodeId("T1", 2),
                        null, ReferenceTypeIds.AliasFor),
                    new AliasNameAddRequest("A",
                        new ExpandedNodeId("T1", 2),
                        null, ReferenceTypeIds.AliasFor)
                }).ConfigureAwait(false);

            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0].Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1].Code,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void AddAliasesToCategoryRejectsMixedReferenceTypes()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            Assert.That(async () =>
            {
                await client.AddAliasesToCategoryAsync(new[]
                {
                    new AliasNameAddRequest("A",
                        new ExpandedNodeId("T", 2),
                        null, ReferenceTypeIds.AliasFor),
                    new AliasNameAddRequest("B",
                        new ExpandedNodeId("T", 2),
                        null, ReferenceTypeIds.Organizes)
                }).ConfigureAwait(false);
            }, Throws.ArgumentException);
        }

        [Test]
        public async Task DeleteAliasesFromCategoryAsyncSendsParallelArraysAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ =>
            {
                ArrayOf<StatusCode> codes =
                    new StatusCode[] { StatusCodes.Good }.ToArrayOf();
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments = new[] { Variant.From(codes) }.ToArrayOf()
                };
            };

            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            StatusCode[] result = await client.DeleteAliasesFromCategoryAsync(
                new[]
                {
                    new AliasNameDeleteRequest("A", new ExpandedNodeId("T", 2))
                }).ConfigureAwait(false);
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(harness.CallRequests, Has.Count.EqualTo(1));
            Assert.That(harness.CallRequests[0].InputArguments.Count, Is.EqualTo(2));
        }

        [Test]
        public void AddAliasesMapsBadUserAccessDeniedToUnauthorizedAccessException()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ => new CallMethodResult
            {
                StatusCode = StatusCodes.BadUserAccessDenied
            };
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            Assert.That(async () =>
            {
                await client.AddAliasesToCategoryAsync(new[]
                {
                    new AliasNameAddRequest("A",
                        new ExpandedNodeId("T", 2),
                        null, ReferenceTypeIds.AliasFor)
                }).ConfigureAwait(false);
            }, Throws.TypeOf<System.UnauthorizedAccessException>());
        }
    }
}
