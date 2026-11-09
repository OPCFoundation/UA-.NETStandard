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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Tests;
using DiNames = Opc.Ua.Di.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Verifies strict reads against adversarial protocol responses while preserving legacy defaults.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Defaults are finite and every limit rejects zero and negative values.
        /// </summary>
        [TestCase(0, 128, 1024, "MaximumBrowseReferences")]
        [TestCase(-1, 128, 1024, "MaximumBrowseReferences")]
        [TestCase(1024, 0, 1024, "MaximumBrowsePages")]
        [TestCase(1024, -1, 1024, "MaximumBrowsePages")]
        [TestCase(1024, 128, 0, "MaximumReadArrayItems")]
        [TestCase(1024, 128, -1, "MaximumReadArrayItems")]
        public void StrictOptionsRejectNonPositiveLimits(int references, int pages, int items, string parameter)
        {
            var defaults = new MachineryClientOptions();
            Assert.That(defaults.MaximumBrowseReferences, Is.EqualTo(1024));
            Assert.That(defaults.MaximumBrowsePages, Is.EqualTo(128));
            Assert.That(defaults.MaximumReadArrayItems, Is.EqualTo(1024));
            var harness = new ReadHarness();
            var options = new MachineryClientOptions
            {
                MaximumBrowseReferences = references,
                MaximumBrowsePages = pages,
                MaximumReadArrayItems = items
            };
            ArgumentOutOfRangeException? error = Assert.Throws<ArgumentOutOfRangeException>(
                () => harness.Client(options));
            Assert.That(error!.ParamName, Is.EqualTo(parameter));
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(() => new MachineryClientFactory(
                    _ => Task.FromResult<ManagedSession>(null!), NUnitTelemetryContext.Create(), options))!.ParamName,
                Is.EqualTo(parameter));
        }

        /// <summary>
        /// Direct and fluent construction reject null options instead of silently selecting legacy mode.
        /// </summary>
        [Test]
        public void StrictConstructionRejectsNullOptions()
        {
            var harness = new ReadHarness();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => new MachineryClient(harness.Session.Object, telemetry, null!))!.ParamName,
                Is.EqualTo("options"));
            Assert.Throws<ArgumentNullException>(() => harness.Session.Object.Machinery(telemetry, null!));
            Assert.Throws<ArgumentNullException>(() =>
                new MachineryClientFactory(_ => Task.FromResult<ManagedSession>(null!), telemetry, null!));
        }

        /// <summary>
        /// DI registers the configured factory and validates bounds during registration.
        /// </summary>
        [Test]
        public async Task StrictOptionsFlowThroughFluentAndDependencyInjectionConstruction()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "StrictMachineryReads";
                options.ApplicationUri = "urn:localhost:strict-machinery-reads";
            });
            var limits = new MachineryClientOptions { MaximumBrowseReferences = 1 };
            Assert.That(builder.AddMachineryClient(limits), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<MachineryClientFactory>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<MachineryClient>>>(), Is.Not.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                builder.AddMachineryClient(limits with { MaximumBrowsePages = 0 }));
            var harness = new ReadHarness();
            MachineryClient client = harness.Session.Object.Machinery(NUnitTelemetryContext.Create(), limits);
            Assert.That(client.Session, Is.SameAs(harness.Session.Object));
            harness.Browse = _ => Page([Reference(harness, 1), Reference(harness, 2)]);
            Assert.That(
                (await Assert.ThrowsAsync<MachineryReadException>(async () =>
                    await client.DiscoverMachinesAsync().ConfigureAwait(false)))!.StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Original quality, both timestamps and picoseconds survive alongside Good-null and absence.
        /// </summary>
        [Test]
        public async Task StrictCounterReadRetainsGoodNullAbsenceAndSourceTime()
        {
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            DataValue original = TimedValue(Variant.From(12.5), StatusCodes.GoodClamped);
            NodeId power = harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], original);
            NodeId operation = harness.Add(
                counters, [Di(harness, DiNames.OperationDuration)], new DataValue(Variant.Null));

            MachineryOperationCounters? result = await harness.Client().ReadOperationCountersAsync(harness.Machine)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.PowerOnDuration, Is.EqualTo(12.5));
            Assert.That(result.OperationDuration, Is.Null);
            Assert.That(result.Values.Count, Is.EqualTo(3));
            MachineryReadValue powerField = Field(result.Values, Di(harness, DiNames.PowerOnDuration));
            Assert.That(powerField.SourceNodeId, Is.EqualTo(power));
            Assert.That(powerField.StartingNodeId, Is.EqualTo(counters));
            Assert.That(powerField.Value, Is.EqualTo(original));
            Assert.That(powerField.Value.SourcePicoseconds, Is.EqualTo(12));
            Assert.That(powerField.Value.ServerPicoseconds, Is.EqualTo(34));
            MachineryReadValue goodNull = Field(result.Values, Di(harness, DiNames.OperationDuration));
            Assert.That(goodNull.SourceNodeId, Is.EqualTo(operation));
            Assert.That(goodNull.IsAbsent, Is.False);
            Assert.That(goodNull.Value.WrappedValue.IsNull, Is.True);
            Assert.That(goodNull.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            MachineryReadValue absent = Field(result.Values, Di(harness, DiNames.OperationCycleCounter));
            Assert.That(absent.SourceNodeId.IsNull, Is.True);
            Assert.That(absent.IsAbsent, Is.True);
            Assert.That(absent.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(harness.ReadTimestamps.ToArray(), Is.EqualTo([TimestampsToReturn.Both]));
            harness.Session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Identification retains all requested fields while preserving typed convenience properties.
        /// </summary>
        [Test]
        public async Task StrictIdentificationRetainsAllFieldEvidence()
        {
            var harness = new ReadHarness();
            NodeId identification = harness.Add(harness.Machine, [Di(harness, DiNames.Identification)]);
            DataValue manufacturer = TimedValue(Variant.From(new LocalizedText("en", "Acme")), StatusCodes.Good);
            harness.Add(identification, [Di(harness, DiNames.Manufacturer)], manufacturer);
            harness.Add(identification, [Di(harness, DiNames.SerialNumber)], new DataValue(Variant.From("SN-1")));
            harness.Add(identification, [Di(harness, DiNames.Model)], new DataValue(Variant.Null));
            harness.Add(identification, [Machinery(harness, BrowseNames.YearOfConstruction)],
                new DataValue(Variant.From((ushort)2026)));
            MachineIdentification? result = await harness.Client().ReadIdentificationAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(result!.Manufacturer.Text, Is.EqualTo("Acme"));
            Assert.That(result.SerialNumber, Is.EqualTo("SN-1"));
            Assert.That(result.YearOfConstruction, Is.EqualTo(2026));
            Assert.That(result.Values.Count, Is.EqualTo(16));
            Assert.That(Field(result.Values, Di(harness, DiNames.Manufacturer)).Value, Is.EqualTo(manufacturer));
            Assert.That(Field(result.Values, Di(harness, DiNames.Model)).IsAbsent, Is.False);
            Assert.That(Field(result.Values, Di(harness, DiNames.AssetId)).IsAbsent, Is.True);
        }

        /// <summary>
        /// Missing mandatory fields and wrongly typed strings do not masquerade as optional absence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StrictIdentificationRejectsMissingOrWrongRequiredFields(bool wrongType)
        {
            var harness = new ReadHarness();
            NodeId identification = harness.Add(harness.Machine, [Di(harness, DiNames.Identification)]);
            harness.Add(identification, [Di(harness, DiNames.SerialNumber)], new DataValue(Variant.From("SN-1")));
            if (wrongType)
            {
                harness.Add(
                    identification, [Di(harness, DiNames.Manufacturer)], new DataValue(Variant.From("not text")));
            }
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadIdentificationAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode,
                Is.EqualTo(wrongType ? StatusCodes.BadTypeMismatch : StatusCodes.BadNoMatch));
            Assert.That(error.ReadValue!.BrowsePath[^1], Is.EqualTo(Di(harness, DiNames.Manufacturer)));
        }

        /// <summary>
        /// Non-Good value quality is returned as the actual exception status with its source payload.
        /// </summary>
        [TestCase(nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase(nameof(StatusCodes.BadNotReadable))]
        [TestCase(nameof(StatusCodes.Uncertain))]
        [TestCase(nameof(StatusCodes.BadNoMatch))]
        public async Task StrictReadPreservesDeniedAndUncertainQuality(string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            DataValue original = TimedValue(Variant.From(123.5), status);
            NodeId source = harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], original);
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
            Assert.That(error.ReadValue.IsAbsent, Is.False);
        }

        /// <summary>
        /// Wire types are validated before tolerant scalar projections can erase them.
        /// </summary>
        [TestCase("String")]
        [TestCase("Float")]
        [TestCase("StatusCode")]
        [TestCase("Array")]
        public async Task StrictTypeMismatchPreservesOriginalGoodValue(string kind)
        {
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            Variant value = kind switch
            {
                "String" => Variant.From("12"),
                "Float" => Variant.From(12.0f),
                "StatusCode" => Variant.From(new StatusCode(12)),
                _ => Variant.From([12.0])
            };
            DataValue original = TimedValue(value, StatusCodes.Good);
            NodeId source = harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], original);
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// A failed translation is not an absent optional block.
        /// </summary>
        [TestCase(nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase(nameof(StatusCodes.BadNodeIdUnknown))]
        [TestCase(nameof(StatusCodes.Uncertain))]
        public async Task StrictTranslationOnlyTreatsBadNoMatchAsAbsence(string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness
            {
                Translate = _ => new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowsePathResult { StatusCode = status }]
                }
            };
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ResolveMonitoringAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.StartingNodeId, Is.EqualTo(harness.Machine));
            Assert.That(error.ReadValue.Value.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue.IsAbsent, Is.False);
            Assert.That(harness.ReadTimestamps, Is.Empty);
        }

        /// <summary>
        /// Translation response cardinality and target locality/completeness are exact.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("MissingResponse", nameof(StatusCodes.BadDecodingError))]
        [TestCase("EmptyResults", nameof(StatusCodes.BadDecodingError))]
        [TestCase("ExtraResults", nameof(StatusCodes.BadDecodingError))]
        [TestCase("Diagnostics", nameof(StatusCodes.BadDecodingError))]
        [TestCase("NullResult", nameof(StatusCodes.BadDecodingError))]
        [TestCase("NoTarget", nameof(StatusCodes.BadDecodingError))]
        [TestCase("Ambiguous", nameof(StatusCodes.BadDecodingError))]
        [TestCase("NullTarget", nameof(StatusCodes.BadDecodingError))]
        [TestCase("Incomplete", nameof(StatusCodes.BadDecodingError))]
        [TestCase("Remote", nameof(StatusCodes.BadNotSupported))]
        [TestCase("UnknownUri", nameof(StatusCodes.BadNodeIdUnknown))]
        [TestCase("UnknownIndex", nameof(StatusCodes.BadNodeIdUnknown))]
        [TestCase("NullNode", nameof(StatusCodes.BadNodeIdUnknown))]
        [TestCase("NoMatchWithTarget", nameof(StatusCodes.BadDecodingError))]
        public async Task StrictTranslationRejectsMalformedOrForeignTargets(string kind, string statusName)
        {
            StatusCode expectedStatus = TestStatus(statusName);
            var harness = new ReadHarness();
            string[] namespaces = harness.Namespaces.ToArray();
            BrowsePathResult result = Target(harness.Machine);
            var response = new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [result]
            };
            switch (kind)
            {
                case "MissingResponse":
                    break;
                case "EmptyResults":
                    response.Results = [];
                    break;
                case "ExtraResults":
                    response.Results = [result, result];
                    break;
                case "Diagnostics":
                    response.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
                    break;
                case "NullResult":
                    response.Results = [null!];
                    break;
                case "NoTarget":
                    result.Targets = [];
                    break;
                case "Ambiguous":
                    result.Targets = [result.Targets[0], result.Targets[0]];
                    break;
                case "NullTarget":
                    result.Targets = [null!];
                    break;
                case "Incomplete":
                    result.Targets[0].RemainingPathIndex = 0;
                    break;
                case "Remote":
                    result.Targets[0].TargetId = new ExpandedNodeId(5u, "urn:machinery:strict-tests", 2);
                    break;
                case "UnknownUri":
                    result.Targets[0].TargetId = new ExpandedNodeId(5u, "urn:foreign:unpublished");
                    break;
                case "UnknownIndex":
                    result.Targets[0].TargetId = new NodeId(5u, ushort.MaxValue);
                    break;
                case "NullNode":
                    result.Targets[0].TargetId = ExpandedNodeId.Null;
                    break;
                case "NoMatchWithTarget":
                    result.StatusCode = StatusCodes.BadNoMatch;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            harness.Translate = _ => kind == "MissingResponse" ? null! : response;
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ResolveMonitoringAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(error.ReadValue!.StartingNodeId, Is.EqualTo(harness.Machine));
            Assert.That(harness.Namespaces.ToArray(), Is.EqualTo(namespaces));
            Assert.That(harness.ReadTimestamps, Is.Empty);
        }

        /// <summary>
        /// Local namespace-URI targets resolve without growing the session namespace table.
        /// </summary>
        [Test]
        public async Task StrictTranslationAcceptsOneCompleteLocalUriTarget()
        {
            var harness = new ReadHarness();
            string[] namespaces = harness.Namespaces.ToArray();
            harness.Translate = _ => new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowsePathResult
                    {
                        Targets =
                        [
                            new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId(123u, "urn:machinery:strict-tests"),
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                    }
                ]
            };
            NodeId result = await harness.Client().ResolveMonitoringAsync(harness.Machine).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(new NodeId(123u, harness.Machine.NamespaceIndex)));
            Assert.That(harness.Namespaces.ToArray(), Is.EqualTo(namespaces));
        }

        /// <summary>
        /// Read batch cardinality, diagnostics and service quality are all validated.
        /// </summary>
        [TestCase("MissingResponse", nameof(StatusCodes.BadDecodingError))]
        [TestCase("MissingValue", nameof(StatusCodes.BadDecodingError))]
        [TestCase("ExtraValue", nameof(StatusCodes.BadDecodingError))]
        [TestCase("Diagnostics", nameof(StatusCodes.BadDecodingError))]
        [TestCase("DeniedService", nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase("UncertainService", nameof(StatusCodes.Uncertain))]
        [TestCase("NoMatchService", nameof(StatusCodes.BadNoMatch))]
        public async Task StrictReadRejectsIncompleteOrFailedBatches(string kind, string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            NodeId source = harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(12.0));
            harness.Read = (nodes, _) =>
            {
                ReadResponse response = harness.ReadNodes(nodes);
                switch (kind)
                {
                    case "MissingResponse":
                        break;
                    case "MissingValue":
                        response.Results = [];
                        break;
                    case "ExtraValue":
                        response.Results = [new DataValue(12.0), new DataValue(13.0)];
                        break;
                    case "Diagnostics":
                        response.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
                        break;
                    default:
                        response.ResponseHeader.ServiceResult = status;
                        break;
                }
                return Task.FromResult(kind == "MissingResponse" ? null! : response);
            };
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
        }

        /// <summary>
        /// The existing constructor still tolerates bad paths and excludes non-Good values.
        /// </summary>
        [Test]
        public async Task LegacyDefaultRetainsTolerantReadBehavior()
        {
            var harness = new ReadHarness();
            MachineryClient legacy = harness.Client(strict: false);
            harness.Translate = _ => new TranslateBrowsePathsToNodeIdsResponse
            {
                Results = [new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied }]
            };
            Assert.That(await legacy.ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false), Is.Null);
            harness.Translate = harness.TranslatePaths;
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)],
                TimedValue(Variant.From(12.0), StatusCodes.Uncertain));
            harness.Add(counters, [Di(harness, DiNames.OperationDuration)], new DataValue(Variant.From(7.5f)));
            MachineryOperationCounters? result = await legacy.ReadOperationCountersAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(result!.PowerOnDuration, Is.Null);
            Assert.That(result.OperationDuration, Is.EqualTo(7.5));
            Assert.That(result.Values.Count, Is.Zero);
            Assert.That(harness.ReadTimestamps.ToArray(), Is.EqualTo([TimestampsToReturn.Neither]));
        }

        /// <summary>
        /// Concurrent operations retain separate values and source evidence on one client.
        /// </summary>
        [Test]
        public async Task OverlappingReadsKeepIndependentEvidence()
        {
            var harness = new ReadHarness();
            var secondMachine = new NodeId("second", harness.Machine.NamespaceIndex);
            NodeId firstCounters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            NodeId secondCounters = harness.Add(secondMachine, [Di(harness, DiNames.OperationCounters)]);
            NodeId firstSource = harness.Add(
                firstCounters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(12.0));
            NodeId secondSource = harness.Add(
                secondCounters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(34.0));
            var firstRead = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Read = (nodes, _) => nodes[0].NodeId == firstSource
                ? firstRead.Task
                : Task.FromResult(harness.ReadNodes(nodes));
            MachineryClient client = harness.Client();
            Task<MachineryOperationCounters?> first = client.ReadOperationCountersAsync(harness.Machine).AsTask();
            MachineryOperationCounters? second;
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                second = await client.ReadOperationCountersAsync(secondMachine).ConfigureAwait(false);
            }
            finally
            {
                firstRead.TrySetResult(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(12.0)]
                });
            }
            MachineryOperationCounters? completedFirst = await first.ConfigureAwait(false);
            Assert.That(completedFirst!.PowerOnDuration, Is.EqualTo(12.0));
            Assert.That(completedFirst.Values[0].SourceNodeId, Is.EqualTo(firstSource));
            Assert.That(second!.PowerOnDuration, Is.EqualTo(34.0));
            Assert.That(second.Values[0].SourceNodeId, Is.EqualTo(secondSource));
            Assert.That(second.Values[0].StartingNodeId, Is.EqualTo(secondCounters));
        }

        /// <summary>
        /// A rejected service call retains its status, original cause and attempted source.
        /// </summary>
        [TestCase("Read")]
        [TestCase("Translate")]
        [TestCase("Browse")]
        public async Task StrictServiceExceptionsKeepAttemptedSource(string operation)
        {
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            NodeId source = harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(1.0));
            var cause = new ServiceResultException(StatusCodes.BadCommunicationError);
            if (operation == "Read")
            {
                harness.Read = (_, _) => Task.FromException<ReadResponse>(cause);
            }
            else if (operation == "Translate")
            {
                harness.Translate = _ => throw cause;
            }
            else
            {
                harness.Browse = _ => throw cause;
            }
            MachineryClient client = harness.Client();
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(async () =>
            {
                if (operation == "Browse")
                {
                    await client.DiscoverMachinesAsync().ConfigureAwait(false);
                }
                else
                {
                    await client.ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(error.InnerException, Is.SameAs(cause));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(operation == "Read" ? source : NodeId.Null));
            Assert.That(error.ReadValue.StartingNodeId,
                Is.EqualTo(operation == "Read" ? counters :
                    operation == "Browse" ? client.MachinesFolderId : harness.Machine));
        }

        /// <summary>
        /// A short read response identifies the first missing value rather than blaming a successful one.
        /// </summary>
        [Test]
        public async Task PartialReadBatchIdentifiesTheFirstMissingSource()
        {
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(1.0));
            NodeId missing = harness.Add(counters, [Di(harness, DiNames.OperationDuration)], new DataValue(2.0));
            harness.Read = (_, _) => Task.FromResult(new ReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new DataValue(1.0)]
            });
            MachineryReadException? error = await Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(missing));
            Assert.That(error.ReadValue.BrowsePath[^1], Is.EqualTo(Di(harness, DiNames.OperationDuration)));
        }

        /// <summary>
        /// Legacy clients still select the first translation target and accept Float durations.
        /// </summary>
        [Test]
        public async Task LegacyTranslationRetainsFirstTargetCompatibility()
        {
            var harness = new ReadHarness();
            NodeId counters = harness.Add(harness.Machine, [Di(harness, DiNames.OperationCounters)]);
            harness.Add(counters, [Di(harness, DiNames.PowerOnDuration)], new DataValue(Variant.From(6.0f)));
            harness.Translate = paths =>
            {
                TranslateBrowsePathsToNodeIdsResponse response = harness.TranslatePaths(paths);
                foreach (BrowsePathResult result in response.Results)
                {
                    if (result.Targets.Count != 0)
                    {
                        result.Targets[0].RemainingPathIndex = 0;
                        result.Targets = [result.Targets[0], new BrowsePathTarget { TargetId = new NodeId(999u) }];
                    }
                }
                return response;
            };
            MachineryOperationCounters? result = await harness.Client(strict: false)
                .ReadOperationCountersAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(result!.PowerOnDuration, Is.EqualTo(6.0));
            Assert.That(result.Values.Count, Is.Zero);
        }
    }
}
