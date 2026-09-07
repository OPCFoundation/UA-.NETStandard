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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Providers
{
    /// <summary>
    /// Shared helpers for the ISA-95 Job Control provider tests.
    /// </summary>
    internal static class Isa95TestData
    {
        public static V1.ISA95JobOrderDataType V1Order(string id)
        {
            return new V1.ISA95JobOrderDataType { ID = id };
        }

        public static V2.ISA95JobOrderDataType V2Order(string id)
        {
            return new V2.ISA95JobOrderDataType { JobOrderID = id };
        }

        public static V1.ISA95JobResponseDataType V1Response(
            string id,
            string orderId,
            V1.ISA95JobOrderStateEnum state = V1.ISA95JobOrderStateEnum.Completed)
        {
            return new V1.ISA95JobResponseDataType
            {
                ID = id,
                JobOrderID = orderId,
                JobState = state
            };
        }

        public static V2.ISA95JobResponseDataType V2Response(
            string id,
            string orderId,
            uint stateNumber = 5,
            string stateText = "Ended")
        {
            return new V2.ISA95JobResponseDataType
            {
                JobResponseID = id,
                JobOrderID = orderId,
                JobState =
                [
                    new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateNumber = stateNumber,
                        StateText = new LocalizedText(stateText)
                    }
                ]
            };
        }

        /// <summary>
        /// Subscribes to the status source, runs the supplied actions, and returns
        /// exactly the expected number of committed status notifications. The
        /// subscription is registered synchronously before the actions run so no
        /// notification is missed.
        /// </summary>
        public static async Task<IReadOnlyList<Isa95JobStatusNotificationV2>> CaptureAsync(
            IIsa95JobStatusSourceV2 source,
            int expected,
            System.Func<Task> actions)
        {
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            IAsyncEnumerator<Isa95JobStatusNotificationV2> enumerator =
                source.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            var results = new List<Isa95JobStatusNotificationV2>();
            try
            {
                ValueTask<bool> pending = enumerator.MoveNextAsync();
                await actions().ConfigureAwait(false);
                for (int index = 0; index < expected; index++)
                {
                    if (!await pending.ConfigureAwait(false))
                    {
                        break;
                    }
                    results.Add(enumerator.Current);
                    pending = index + 1 < expected
                        ? enumerator.MoveNextAsync()
                        : new ValueTask<bool>(false);
                }
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            return results;
        }

        /// <summary>
        /// Subscribes to the catalog-change source, runs the supplied actions, and
        /// returns exactly the expected number of committed catalog changes. The
        /// subscription is registered synchronously before the actions run so no
        /// change is missed.
        /// </summary>
        public static async Task<IReadOnlyList<Isa95JobOrderCatalogChange>> CaptureCatalogAsync(
            IIsa95JobOrderCatalogChangeSource source,
            int expected,
            System.Func<Task> actions)
        {
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            IAsyncEnumerator<Isa95JobOrderCatalogChange> enumerator =
                source.SubscribeCatalogChangesAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            var results = new List<Isa95JobOrderCatalogChange>();
            try
            {
                ValueTask<bool> pending = enumerator.MoveNextAsync();
                await actions().ConfigureAwait(false);
                for (int index = 0; index < expected; index++)
                {
                    if (!await pending.ConfigureAwait(false))
                    {
                        break;
                    }
                    results.Add(enumerator.Current);
                    pending = index + 1 < expected
                        ? enumerator.MoveNextAsync()
                        : new ValueTask<bool>(false);
                }
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            return results;
        }
    }
}
