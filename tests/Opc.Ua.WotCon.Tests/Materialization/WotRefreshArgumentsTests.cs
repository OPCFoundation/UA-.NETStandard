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
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Unit tests for <see cref="WotRefreshArguments"/>, the decoder for the
    /// generated <c>WoTRegistryType.Refresh</c> Method's Selection / Options /
    /// ExpectedGeneration / RequestId arguments.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRefreshArgumentsTests
    {
        private static IServiceMessageContext Context => ServiceMessageContext.CreateEmpty(null!);

        private static ArrayOf<Variant> Args(params Variant[] values) => values;

        [Test]
        public void EmptyArgumentsDecodeToFullRefreshWithDefaults()
        {
            ServiceResult status = WotRefreshArguments.TryDecode(
                Args(), Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Selection, Is.Empty);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(0u));
            Assert.That(request.RequestId, Is.EqualTo(string.Empty));
            Assert.That(request.Options, Is.Not.Null);
        }

        [Test]
        public void DecodesSelectionArrayOptionsGenerationAndRequestId()
        {
            var selector = new WoTResourceSelectorDataType
            {
                GroupId = "thingdescriptions",
                ResourceId = "sensor",
                Kind = WoTDocumentKindEnum.ThingDescription
            };
            var options = new WoTRefreshOptionsDataType
            {
                Force = true,
                DryRun = true,
                Atomicity = WoTAtomicityEnum.PerGroup,
                DeletePolicy = WoTDeletePolicyEnum.Retire,
                IncludeDependents = true
            };
            ArrayOf<Variant> input = Args(
                new Variant(new ExtensionObject[] { new ExtensionObject(selector) }),
                new Variant(new ExtensionObject(options)),
                new Variant(7u),
                new Variant("req-42"));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Selection, Has.Length.EqualTo(1));
            Assert.That(request.Selection[0].ResourceId, Is.EqualTo("sensor"));
            Assert.That(request.Options.Force, Is.True);
            Assert.That(request.Options.DryRun, Is.True);
            Assert.That(request.Options.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerGroup));
            Assert.That(request.Options.DeletePolicy, Is.EqualTo(WoTDeletePolicyEnum.Retire));
            Assert.That(request.Options.IncludeDependents, Is.True);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(7u));
            Assert.That(request.RequestId, Is.EqualTo("req-42"));
        }

        [Test]
        public void DecodesSelectionFromArrayOfExtensionObject()
        {
            var selectors = new ArrayOf<ExtensionObject>(new[]
            {
                new ExtensionObject(new WoTResourceSelectorDataType { Xid = "/groups/g/resources/a" }),
                new ExtensionObject(new WoTResourceSelectorDataType { Xid = "/groups/g/resources/b" })
            });
            ArrayOf<Variant> input = Args(new Variant(selectors));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Selection, Has.Length.EqualTo(2));
            Assert.That(request.Selection[1].Xid, Is.EqualTo("/groups/g/resources/b"));
        }

        [Test]
        public void RejectsSelectionOfWrongElementType()
        {
            var wrongSelection = new string[] { "not-a-selector" };
            ArrayOf<Variant> input = Args(new Variant(wrongSelection));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RejectsOptionsOfWrongType()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                new Variant("not-an-options-structure"));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RejectsExpectedGenerationOfWrongType()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant("five"));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AcceptsExpectedGenerationAsInt32()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                new Variant(9));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.ExpectedGeneration, Is.EqualTo(9u));
        }

        [Test]
        public void RejectsRequestIdOfWrongType()
        {
            ArrayOf<Variant> input = Args(
                Variant.Null,
                Variant.Null,
                Variant.Null,
                new Variant(123));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, Context, out _);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void DecodesBinaryEncodedSelectionBody()
        {
            IServiceMessageContext context = Context;
            var selector = new WoTResourceSelectorDataType { ResourceId = "encoded" };
            byte[] encoded;
            using (var encoder = new BinaryEncoder(context))
            {
                selector.Encode(encoder);
                encoded = encoder.CloseAndReturnBuffer()!;
            }
            var extension = new ExtensionObject(
                Opc.Ua.WotCon.V2.DataTypeIds.WoTResourceSelectorDataType, ByteString.From(encoded));
            ArrayOf<Variant> input = Args(new Variant(new[] { extension }));

            ServiceResult status = WotRefreshArguments.TryDecode(
                input, context, out WotRefreshRequest request);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(request.Selection, Has.Length.EqualTo(1));
            Assert.That(request.Selection[0].ResourceId, Is.EqualTo("encoded"));
        }
    }
}
