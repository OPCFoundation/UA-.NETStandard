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

#if NET10_0
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests for <see cref="RoboticsListPaging"/> and the related paging DTOs.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsListPagingTests
    {
        [Test]
        public void DefaultPageIsReturned()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeOps(3);

            OperationListResult result = RoboticsListPaging.PageOperations(ops, null);

            Assert.That(result.Total, Is.EqualTo(3));
            Assert.That(result.Returned, Is.EqualTo(3));
            Assert.That(result.NextCursor, Is.Null);
            Assert.That(result.Summaries, Is.Not.Null);
            Assert.That(result.Summaries, Has.Length.EqualTo(3));
        }

        [Test]
        public void PageSizeIsBounded()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeOps(5);

            var query = new OperationListQuery { PageSize = 2 };
            OperationListResult result = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(result.Returned, Is.EqualTo(2));
            Assert.That(result.NextCursor, Is.Not.Null);
        }

        [Test]
        public void CursorContinuesFromCorrectOffset()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeOps(5);

            var query = new OperationListQuery { PageSize = 2 };
            OperationListResult page1 = RoboticsListPaging.PageOperations(ops, query);

            query.Cursor = page1.NextCursor;
            OperationListResult page2 = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(page2.Returned, Is.EqualTo(2));
            Assert.That(page2.NextCursor, Is.Not.Null);

            query.Cursor = page2.NextCursor;
            OperationListResult page3 = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(page3.Returned, Is.EqualTo(1));
            Assert.That(page3.NextCursor, Is.Null);
        }

        [Test]
        public void FilterByExecutionState()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("Op1", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                },
                new IntentOperationSnapshot
                {
                    IntentId = "b",
                    Operation = new NodeId("Op2", 2),
                    ExecutionState = ExecutionStateEnum.Succeeded
                }
            ];

            var query = new OperationListQuery { ExecutionState = ExecutionStateEnum.Executing };
            OperationListResult result = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.Summaries![0].IntentId, Is.EqualTo("a"));
        }

        [Test]
        public void FilterActiveOnly()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("Op1", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                },
                new IntentOperationSnapshot
                {
                    IntentId = "b",
                    Operation = new NodeId("Op2", 2),
                    ExecutionState = ExecutionStateEnum.Succeeded
                },
                new IntentOperationSnapshot
                {
                    IntentId = "c",
                    Operation = new NodeId("Op3", 2),
                    ExecutionState = ExecutionStateEnum.Cancelled
                }
            ];

            var query = new OperationListQuery { Work = WorkSelector.Active };
            OperationListResult result = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.Summaries![0].IntentId, Is.EqualTo("a"));
        }

        [Test]
        public void FullDetailReturnsSnapshots()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeOps(2);

            var query = new OperationListQuery { Detail = DetailLevel.Full };
            OperationListResult result = RoboticsListPaging.PageOperations(ops, query);

            Assert.That(result.Summaries, Is.Null);
            Assert.That(result.Operations, Is.Not.Null);
            Assert.That(result.Operations, Has.Length.EqualTo(2));
        }

        [Test]
        public void OperationsAreSortedDeterministically()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "z",
                    Operation = new NodeId("OpZ", 2),
                    ExecutionState = ExecutionStateEnum.Accepted
                },
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("OpA", 2),
                    ExecutionState = ExecutionStateEnum.Accepted
                }
            ];

            OperationListResult result = RoboticsListPaging.PageOperations(ops, null);

            Assert.That(result.Summaries![0].IntentId, Is.EqualTo("a"));
            Assert.That(result.Summaries![1].IntentId, Is.EqualTo("z"));
        }

        [Test]
        public void MissionPageDefaultSummary()
        {
            ArrayOf<MissionSnapshot> missions = MakeMissions(3);

            MissionListResult result = RoboticsListPaging.PageMissions(missions, null);

            Assert.That(result.Total, Is.EqualTo(3));
            Assert.That(result.Returned, Is.EqualTo(3));
            Assert.That(result.Summaries, Is.Not.Null);
        }

        [Test]
        public void MissionPageSizeAndCursor()
        {
            ArrayOf<MissionSnapshot> missions = MakeMissions(5);

            var query = new MissionListQuery { PageSize = 2 };
            MissionListResult page1 = RoboticsListPaging.PageMissions(missions, query);

            Assert.That(page1.Returned, Is.EqualTo(2));
            Assert.That(page1.NextCursor, Is.Not.Null);
        }

        [Test]
        public void MissionFilterByMissionId()
        {
            ArrayOf<MissionSnapshot> missions =
            [
                new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("M1", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                },
                new MissionSnapshot
                {
                    MissionId = "m2",
                    MissionNode = new NodeId("M2", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                }
            ];

            var query = new MissionListQuery { MissionId = "m1" };
            MissionListResult result = RoboticsListPaging.PageMissions(missions, query);

            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.Summaries![0].MissionId, Is.EqualTo("m1"));
        }

        [Test]
        public void MissionFullDetailReturnsSnapshots()
        {
            ArrayOf<MissionSnapshot> missions = MakeMissions(2);

            var query = new MissionListQuery { Detail = DetailLevel.Full };
            MissionListResult result = RoboticsListPaging.PageMissions(missions, query);

            Assert.That(result.Summaries, Is.Null);
            Assert.That(result.Missions, Is.Not.Null);
        }

        [Test]
        public void PageSizeDefaultsWhenOmitted()
        {
            Assert.That(RoboticsListPaging.ResolvePageSize(null), Is.EqualTo(20));
        }

        [Test]
        public void ExplicitPageSizeWithinBoundsIsUsed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RoboticsListPaging.ResolvePageSize(1), Is.EqualTo(1));
                Assert.That(RoboticsListPaging.ResolvePageSize(50), Is.EqualTo(50));
                Assert.That(RoboticsListPaging.ResolvePageSize(100), Is.EqualTo(100));
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(101)]
        [TestCase(int.MaxValue)]
        public void OutOfRangePageSizeIsRejected(int pageSize)
        {
            Assert.That(
                () => RoboticsListPaging.ResolvePageSize(pageSize),
                Throws.ArgumentException.With.Message.Contains("pageSize"));
        }

        [Test]
        public void OutOfRangePageSizeIsRejectedByListing()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeOps(3);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => RoboticsListPaging.PageOperations(ops, new OperationListQuery { PageSize = 0 }),
                    Throws.ArgumentException);
                Assert.That(
                    () => RoboticsListPaging.PageMissions(MakeMissions(3), new MissionListQuery { PageSize = 500 }),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public void CursorIsExactlyFiveBytesAndLittleEndian()
        {
            byte[] bytes = Convert.FromBase64String(RoboticsListPaging.EncodeCursor(258));

            Assert.Multiple(() =>
            {
                Assert.That(bytes, Has.Length.EqualTo(5));
                Assert.That(bytes[0], Is.EqualTo(1));
                Assert.That(bytes[1], Is.EqualTo(2));
                Assert.That(bytes[2], Is.EqualTo(1));
                Assert.That(bytes[3], Is.Zero);
                Assert.That(bytes[4], Is.Zero);
            });
        }

        [Test]
        public void InvalidCursorIsRejected()
        {
            Assert.That(() => RoboticsListPaging.DecodeCursor("abc"), Throws.ArgumentException);
        }

        [Test]
        public void CursorWithExtraBytesIsRejected()
        {
            string cursor = Convert.ToBase64String([1, 42, 0, 0, 0, 0]);

            Assert.That(
                () => RoboticsListPaging.DecodeCursor(cursor),
                Throws.ArgumentException.With.Message.Contains("exactly 5"));
        }

        [Test]
        public void CursorWithTooFewBytesIsRejected()
        {
            string cursor = Convert.ToBase64String([1, 42, 0, 0]);

            Assert.That(
                () => RoboticsListPaging.DecodeCursor(cursor),
                Throws.ArgumentException.With.Message.Contains("exactly 5"));
        }

        [Test]
        public void CursorWithUnsupportedVersionIsRejected()
        {
            string cursor = Convert.ToBase64String([2, 42, 0, 0, 0]);

            Assert.That(
                () => RoboticsListPaging.DecodeCursor(cursor),
                Throws.ArgumentException.With.Message.Contains("version"));
        }

        [Test]
        public void CursorWithNegativeOffsetIsRejected()
        {
            string cursor = Convert.ToBase64String([1, 0, 0, 0, 128]);

            Assert.That(
                () => RoboticsListPaging.DecodeCursor(cursor),
                Throws.ArgumentException.With.Message.Contains("negative"));
        }

        [Test]
        public void CursorRoundTrips()
        {
            string cursor = RoboticsListPaging.EncodeCursor(42);
            int index = RoboticsListPaging.DecodeCursor(cursor);
            Assert.That(index, Is.EqualTo(42));
        }

        [Test]
        public void OperationsCanBeFilteredByMissionId()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("Op1", 2),
                    MissionId = "m1",
                    ExecutionState = ExecutionStateEnum.Executing
                },
                new IntentOperationSnapshot
                {
                    IntentId = "b",
                    Operation = new NodeId("Op2", 2),
                    MissionId = "m2",
                    ExecutionState = ExecutionStateEnum.Executing
                }
            ];

            OperationListResult result = RoboticsListPaging.PageOperations(
                ops, new OperationListQuery { MissionId = "m1" });

            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.Summaries![0].MissionId, Is.EqualTo("m1"));
        }

        [Test]
        public void TerminalSelectorReturnsOnlyTerminalOperations()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("Op1", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                },
                new IntentOperationSnapshot
                {
                    IntentId = "b",
                    Operation = new NodeId("Op2", 2),
                    ExecutionState = ExecutionStateEnum.Failed
                }
            ];

            OperationListResult terminal = RoboticsListPaging.PageOperations(
                ops, new OperationListQuery { Work = WorkSelector.Terminal });
            OperationListResult all = RoboticsListPaging.PageOperations(
                ops, new OperationListQuery { Work = WorkSelector.All });

            Assert.Multiple(() =>
            {
                Assert.That(terminal.Total, Is.EqualTo(1));
                Assert.That(terminal.Summaries![0].IntentId, Is.EqualTo("b"));
                Assert.That(all.Total, Is.EqualTo(2));
            });
        }

        [Test]
        public void OperationSummaryCarriesTypedFailureAndMessage()
        {
            ArrayOf<IntentOperationSnapshot> ops =
            [
                new IntentOperationSnapshot
                {
                    IntentId = "a",
                    Operation = new NodeId("Op1", 2),
                    ExecutionState = ExecutionStateEnum.Failed,
                    Result = new IntentResultDataType
                    {
                        Failure = IntentFailureEnum.SafetyLimitExceeded,
                        Message = new LocalizedText("safe speed limit active")
                    }
                }
            ];

            OperationListResult result = RoboticsListPaging.PageOperations(ops, null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Summaries![0].Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(result.Summaries![0].Message, Is.EqualTo("safe speed limit active"));
            });
        }

        [Test]
        public void OperationSummaryOmitsFailureWhenNone()
        {
            OperationListResult result = RoboticsListPaging.PageOperations(MakeOps(1), null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Summaries![0].Failure, Is.Null);
                Assert.That(result.Summaries![0].Message, Is.Null);
            });
        }

        [Test]
        public void MissionSummaryCarriesTypedFailureAndStepMapping()
        {
            ArrayOf<MissionSnapshot> missions =
            [
                new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("M1", 2),
                    MissionUpdateId = 4,
                    ExecutionState = ExecutionStateEnum.Failed,
                    CurrentStepId = "s2",
                    ReleasedStepCount = 2,
                    Failure = IntentFailureEnum.ParameterInvalid,
                    FailureMessage = new LocalizedText("bad pose"),
                    Steps =
                    [
                        new MissionStepOperation
                        {
                            StepId = "s1",
                            IntentId = "i1",
                            OperationNodeId = new NodeId("Op1", 2),
                            State = ExecutionStateEnum.Succeeded
                        },
                        new MissionStepOperation
                        {
                            StepId = "s2",
                            IntentId = "i2",
                            State = ExecutionStateEnum.Failed
                        }
                    ]
                }
            ];

            MissionListResult result = RoboticsListPaging.PageMissions(missions, null);
            MissionSummary summary = result.Summaries![0];

            Assert.Multiple(() =>
            {
                Assert.That(summary.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(summary.Message, Is.EqualTo("bad pose"));
                Assert.That(summary.MissionUpdateId, Is.EqualTo(4u));
                Assert.That(summary.CurrentStepId, Is.EqualTo("s2"));
                Assert.That(summary.Steps, Has.Length.EqualTo(2));
                Assert.That(summary.Steps[0].Operation, Is.EqualTo("ns=2;s=Op1"));
                Assert.That(summary.Steps[1].Operation, Is.Null);
                Assert.That(summary.Steps[1].State, Is.EqualTo(ExecutionStateEnum.Failed));
            });
        }

        [Test]
        public void MissionsAreSortedDeterministically()
        {
            ArrayOf<MissionSnapshot> missions =
            [
                new MissionSnapshot { MissionId = "z", MissionNode = new NodeId("MZ", 2) },
                new MissionSnapshot { MissionId = "a", MissionNode = new NodeId("MA", 2) }
            ];

            MissionListResult result = RoboticsListPaging.PageMissions(missions, null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Summaries![0].MissionId, Is.EqualTo("a"));
                Assert.That(result.Summaries![1].MissionId, Is.EqualTo("z"));
            });
        }

        [Test]
        public void FortyRealisticSummariesStayUnderThirtyTwoKilobytes()
        {
            ArrayOf<IntentOperationSnapshot> ops = MakeRealisticOps(40);

            OperationListResult summaryPage = RoboticsListPaging.PageOperations(
                ops, new OperationListQuery { PageSize = 40, Detail = DetailLevel.Summary });
            OperationListResult fullPage = RoboticsListPaging.PageOperations(
                ops, new OperationListQuery { PageSize = 40, Detail = DetailLevel.Full });

            int summaryBytes = MeasureUtf8(summaryPage);
            int fullBytes = MeasureUtf8(fullPage);

            Assert.Multiple(() =>
            {
                Assert.That(summaryPage.Returned, Is.EqualTo(40));
                Assert.That(summaryBytes, Is.LessThan(32 * 1024),
                    $"summary payload was {summaryBytes} bytes");
                Assert.That(summaryBytes, Is.LessThan(fullBytes / 4),
                    $"summary {summaryBytes} bytes vs full {fullBytes} bytes");
            });
        }

        private static int MeasureUtf8<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, kCompactJson).Length;
        }

        private static ArrayOf<IntentOperationSnapshot> MakeRealisticOps(int count)
        {
            var ops = new IntentOperationSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                ops[i] = new IntentOperationSnapshot
                {
                    IntentId = $"intent-{i:D4}-6f9d2a1c-palletise-cycle",
                    Operation = new NodeId($"RobotIntent/Controllers/Controller1/Intents/Operation{i:D4}", 2),
                    MissionId = $"mission-{i % 4:D2}-line-a-shift-3",
                    ExecutionState = (i % 3) switch
                    {
                        0 => ExecutionStateEnum.Executing,
                        1 => ExecutionStateEnum.Succeeded,
                        _ => ExecutionStateEnum.Queued
                    },
                    Progress = i % 3 == 0 ? 0.42 : -1,
                    QueuePosition = (uint)(i % 7),
                    CurrentPose = RobotIntentBuilder.Pose(
                        0.42 + (i * 0.001), -0.13, 0.87, 0, 0, 0, 1,
                        "RobotIntent/Controllers/Controller1/Frames/Base"),
                    Result = new IntentResultDataType
                    {
                        Failure = IntentFailureEnum.None,
                        Message = new LocalizedText(
                            "en-US",
                            $"Operation {i:D4} accepted and queued behind the palletise cycle for line A.")
                    }
                };
            }
            return [.. ops];
        }

        private static readonly JsonSerializerOptions kCompactJson = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static ArrayOf<IntentOperationSnapshot> MakeOps(int count)
        {
            var ops = new IntentOperationSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                ops[i] = new IntentOperationSnapshot
                {
                    IntentId = $"i-{i:D3}",
                    Operation = new NodeId($"Op{i:D3}", 2),
                    ExecutionState = ExecutionStateEnum.Accepted
                };
            }
            return [.. ops];
        }

        private static ArrayOf<MissionSnapshot> MakeMissions(int count)
        {
            var missions = new MissionSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                missions[i] = new MissionSnapshot
                {
                    MissionId = $"m-{i:D3}",
                    MissionNode = new NodeId($"M{i:D3}", 2),
                    ExecutionState = ExecutionStateEnum.Accepted
                };
            }
            return [.. missions];
        }
    }
}
#endif
