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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Covers the <c>filter</c> and <c>orderedBy</c> arguments of
    /// OPC 40001-101 <c>GetResultIdListFiltered</c> (§7.1.3), the two
    /// write-side <c>ResultTransfer</c> methods, and publishing through a
    /// store the application brought itself.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryResultQueryTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineryServerFixture();
            await m_fixture.StartAsync();
            m_context = m_fixture.CreateBuildContext();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync();
            }
        }

        [Test]
        public async Task AFilterOnTheJobIdReturnsOnlyTheMatchingResultsAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-Job");

            // The ResultType VariableType spelling of the path.
            var filter = new ContentFilter();
            filter.Push(
                FilterOperator.Equals,
                Operand(MetaDataPath(nameof(ResultMetaDataType.JobId))),
                Variant.From("J-1"));

            (ServiceResult status, ArrayOf<string> ids, uint handle) =
                await CallFilteredAsync(management, filter, default, 0);

            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-3", "R-1")));
            Assert.That(handle, Is.Not.Zero, "A non-empty list pins a handle.");
        }

        [Test]
        public async Task TheEventAndDataTypeSpellingsOfAPathSelectTheSameFieldAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-Spelling");

            // ResultReadyEventType spelling: Result/ResultMetaData/<field>.
            var eventStyle = new ContentFilter();
            eventStyle.Push(
                FilterOperator.GreaterThan,
                Operand(
                    ResultReadyEventType(),
                    Name(ResultBrowseNamesResult),
                    Name(nameof(ResultDataType.ResultMetaData)),
                    Name(nameof(ResultMetaDataType.ResultEvaluationCode))),
                Variant.From(15L));
            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, eventStyle, default, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-3", "R-2")));

            // ResultDataType spelling with a namespace-0 name: DataType
            // fields carry no namespace, so the index is not compared.
            var dataStyle = new ContentFilter();
            dataStyle.Push(
                FilterOperator.GreaterThan,
                Operand(
                    ResultDataTypeId(),
                    new QualifiedName(nameof(ResultDataType.ResultMetaData)),
                    new QualifiedName(nameof(ResultMetaDataType.ResultEvaluationCode))),
                Variant.From(15L));
            (status, ids, _) = await CallFilteredAsync(management, dataStyle, default, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-3", "R-2")));
        }

        [Test]
        public async Task CombinedOperatorsAndMaxResultsNarrowTheListAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-And");

            var filter = new ContentFilter();
            ContentFilterElement notSimulated = filter.Push(
                FilterOperator.Equals,
                Operand(MetaDataPath(nameof(ResultMetaDataType.IsSimulated))),
                Variant.From(false));
            ContentFilterElement evaluatedOk = filter.Push(
                FilterOperator.Equals,
                Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluation))),
                Variant.From((int)ResultEvaluationEnum.OK));
            filter.Push(
                FilterOperator.And,
                Variant.FromStructure(evaluatedOk),
                Variant.FromStructure(notSimulated));

            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, filter, default, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-3", "R-1")));

            // Without an order the store's newest-first listing is cut.
            (status, ids, _) = await CallFilteredAsync(management, filter, default, 1);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-3")));
        }

        [Test]
        public async Task OrderedBySortsAscendingWithUnsetValuesLastAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Order-Time");

            ArrayOf<RelativePath> byCreationTime = new[]
            {
                RelativeMetaDataPath(nameof(ResultMetaDataType.CreationTime))
            }.ToArrayOf();
            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, null, byCreationTime, 0);

            // R-2 is the oldest, R-1 next, R-3 newest; R-4 has no creation
            // time and sorts after every result that has one.
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-2", "R-1", "R-3", "R-4")));

            // The cap applies after sorting.
            (status, ids, _) = await CallFilteredAsync(management, null, byCreationTime, 2);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-2", "R-1")));
        }

        [Test]
        public async Task LaterOrderingCriteriaBreakTiesAndEqualKeysKeepNewestFirstAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Order-Ties");

            // R-1 and R-3 share J-1, R-2 has J-2, R-4 has no job.
            ArrayOf<RelativePath> byJob = new[]
            {
                RelativeMetaDataPath(nameof(ResultMetaDataType.JobId))
            }.ToArrayOf();
            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, null, byJob, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(
                ids.ToArray(),
                Is.EqualTo(Ids("R-3", "R-1", "R-2", "R-4")),
                "Equal keys keep the store's newest-first order.");

            ArrayOf<RelativePath> byJobThenCode = new[]
            {
                RelativeMetaDataPath(nameof(ResultMetaDataType.JobId)),
                RelativeMetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))
            }.ToArrayOf();
            (status, ids, _) = await CallFilteredAsync(management, null, byJobThenCode, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-1", "R-3", "R-2", "R-4")));
        }

        [Test]
        public async Task AFilterAndAnOrderCombineAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-Order");

            var filter = new ContentFilter();
            filter.Push(
                FilterOperator.IsNull,
                Operand(MetaDataPath(nameof(ResultMetaDataType.PartId))));
            ArrayOf<RelativePath> byCode = new[]
            {
                RelativeMetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))
            }.ToArrayOf();

            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, filter, byCode, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-1", "R-3", "R-4")));
        }

        [Test]
        public async Task AFilterMatchingNothingReturnsAnEmptyListWithoutAHandleAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-None");

            // A path naming no field evaluates to null, the way an event
            // filter treats an unknown field.
            var filter = new ContentFilter();
            filter.Push(
                FilterOperator.Equals,
                Operand(MetaDataPath("NoSuchField")),
                Variant.From("x"));

            (ServiceResult status, ArrayOf<string> ids, uint handle) =
                await CallFilteredAsync(management, filter, default, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.Count, Is.Zero);
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public async Task OfTypeAcceptsTheThreeResultTypesAndTheirSupertypesAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-OfType");

            foreach (NodeId typeId in new[]
            {
                ResultReadyEventType(),
                ResultDataTypeId(),
                Opc.Ua.ObjectTypeIds.BaseEventType
            })
            {
                var filter = new ContentFilter();
                filter.Push(FilterOperator.OfType, Variant.From(typeId));
                (ServiceResult status, ArrayOf<string> ids, _) =
                    await CallFilteredAsync(management, filter, default, 0);
                Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
                Assert.That(ids.Count, Is.EqualTo(4), typeId.ToString());
            }

            var folder = new ContentFilter();
            folder.Push(FilterOperator.OfType, Variant.From(Opc.Ua.ObjectTypeIds.FolderType));
            (ServiceResult folderStatus, ArrayOf<string> none, _) =
                await CallFilteredAsync(management, folder, default, 0);
            Assert.That(ServiceResult.IsGood(folderStatus), Is.True, folderStatus.ToString());
            Assert.That(none.Count, Is.Zero);
        }

        [Test]
        public async Task AnIndexRangeSelectsOneEntryOfTheResultContentAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-Range");

            var operand = new SimpleAttributeOperand(
                ResultTypeId(),
                new[] { Name(nameof(ResultDataType.ResultContent)) }.ToArrayOf())
            {
                IndexRange = "0"
            };
            var filter = new ContentFilter();
            // An index range on an array selects a one-element array, as it
            // does in an event filter.
            filter.Push(
                FilterOperator.Equals,
                Variant.FromStructure(operand),
                Variant.From(new[] { Variant.From(7.5) }.ToArrayOf()));

            (ServiceResult status, ArrayOf<string> ids, _) =
                await CallFilteredAsync(management, filter, default, 0);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("R-2")));
        }

        [Test]
        public async Task AnInvalidFilterIsRefusedAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Filter-Invalid");

            // Equals needs two operands.
            var filter = new ContentFilter
            {
                Elements = new[]
                {
                    new ContentFilterElement
                    {
                        FilterOperator = FilterOperator.Equals,
                        FilterOperands = new[]
                        {
                            new ExtensionObject(new LiteralOperand(Variant.From(1)))
                        }.ToArrayOf()
                    }
                }.ToArrayOf()
            };

            (ServiceResult status, ArrayOf<string> ids, uint handle) =
                await CallFilteredAsync(management, filter, default, 0);
            Assert.That(
                status.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadContentFilterInvalid));
            Assert.That(ids.Count, Is.Zero);
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public async Task AnOrderingPathThatNamesNoFieldIsRefusedAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Order-Invalid");

            ArrayOf<RelativePath> bogus = new[]
            {
                RelativeMetaDataPath("NoSuchField")
            }.ToArrayOf();
            (ServiceResult status, _, _) = await CallFilteredAsync(management, null, bogus, 0);
            Assert.That(
                status.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AResultDroppedWhileTheQueryRunsIsSkippedAsync()
        {
            var store = new ForgetfulStore(
                new[] { Result("A", jobId: "J"), Result("B", jobId: "J") },
                forget: "A");
            var filter = new ContentFilter();
            filter.Push(
                FilterOperator.Equals,
                Operand(MetaDataPath(nameof(ResultMetaDataType.JobId))),
                Variant.From("J"));

            ArrayOf<string> ids = await MachineryResultQuery.ExecuteAsync(
                store,
                filter,
                default,
                0,
                CreateFilterContext(),
                CancellationToken.None);
            Assert.That(ids.ToArray(), Is.EqualTo(Ids("B")));
        }

        [Test]
        public void EveryMetaDataFieldResolvesInAllThreeSpellings()
        {
            ResultDataType data = FullyPopulated();
            string[] fields =
            [
                nameof(ResultMetaDataType.ResultId),
                nameof(ResultMetaDataType.HasTransferableDataOnFile),
                nameof(ResultMetaDataType.IsPartial),
                nameof(ResultMetaDataType.IsSimulated),
                nameof(ResultMetaDataType.ResultState),
                nameof(ResultMetaDataType.StepId),
                nameof(ResultMetaDataType.PartId),
                nameof(ResultMetaDataType.ExternalRecipeId),
                nameof(ResultMetaDataType.InternalRecipeId),
                nameof(ResultMetaDataType.ProductId),
                nameof(ResultMetaDataType.ExternalConfigurationId),
                nameof(ResultMetaDataType.InternalConfigurationId),
                nameof(ResultMetaDataType.JobId),
                nameof(ResultMetaDataType.CreationTime),
                nameof(ResultMetaDataType.ProcessingTimes),
                nameof(ResultMetaDataType.ResultUri),
                nameof(ResultMetaDataType.ResultEvaluation),
                nameof(ResultMetaDataType.ResultEvaluationCode),
                nameof(ResultMetaDataType.ResultEvaluationDetails),
                nameof(ResultMetaDataType.FileFormat)
            ];
            foreach (string field in fields)
            {
                Assert.That(
                    MachineryResultQuery.TryResolve(data, MetaDataPath(field), out Variant value),
                    Is.True,
                    field);
                Assert.That(value.IsNull, Is.False, field);
                Assert.That(
                    MachineryResultQuery.TryResolve(
                        data,
                        new[]
                        {
                            Name(ResultBrowseNamesResult),
                            Name(nameof(ResultDataType.ResultMetaData)),
                            Name(field)
                        }.ToArrayOf(),
                        out Variant eventValue),
                    Is.True,
                    field);
                Assert.That(eventValue, Is.EqualTo(value), field);

                // The same field on an empty result is known but unset.
                Assert.That(
                    MachineryResultQuery.TryResolve(
                        new ResultDataType(),
                        MetaDataPath(field),
                        out Variant unset),
                    Is.True,
                    field);
                Assert.That(unset.IsNull, Is.True, field);
            }
        }

        [Test]
        public void TheEncodingMaskMarksAFieldSetEvenAtItsDefaultValue()
        {
            var data = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = "M",
                    EncodingMask = (uint)(ResultMetaDataTypeFields.IsPartial |
                        ResultMetaDataTypeFields.ResultState |
                        ResultMetaDataTypeFields.ResultEvaluation |
                        ResultMetaDataTypeFields.ProcessingTimes),
                    ProcessingTimes = new ProcessingTimesDataType
                    {
                        EncodingMask = (uint)ProcessingTimesDataTypeFields.ProcessingDuration
                    }
                }
            };
            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    MetaDataPath(nameof(ResultMetaDataType.IsPartial)),
                    out Variant partial),
                Is.True);
            Assert.That(partial, Is.EqualTo(Variant.From(false)));
            MachineryResultQuery.TryResolve(
                data,
                MetaDataPath(nameof(ResultMetaDataType.ResultState)),
                out Variant state);
            Assert.That(state, Is.EqualTo(Variant.From(0)));
            MachineryResultQuery.TryResolve(
                data,
                MetaDataPath(nameof(ResultMetaDataType.ResultEvaluation)),
                out Variant evaluation);
            Assert.That(evaluation, Is.EqualTo(Variant.From(0)));
            MachineryResultQuery.TryResolve(
                data,
                MetaDataPath(
                    nameof(ResultMetaDataType.ProcessingTimes),
                    nameof(ProcessingTimesDataType.ProcessingDuration)),
                out Variant duration);
            Assert.That(duration, Is.EqualTo(Variant.From(0.0)));
        }

        [Test]
        public void ProcessingTimesResolveAsAStructureAndFieldByField()
        {
            ResultDataType data = FullyPopulated();
            foreach (string field in new[]
            {
                nameof(ProcessingTimesDataType.StartTime),
                nameof(ProcessingTimesDataType.EndTime),
                nameof(ProcessingTimesDataType.AcquisitionDuration),
                nameof(ProcessingTimesDataType.ProcessingDuration)
            })
            {
                Assert.That(
                    MachineryResultQuery.TryResolve(
                        data,
                        MetaDataPath(nameof(ResultMetaDataType.ProcessingTimes), field),
                        out Variant value),
                    Is.True,
                    field);
                Assert.That(value.IsNull, Is.False, field);

                Assert.That(
                    MachineryResultQuery.TryResolve(
                        new ResultDataType(),
                        MetaDataPath(nameof(ResultMetaDataType.ProcessingTimes), field),
                        out Variant unset),
                    Is.True,
                    field);
                Assert.That(unset.IsNull, Is.True, field);
            }

            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    MetaDataPath(nameof(ResultMetaDataType.ProcessingTimes), "Nope"),
                    out _),
                Is.False);
        }

        [Test]
        public void WholeStructuresAndUnknownPathsResolveAsExpected()
        {
            ResultDataType data = FullyPopulated();

            Assert.That(
                MachineryResultQuery.TryResolve(data, default, out Variant whole),
                Is.True,
                "An empty path is the ResultType variable's own value.");
            Assert.That(whole.TryGetStructure<ResultDataType>(out ResultDataType? structure), Is.True);
            Assert.That(structure, Is.SameAs(data));

            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    new[] { Name(nameof(ResultDataType.ResultMetaData)) }.ToArrayOf(),
                    out Variant metaData),
                Is.True);
            Assert.That(metaData.IsNull, Is.False);
            Assert.That(
                MachineryResultQuery.TryResolve(
                    new ResultDataType { ResultMetaData = null! },
                    new[] { Name(nameof(ResultDataType.ResultMetaData)) }.ToArrayOf(),
                    out Variant noMetaData),
                Is.True);
            Assert.That(noMetaData.IsNull, Is.True);
            Assert.That(
                MachineryResultQuery.TryResolve(
                    new ResultDataType { ResultMetaData = null! },
                    MetaDataPath(nameof(ResultMetaDataType.JobId)),
                    out Variant noJob),
                Is.True);
            Assert.That(noJob.IsNull, Is.True);

            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    new[] { Name(nameof(ResultDataType.ResultContent)) }.ToArrayOf(),
                    out Variant content),
                Is.True);
            Assert.That(content.IsNull, Is.False);
            // ResultContent is a mandatory field: a result without content
            // carries an empty array, not a missing one.
            Assert.That(
                MachineryResultQuery.TryResolve(
                    new ResultDataType { ResultContent = default },
                    new[] { Name(nameof(ResultDataType.ResultContent)) }.ToArrayOf(),
                    out Variant noContent),
                Is.True);
            Assert.That(noContent.TryGetValue(out ArrayOf<Variant> empty), Is.True);
            Assert.That(empty.Count, Is.Zero);

            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    new[] { Name(nameof(ResultDataType.ResultContent)), Name("x") }.ToArrayOf(),
                    out _),
                Is.False);
            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    new[] { Name("Severity") }.ToArrayOf(),
                    out _),
                Is.False,
                "A BaseEventType field is not stored with a result.");
            Assert.That(
                MachineryResultQuery.TryResolve(
                    data,
                    MetaDataPath(nameof(ResultMetaDataType.JobId), "Deeper"),
                    out _),
                Is.False);
        }

        [Test]
        public void TheFilterTargetAnswersOnlyValueAttributesAndValidRanges()
        {
            var target = new MachineryResultFilterTarget(FullyPopulated());
            IFilterContext context = CreateFilterContext();

            Assert.That(
                target.GetAttributeValue(
                    context,
                    ResultTypeId(),
                    MetaDataPath(nameof(ResultMetaDataType.JobId)),
                    Attributes.DisplayName,
                    default).IsNull,
                Is.True);
            Assert.That(
                target.GetAttributeValue(
                    context,
                    ResultTypeId(),
                    new[] { Name(nameof(ResultDataType.ResultContent)) }.ToArrayOf(),
                    Attributes.Value,
                    NumericRange.Parse("9")).IsNull,
                Is.True,
                "An index range past the end yields null.");
            Assert.That(target.IsTypeOf(context, NodeId.Null), Is.False);
            Assert.Throws<ArgumentNullException>(() => _ = new MachineryResultFilterTarget(null!));
        }

        [Test]
        public void SortKeysCompareAscendingWithUnsetLast()
        {
            Assert.That(MachineryResultQuery.CompareKeys(Variant.Null, Variant.Null), Is.Zero);
            Assert.That(MachineryResultQuery.CompareKeys(Variant.Null, Variant.From(1)), Is.Positive);
            Assert.That(MachineryResultQuery.CompareKeys(Variant.From(1), Variant.Null), Is.Negative);
            Assert.That(
                MachineryResultQuery.CompareKeys(Variant.From("B"), Variant.From("a")),
                Is.Negative,
                "Strings compare ordinally.");
            Assert.That(
                MachineryResultQuery.CompareKeys(
                    Variant.From(new LocalizedText("en", "a")),
                    Variant.From(new LocalizedText("en", "b"))),
                Is.Negative);
            Assert.That(MachineryResultQuery.CompareKeys(Variant.From(2), Variant.From(10)), Is.Negative);
            Assert.That(
                MachineryResultQuery.CompareKeys(Variant.From(2.5), Variant.From(2)),
                Is.Positive,
                "Numeric keys of different types still compare.");
            Assert.That(
                MachineryResultQuery.CompareKeys(Variant.From("x"), Variant.From(1)),
                Is.Zero,
                "Values that cannot be compared are treated as equal.");
        }

        [Test]
        public void AComparisonWithAnUnsetFieldIsNullNotTrue()
        {
            // No evaluation code: every comparison with it is NULL, so neither
            // the comparison nor its negation selects the result.
            var unset = new MachineryResultFilterTarget(Result("U-1").Data);
            foreach (FilterOperator comparison in new[]
            {
                FilterOperator.Equals,
                FilterOperator.GreaterThan,
                FilterOperator.GreaterThanOrEqual,
                FilterOperator.LessThan,
                FilterOperator.LessThanOrEqual
            })
            {
                var filter = new ContentFilter();
                filter.Push(
                    comparison,
                    Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))),
                    Variant.From(5L));
                Assert.That(Evaluate(filter, unset), Is.False, comparison.ToString());

                var negated = new ContentFilter();
                ContentFilterElement inner = negated.Push(
                    comparison,
                    Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))),
                    Variant.From(5L));
                negated.Push(FilterOperator.Not, Variant.FromStructure(inner));
                Assert.That(Evaluate(negated, unset), Is.False, "Not " + comparison);
            }

            // IsNull is the one operator that sees the missing value - and a
            // field that is set to zero is not missing.
            var isNull = new ContentFilter();
            isNull.Push(
                FilterOperator.IsNull,
                Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))));
            Assert.That(Evaluate(isNull, unset), Is.True);
            Assert.That(
                Evaluate(isNull, new MachineryResultFilterTarget(Result("U-2", code: 0).Data)),
                Is.False);
        }

        [Test]
        public void AndAndOrFollowTheNullTruthTables()
        {
            var target = new MachineryResultFilterTarget(Result("T-1", jobId: "J").Data);

            Assert.That(Evaluate(Combine(FilterOperator.And, true, null), target), Is.False);
            Assert.That(Evaluate(Combine(FilterOperator.And, true, true), target), Is.True);
            Assert.That(Evaluate(Combine(FilterOperator.And, false, null), target), Is.False);
            Assert.That(Evaluate(Combine(FilterOperator.And, null, false), target), Is.False);
            Assert.That(Evaluate(Combine(FilterOperator.Or, true, null), target), Is.True);
            Assert.That(Evaluate(Combine(FilterOperator.Or, null, true), target), Is.True);
            Assert.That(Evaluate(Combine(FilterOperator.Or, false, null), target), Is.False);
            Assert.That(Evaluate(Combine(FilterOperator.Or, false, false), target), Is.False);

            // Not(Or(FALSE, NULL)) is still NULL.
            ContentFilter notOr = Combine(FilterOperator.Or, false, null);
            notOr.Push(FilterOperator.Not, Variant.FromStructure(notOr.Elements[0]));
            Assert.That(Evaluate(notOr, target), Is.False);
        }

        [Test]
        public void LikeBetweenInListAndBitwiseEvaluateAgainstTheResult()
        {
            MachineryResult result = Result("L-1", jobId: "Job-42", code: 12);
            result.Data.ResultMetaData!.ResultEvaluationDetails =
                new LocalizedText("en", "within limits");
            var target = new MachineryResultFilterTarget(result.Data);
            Variant jobId = Operand(MetaDataPath(nameof(ResultMetaDataType.JobId)));
            Variant code = Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode)));
            Variant partId = Operand(MetaDataPath(nameof(ResultMetaDataType.PartId)));

            Assert.That(
                Evaluate(Single(FilterOperator.Like, jobId, Variant.From("Job-%")), target),
                Is.True);
            Assert.That(
                Evaluate(Single(FilterOperator.Like, jobId, Variant.From("X%")), target),
                Is.False);
            Assert.That(
                Evaluate(
                    Single(
                        FilterOperator.Like,
                        Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationDetails))),
                        Variant.From("within%")),
                    target),
                Is.True,
                "Like matches the text of a LocalizedText.");
            Assert.That(
                Evaluate(Single(FilterOperator.Like, code, Variant.From("1%")), target),
                Is.False,
                "Like needs strings.");
            Assert.That(
                Evaluate(Single(FilterOperator.Like, partId, Variant.From("%")), target),
                Is.False,
                "Like on an unset field is NULL.");

            Assert.That(
                Evaluate(
                    Single(FilterOperator.Between, code, Variant.From(10L), Variant.From(20L)),
                    target),
                Is.True);
            Assert.That(
                Evaluate(
                    Single(FilterOperator.Between, code, Variant.From(13L), Variant.From(20L)),
                    target),
                Is.False);
            Assert.That(
                Evaluate(
                    Single(FilterOperator.Between, code, Variant.From("a"), Variant.From(20L)),
                    target),
                Is.False,
                "Operands without an order are not between anything.");
            Assert.That(
                Evaluate(
                    Single(FilterOperator.Between, code, Variant.Null, Variant.From(20L)),
                    target),
                Is.False);

            Assert.That(
                Evaluate(
                    Single(
                        FilterOperator.InList,
                        jobId,
                        Variant.Null,
                        Variant.From("Job-1"),
                        Variant.From("Job-42")),
                    target),
                Is.True);
            Assert.That(
                Evaluate(
                    Single(FilterOperator.InList, code, Variant.From(11L), Variant.From(12L)),
                    target),
                Is.True);
            Assert.That(
                Evaluate(Single(FilterOperator.InList, jobId, Variant.From("Job-1")), target),
                Is.False);
            Assert.That(
                Evaluate(Single(FilterOperator.InList, partId, Variant.From("P")), target),
                Is.False);

            // Without a match a null candidate makes InList NULL, so its negation
            // must not select the result either (FALSE OR NULL is NULL).
            var notInList = new ContentFilter();
            ContentFilterElement inList = notInList.Push(
                FilterOperator.InList,
                jobId,
                Variant.Null,
                Variant.From("Job-1"));
            notInList.Push(FilterOperator.Not, Variant.FromStructure(inList));
            Assert.That(Evaluate(notInList, target), Is.False);

            // BitwiseAnd and BitwiseOr yield a number an Equals on top compares.
            var bitwiseAnd = new ContentFilter();
            ContentFilterElement masked = bitwiseAnd.Push(
                FilterOperator.BitwiseAnd,
                code,
                Variant.From(4L));
            bitwiseAnd.Push(FilterOperator.Equals, Variant.FromStructure(masked), Variant.From(4L));
            Assert.That(Evaluate(bitwiseAnd, target), Is.True);

            var bitwiseOr = new ContentFilter();
            ContentFilterElement ored = bitwiseOr.Push(
                FilterOperator.BitwiseOr,
                code,
                Variant.From(1L));
            bitwiseOr.Push(FilterOperator.Equals, Variant.FromStructure(ored), Variant.From(13L));
            Assert.That(Evaluate(bitwiseOr, target), Is.True);

            var bitwiseNull = new ContentFilter();
            ContentFilterElement nullMasked = bitwiseNull.Push(
                FilterOperator.BitwiseAnd,
                Operand(MetaDataPath(nameof(ResultMetaDataType.ResultState))),
                Variant.From(1));
            bitwiseNull.Push(FilterOperator.IsNull, Variant.FromStructure(nullMasked));
            Assert.That(Evaluate(bitwiseNull, target), Is.True);
        }

        [Test]
        public void OperatorsThatNeedANodeNeverSelectAResult()
        {
            var target = new MachineryResultFilterTarget(Result("N-1", jobId: "J").Data);

            Assert.That(
                Evaluate(
                    Single(FilterOperator.InView, Variant.From(Opc.Ua.ObjectIds.ObjectsFolder)),
                    target),
                Is.False);
            Assert.That(
                Evaluate(
                    Single(
                        FilterOperator.Cast,
                        Operand(MetaDataPath(nameof(ResultMetaDataType.JobId))),
                        Variant.From(Opc.Ua.DataTypeIds.String)),
                    target),
                Is.False);
            Assert.That(
                Evaluate(
                    Single(
                        FilterOperator.Equals,
                        Variant.FromStructure(new AttributeOperand(
                            Opc.Ua.ObjectIds.ObjectsFolder,
                            new QualifiedName("JobId"))),
                        Variant.From("J")),
                    target),
                Is.False,
                "An AttributeOperand needs a node and resolves to null.");
            Assert.That(
                Evaluate(Single(FilterOperator.OfType, Variant.From("not a NodeId")), target),
                Is.False);
            Assert.That(
                Evaluate(new ContentFilter(), target),
                Is.True,
                "An empty filter selects everything.");

            // A cycle Validate() does not catch ends as NULL instead of
            // overflowing the stack.
            var cyclic = new ContentFilter
            {
                Elements = new[]
                {
                    new ContentFilterElement
                    {
                        FilterOperator = FilterOperator.Not,
                        FilterOperands = new[]
                        {
                            new ExtensionObject(new ElementOperand(0))
                        }.ToArrayOf()
                    }
                }.ToArrayOf()
            };
            Assert.That(Evaluate(cyclic, target), Is.False);
        }

        [Test]
        public async Task GenerateFileForWriteIsRefusedAsNotWritableAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Write-Refused");

            var outputs = new List<Variant>();
            ServiceResult status = management.ResultTransfer!.GenerateFileForWrite!.Call(
                m_fixture!.Manager.SystemContext,
                management.ResultTransfer.NodeId,
                new[] { Variant.Null }.ToArrayOf(),
                new List<ServiceResult>(),
                outputs);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
        }

        [Test]
        public async Task CloseAndCommitRefusesEveryHandleAndLeavesADownloadOpenAsync()
        {
            ResultManagementState management = await BuildWithResultsAsync("Commit-Refused");

            Assert.That(
                CallCloseAndCommit(management, 4711).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            var outputs = new List<Variant>();
            ServiceResult generated = await management.ResultTransfer!.GenerateFileForRead!.CallAsync(
                m_fixture!.Manager.SystemContext,
                management.ResultTransfer.NodeId,
                new[]
                {
                    Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "R-2" })
                }.ToArrayOf(),
                new List<ServiceResult>(),
                outputs);
            Assert.That(ServiceResult.IsGood(generated), Is.True, generated.ToString());
            Assert.That(outputs[0].TryGetValue(out NodeId fileNodeId), Is.True);
            Assert.That(outputs[1].TryGetValue(out uint readHandle), Is.True);

            // A download handle is not a write handle either.
            Assert.That(
                CallCloseAndCommit(management, readHandle).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            // ...and refusing it did not close the download.
            var file = (FileState)m_fixture.Manager.FindPredefinedNode(fileNodeId)!;
            var readOutputs = new List<Variant>();
            ServiceResult read = file.Read!.Call(
                m_fixture.Manager.SystemContext,
                file.NodeId,
                new Variant[] { Variant.From(readHandle), Variant.From(64) }.ToArrayOf(),
                new List<ServiceResult>(),
                readOutputs);
            Assert.That(ServiceResult.IsGood(read), Is.True, read.ToString());
            Assert.That(readOutputs[0].TryGetValue(out ByteString data), Is.True);
            Assert.That(data.Span.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task AStoreWithItsOwnIngestionPathCanPublishAsync()
        {
            var inner = new InMemoryMachineryResultStore();
            var store = new ReadOnlyStore(inner);
            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await NewMachine("Custom-ReadOnly")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithStore(store).WithResultsFolder(publishedResults: 1);
                })
                .BuildAsync();

            // The application fills its store its own way, then announces.
            MachineryResult result = Result("C-1", jobId: "J-7");
            inner.Add(result);
            await machine.Results!.PublishAsync(result);

            var slots = new List<BaseInstanceState>();
            management!.Results!.GetChildren(m_fixture!.Manager.SystemContext, slots);
            Assert.That(
                ((ResultState)slots[0]).Value?.ResultMetaData?.ResultId,
                Is.EqualTo("C-1"),
                "Publishing from a custom store updates the result variables.");
            Assert.That(
                m_fixture.Manager.ConformanceUnits.ToArray(),
                Contains.Item(new QualifiedName("Machinery-Result ResultEvents")),
                "...and reports the result-ready event.");

            // A result the store does not hold cannot be announced.
            ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(
                async () => await machine.Results.PublishAsync(Result("C-missing")))!;
            Assert.That(refused.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task AWritableCustomStoreReceivesPublishedResultsAsync()
        {
            var store = new RecordingStore();
            IMachineHandle<BaseObjectState> machine = await NewMachine("Custom-Writable")
                .WithResultManagement(results => results
                    .WithStore(store)
                    .WithPredefinedResultMetaData())
                .BuildAsync();

            MachineryResult complete = Result("W-1", jobId: "J-1");
            ResultMetaDataType metaData = complete.Data.ResultMetaData!;
            metaData.ExternalRecipeId = "ext";
            metaData.InternalRecipeId = "int";
            metaData.ProductId = "product";
            metaData.StepId = "step";
            metaData.CreationTime = DateTime.UtcNow;
            await machine.Results!.PublishAsync(complete);
            Assert.That(store.Added, Has.Count.EqualTo(1));
            Assert.That(store.Added[0], Is.SameAs(complete));

            // The metadata check still runs before the store sees anything.
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await machine.Results.PublishAsync(Result("W-2")));
            Assert.That(store.Added, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TheInMemoryStoreIsWritableThroughTheInterfaceAsync()
        {
            var store = new InMemoryMachineryResultStore();
            await store.AddResultAsync(Result("I-1"));
            Assert.That(store, Is.InstanceOf<IWritableMachineryResultStore>());
            Assert.That((await store.GetLatestResultAsync())!.ResultId, Is.EqualTo("I-1"));
        }

        [Test]
        public async Task TheStandaloneServerPublishesFromACustomStoreAsync()
        {
            var inner = new InMemoryMachineryResultStore();
            var store = new ReadOnlyStore(inner);
            using var manager = new MachineryResultNodeManager(
                m_fixture!.Server.CurrentInstance,
                m_fixture.Configuration,
                new MachineryResultServerOptions
                {
                    InstanceNamespaceUri = "urn:opcua-netstandard:machinery:results:query"
                },
                store);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());

            MachineryResult result = Result("S-1");
            inner.Add(result);
            await manager.Publisher!.PublishAsync(result);
            Assert.That(
                manager.ConformanceUnits.ToArray(),
                Contains.Item(new QualifiedName("Machinery-Result ResultEvents")));

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.Publisher.PublishAsync(Result("S-missing")));

            // The stand-alone ResultTransfer refuses writes the same way.
            ServiceResult write = manager.ResultManagement!.ResultTransfer!.GenerateFileForWrite!
                .Call(
                    manager.SystemContext,
                    manager.ResultManagement.ResultTransfer.NodeId,
                    new[] { Variant.Null }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>());
            Assert.That(write.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
        }

        /// <summary>
        /// Builds a machine with four results the filter tests select from:
        /// <list type="table">
        /// <item>R-1: J-1, OK, code 10, created 10:00, not simulated</item>
        /// <item>R-2: J-2, NotOK, code 20, created 09:00, simulated, part P-2, content 7.5</item>
        /// <item>R-3: J-1, OK, code 30, created 11:00, not simulated</item>
        /// <item>R-4: no job, no evaluation, no code, no creation time</item>
        /// </list>
        /// Published in that order, so the store lists R-4 first.
        /// </summary>
        private async Task<ResultManagementState> BuildWithResultsAsync(string name)
        {
            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await NewMachine(name)
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore(8).WithFileTransfer();
                })
                .BuildAsync();

            var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            await machine.Results!.PublishAsync(Result(
                "R-1",
                jobId: "J-1",
                evaluation: ResultEvaluationEnum.OK,
                code: 10,
                created: day.AddHours(10),
                simulated: false));
            MachineryResult second = Result(
                "R-2",
                jobId: "J-2",
                evaluation: ResultEvaluationEnum.NotOK,
                code: 20,
                created: day.AddHours(9),
                simulated: true,
                payload: new ByteString(new byte[] { 1, 2, 3 }));
            second.Data.ResultMetaData!.PartId = "P-2";
            second.Data.ResultContent = new[] { Variant.From(7.5), Variant.From("mm") }.ToArrayOf();
            await machine.Results.PublishAsync(second);
            await machine.Results.PublishAsync(Result(
                "R-3",
                jobId: "J-1",
                evaluation: ResultEvaluationEnum.OK,
                code: 30,
                created: day.AddHours(11),
                simulated: false));
            await machine.Results.PublishAsync(Result("R-4"));
            return management!;
        }

        private async Task<(ServiceResult Status, ArrayOf<string> Ids, uint Handle)>
            CallFilteredAsync(
                ResultManagementState management,
                ContentFilter? filter,
                ArrayOf<RelativePath> orderedBy,
                uint maxResults)
        {
            var outputs = new List<Variant>();
            ServiceResult status = await management.GetResultIdListFiltered!.CallAsync(
                m_fixture!.Manager.SystemContext,
                management.NodeId,
                new[]
                {
                    filter == null ? Variant.Null : Variant.FromStructure(filter),
                    orderedBy.Count == 0 ? Variant.Null : Variant.FromStructure(orderedBy),
                    Variant.From(maxResults),
                    Variant.From(0)
                }.ToArrayOf(),
                new List<ServiceResult>(),
                outputs);
            if (ServiceResult.IsBad(status) || outputs.Count < 2)
            {
                return (status, ArrayOf<string>.Empty, 0);
            }
            outputs[0].TryGetValue(out uint handle);
            outputs[1].TryGetValue(out ArrayOf<string> ids);
            return (status, ids, handle);
        }

        private ServiceResult CallCloseAndCommit(ResultManagementState management, uint handle)
        {
            return management.ResultTransfer!.CloseAndCommit!.Call(
                m_fixture!.Manager.SystemContext,
                management.ResultTransfer.NodeId,
                new[] { Variant.From(handle) }.ToArrayOf(),
                new List<ServiceResult>(),
                new List<Variant>());
        }

        private FilterContext CreateFilterContext()
        {
            ServerSystemContext context = m_fixture!.Manager.SystemContext;
            return new FilterContext(
                context.NamespaceUris,
                context.TypeTable,
                context.Telemetry);
        }

        private NodeId ResultReadyEventType()
        {
            return NodeId.Create(
                Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                m_fixture!.Manager.SystemContext.NamespaceUris);
        }

        private NodeId ResultTypeId()
        {
            return NodeId.Create(
                Opc.Ua.Machinery.Result.VariableTypes.ResultType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                m_fixture!.Manager.SystemContext.NamespaceUris);
        }

        private NodeId ResultDataTypeId()
        {
            return NodeId.Create(
                Opc.Ua.Machinery.Result.DataTypes.ResultDataType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                m_fixture!.Manager.SystemContext.NamespaceUris);
        }

        private QualifiedName Name(string name)
        {
            return new QualifiedName(
                name,
                (ushort)m_fixture!.Manager.SystemContext.NamespaceUris.GetIndex(
                    Opc.Ua.Machinery.Result.Namespaces.MachineryResult));
        }

        private ArrayOf<QualifiedName> MetaDataPath(params string[] fields)
        {
            var path = new QualifiedName[fields.Length + 1];
            path[0] = Name(nameof(ResultDataType.ResultMetaData));
            for (int ii = 0; ii < fields.Length; ii++)
            {
                path[ii + 1] = Name(fields[ii]);
            }
            return path.ToArrayOf();
        }

        private RelativePath RelativeMetaDataPath(string field)
        {
            return new RelativePath
            {
                Elements = new[]
                {
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = Name(nameof(ResultDataType.ResultMetaData))
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = Name(field)
                    }
                }.ToArrayOf()
            };
        }

        private Variant Operand(ArrayOf<QualifiedName> path)
        {
            return Variant.FromStructure(new SimpleAttributeOperand(ResultTypeId(), path));
        }

        private static Variant Operand(NodeId typeId, params QualifiedName[] path)
        {
            return Variant.FromStructure(new SimpleAttributeOperand(typeId, path.ToArrayOf()));
        }

        private IMachineBuilder<BaseObjectState> NewMachine(string name)
        {
            return m_context!
                .AddMachine(new QualifiedName(name))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = $"SN-{name}";
                    id.ProductInstanceUri = $"urn:acme:{name}";
                });
        }

        private static MachineryResult Result(
            string id,
            string? jobId = null,
            ResultEvaluationEnum? evaluation = null,
            long? code = null,
            DateTime? created = null,
            bool? simulated = null,
            ByteString payload = default)
        {
            var metaData = new ResultMetaDataType { ResultId = id, JobId = jobId };
            if (evaluation != null)
            {
                metaData.ResultEvaluation = evaluation.Value;
                metaData.EncodingMask |= (uint)ResultMetaDataTypeFields.ResultEvaluation;
            }
            if (code != null)
            {
                metaData.ResultEvaluationCode = code.Value;
                metaData.EncodingMask |= (uint)ResultMetaDataTypeFields.ResultEvaluationCode;
            }
            if (created != null)
            {
                metaData.CreationTime = created.Value;
                metaData.EncodingMask |= (uint)ResultMetaDataTypeFields.CreationTime;
            }
            if (simulated != null)
            {
                metaData.IsSimulated = simulated.Value;
                metaData.EncodingMask |= (uint)ResultMetaDataTypeFields.IsSimulated;
            }
            return new MachineryResult(new ResultDataType { ResultMetaData = metaData }, payload);
        }

        private static ResultDataType FullyPopulated()
        {
            var created = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
            return new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = "F-1",
                    HasTransferableDataOnFile = true,
                    IsPartial = true,
                    IsSimulated = true,
                    ResultState = 3,
                    StepId = "step",
                    PartId = "part",
                    ExternalRecipeId = "ext-recipe",
                    InternalRecipeId = "int-recipe",
                    ProductId = "product",
                    ExternalConfigurationId = "ext-config",
                    InternalConfigurationId = "int-config",
                    JobId = "job",
                    CreationTime = created,
                    ProcessingTimes = new ProcessingTimesDataType
                    {
                        StartTime = created,
                        EndTime = created.AddMinutes(1),
                        AcquisitionDuration = 12.5,
                        ProcessingDuration = 47.5
                    },
                    ResultUri = Ids("https://example.com/results/F-1").ToArrayOf(),
                    ResultEvaluation = ResultEvaluationEnum.NotDecidable,
                    ResultEvaluationCode = 42,
                    ResultEvaluationDetails = new LocalizedText("en", "borderline"),
                    FileFormat = Ids("text/csv").ToArrayOf()
                },
                ResultContent = new[] { Variant.From(1.25) }.ToArrayOf()
            };
        }

        private bool Evaluate(ContentFilter filter, MachineryResultFilterTarget target)
        {
            return MachineryResultFilterEvaluator.Evaluate(filter, CreateFilterContext(), target);
        }

        private static ContentFilter Single(FilterOperator op, params Variant[] operands)
        {
            var filter = new ContentFilter();
            filter.Push(op, operands);
            return filter;
        }

        /// <summary>
        /// Combines two comparisons that evaluate TRUE, FALSE or NULL against
        /// a result whose JobId is "J" and which carries no evaluation code.
        /// </summary>
        private ContentFilter Combine(FilterOperator op, bool? left, bool? right)
        {
            var filter = new ContentFilter();
            ContentFilterElement rightElement = filter.Push(FilterOperator.Equals, Leaf(right));
            ContentFilterElement leftElement = filter.Push(FilterOperator.Equals, Leaf(left));
            filter.Push(op, Variant.FromStructure(leftElement), Variant.FromStructure(rightElement));
            return filter;
        }

        private Variant[] Leaf(bool? value)
        {
            if (value == null)
            {
                return
                [
                    Operand(MetaDataPath(nameof(ResultMetaDataType.ResultEvaluationCode))),
                    Variant.From(1L)
                ];
            }
            return
            [
                Operand(MetaDataPath(nameof(ResultMetaDataType.JobId))),
                Variant.From(value.Value ? "J" : "K")
            ];
        }

        private static string[] Ids(params string[] ids)
        {
            return ids;
        }

        private const string ResultBrowseNamesResult = Opc.Ua.Machinery.Result.BrowseNames.Result;

        private MachineryServerFixture? m_fixture;
        private IMachineryBuildContext? m_context;

        /// <summary>
        /// A store the application fills through its own path: it answers
        /// reads but deliberately does not implement
        /// <see cref="IWritableMachineryResultStore"/>.
        /// </summary>
        private sealed class ReadOnlyStore : IMachineryResultStore
        {
            public ReadOnlyStore(InMemoryMachineryResultStore inner)
            {
                m_inner = inner;
            }

            public ValueTask<MachineryResult?> GetLatestResultAsync(
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetLatestResultAsync(cancellationToken);
            }

            public ValueTask<MachineryResult?> GetResultByIdAsync(
                string resultId,
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetResultByIdAsync(resultId, cancellationToken);
            }

            public ValueTask<ArrayOf<string>> GetResultIdsAsync(
                uint maxResults,
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetResultIdsAsync(maxResults, cancellationToken);
            }

            public ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
                ArrayOf<string> resultIds,
                CancellationToken cancellationToken = default)
            {
                return m_inner.AcknowledgeResultsAsync(resultIds, cancellationToken);
            }

            private readonly InMemoryMachineryResultStore m_inner;
        }

        /// <summary>
        /// A writable store that records what the publisher handed it.
        /// </summary>
        private sealed class RecordingStore : IWritableMachineryResultStore
        {
            public List<MachineryResult> Added { get; } = [];

            public ValueTask AddResultAsync(
                MachineryResult result,
                CancellationToken cancellationToken = default)
            {
                Added.Add(result);
                return default;
            }

            public ValueTask<MachineryResult?> GetLatestResultAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<MachineryResult?>(Added.Count == 0 ? null : Added[^1]);
            }

            public ValueTask<MachineryResult?> GetResultByIdAsync(
                string resultId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<MachineryResult?>(
                    Added.Find(result => result.ResultId == resultId));
            }

            public ValueTask<ArrayOf<string>> GetResultIdsAsync(
                uint maxResults,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<string>>(
                    Added.ConvertAll(result => result.ResultId).ToArray().ToArrayOf());
            }

            public ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
                ArrayOf<string> resultIds,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<int>>(ArrayOf<int>.Empty);
            }
        }

        /// <summary>
        /// Lists a result and then no longer finds it, the way a bounded store
        /// that evicts between the two reads behaves.
        /// </summary>
        private sealed class ForgetfulStore : IMachineryResultStore
        {
            public ForgetfulStore(MachineryResult[] results, string forget)
            {
                m_results = results;
                m_forget = forget;
            }

            public ValueTask<MachineryResult?> GetLatestResultAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<MachineryResult?>(m_results[^1]);
            }

            public ValueTask<MachineryResult?> GetResultByIdAsync(
                string resultId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<MachineryResult?>(
                    resultId == m_forget
                        ? null
                        : Array.Find(m_results, result => result.ResultId == resultId));
            }

            public ValueTask<ArrayOf<string>> GetResultIdsAsync(
                uint maxResults,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<string>>(
                    Array.ConvertAll(m_results, result => result.ResultId).ToArrayOf());
            }

            public ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
                ArrayOf<string> resultIds,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<int>>(ArrayOf<int>.Empty);
            }

            private readonly MachineryResult[] m_results;
            private readonly string m_forget;
        }
    }
}
