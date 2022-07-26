/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with support for operation limits.
    /// </summary>
    public class SessionClientOperationLimits : SessionClient
    {
        #region Constructors
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        public SessionClientOperationLimits(ITransportChannel channel)
        :
            base(channel)
        {
            OperationLimits = new OperationLimits();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The operation limits are used to chunk service requests.
        /// </summary>
        public OperationLimits OperationLimits { get => m_operationLimits; set => m_operationLimits = value; }
        #endregion

        #region Read Methods
        /// <inheritdoc/>
        public override ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            ResponseHeader responseHeader = null;
            results = new DataValueCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToRead.Count > results.Count)
            {
                ReadValueIdCollection chunkAttributesToRead;
                if (OperationLimits.MaxNodesPerRead > 0 &&
                    (nodesToRead.Count - results.Count) > OperationLimits.MaxNodesPerRead)
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerRead));
                }
                else
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Read(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    chunkAttributesToRead,
                    out DataValueCollection chunkValues,
                    out DiagnosticInfoCollection chunkDiagnosticInfos);

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            return responseHeader;
        }

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        public override ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            ResponseHeader responseHeader = null;
            results = new BrowseResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToBrowse.Count > results.Count)
            {
                BrowseDescriptionCollection chunknodesToBrowse;
                if (OperationLimits.MaxNodesPerBrowse > 0 &&
                    (nodesToBrowse.Count - results.Count) > OperationLimits.MaxNodesPerBrowse)
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerBrowse));
                }
                else
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                responseHeader = base.Browse(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    chunknodesToBrowse,
                    out BrowseResultCollection chunkResults,
                    out DiagnosticInfoCollection chunkDiagnosticInfos);

                ClientBase.ValidateResponse(chunkResults, chunknodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunknodesToBrowse);

                results.AddRange(chunkResults);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            return responseHeader;
        }


#if (CLIENT_ASYNC)
        /// <inheritdoc/>
        public override async Task<ReadResponse> ReadAsync(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            ReadResponse response = null;
            DataValueCollection results = new DataValueCollection();
            DiagnosticInfoCollection diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToRead.Count > results.Count)
            {
                ReadValueIdCollection chunkAttributesToRead;
                if (OperationLimits.MaxNodesPerRead > 0 &&
                    (nodesToRead.Count - results.Count) > OperationLimits.MaxNodesPerRead)
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerRead));
                }
                else
                {
                    chunkAttributesToRead = new ReadValueIdCollection(nodesToRead.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    chunkAttributesToRead, ct).ConfigureAwait(false);

                DataValueCollection chunkValues = response.Results;
                DiagnosticInfoCollection chunkDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(chunkValues, chunkAttributesToRead);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunkAttributesToRead);

                results.AddRange(chunkValues);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        /// <summary>
        /// Invokes the Browse service using async Task based request.
        /// </summary>
        public override async Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct)
        {
            BrowseResponse response = null;
            var results = new BrowseResultCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            while (nodesToBrowse.Count > results.Count)
            {
                BrowseDescriptionCollection chunknodesToBrowse;
                if (OperationLimits.MaxNodesPerBrowse > 0 &&
                    (nodesToBrowse.Count - results.Count) > OperationLimits.MaxNodesPerBrowse)
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count).Take((int)OperationLimits.MaxNodesPerBrowse));
                }
                else
                {
                    chunknodesToBrowse = new BrowseDescriptionCollection(nodesToBrowse.Skip(results.Count));
                }

                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                response = await base.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    chunknodesToBrowse, ct).ConfigureAwait(false);

                BrowseResultCollection chunkResults = response.Results;
                DiagnosticInfoCollection chunkDiagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(chunkResults, chunknodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(chunkDiagnosticInfos, chunknodesToBrowse);

                results.AddRange(chunkResults);
                diagnosticInfos.AddRange(chunkDiagnosticInfos);
            }

            response.Results = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }
#endif
        #endregion

        #region Private 
        private OperationLimits m_operationLimits;
        #endregion
    }
}
