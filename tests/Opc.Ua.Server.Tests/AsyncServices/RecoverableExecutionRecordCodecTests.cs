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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Tests the bounded private record format and its metadata projection, not wire/schema conformance.
    /// Request, response and witness bytes are synthetic opaque local inputs, never executable messages.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableExecutionRecordCodecTests
    {
        [Test]
        public void ConstructorRejectsNullContext()
        {
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(
                () => new RecoverableExecutionRecordCodec(null!, RecoverableJournalTestData.Witness))!;
            Assert.That(error.ParamName, Is.EqualTo("context"));
        }

        [TestCase(0, 37)]
        [TestCase(-1, 37)]
        [TestCase(int.MaxValue, 37)]
        [TestCase(1024, 0)]
        [TestCase(1024, 1025)]
        public void ConstructorRejectsUnboundedContextOrWitness(int messageLimit, int witnessLength)
        {
            ServiceMessageContext context = RecoverableJournalTestData.Context();
            context.MaxMessageSize = messageLimit;
            AssertUnavailable(() => _ = new RecoverableExecutionRecordCodec(
                context, new ByteString(new byte[witnessLength])));
        }

        [Test]
        public void ConstructorRejectsNullWitness()
        {
            AssertUnavailable(() => _ = new RecoverableExecutionRecordCodec(
                RecoverableJournalTestData.Context(), default));
        }

        [TestCase(1)]
        [TestCase(32)]
        [TestCase(37)]
        [TestCase(1024)]
        public void OpaqueWitnessLengthsAreAcceptedWithoutSchemaClaims(int length)
        {
            byte[] bytes = [.. Enumerable.Range(0, length).Select(value => (byte)(value % 251))];
            var witness = new ByteString(bytes);
            var codec = new RecoverableExecutionRecordCodec(RecoverableJournalTestData.Context(), witness);
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Admission() with
            {
                TypeWitness = witness
            };

            ByteString encoded = codec.Encode(record);
            RecoverableExecutionDurableRecord decoded = codec.Decode(
                RecoverableJournalTestData.Raw(record));

            Assert.That(codec.MaximumRecordBytes, Is.EqualTo(12288));
            Assert.That(encoded, Is.EqualTo(RecoverableJournalTestData.Raw(record)));
            Assert.That(decoded.TypeWitness.Span.ToArray(), Is.EqualTo(bytes));
            Assert.That(decoded.ClientId, Is.EqualTo(RecoverableJournalTestData.ClientId));
            Assert.That(decoded.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
        }

        [TestCase(RequestEncoding.Binary)]
        [TestCase(RequestEncoding.Json)]
        public void EncodeMatchesIndependentLocalFormatAndDecodePreservesEveryField(RequestEncoding mapping)
        {
            RecoverableExecutionDurableRecord original = RecoverableJournalTestData.Available() with
            {
                Mapping = mapping,
                CancellationRequested = true,
                Revision = 19
            };
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            ByteString independentlyWritten = RecoverableJournalTestData.Raw(original);

            Assert.That(codec.Encode(original), Is.EqualTo(independentlyWritten),
                "The oracle is a separate private-format writer, not Decode(Encode(x)).");
            RecoverableExecutionDurableRecord decoded = codec.Decode(independentlyWritten);

            Assert.That(decoded.Owner.Id, Is.EqualTo(RecoverableJournalTestData.Owner.Id));
            Assert.That(decoded.Owner.ContinuityContext, Is.EqualTo(ByteString.From(0x6f, 0x77, 0x6e, 0x01)));
            Assert.That(decoded.ClientId, Is.EqualTo(RecoverableJournalTestData.ClientId));
            Assert.That(decoded.Incarnation, Is.EqualTo(RecoverableJournalTestData.Incarnation));
            Assert.That(decoded.Token, Is.EqualTo(ByteString.From(0x74, 0x6f, 0x6b, 0x00, 0xfe)));
            Assert.That(decoded.IssuingSessionId, Is.EqualTo(new NodeId("synthetic-session", 1)));
            Assert.That(decoded.ServiceId, Is.EqualTo(DataTypeIds.ReadRequest));
            Assert.That(decoded.TypeWitness, Is.EqualTo(RecoverableJournalTestData.Witness));
            Assert.That(decoded.MessageLimit, Is.EqualTo(1024));
            Assert.That(decoded.Mapping, Is.EqualTo(mapping));
            Assert.That(decoded.Execution.Ticks, Is.EqualTo(1_000_000));
            Assert.That(decoded.Retention.Ticks, Is.EqualTo(500_000));
            Assert.That(decoded.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(decoded.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(decoded.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(decoded.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(decoded.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(decoded.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(decoded.ReleasedAt, Is.Default);
            Assert.That(decoded.DispatchStarted, Is.True);
            Assert.That(decoded.WorkFinished, Is.True);
            Assert.That(decoded.CancellationRequested, Is.True);
            Assert.That(decoded.Revision, Is.EqualTo(19));
            Assert.That(decoded.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));
            Assert.That(decoded.Roles.Span.ToArray(), Is.EqualTo([new NodeId(101), new NodeId(102)]));
            Assert.That(decoded.Permissions.Span.ToArray(), Is.EqualTo(
            [
                new RecoverableNodePermission(new NodeId(201), PermissionType.Read),
                new RecoverableNodePermission(new NodeId(202), PermissionType.Write)
            ]));
            Assert.That(decoded.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
            Assert.That(decoded.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
        }

        [TestCase("format")]
        [TestCase("old-format")]
        [TestCase("witness-byte")]
        [TestCase("old-32-byte-witness")]
        [TestCase("empty-witness")]
        public void PrivateFormatOrOpaqueWitnessMismatchFailsClosed(string corruption)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Admission();
            var changes = new RecoverableJournalTestData.RawChanges { StopAfterWitness = true };
            if (corruption == "format")
            {
                changes.Format = 0;
            }
            else if (corruption == "old-format")
            {
                changes.Format = 0x52530400;
            }
            else
            {
                byte[] witness = record.TypeWitness.Span.ToArray();
                witness[5] ^= 0x80;
                changes.Witness = corruption switch
                {
                    "old-32-byte-witness" => new ByteString(new byte[32]),
                    "empty-witness" => ByteString.Empty,
                    _ => new ByteString(witness)
                };
            }

            ByteString encoded = RecoverableJournalTestData.Raw(record, changes);
            Assert.That(encoded.Length, Is.LessThan(50));
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Decode(encoded));
        }

        [TestCaseSource(nameof(ValidPhases))]
        public void EveryValidPhasePreservesExactStateAndOrigins(object testRecord)
        {
            var expected = (RecoverableExecutionDurableRecord)testRecord;
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            RecoverableExecutionDurableRecord decoded = codec.Decode(RecoverableJournalTestData.Raw(expected));

            Assert.That(codec.Encode(expected), Is.EqualTo(RecoverableJournalTestData.Raw(expected)));
            Assert.That(decoded.Phase, Is.EqualTo(expected.Phase));
            Assert.That(decoded.State, Is.EqualTo(expected.State));
            Assert.That(decoded.Kind, Is.EqualTo(expected.Kind));
            Assert.That(decoded.Status, Is.EqualTo(expected.Status));
            Assert.That(decoded.AdmittedAt, Is.EqualTo(expected.AdmittedAt));
            Assert.That(decoded.OutcomeAt, Is.EqualTo(expected.OutcomeAt));
            Assert.That(decoded.ReleasedAt, Is.EqualTo(expected.ReleasedAt));
            Assert.That(decoded.Request, Is.EqualTo(expected.Request));
            Assert.That(decoded.Response, Is.EqualTo(expected.Response));
            Assert.That(decoded.IsReleased, Is.EqualTo(expected.Phase == RecoverableExecutionCommitPhase.Released));
            Assert.That(decoded.ClientId, Is.EqualTo(RecoverableJournalTestData.ClientId));
        }

        [TestCaseSource(nameof(InvalidPhases))]
        public void InvalidPhaseStateOutcomeOrFlagsAreRejected(object testRecord)
        {
            var invalid = (RecoverableExecutionDurableRecord)testRecord;
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            AssertUnavailable(() => codec.Encode(invalid));
            AssertUnavailable(() => codec.Decode(RecoverableJournalTestData.Raw(invalid)));
        }

        [TestCaseSource(nameof(InvalidMetadata))]
        public void InvalidIdentityMetadataPayloadAndDurationsAreRejected(object testRecord, StatusCode decodingStatus)
        {
            var invalid = (RecoverableExecutionDurableRecord)testRecord;
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            AssertUnavailable(() => codec.Encode(invalid));
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => codec.Decode(RecoverableJournalTestData.Raw(invalid)))!;
            // The primitive decoder's size error is normalized by Journal.LoadAsync, tested separately.
            Assert.That(error.StatusCode, Is.EqualTo(decodingStatus));
        }

        [Test]
        public void MissingOwnerCannotBeEncodedAsQualifiedState()
        {
            RecoverableExecutionDurableRecord invalid = RecoverableJournalTestData.Admission() with { Owner = null! };
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Encode(invalid));
        }

        [TestCase("missing-admission")]
        [TestCase("mixed-domain")]
        [TestCase("execution-overflow")]
        [TestCase("retention-overflow")]
        [TestCase("history-overflow")]
        public void InvalidQualifiedTimeCoordinatesAreRejected(string corruption)
        {
            RecoverableExecutionDurableRecord record = corruption switch
            {
                "missing-admission" => RecoverableJournalTestData.Pending() with { AdmittedAt = default },
                "mixed-domain" => RecoverableJournalTestData.Available() with
                {
                    OutcomeAt = new RecoverableExecutionTime(RecoverableJournalTestData.OtherId, 10_200_000)
                },
                "execution-overflow" => RecoverableJournalTestData.Pending() with
                {
                    AdmittedAt = RecoverableJournalTestData.AtTicks(long.MaxValue)
                },
                "retention-overflow" => RecoverableJournalTestData.Available() with
                {
                    OutcomeAt = RecoverableJournalTestData.AtTicks(long.MaxValue)
                },
                _ => RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged) with
                {
                    ReleasedAt = RecoverableJournalTestData.AtTicks(long.MaxValue)
                }
            };
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Encode(record));
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Decode(
                RecoverableJournalTestData.Raw(record)));
        }

        [TestCase("outcome-before-admission")]
        [TestCase("release-before-outcome")]
        [TestCase("release-before-admission")]
        [TestCase("expired-before-result-deadline")]
        [TestCase("expired-after-result-deadline")]
        public void InvalidTimeChronologyIsRejected(string corruption)
        {
            RecoverableExecutionDurableRecord record = corruption switch
            {
                "outcome-before-admission" => RecoverableJournalTestData.Available() with
                {
                    OutcomeAt = RecoverableJournalTestData.At(999)
                },
                "release-before-outcome" =>
                    RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged) with
                    {
                        ReleasedAt = RecoverableJournalTestData.At(1019)
                    },
                "expired-before-result-deadline" =>
                    RecoverableJournalTestData.Released(ServiceOperationState.Expired) with
                    {
                        ReleasedAt = RecoverableJournalTestData.AtTicks(10_700_000 - 1)
                    },
                "expired-after-result-deadline" =>
                    RecoverableJournalTestData.Released(ServiceOperationState.Expired) with
                    {
                        ReleasedAt = RecoverableJournalTestData.AtTicks(10_700_000 + 1)
                    },
                _ => RecoverableJournalTestData.Released(ServiceOperationState.Abandoned) with
                {
                    OutcomeAt = default,
                    ReleasedAt = RecoverableJournalTestData.At(999)
                }
            };
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Decode(
                RecoverableJournalTestData.Raw(record)));
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Encode(record));
        }

        [Test]
        public void EmptyTimeDomainCannotHideNonzeroTicks()
        {
            ByteString raw = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Admission(),
                new RecoverableJournalTestData.RawChanges
                {
                    TimeField = "admitted",
                    TimeDomain = Uuid.Empty,
                    TimeTicks = 1
                });
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Decode(raw));
        }

        [TestCase("roles", -1)]
        [TestCase("roles", 33)]
        [TestCase("roles", int.MaxValue)]
        [TestCase("permissions", -1)]
        [TestCase("permissions", 33)]
        [TestCase("permissions", int.MaxValue)]
        public void DecodedCountsAreBoundedBeforeAllocation(string field, int count)
        {
            var changes = new RecoverableJournalTestData.RawChanges();
            if (field == "roles")
            {
                changes.RoleCount = count;
            }
            else
            {
                changes.PermissionCount = count;
            }
            ByteString raw = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Available(), changes);

            Assert.That(raw.Length, Is.LessThan(512), "Only the declared count is huge, never the test allocation.");
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Decode(raw));
        }

        [TestCase("roles", 0)]
        [TestCase("roles", 1)]
        [TestCase("roles", 32)]
        [TestCase("permissions", 0)]
        [TestCase("permissions", 1)]
        [TestCase("permissions", 32)]
        public void BoundedMetadataCountsPreserveEveryElement(string field, int count)
        {
            RecoverableExecutionDurableRecord record = WithMetadataCount(field, count);
            RecoverableExecutionDurableRecord decoded =
                RecoverableJournalTestData.Codec().Decode(RecoverableJournalTestData.Raw(record));

            Assert.That(decoded.Roles.Span.ToArray(), Is.EqualTo(record.Roles.Span.ToArray()));
            Assert.That(decoded.Permissions.Span.ToArray(), Is.EqualTo(record.Permissions.Span.ToArray()));
            Assert.That(field == "roles" ? decoded.Roles.Count : decoded.Permissions.Count, Is.EqualTo(count));
            Assert.That(decoded.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
        }

        [TestCase("roles")]
        [TestCase("permissions")]
        public void EncodeCannotProduceMetadataThatItsDecoderRejects(string field)
        {
            RecoverableExecutionDurableRecord invalid = WithMetadataCount(field, 33);
            AssertUnavailable(() => RecoverableJournalTestData.Codec().Encode(invalid));
        }

        [TestCase("empty")]
        [TestCase("null")]
        [TestCase("oversized")]
        [TestCase("trailing-byte")]
        public void EmptyOversizedOrTrailingLocalRecordsAreRejected(string corruption)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            byte[] valid = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Admission()).Span.ToArray();
            ByteString invalid = corruption switch
            {
                "empty" => ByteString.Empty,
                "null" => default,
                "oversized" => new ByteString(new byte[12289]),
                _ => ByteString.From([.. valid, 0x5a])
            };
            AssertUnavailable(() => codec.Decode(invalid));
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(8)]
        public void TruncatedLocalRecordsAreRejectedByDecoder(int bytesRemoved)
        {
            byte[] raw = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Available()).Span.ToArray();
            var truncated = new ByteString(raw.AsSpan(0, raw.Length - bytesRemoved).ToArray());
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => RecoverableJournalTestData.Codec().Decode(truncated))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [TestCase(-2)]
        [TestCase(1025)]
        [TestCase(int.MaxValue)]
        public void ForgedPayloadLengthsCannotAllocateUnboundedBuffers(int declaredLength)
        {
            StatusCode expectedStatus = declaredLength < 0
                ? StatusCodes.BadResourceUnavailable
                : StatusCodes.BadEncodingLimitsExceeded;
            ByteString raw = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Available(),
                new RecoverableJournalTestData.RawChanges { RequestLength = declaredLength });
            Assert.That(raw.Length, Is.LessThan(512));
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => RecoverableJournalTestData.Codec().Decode(raw))!;
            Assert.That(error.StatusCode, Is.EqualTo(expectedStatus));
        }

        [TestCase(1)]
        [TestCase(64)]
        public void TokenBoundaryLengthsArePreserved(int length)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Admission() with
            {
                Token = new ByteString(Enumerable.Repeat((byte)0xa5, length).ToArray())
            };
            RecoverableExecutionDurableRecord decoded =
                RecoverableJournalTestData.Codec().Decode(RecoverableJournalTestData.Raw(record));
            Assert.That(decoded.Token.Span.ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0xa5, length).ToArray()));
            Assert.That(decoded.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
        }

        [TestCase(1)]
        [TestCase(1024)]
        public void PayloadBoundaryLengthsKeepRequestAndResponseDistinct(int length)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available() with
            {
                Request = new ByteString(Enumerable.Repeat((byte)0x31, length).ToArray()),
                Response = new ByteString(Enumerable.Repeat((byte)0x72, length).ToArray())
            };
            RecoverableExecutionDurableRecord decoded =
                RecoverableJournalTestData.Codec().Decode(RecoverableJournalTestData.Raw(record));
            Assert.That(RecoverableJournalTestData.Codec().Encode(record),
                Is.EqualTo(RecoverableJournalTestData.Raw(record)));
            Assert.That(decoded.Request.Span.ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0x31, length).ToArray()));
            Assert.That(decoded.Response.Span.ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0x72, length).ToArray()));
        }

        [TestCase(1L)]
        [TestCase(21_474_836_470_000L)]
        public void AcceptedDurationEndpointsRemainExact(long ticks)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Admission() with
            {
                Execution = TimeSpan.FromTicks(ticks),
                Retention = TimeSpan.FromTicks(ticks)
            };
            RecoverableExecutionDurableRecord decoded =
                RecoverableJournalTestData.Codec().Decode(RecoverableJournalTestData.Raw(record));
            Assert.That(decoded.Execution.Ticks, Is.EqualTo(ticks));
            Assert.That(decoded.Retention.Ticks, Is.EqualTo(ticks));
            Assert.That(decoded.AdmittedAt, Is.Default);
        }

        [Test]
        public void StorageKeysSeparateEveryIdentityDimensionWithoutLeakingRawIdentity()
        {
            RecoverableExecutionOwnerId owner = RecoverableJournalTestData.Owner.Id;
            string key = RecoverableExecutionRecordCodec.GetKey("local/", owner, RecoverableJournalTestData.ClientId);
            Assert.That(key, Is.EqualTo("local/EWd6THY87SBJY8Z/Qd4Q96bXZgb0XB7hdFUvLIY0RYg="));
            var keys = new HashSet<string>(StringComparer.Ordinal) { key };
            foreach (RecoverableExecutionOwnerId other in new[]
            {
                new RecoverableExecutionOwnerId(RecoverableJournalTestData.OtherId, owner.ApplicationId, owner.UserId),
                new RecoverableExecutionOwnerId(owner.AuthorityId, RecoverableJournalTestData.OtherId, owner.UserId),
                new RecoverableExecutionOwnerId(
                    owner.AuthorityId, owner.ApplicationId, RecoverableJournalTestData.OtherId)
            })
            {
                Assert.That(keys.Add(RecoverableExecutionRecordCodec.GetKey(
                    "local/", other, RecoverableJournalTestData.ClientId)), Is.True);
            }
            Assert.That(keys.Add(RecoverableExecutionRecordCodec.GetKey(
                "local/", owner, RecoverableJournalTestData.OtherId)), Is.True);
            Assert.That(keys, Has.Count.EqualTo(5));
            Assert.That(key, Does.Not.Contain(owner.AuthorityId.ToString())
                .And.Not.Contain(owner.ApplicationId.ToString())
                .And.Not.Contain(owner.UserId.ToString())
                .And.Not.Contain(RecoverableJournalTestData.ClientId.ToString()));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void SnapshotRejectsIncompleteCommitPhases(int phase)
        {
            RecoverableExecutionDurableRecord record = (RecoverableExecutionCommitPhase)phase ==
                RecoverableExecutionCommitPhase.Admission
                ? RecoverableJournalTestData.Admission()
                : RecoverableJournalTestData.Candidate();
            AssertUnavailable(() => record.Snapshot(RecoverableJournalTestData.At(1030)));
        }

        [TestCase("execution", 0L, 100d)]
        [TestCase("execution", 8L, 99d)]
        [TestCase("execution", 10_000L, 99d)]
        [TestCase("execution", 1_000_006L, 0d)]
        [TestCase("execution", 1_000_007L, 0d)]
        [TestCase("execution", 1_000_008L, 0d)]
        [TestCase("result", 0L, 50d)]
        [TestCase("result", 10L, 49d)]
        [TestCase("result", 500_008L, 0d)]
        [TestCase("result", 500_009L, 0d)]
        [TestCase("result", 500_010L, 0d)]
        [TestCase("history", 0L, 50d)]
        [TestCase("history", 10L, 49d)]
        [TestCase("history", 500_008L, 0d)]
        [TestCase("history", 500_009L, 0d)]
        [TestCase("history", 500_010L, 0d)]
        public void SnapshotReportsExactAcceptedAndNonnegativeRemainingDurations(
            string timer, long elapsedTicks, double remaining)
        {
            RecoverableExecutionDurableRecord record = (timer switch
            {
                "execution" => RecoverableJournalTestData.Pending(),
                "result" => RecoverableJournalTestData.Available(),
                _ => RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged)
            }) with
            {
                Execution = TimeSpan.FromTicks(1_000_007),
                Retention = TimeSpan.FromTicks(500_009),
                CancellationRequested = true
            };
            RecoverableExecutionTime origin = timer switch
            {
                "execution" => record.AdmittedAt,
                "result" => record.OutcomeAt,
                _ => record.ReleasedAt
            };
            ServiceOperationInfoDataType snapshot = record.Snapshot(
                RecoverableJournalTestData.AtTicks(origin.Ticks + elapsedTicks));

            Assert.That(snapshot.ClientOperationId, Is.EqualTo(RecoverableJournalTestData.ClientId));
            Assert.That(snapshot.ServerToken, Is.EqualTo(ByteString.From(0x74, 0x6f, 0x6b, 0x00, 0xfe)));
            Assert.That(snapshot.ServiceId, Is.EqualTo(DataTypeIds.ReadRequest));
            Assert.That(snapshot.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Durable));
            Assert.That(snapshot.State, Is.EqualTo(record.State));
            Assert.That(snapshot.OutcomeKind, Is.EqualTo(record.Kind));
            Assert.That(snapshot.OutcomeStatus, Is.EqualTo(record.Status));
            Assert.That(snapshot.CancellationRequested, Is.True);
            Assert.That(snapshot.ExecutionAllowance, Is.EqualTo(100.0007d));
            Assert.That(snapshot.ResultRetentionTime, Is.EqualTo(50.0009d));
            Assert.That(snapshot.ExecutionRemaining, Is.EqualTo(timer == "execution" ? remaining : 0));
            Assert.That(snapshot.ResultRemaining, Is.EqualTo(timer == "result" ? remaining : 0));
            Assert.That(snapshot.HistoryRemaining, Is.EqualTo(timer == "history" ? remaining : 0));
        }

        [TestCase(ServiceOperationState.Acknowledged, -1L, false)]
        [TestCase(ServiceOperationState.Acknowledged, 0L, true)]
        [TestCase(ServiceOperationState.Acknowledged, 1L, true)]
        [TestCase(ServiceOperationState.Abandoned, -1L, false)]
        [TestCase(ServiceOperationState.Abandoned, 0L, true)]
        [TestCase(ServiceOperationState.Abandoned, 1L, true)]
        [TestCase(ServiceOperationState.Expired, -1L, false)]
        [TestCase(ServiceOperationState.Expired, 0L, true)]
        [TestCase(ServiceOperationState.Expired, 1L, true)]
        public void HistoryExpiryUsesExactReleasedBoundaryOnly(
            ServiceOperationState state, long deltaTicks, bool expired)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Released(state);
            long releaseMilliseconds = state == ServiceOperationState.Expired ? 1070 : 1040;
            RecoverableExecutionTime now = RecoverableJournalTestData.AtTicks(
                ((releaseMilliseconds + 50) * TimeSpan.TicksPerMillisecond) + deltaTicks);
            Assert.That(record.IsReleased, Is.True);
            Assert.That(record.IsHistoryExpired(now), Is.EqualTo(expired));
            Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
            Assert.That(record.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(releaseMilliseconds)));
        }

        [Test]
        public void PendingAndAvailableRecordsNeverHaveExpiredHistory()
        {
            RecoverableExecutionTime now = RecoverableJournalTestData.At(2000);
            foreach (RecoverableExecutionDurableRecord record in new[]
            {
                RecoverableJournalTestData.Pending(), RecoverableJournalTestData.Available()
            })
            {
                Assert.That(record.IsReleased, Is.False);
                Assert.That(record.IsHistoryExpired(now), Is.False);
                Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
            }
        }

        [Test]
        public void ProtectedEnvelopeHasExactLittleEndianHeaderAndZeroPadding()
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available();
            using AesCbcHmacRecordProtector protector = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();

            ByteString envelope = codec.Protect(record, protector, context);

            Assert.That(codec.MaximumRecordBytes, Is.EqualTo(12288));
            Assert.That(codec.MaximumProtectedRecordBytes, Is.EqualTo(16392));
            Assert.That(envelope.Length, Is.EqualTo(16392));
            Assert.That(envelope.Span[..4].ToArray(), Is.EqualTo(new byte[] { 0x01, 0x45, 0x53, 0x52 }));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(envelope.Span), Is.EqualTo(0x52534501u));
            int ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(envelope.Span[4..]);
            Assert.That(ciphertextLength, Is.GreaterThan(0).And.LessThan(16384));
            var ciphertext = new ByteString(envelope.Span.Slice(8, ciphertextLength).ToArray());
            Assert.That(protector.TryUnprotect(context, ciphertext, out ByteString plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(RecoverableJournalTestData.Raw(record)));
            Assert.That(envelope.Span[(8 + ciphertextLength)..].ToArray(), Is.All.EqualTo((byte)0));
            RecoverableExecutionDurableRecord decoded = codec.Unprotect(envelope, protector, context);
            Assert.That(decoded.ClientId, Is.EqualTo(RecoverableJournalTestData.ClientId));
            Assert.That(decoded.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(decoded.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(decoded.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
        }

        [TestCase(64, 12552)]
        [TestCase(512, 14344)]
        [TestCase(1024, 16392)]
        public void AdmissionThroughOutcomeKeepsReservedProtectedSlotSize(int limit, int reservedBytes)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            using AesCbcHmacRecordProtector protector = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();
            byte[] fullResponse = [.. Enumerable.Repeat((byte)0xa7, limit)];
            RecoverableExecutionDurableRecord[] records =
            [
                RecoverableJournalTestData.Admission() with { MessageLimit = limit },
                RecoverableJournalTestData.Pending() with { MessageLimit = limit },
                RecoverableJournalTestData.Candidate() with
                {
                    MessageLimit = limit,
                    Response = new ByteString(fullResponse)
                },
                RecoverableJournalTestData.Available() with
                {
                    MessageLimit = limit,
                    Response = new ByteString(fullResponse)
                }
            ];

            foreach (RecoverableExecutionDurableRecord record in records)
            {
                ByteString envelope = codec.Protect(record, protector, context);
                RecoverableExecutionDurableRecord decoded = codec.Unprotect(envelope, protector, context);
                Assert.That(envelope.Length, Is.EqualTo(reservedBytes));
                Assert.That(decoded.MessageLimit, Is.EqualTo(limit));
                Assert.That(decoded.Phase, Is.EqualTo(record.Phase));
                Assert.That(decoded.AdmittedAt, Is.EqualTo(record.AdmittedAt));
                Assert.That(decoded.OutcomeAt, Is.EqualTo(record.OutcomeAt));
                Assert.That(decoded.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
                Assert.That(decoded.Response, Is.EqualTo(record.Phase is
                    RecoverableExecutionCommitPhase.OutcomePreparing or RecoverableExecutionCommitPhase.OutcomeAvailable
                    ? new ByteString(fullResponse) : default));
            }
        }

        [TestCase(ServiceOperationState.Acknowledged)]
        [TestCase(ServiceOperationState.Abandoned)]
        [TestCase(ServiceOperationState.Expired)]
        public void ReleasedEnvelopeShrinksToHistoryAndKeepsFixedSizeForFlagWrites(ServiceOperationState state)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            using AesCbcHmacRecordProtector protector = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();
            RecoverableExecutionDurableRecord released = RecoverableJournalTestData.Released(state) with
            {
                WorkFinished = false
            };
            int expectedHistoryBytes = RecoverableJournalTestData.Raw(released).Length + 4104;

            ByteString history = codec.Protect(released, protector, context);
            ByteString updated = codec.Protect(released with
            {
                WorkFinished = true,
                CancellationRequested = true,
                Revision = released.Revision + 1
            }, protector, context);

            Assert.That(history.Length, Is.EqualTo(expectedHistoryBytes).And.LessThan(16392));
            Assert.That(updated.Length, Is.EqualTo(expectedHistoryBytes));
            RecoverableExecutionDurableRecord decoded = codec.Unprotect(updated, protector, context);
            Assert.That(decoded.State, Is.EqualTo(state));
            Assert.That(decoded.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Released));
            Assert.That(decoded.Request.IsNull, Is.True);
            Assert.That(decoded.Response.IsNull, Is.True);
            Assert.That(decoded.Permissions.Count, Is.Zero);
            Assert.That(decoded.WorkFinished, Is.True);
            Assert.That(decoded.CancellationRequested, Is.True);
            Assert.That(decoded.Revision, Is.EqualTo(7));
            Assert.That(decoded.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(decoded.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(
                state == ServiceOperationState.Expired ? 1070 : 1040)));
        }

        [TestCase("null")]
        [TestCase("empty")]
        [TestCase("short")]
        [TestCase("header-only")]
        [TestCase("oversized")]
        [TestCase("magic")]
        [TestCase("zero-ciphertext")]
        [TestCase("negative-ciphertext")]
        [TestCase("past-end-ciphertext")]
        [TestCase("maximum-ciphertext")]
        [TestCase("padding")]
        public void InvalidEnvelopeHeaderOrPaddingIsRejectedBeforeAuthentication(string corruption)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            using AesCbcHmacRecordProtector real = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();
            byte[] bytes = codec.Protect(RecoverableJournalTestData.Pending() with { MessageLimit = 512 },
                real, context).Span.ToArray();
            ByteString invalid;
            switch (corruption)
            {
                case "null":
                    invalid = default;
                    break;
                case "empty":
                    invalid = ByteString.Empty;
                    break;
                case "short":
                    invalid = new ByteString(new byte[7]);
                    break;
                case "header-only":
                    invalid = new ByteString(bytes.AsSpan(0, 8).ToArray());
                    break;
                case "oversized":
                    byte[] oversized = new byte[16393];
                    Array.Copy(bytes, oversized, bytes.Length);
                    invalid = new ByteString(oversized);
                    break;
                default:
                    if (corruption == "magic")
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x52534500);
                    }
                    else if (corruption == "padding")
                    {
                        bytes[^1] = 1;
                    }
                    else
                    {
                        int length = corruption switch
                        {
                            "zero-ciphertext" => 0,
                            "negative-ciphertext" => -1,
                            "past-end-ciphertext" => bytes.Length - 7,
                            _ => int.MaxValue
                        };
                        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), length);
                        if (length == 0)
                        {
                            bytes.AsSpan(8).Clear();
                        }
                    }
                    invalid = new ByteString(bytes);
                    break;
            }
            var unusedProtector = new Mock<IRecordProtector>(MockBehavior.Strict);

            AssertUnavailable(() => codec.Unprotect(invalid, unusedProtector.Object, context));

            unusedProtector.VerifyNoOtherCalls();
        }

        [TestCase("cipher-first")]
        [TestCase("cipher-middle")]
        [TestCase("cipher-last")]
        [TestCase("context")]
        [TestCase("key")]
        public void CiphertextContextAndProtectionKeyTamperingFailClosed(string corruption)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            using AesCbcHmacRecordProtector protector = CreateEnvelopeProtector();
            using AesCbcHmacRecordProtector other = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();
            byte[] bytes = codec.Protect(RecoverableJournalTestData.Available(), protector, context).Span.ToArray();
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            if (corruption.StartsWith("cipher-", StringComparison.Ordinal))
            {
                int offset = corruption switch
                {
                    "cipher-first" => 0,
                    "cipher-middle" => length / 2,
                    _ => length - 1
                };
                bytes[8 + offset] ^= 0x20;
            }
            ByteString attemptedContext = corruption == "context"
                ? RecordProtectionContext.Create("OpcUa.RSE.Server/0.4/v1", "another-synthetic-key")
                : context;

            AssertUnavailable(() => codec.Unprotect(
                new ByteString(bytes), corruption == "key" ? other : protector, attemptedContext));

            Assert.That(bytes, Has.Length.EqualTo(16392));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(0x52534501u));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(length));
        }

        [TestCase(false, -1)]
        [TestCase(false, 1)]
        [TestCase(true, -1)]
        [TestCase(true, 1)]
        public void ReservedEnvelopeLengthMustMatchAuthenticatedRecord(bool released, int change)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            using AesCbcHmacRecordProtector protector = CreateEnvelopeProtector();
            ByteString context = EnvelopeContext();
            RecoverableExecutionDurableRecord record = (released
                ? RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged)
                : RecoverableJournalTestData.Pending()) with
            { MessageLimit = 512 };
            byte[] valid = codec.Protect(record, protector, context).Span.ToArray();
            byte[] resized = new byte[valid.Length + change];
            Array.Copy(valid, resized, Math.Min(valid.Length, resized.Length));
            int ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(resized.AsSpan(4));
            Assert.That(protector.TryUnprotect(context,
                new ByteString(resized.AsSpan(8, ciphertextLength).ToArray()), out ByteString plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(RecoverableJournalTestData.Raw(record)));
            Assert.That(resized, Has.Length.LessThan(16392),
                "This must exercise exact per-record reservation length, not the global upper bound.");

            AssertUnavailable(() => codec.Unprotect(new ByteString(resized), protector, context));
        }

        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(12543, true)]
        [TestCase(12544, true)]
        [TestCase(12545, false)]
        public void ProtectorOutputLengthIsBoundedBeforeEnvelopePublication(int outputLength, bool accepted)
        {
            RecoverableExecutionRecordCodec codec = RecoverableJournalTestData.Codec();
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Admission() with
            {
                MessageLimit = 64
            };
            ByteString context = EnvelopeContext();
            ByteString output = outputLength < 0 ? default :
                new ByteString(Enumerable.Repeat((byte)0x5a, outputLength).ToArray());
            // This mock probes an opaque protector-output length contract; it does not simulate authentication.
            var protector = new Mock<IRecordProtector>(MockBehavior.Strict);
            protector.Setup(value => value.Protect(context, It.IsAny<ByteString>())).Returns(output);
            if (accepted)
            {
                ByteString envelope = codec.Protect(record, protector.Object, context);
                Assert.That(envelope.Length, Is.EqualTo(12552));
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(envelope.Span[4..]), Is.EqualTo(outputLength));
                Assert.That(envelope.Span.Slice(8, outputLength).ToArray(),
                    Is.EqualTo(Enumerable.Repeat((byte)0x5a, outputLength).ToArray()));
                Assert.That(envelope.Span[(8 + outputLength)..].ToArray(), Is.All.EqualTo((byte)0));
            }
            else
            {
                AssertUnavailable(() => codec.Protect(record, protector.Object, context));
            }
            protector.Verify(value => value.Protect(context, RecoverableJournalTestData.Raw(record)), Times.Once());
            protector.VerifyNoOtherCalls();
        }

        [Test]
        public void ProtectedSizeConstructorBoundaryCannotOverflow()
        {
            ServiceMessageContext context = RecoverableJournalTestData.Context();
            context.MaxMessageSize = 536_867_837;
            var codec = new RecoverableExecutionRecordCodec(context, RecoverableJournalTestData.Witness);
            Assert.That(codec.MaximumRecordBytes, Is.EqualTo(2_147_479_540));
            Assert.That(codec.MaximumProtectedRecordBytes, Is.EqualTo(2_147_483_644));
            context.MaxMessageSize = 536_867_838;
            AssertUnavailable(() => _ = new RecoverableExecutionRecordCodec(
                context, RecoverableJournalTestData.Witness));
        }

        private static ByteString EnvelopeContext()
        {
            return RecordProtectionContext.Create("OpcUa.RSE.Server/0.4/v1", "synthetic-envelope-test");
        }

        private static AesCbcHmacRecordProtector CreateEnvelopeProtector()
        {
            byte[] key = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(key);
            try
            {
                return new AesCbcHmacRecordProtector(key);
            }
            finally
            {
                CryptoUtils.ZeroMemory(key);
            }
        }

        private static RecoverableExecutionDurableRecord WithMetadataCount(string field, int count)
        {
            return field == "roles"
                ? RecoverableJournalTestData.Available() with
                {
                    Roles = Enumerable.Range(1, count).Select(value => new NodeId((uint)value)).ToArray()
                }
                : RecoverableJournalTestData.Available() with
                {
                    Permissions = Enumerable.Range(1, count).Select(value =>
                        new RecoverableNodePermission(new NodeId((uint)value), PermissionType.Read)).ToArray()
                };
        }

        private static void AssertUnavailable(Action action)
        {
            ServiceResultException error = Assert.Throws<ServiceResultException>(action)!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        private static IEnumerable<TestCaseData> ValidPhases()
        {
            yield return new TestCaseData(RecoverableJournalTestData.Admission()).SetArgDisplayNames("Admission");
            yield return new TestCaseData(RecoverableJournalTestData.Pending()).SetArgDisplayNames("Pending");
            yield return new TestCaseData(RecoverableJournalTestData.Candidate()).SetArgDisplayNames("Candidate");
            foreach ((ServiceOperationOutcome kind, StatusCode status) in new[]
            {
                (ServiceOperationOutcome.ServiceResponse, StatusCodes.Good),
                (ServiceOperationOutcome.ResultUnavailable, StatusCodes.BadResponseTooLarge),
                (ServiceOperationOutcome.ResultUnavailable, StatusCodes.BadEncodingError),
                (ServiceOperationOutcome.ExecutionDeadlineExceeded, StatusCodes.BadTimeout),
                (ServiceOperationOutcome.ExecutionIndeterminate, StatusCodes.BadUnexpectedError)
            })
            {
                RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available() with
                {
                    Kind = kind,
                    Status = status,
                    Response = kind == ServiceOperationOutcome.ServiceResponse
                        ? RecoverableJournalTestData.Response : default
                };
                yield return new TestCaseData(record).SetArgDisplayNames($"{kind}-{status}");
            }
            foreach (ServiceOperationState state in new[]
            {
                ServiceOperationState.Acknowledged, ServiceOperationState.Abandoned, ServiceOperationState.Expired
            })
            {
                yield return new TestCaseData(RecoverableJournalTestData.Released(state))
                    .SetArgDisplayNames(state.ToString());
            }
        }

        private static IEnumerable<TestCaseData> InvalidPhases()
        {
            RecoverableExecutionDurableRecord admission = RecoverableJournalTestData.Admission();
            RecoverableExecutionDurableRecord pending = RecoverableJournalTestData.Pending();
            RecoverableExecutionDurableRecord candidate = RecoverableJournalTestData.Candidate();
            RecoverableExecutionDurableRecord available = RecoverableJournalTestData.Available();
            RecoverableExecutionDurableRecord released =
                RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged);
            (string Name, RecoverableExecutionDurableRecord Record)[] invalid =
            [
                ("phase-negative", admission with { Phase = (RecoverableExecutionCommitPhase)(-1) }),
                ("phase-overflow", admission with { Phase = (RecoverableExecutionCommitPhase)5 }),
                ("admission-state", admission with { State = ServiceOperationState.OutcomeAvailable }),
                ("admission-outcome", admission with { Kind = ServiceOperationOutcome.ExecutionIndeterminate }),
                ("admission-status", admission with { Status = StatusCodes.BadTimeout }),
                ("admission-dispatch", admission with { DispatchStarted = true }),
                ("admission-work-finished", admission with { WorkFinished = true }),
                ("admission-origin", admission with { AdmittedAt = RecoverableJournalTestData.At(1000) }),
                ("admission-outcome-origin", admission with { OutcomeAt = RecoverableJournalTestData.At(1000) }),
                ("admission-release-origin", admission with { ReleasedAt = RecoverableJournalTestData.At(1000) }),
                ("admission-response", admission with { Response = ByteString.Empty }),
                ("admission-empty-request", admission with { Request = ByteString.Empty }),
                ("pending-state", pending with { State = ServiceOperationState.Acknowledged }),
                ("pending-outcome", pending with { Kind = ServiceOperationOutcome.ServiceResponse }),
                ("pending-status", pending with { Status = StatusCodes.BadUnexpectedError }),
                ("pending-response", pending with { Response = RecoverableJournalTestData.Response }),
                ("pending-outcome-origin", pending with { OutcomeAt = RecoverableJournalTestData.At(1020) }),
                ("pending-release-origin", pending with { ReleasedAt = RecoverableJournalTestData.At(1040) }),
                ("pending-request", pending with { Request = default }),
                ("candidate-state", candidate with { State = ServiceOperationState.OutcomeAvailable }),
                ("candidate-origin", candidate with { OutcomeAt = RecoverableJournalTestData.At(1020) }),
                ("candidate-release-origin", candidate with { ReleasedAt = RecoverableJournalTestData.At(1040) }),
                ("candidate-none", candidate with { Kind = ServiceOperationOutcome.None }),
                ("available-state", available with { State = ServiceOperationState.Pending }),
                ("available-no-origin", available with { OutcomeAt = default }),
                ("available-release-origin", available with { ReleasedAt = RecoverableJournalTestData.At(1040) }),
                ("available-no-request", available with { Request = default }),
                ("available-empty-response", available with { Response = ByteString.Empty }),
                ("available-undefined-outcome", available with { Kind = (ServiceOperationOutcome)int.MaxValue }),
                ("timeout-wrong-status", available with
                {
                    Kind = ServiceOperationOutcome.ExecutionDeadlineExceeded,
                    Status = StatusCodes.BadUnexpectedError,
                    Response = default
                }),
                ("indeterminate-wrong-status", available with
                {
                    Kind = ServiceOperationOutcome.ExecutionIndeterminate,
                    Status = StatusCodes.BadTimeout,
                    Response = default
                }),
                ("unavailable-wrong-status", available with
                {
                    Kind = ServiceOperationOutcome.ResultUnavailable,
                    Status = StatusCodes.Good,
                    Response = default
                }),
                ("protocol-outcome-payload", available with
                {
                    Kind = ServiceOperationOutcome.ExecutionDeadlineExceeded,
                    Status = StatusCodes.BadTimeout
                }),
                ("released-state", released with { State = ServiceOperationState.OutcomeAvailable }),
                ("released-undefined-state", released with { State = (ServiceOperationState)int.MaxValue }),
                ("released-request", released with { Request = ByteString.Empty }),
                ("released-response", released with { Response = ByteString.Empty }),
                ("released-permissions", released with { Permissions = available.Permissions }),
                ("released-undefined-outcome", released with { Kind = (ServiceOperationOutcome)int.MaxValue }),
                ("acknowledged-no-outcome", released with { Kind = ServiceOperationOutcome.None }),
                ("acknowledged-no-outcome-origin", released with { OutcomeAt = default })
            ];
            foreach ((string name, RecoverableExecutionDurableRecord record) in invalid)
            {
                yield return new TestCaseData(record).SetArgDisplayNames(name);
            }
        }

        private static IEnumerable<TestCaseData> InvalidMetadata()
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available();
            var tooLarge = new ByteString(new byte[1025]);
            (string Name, RecoverableExecutionDurableRecord Record)[] invalid =
            [
                ("client", record with { ClientId = Uuid.Empty }),
                ("incarnation", record with { Incarnation = Uuid.Empty }),
                ("token-null", record with { Token = default }),
                ("token-empty", record with { Token = ByteString.Empty }),
                ("token-too-large", record with { Token = new ByteString(new byte[65]) }),
                ("witness", record with { TypeWitness = new ByteString(new byte[37]) }),
                ("service-null", record with { ServiceId = NodeId.Null }),
                ("service-namespace", record with { ServiceId = new NodeId(629, 1) }),
                ("limit-zero", record with { MessageLimit = 0 }),
                ("limit-negative", record with { MessageLimit = -1 }),
                ("limit-too-large", record with { MessageLimit = 1025 }),
                ("request-too-large", record with { Request = tooLarge }),
                ("response-too-large", record with { Response = tooLarge }),
                ("owner-evidence-too-large", record with
                {
                    Owner = new RecoverableExecutionOwner(record.Owner.Id, tooLarge)
                }),
                ("execution-zero", record with { Execution = TimeSpan.Zero }),
                ("execution-negative", record with { Execution = TimeSpan.FromTicks(-1) }),
                ("execution-too-large", record with { Execution = TimeSpan.FromTicks(21_474_836_470_001) }),
                ("retention-zero", record with { Retention = TimeSpan.Zero }),
                ("retention-negative", record with { Retention = TimeSpan.FromTicks(-1) }),
                ("retention-too-large", record with { Retention = TimeSpan.FromTicks(21_474_836_470_001) }),
                ("mapping", record with { Mapping = (RequestEncoding)int.MaxValue }),
                ("revision-zero", record with { Revision = 0 }),
                ("null-role-node", record with { Roles = [NodeId.Null] }),
                ("null-permission-node", record with
                {
                    Permissions = [new RecoverableNodePermission(NodeId.Null, PermissionType.Read)]
                }),
                ("duplicate-permission", record with
                {
                    Permissions =
                    [
                        new RecoverableNodePermission(new NodeId(201), PermissionType.Read),
                        new RecoverableNodePermission(new NodeId(201), PermissionType.Read)
                    ]
                })
            ];
            foreach ((string name, RecoverableExecutionDurableRecord value) in invalid)
            {
                StatusCode decodingStatus = name is
                    "request-too-large" or "response-too-large" or "owner-evidence-too-large"
                    ? StatusCodes.BadEncodingLimitsExceeded
                    : StatusCodes.BadResourceUnavailable;
                yield return new TestCaseData(value, decodingStatus)
                    .SetArgDisplayNames(name, decodingStatus.ToString());
            }
        }
    }

    /// <summary>
    /// Fixed local-record oracles shared only by the two journal fixtures. Nothing here proves a UA schema.
    /// </summary>
    internal static class RecoverableJournalTestData
    {
        internal const int MessageLimit = 1024;
        internal const string LogicalServerId = "urn:synthetic:journal-server";
        internal const string KeyPrefix = "rse/server/iYa1vliKjxYcod9oBpcYNoP8IQhlEW0zveaXysJbyPY=/";
        internal static Uuid Domain { get; } = new("76000000-0000-0000-0000-000000000006");
        internal static Uuid ClientId { get; } = new("74000000-0000-0000-0000-000000000004");
        internal static Uuid Incarnation { get; } = new("75000000-0000-0000-0000-000000000005");
        internal static Uuid OtherId { get; } = new("77000000-0000-0000-0000-000000000007");

        internal static RecoverableExecutionOwner Owner { get; } = new(
            new RecoverableExecutionOwnerId(
                new Uuid("71000000-0000-0000-0000-000000000001"),
                new Uuid("72000000-0000-0000-0000-000000000002"),
                new Uuid("73000000-0000-0000-0000-000000000003")),
            ByteString.From(0x6f, 0x77, 0x6e, 0x01));

        internal static ByteString Response => ByteString.From(0x52, 0x50, 0x7f, 0x80);

        /// <summary>
        /// RSEW/version-shaped, but deliberately opaque synthetic evidence, not a schema hash.
        /// </summary>
        internal static ByteString Witness => ByteString.From(
        [
            0x52, 0x53, 0x45, 0x57, 0x01,
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
        ]);

        internal static ServiceMessageContext Context()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.MaxMessageSize = MessageLimit;
            context.MaxByteStringLength = MessageLimit;
            context.MaxArrayLength = MessageLimit;
            context.NamespaceUris.GetIndexOrAppend("urn:synthetic:journal");
            return context;
        }

        internal static RecoverableExecutionRecordCodec Codec()
        {
            return new RecoverableExecutionRecordCodec(Context(), Witness);
        }

        internal static RecoverableExecutionTime At(long milliseconds)
        {
            return AtTicks(checked(milliseconds * TimeSpan.TicksPerMillisecond));
        }

        internal static RecoverableExecutionTime AtTicks(long ticks)
        {
            return new RecoverableExecutionTime(Domain, ticks);
        }

        internal static RecoverableExecutionDurableRecord Admission()
        {
            return new RecoverableExecutionDurableRecord
            {
                Owner = Owner,
                ClientId = ClientId,
                Token = ByteString.From(0x74, 0x6f, 0x6b, 0x00, 0xfe),
                Incarnation = Incarnation,
                IssuingSessionId = new NodeId("synthetic-session", 1),
                ServiceId = DataTypeIds.ReadRequest,
                TypeWitness = Witness,
                Request = ByteString.From(0x52, 0x51, 0x00, 0xff),
                DiagnosticsMask = DiagnosticsMasks.ServiceSymbolicId,
                Roles = [new NodeId(101), new NodeId(102)],
                MessageLimit = MessageLimit,
                Mapping = RequestEncoding.Binary,
                Execution = TimeSpan.FromMilliseconds(100),
                Retention = TimeSpan.FromMilliseconds(50)
            };
        }

        internal static RecoverableExecutionDurableRecord Pending()
        {
            return Admission() with
            {
                Phase = RecoverableExecutionCommitPhase.Pending,
                AdmittedAt = At(1000),
                Revision = 2
            };
        }

        internal static RecoverableExecutionDurableRecord Candidate()
        {
            return Pending() with
            {
                Phase = RecoverableExecutionCommitPhase.OutcomePreparing,
                Kind = ServiceOperationOutcome.ServiceResponse,
                Status = StatusCodes.Good,
                Response = Response,
                Permissions =
                [
                    new RecoverableNodePermission(new NodeId(201), PermissionType.Read),
                    new RecoverableNodePermission(new NodeId(202), PermissionType.Write)
                ],
                DispatchStarted = true,
                WorkFinished = true,
                Revision = 4
            };
        }

        internal static RecoverableExecutionDurableRecord Available()
        {
            return Candidate() with
            {
                Phase = RecoverableExecutionCommitPhase.OutcomeAvailable,
                State = ServiceOperationState.OutcomeAvailable,
                OutcomeAt = At(1020),
                Revision = 5
            };
        }

        internal static RecoverableExecutionDurableRecord Released(ServiceOperationState state)
        {
            return Available() with
            {
                Phase = RecoverableExecutionCommitPhase.Released,
                State = state,
                ReleasedAt = At(state == ServiceOperationState.Expired ? 1070 : 1040),
                Request = default,
                Response = default,
                Permissions = default,
                Revision = 6
            };
        }

        /// <summary>
        /// Independently writes the documented local field order, bypassing production Validate for corruption cases.
        /// Forged counts and lengths occupy four bytes; they never determine a test-side allocation.
        /// </summary>
        internal static ByteString Raw(RecoverableExecutionDurableRecord record, RawChanges? changes = null)
        {
            using var stream = new MemoryStream();
            ServiceMessageContext context = Context();
            context.MaxMessageSize = 16384;
            context.MaxByteStringLength = 16384;
            using var writer = new BinaryEncoder(stream, context, leaveOpen: true);
            writer.WriteUInt32(null, changes?.Format ?? 0x52530401);
            writer.WriteByteString(null, changes?.Witness ?? record.TypeWitness);
            if (changes?.StopAfterWitness == true)
            {
                _ = writer.Close();
                return new ByteString(stream.ToArray());
            }
            writer.WriteGuid(null, changes?.AuthorityId ?? record.Owner.Id.AuthorityId);
            writer.WriteGuid(null, changes?.ApplicationId ?? record.Owner.Id.ApplicationId);
            writer.WriteGuid(null, changes?.UserId ?? record.Owner.Id.UserId);
            writer.WriteByteString(null, changes?.OwnerContext ?? record.Owner.ContinuityContext);
            writer.WriteGuid(null, record.ClientId);
            writer.WriteByteString(null, record.Token);
            writer.WriteGuid(null, record.Incarnation);
            writer.WriteNodeId(null, record.IssuingSessionId);
            writer.WriteNodeId(null, record.ServiceId);
            writer.WriteInt32(null, record.MessageLimit);
            writer.WriteInt32(null, (int)record.Mapping);
            writer.WriteInt64(null, record.Execution.Ticks);
            writer.WriteInt64(null, record.Retention.Ticks);
            writer.WriteInt32(null, (int)record.Phase);
            writer.WriteInt32(null, (int)record.State);
            writer.WriteInt32(null, (int)record.Kind);
            writer.WriteStatusCode(null, record.Status);
            WriteTime(writer, "admitted", record.AdmittedAt, changes);
            WriteTime(writer, "outcome", record.OutcomeAt, changes);
            WriteTime(writer, "released", record.ReleasedAt, changes);
            writer.WriteBoolean(null, record.DispatchStarted);
            writer.WriteBoolean(null, record.WorkFinished);
            writer.WriteBoolean(null, record.CancellationRequested);
            writer.WriteUInt64(null, record.Revision);
            writer.WriteUInt32(null, (uint)record.DiagnosticsMask);
            writer.WriteInt32(null, changes?.RoleCount ?? record.Roles.Count);
            foreach (NodeId role in record.Roles)
            {
                writer.WriteNodeId(null, role);
            }
            writer.WriteInt32(null, changes?.PermissionCount ?? record.Permissions.Count);
            foreach (RecoverableNodePermission permission in record.Permissions)
            {
                writer.WriteNodeId(null, permission.NodeId);
                writer.WriteUInt32(null, (uint)permission.Permission);
            }
            if (changes?.RequestLength is int length)
            {
                writer.WriteInt32(null, length);
            }
            else
            {
                writer.WriteByteString(null, record.Request);
            }
            writer.WriteByteString(null, record.Response);
            _ = writer.Close();
            return new ByteString(stream.ToArray());
        }

        private static void WriteTime(
            BinaryEncoder writer, string field, RecoverableExecutionTime time, RawChanges? changes)
        {
            bool replace = changes?.TimeField == field;
            writer.WriteGuid(null, replace ? changes!.TimeDomain ?? time.DomainId : time.DomainId);
            writer.WriteInt64(null, replace ? changes!.TimeTicks ?? time.Ticks : time.Ticks);
        }

        internal sealed class RawChanges
        {
            internal uint? Format { get; set; }
            internal ByteString? Witness { get; set; }
            internal int? RoleCount { get; set; }
            internal int? PermissionCount { get; set; }
            internal int? RequestLength { get; set; }
            internal string? TimeField { get; set; }
            internal Uuid? TimeDomain { get; set; }
            internal long? TimeTicks { get; set; }
            internal bool StopAfterWitness { get; set; }
            internal Uuid? AuthorityId { get; set; }
            internal Uuid? ApplicationId { get; set; }
            internal Uuid? UserId { get; set; }
            internal ByteString? OwnerContext { get; set; }
        }
    }
}
