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
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests that String values with leading or trailing white space are rejected with
    /// Bad_TypeMismatch when written to a TrimmedString Variable or passed as a TrimmedString
    /// Method input argument (Part 5 12.43).
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TrimmedStringValidationTests
    {
        private static readonly NodeId s_trimmedStringTypeId = new(31918u);
        private static readonly NodeId s_trimmedSubtypeId = new(5001, 1);
        private static readonly string[] s_cleanArray = ["a", "b c", string.Empty];
        private static readonly string[] s_untrimmedArray = ["a", "b ", "c"];
        private static readonly string[] s_untrimmedSlice = [" x"];
        private static readonly string[] s_untrimmedMatrix = ["a", "b", "c", "\u00A0d"];
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        private SystemContext CreateSystemContext()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:trimmed");

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(DataTypeIds.String, DataTypeIds.BaseDataType);
            typeTable.AddSubtype(s_trimmedStringTypeId, DataTypeIds.String);
            typeTable.AddSubtype(s_trimmedSubtypeId, s_trimmedStringTypeId);

            return new SystemContext(m_telemetry)
            {
                NamespaceUris = namespaceUris,
                TypeTable = typeTable
            };
        }

        private static BaseDataVariableState CreateVariable(NodeId dataType, int valueRank)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Var", 0),
                BrowseName = new QualifiedName("Var", 0),
                DisplayName = new LocalizedText("Var"),
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
        }

        private static ServiceResult Write(
            SystemContext context,
            BaseDataVariableState variable,
            Variant value,
            string? indexRange = null)
        {
            return variable.WriteAttribute(
                context,
                Attributes.Value,
                indexRange == null ? NumericRange.Null : NumericRange.Parse(indexRange),
                new DataValue(value));
        }

        [Test]
        public void WriteScalarWithLeadingSpaceIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "old";

            ServiceResult result = Write(context, variable, Variant.From(" value"));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.GetString(), Is.EqualTo("old"));
        }

        [Test]
        public void WriteScalarWithTrailingTabIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);

            ServiceResult result = Write(context, variable, Variant.From("value\t"));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WriteScalarWithUnicodeWhiteSpaceToSubtypeIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedSubtypeId, ValueRanks.Scalar);

            ServiceResult result = Write(context, variable, Variant.From("value\u2003"));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public async Task WriteAsyncScalarWithLeadingSpaceIsRejectedAsync()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "old";

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(" value"))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.GetString(), Is.EqualTo("old"));
        }

        [Test]
        public void WriteArrayWithUntrimmedElementIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                s_trimmedStringTypeId,
                ValueRanks.OneDimension);

            ServiceResult result = Write(context, variable, Variant.From(s_untrimmedArray.ToArrayOf()));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WriteArraySliceWithUntrimmedElementIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                s_trimmedStringTypeId,
                ValueRanks.OneDimension);
            variable.Value = Variant.From(s_cleanArray.ToArrayOf());

            ServiceResult result = Write(
                context,
                variable,
                Variant.From(s_untrimmedSlice.ToArrayOf()),
                "1");

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WriteMatrixWithUntrimmedElementIsRejected()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                s_trimmedStringTypeId,
                ValueRanks.TwoDimensions);

            ServiceResult result = Write(
                context,
                variable,
                Variant.From(s_untrimmedMatrix.ToMatrixOf(2, 2)));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        /// <summary>
        /// A Variant array or matrix whose elements are Strings is accepted for a String
        /// DataType, so its elements are trim-validated as well.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void WriteVariantCollectionIsTrimValidated(bool matrix, bool untrimmed)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(
                s_trimmedStringTypeId,
                matrix ? ValueRanks.TwoDimensions : ValueRanks.OneDimension);
            Variant[] elements =
            [
                Variant.From("a"),
                Variant.From("b"),
                Variant.From("c"),
                Variant.From(untrimmed ? " padded " : "d")
            ];
            Variant value = matrix
                ? Variant.From(elements.ToMatrixOf(2, 2))
                : Variant.From(elements.ToArrayOf());

            ServiceResult result = Write(context, variable, value);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(untrimmed ? StatusCodes.BadTypeMismatch : StatusCodes.Good),
                result.ToString());
        }

        [Test]
        public void WriteTrimmedValuesIsAccepted()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState scalar = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            BaseDataVariableState array = CreateVariable(
                s_trimmedStringTypeId,
                ValueRanks.OneDimension);

            ServiceResult scalarResult = Write(context, scalar, Variant.From("inner space ok"));
            ServiceResult emptyResult = Write(context, scalar, Variant.From(string.Empty));
            ServiceResult arrayResult = Write(context, array, Variant.From(s_cleanArray.ToArrayOf()));

            Assert.That(ServiceResult.IsGood(scalarResult), Is.True, scalarResult.ToString());
            Assert.That(ServiceResult.IsGood(emptyResult), Is.True, emptyResult.ToString());
            Assert.That(ServiceResult.IsGood(arrayResult), Is.True, arrayResult.ToString());
            Assert.That(array.Value.GetStringArray(), Is.EqualTo(s_cleanArray.ToArrayOf()));
        }

        [Test]
        public void WriteNullStringIsAccepted()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);

            ServiceResult result = Write(context, variable, Variant.From((string)null!));

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        [Test]
        public void WritePlainStringWithSpacesIsAccepted()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(DataTypeIds.String, ValueRanks.Scalar);

            ServiceResult result = Write(context, variable, Variant.From(" padded\t"));

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(variable.Value.GetString(), Is.EqualTo(" padded\t"));
        }

        [Test]
        public void WriteSubstringIndexRangeIsNotTrimValidated()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "abc";

            // replaces the middle character only; the slice itself is a space but the
            // resulting value "a c" is trimmed.
            ServiceResult result = Write(context, variable, Variant.From(" "), "1");

            Assert.That(result.StatusCode, Is.Not.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [TestCase("0")]
        [TestCase("2")]
        public void WriteSubstringAtBoundaryWithWhiteSpaceIsRejected(string indexRange)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "abc";

            ServiceResult result = Write(context, variable, Variant.From(" "), indexRange);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.GetString(), Is.EqualTo("abc"));
        }

        [Test]
        public void WriteSubstringIsValidatedAgainstValueAtCommit()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "abc";

            // another writer shortens the value first; the space at index 1 is now the last
            // character of the merged value.
            Assert.That(ServiceResult.IsGood(Write(context, variable, Variant.From("ab"))), Is.True);
            ServiceResult result = Write(context, variable, Variant.From(" "), "1");

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.GetString(), Is.EqualTo("ab"));
        }

        [Test]
        public void WriteSubstringToHandlerBackedValueIsNotSupported()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            bool handlerCalled = false;
            variable.OnWriteValue = (
                ISystemContext _,
                NodeState _,
                NumericRange _,
                QualifiedName _,
                ref Variant _,
                ref StatusCode _,
                ref DateTimeUtc _) =>
            {
                handlerCalled = true;
                return ServiceResult.Good;
            };

            ServiceResult result = Write(context, variable, Variant.From("x"), "1");

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(handlerCalled, Is.False);
        }

        [TestCase("0")]
        [TestCase("2")]
        public async Task WriteAsyncSubstringAtBoundaryWithWhiteSpaceIsRejectedAsync(string indexRange)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateVariable(s_trimmedStringTypeId, ValueRanks.Scalar);
            variable.Value = "abc";

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Parse(indexRange),
                new DataValue(Variant.From(" "))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.GetString(), Is.EqualTo("abc"));
        }

        [TestCase(" lead", false)]
        [TestCase("trail\t", false)]
        [TestCase("clean", true)]
        public async Task CallWithTrimmedStringArgumentValidatesWhiteSpaceAsync(
            string argument,
            bool expectGood)
        {
            SystemContext context = CreateSystemContext();
            var method = new MethodState(null);
            method.CreateOrReplaceInputArguments(context, null).Value =
            [
                new Argument
                {
                    Name = "Id",
                    DataType = s_trimmedStringTypeId,
                    ValueRank = ValueRanks.Scalar
                }
            ];
            method.Executable = true;
            method.UserExecutable = true;
            int invoked = 0;
            method.OnCallMethod2Async = (_, _, _, _, _, _) =>
            {
                invoked++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };

            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(
                context,
                new NodeId(123u),
                [Variant.From(argument)],
                errors,
                []).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            if (expectGood)
            {
                Assert.That(invoked, Is.EqualTo(1));
                Assert.That(errors, Has.All.Matches<ServiceResult>(e => ServiceResult.IsGood(e)));
            }
            else
            {
                Assert.That(invoked, Is.Zero);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            }
        }

        [TestCase(" padded ", false)]
        [TestCase("clean", true)]
        public async Task CallWithTrimmedStringVariantArrayArgumentValidatesWhiteSpaceAsync(
            string element,
            bool expectGood)
        {
            SystemContext context = CreateSystemContext();
            var method = new MethodState(null);
            method.CreateOrReplaceInputArguments(context, null).Value =
            [
                new Argument
                {
                    Name = "Ids",
                    DataType = s_trimmedStringTypeId,
                    ValueRank = ValueRanks.OneDimension
                }
            ];
            method.Executable = true;
            method.UserExecutable = true;
            int invoked = 0;
            method.OnCallMethod2Async = (_, _, _, _, _, _) =>
            {
                invoked++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };
            Variant[] elements = [Variant.From("a"), Variant.From(element)];

            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(
                context,
                new NodeId(123u),
                [Variant.From(elements.ToArrayOf())],
                errors,
                []).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            if (expectGood)
            {
                Assert.That(invoked, Is.EqualTo(1));
            }
            else
            {
                Assert.That(invoked, Is.Zero);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            }
        }

        [Test]
        public async Task CallWithPlainStringArgumentAcceptsWhiteSpaceAsync()
        {
            SystemContext context = CreateSystemContext();
            var method = new MethodState(null);
            method.CreateOrReplaceInputArguments(context, null).Value =
            [
                new Argument { Name = "Text", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
            ];
            method.Executable = true;
            method.UserExecutable = true;
            int invoked = 0;
            method.OnCallMethod2Async = (_, _, _, _, _, _) =>
            {
                invoked++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };

            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(
                context,
                new NodeId(123u),
                [Variant.From(" padded ")],
                errors,
                []).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(invoked, Is.EqualTo(1));
        }
    }
}
