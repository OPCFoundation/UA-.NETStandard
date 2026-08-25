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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Applies deterministic filtering, sorting, and cursor-based paging to
    /// operation and mission snapshot collections.
    /// </summary>
    internal static class RoboticsListPaging
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;
        private const byte CursorVersion = 1;
        private const int CursorLength = 5;

        public static OperationListResult PageOperations(
            ArrayOf<IntentOperationSnapshot> allOps,
            OperationListQuery? query)
        {
            query ??= new OperationListQuery();
            int pageSize = ResolvePageSize(query.PageSize);
            int startIndex = DecodeCursor(query.Cursor);

            ArrayOf<IntentOperationSnapshot> filtered = FilterOperations(allOps, query);
            ArrayOf<IntentOperationSnapshot> sorted = SortOperationsDeterministically(filtered);

            int total = sorted.Count;
            if (startIndex > total)
            {
                startIndex = total;
            }
            int end = Math.Min(startIndex + pageSize, total);
            int returned = Math.Max(0, end - startIndex);

            string? nextCursor = end < total
                ? EncodeCursor(end)
                : null;

            if (query.Detail == DetailLevel.Full)
            {
                var items = new List<IntentOperationSnapshot>(returned);
                for (int i = startIndex; i < end; i++)
                {
                    items.Add(sorted[i]);
                }
                return new OperationListResult
                {
                    Total = total,
                    Returned = returned,
                    NextCursor = nextCursor,
                    Operations = [.. items]
                };
            }

            var summaries = new List<OperationSummary>(returned);
            for (int i = startIndex; i < end; i++)
            {
                summaries.Add(ToSummary(sorted[i]));
            }
            return new OperationListResult
            {
                Total = total,
                Returned = returned,
                NextCursor = nextCursor,
                Summaries = [.. summaries]
            };
        }

        public static MissionListResult PageMissions(
            ArrayOf<MissionSnapshot> allMissions,
            MissionListQuery? query)
        {
            query ??= new MissionListQuery();
            int pageSize = ResolvePageSize(query.PageSize);
            int startIndex = DecodeCursor(query.Cursor);

            ArrayOf<MissionSnapshot> filtered = FilterMissions(allMissions, query);
            ArrayOf<MissionSnapshot> sorted = SortMissionsDeterministically(filtered);

            int total = sorted.Count;
            if (startIndex > total)
            {
                startIndex = total;
            }
            int end = Math.Min(startIndex + pageSize, total);
            int returned = Math.Max(0, end - startIndex);

            string? nextCursor = end < total
                ? EncodeCursor(end)
                : null;

            if (query.Detail == DetailLevel.Full)
            {
                var items = new List<MissionSnapshot>(returned);
                for (int i = startIndex; i < end; i++)
                {
                    items.Add(sorted[i]);
                }
                return new MissionListResult
                {
                    Total = total,
                    Returned = returned,
                    NextCursor = nextCursor,
                    Missions = [.. items]
                };
            }

            var summaries = new List<MissionSummary>(returned);
            for (int i = startIndex; i < end; i++)
            {
                summaries.Add(ToMissionSummary(sorted[i]));
            }
            return new MissionListResult
            {
                Total = total,
                Returned = returned,
                NextCursor = nextCursor,
                Summaries = [.. summaries]
            };
        }

        private static ArrayOf<IntentOperationSnapshot> FilterOperations(
            ArrayOf<IntentOperationSnapshot> ops,
            OperationListQuery query)
        {
            var result = new List<IntentOperationSnapshot>(ops.Count);
            for (int i = 0; i < ops.Count; i++)
            {
                IntentOperationSnapshot op = ops[i];

                if (!string.IsNullOrEmpty(query.IntentId) &&
                    !string.Equals(op.IntentId, query.IntentId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(query.MissionId) &&
                    !string.Equals(op.MissionId, query.MissionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (query.ExecutionState.HasValue && op.ExecutionState != query.ExecutionState.Value)
                {
                    continue;
                }

                if (query.Work == WorkSelector.Active && IsTerminal(op.ExecutionState))
                {
                    continue;
                }

                if (query.Work == WorkSelector.Terminal && !IsTerminal(op.ExecutionState))
                {
                    continue;
                }

                result.Add(op);
            }
            return [.. result];
        }

        private static ArrayOf<MissionSnapshot> FilterMissions(
            ArrayOf<MissionSnapshot> missions,
            MissionListQuery query)
        {
            var result = new List<MissionSnapshot>(missions.Count);
            for (int i = 0; i < missions.Count; i++)
            {
                MissionSnapshot m = missions[i];

                if (!string.IsNullOrEmpty(query.MissionId) &&
                    !string.Equals(m.MissionId, query.MissionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (query.ExecutionState.HasValue && m.ExecutionState != query.ExecutionState.Value)
                {
                    continue;
                }

                if (query.Work == WorkSelector.Active && IsTerminal(m.ExecutionState))
                {
                    continue;
                }

                if (query.Work == WorkSelector.Terminal && !IsTerminal(m.ExecutionState))
                {
                    continue;
                }

                result.Add(m);
            }
            return [.. result];
        }

        private static ArrayOf<IntentOperationSnapshot> SortOperationsDeterministically(
            ArrayOf<IntentOperationSnapshot> ops)
        {
            var list = new List<IntentOperationSnapshot>(ops.Count);
            for (int i = 0; i < ops.Count; i++)
            {
                list.Add(ops[i]);
            }
            list.Sort((a, b) =>
            {
                int cmp = string.Compare(a.IntentId, b.IntentId, StringComparison.Ordinal);
                if (cmp != 0)
                {
                    return cmp;
                }
                return string.Compare(
                    a.Operation.ToString(), b.Operation.ToString(), StringComparison.Ordinal);
            });
            return [.. list];
        }

        private static ArrayOf<MissionSnapshot> SortMissionsDeterministically(
            ArrayOf<MissionSnapshot> missions)
        {
            var list = new List<MissionSnapshot>(missions.Count);
            for (int i = 0; i < missions.Count; i++)
            {
                list.Add(missions[i]);
            }
            list.Sort((a, b) => string.Compare(a.MissionId, b.MissionId, StringComparison.Ordinal));
            return [.. list];
        }

        private static OperationSummary ToSummary(IntentOperationSnapshot op)
        {
            return new OperationSummary
            {
                Operation = op.Operation.ToString(),
                IntentId = op.IntentId,
                ExecutionState = op.ExecutionState,
                Progress = op.Progress,
                QueuePosition = op.QueuePosition,
                Failure = op.Result.Failure != IntentFailureEnum.None
                    ? op.Result.Failure
                    : null,
                Message = op.Result.Message.Text is { Length: > 0 } text
                    ? text
                    : null,
                MissionId = op.MissionId
            };
        }

        private static MissionSummary ToMissionSummary(MissionSnapshot m)
        {
            var stepSummaries = new List<MissionStepOperationSummary>();
            if (!m.Steps.IsNull && m.Steps.Count > 0)
            {
                for (int i = 0; i < m.Steps.Count; i++)
                {
                    MissionStepOperation step = m.Steps[i];
                    stepSummaries.Add(new MissionStepOperationSummary
                    {
                        StepId = step.StepId,
                        IntentId = step.IntentId,
                        Operation = step.OperationNodeId.IsNull
                            ? null
                            : step.OperationNodeId.ToString(),
                        State = step.State
                    });
                }
            }

            return new MissionSummary
            {
                MissionNode = m.MissionNode.ToString(),
                MissionId = m.MissionId,
                MissionUpdateId = m.MissionUpdateId,
                ExecutionState = m.ExecutionState,
                CurrentStepId = m.CurrentStepId,
                Failure = m.Failure != IntentFailureEnum.None
                    ? m.Failure
                    : null,
                Message = m.FailureMessage.Text is { Length: > 0 } text
                    ? text
                    : null,
                ReleasedStepCount = m.ReleasedStepCount,
                Steps = [.. stepSummaries]
            };
        }

        private static bool IsTerminal(ExecutionStateEnum state)
        {
            return state is ExecutionStateEnum.Succeeded
                or ExecutionStateEnum.Cancelled
                or ExecutionStateEnum.Failed
                or ExecutionStateEnum.Retriable;
        }

        internal static int ResolvePageSize(int? requested)
        {
            if (requested is null)
            {
                return DefaultPageSize;
            }

            int value = requested.Value;
            if (value <= 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'pageSize' must be between 1 and {MaxPageSize} but was {value}. " +
                        $"Omit it to use the default of {DefaultPageSize}."),
                    nameof(requested));
            }

            if (value > MaxPageSize)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'pageSize' must be between 1 and {MaxPageSize} but was {value}."),
                    nameof(requested));
            }

            return value;
        }

        internal static int DecodeCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return 0;
            }

            Span<byte> bytes = stackalloc byte[CursorLength];
            if (!Convert.TryFromBase64String(cursor, bytes, out int written) || written != CursorLength)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Invalid cursor '{cursor}': a cursor is exactly {CursorLength} base64-encoded bytes."),
                    nameof(cursor));
            }

            if (bytes[0] != CursorVersion)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Invalid cursor '{cursor}': unsupported cursor version {bytes[0]}."),
                    nameof(cursor));
            }

            int index = BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]);
            if (index < 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Invalid cursor '{cursor}': the offset {index} is negative."),
                    nameof(cursor));
            }

            return index;
        }

        internal static string EncodeCursor(int index)
        {
            Span<byte> bytes = stackalloc byte[CursorLength];
            bytes[0] = CursorVersion;
            BinaryPrimitives.WriteInt32LittleEndian(bytes[1..], index);
            return Convert.ToBase64String(bytes);
        }
    }
}
