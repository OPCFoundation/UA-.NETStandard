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

using Opc.Ua.Client;
namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for history read operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class HistoryAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task HistoryReadRawAsync()
        {
            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.MinValue,
                EndTime = DateTime.UtcNow,
                NumValuesPerNode = 10,
                IsReadModified = false,
                ReturnBounds = false
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            ];

            try
            {
                HistoryReadResponse response =
                    await fixture.Session.HistoryReadAsync(
                        null,
                        new ExtensionObject(details),
                        TimestampsToReturn.Source,
                        false,
                        nodesToRead,
                        CancellationToken.None).ConfigureAwait(false);

                await Assert.That(response.Results.Count)
                    .IsEqualTo(nodesToRead.Count);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported
                    || ex.StatusCode == StatusCodes.BadHistoryOperationInvalid
                    || ex.StatusCode == StatusCodes.BadNotSupported)
            {
                // Server does not support history — test passes
            }
        }

        [Test]
        public async Task HistoryReadProcessedAsync()
        {
            var details = new ReadProcessedDetails
            {
                StartTime = DateTime.MinValue,
                EndTime = DateTime.UtcNow,
                ProcessingInterval = 1000,
                AggregateType = [new NodeId(2342)] // Average aggregate
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            ];

            try
            {
                HistoryReadResponse response =
                    await fixture.Session.HistoryReadAsync(
                        null,
                        new ExtensionObject(details),
                        TimestampsToReturn.Source,
                        false,
                        nodesToRead,
                        CancellationToken.None).ConfigureAwait(false);

                await Assert.That(response.Results.Count)
                    .IsEqualTo(nodesToRead.Count);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported
                    || ex.StatusCode == StatusCodes.BadHistoryOperationInvalid
                    || ex.StatusCode == StatusCodes.BadNotSupported
                    || ex.StatusCode == StatusCodes.BadAggregateNotSupported)
            {
                // Server does not support history — test passes
            }
        }
    }
}
