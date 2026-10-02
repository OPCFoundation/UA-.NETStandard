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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    [TestFixture]
    public sealed class VisionSensorReadQualityTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CalibrationStatusCannotBeReplacedByItsPayloadOrADefault(bool extrinsic, bool uncertain)
        {
            var harness = new VisionSessionHarness();
            NodeId target = extrinsic ? harness.ExtrinsicCalibrationNodeId : harness.IntrinsicCalibrationNodeId;
            var member = new NodeId(2600u, 3);
            harness.AddValueChild(target, BrowseNames.Valid, member, Variant.From(true));
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied;
            harness.Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                    CancellationToken _) =>
                {
                    Assert.That(reads, Has.Count.EqualTo(1));
                    Assert.That(reads[0].NodeId, Is.EqualTo(member));
                    return new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new DataValue(Variant.From(true)).WithStatus(status)]
                    });
                });
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            if (extrinsic)
            {
                await Assert.ThatAsync(() => sensor.ReadExtrinsicCalibrationAsync(target),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                    .ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => sensor.ReadIntrinsicCalibrationAsync(target),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ExplicitFalseCalibrationRemainsFalseAndMissingOptionalIdentityStaysAbsent()
        {
            var harness = new VisionSessionHarness();
            var member = new NodeId(2600u, 3);
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.Valid, member, Variant.From(false));
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            VisionIntrinsicCalibrationSnapshot result = await sensor.ReadIntrinsicCalibrationAsync(
                harness.IntrinsicCalibrationNodeId).ConfigureAwait(false);

            Assert.That(result.Valid, Is.False);
            Assert.That(result.NodeId, Is.EqualTo(harness.IntrinsicCalibrationNodeId));
            Assert.That(result.CalibrationId, Is.Null);
        }

        [Test]
        public async Task DeniedSensorIdentityIsNotAnAnonymousSuccessfulSnapshot()
        {
            var harness = new VisionSessionHarness();
            var member = new NodeId(2600u, 3);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.SensorId, member, Variant.From("camera"));
            harness.AddValueStatus(member, StatusCodes.BadUserAccessDenied);

            await Assert.ThatAsync(() => harness.Client.Sensor(harness.SensorNodeId).ReadIdentityAsync(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
        }
    }
}
