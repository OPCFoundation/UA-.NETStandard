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

using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Supplemental tests for <see cref="WotRefreshArguments"/> covering the
    /// numeric type coercions (<c>long</c>, <c>ushort</c>, <c>byte</c>, negative
    /// <c>int</c>) and the <c>Enumerate / TryCoerce</c> edge-cases (single
    /// <see cref="ExtensionObject"/>, wrapped <see cref="IEncodeable"/>, and null
    /// extension body).
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotRefreshArgumentsExtendedTests
    {
        private static IServiceMessageContext Context => ServiceMessageContext.CreateEmpty(null!);

        private static ArrayOf<Variant> Args(params Variant[] values)
        {
            return values;
        }

        [Test]
        public void AcceptsExpectedGenerationAsLong()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant((long)42));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(42u));
        }

        [Test]
        public void AcceptsExpectedGenerationAsZeroLong()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant((long)0));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.ExpectedGeneration, Is.Zero);
        }

        [Test]
        public void AcceptsExpectedGenerationAsUshort()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant((ushort)100));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(100u));
        }

        [Test]
        public void AcceptsExpectedGenerationAsByte()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant((byte)7));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(7u));
        }

        [Test]
        public void RejectsNegativeIntForExpectedGeneration()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant(-1));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RejectsLongOutOfRangeForExpectedGeneration()
        {
            long overflow = (long)uint.MaxValue + 1;
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant(overflow));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AcceptsSingleExtensionObjectAsSelection()
        {
            var selector = new WoTResourceSelectorDataType
            {
                ResourceId = "single-eo",
                GroupId = WotRegistryGroups.ThingDescriptions
            };
            var extension = new ExtensionObject(selector);

            ArrayOf<Variant> input = Args(new Variant(extension));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Selection, Has.Length.EqualTo(1));
            Assert.That(request.Selection[0].ResourceId, Is.EqualTo("single-eo"));
        }

        [Test]
        public void RejectsNullExtensionObjectInSelection()
        {
            var nullExtension = new ExtensionObject();
            ArrayOf<Variant> input = Args(new Variant(new[] { nullExtension }));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AcceptsIEncodeableWrappedInExtensionObjectAsOptions()
        {
            var options = new WoTRefreshOptionsDataType
            {
                Force = true,
                DryRun = false
            };
            var extension = new ExtensionObject(options);

            ArrayOf<Variant> input = Args(
                Variant.Null,
                new Variant(extension));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Options.Force, Is.True);
        }

        [Test]
        public void FullyNullArgumentsDefaultsAreNonNull()
        {
            ServiceResult status = WotRefreshArguments.TryDecode(
                Args(Variant.Null, Variant.Null, Variant.Null, Variant.Null),
                Context,
                out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Options, Is.Not.Null);
            Assert.That(request.Selection, Is.Empty);
            Assert.That(request.RequestId, Is.EqualTo(string.Empty));
        }
    }
}
