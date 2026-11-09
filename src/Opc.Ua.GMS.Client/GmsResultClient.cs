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
using Opc.Ua.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Bounded GMS result workflows over a caller-owned session and generated standard method proxies.
    /// </summary>
    public sealed partial class GmsResultClient
    {
        /// <summary>
        /// Creates a result client for one actual GMSType instance without owning or replacing the session.
        /// </summary>
        public GmsResultClient(
            ISession session,
            NodeId gmsNodeId,
            ITelemetryContext telemetry,
            GmsResultLimits? limits = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (gmsNodeId.IsNull)
            {
                throw new ArgumentException("A GMS instance is required.", nameof(gmsNodeId));
            }
            m_limits = limits ?? new GmsResultLimits();
            m_limits.Validate();
            session.MessageContext.Factory.Builder.AddGmsTypeSystem().Commit();
            Root = new GMSTypeClient(session, gmsNodeId, telemetry);
        }

        /// <summary>
        /// Gets the caller-owned session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the generated GMS proxy for advanced companion access.
        /// </summary>
        public GMSTypeClient Root { get; }

        /// <summary>
        /// Reads the latest result and releases only the metadata handle acquired by this call.
        /// </summary>
        public async ValueTask<ResultDataType> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            (uint handle, ResultDataType result, int error) =
                await management.GetLatestResultAsync(0, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(management, handle, () =>
            {
                ThrowIfDomainError(error, "GetLatestResult");
                return ValidateResult(result);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads an identified result and releases its metadata handle before returning a detached envelope.
        /// </summary>
        public async ValueTask<ResultDataType> GetByIdAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            RequireIdentifier(resultId);
            cancellationToken.ThrowIfCancellationRequested();
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            (uint handle, ResultDataType result, int error) =
                await management.GetResultByIdAsync(resultId, 0, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(management, handle, () =>
            {
                ThrowIfDomainError(error, "GetResultById");
                if (!string.Equals(result?.ResultMetaData?.ResultId, resultId, StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError, "The server returned a different result identifier.");
                }
                return ValidateResult(result);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Queries result identifiers using the shared Machinery filter model and releases the query handle.
        /// </summary>
        public async ValueTask<ArrayOf<string>> QueryAsync(
            int maximumResults,
            ContentFilter? filter = null,
            ArrayOf<RelativePath> orderedBy = default,
            CancellationToken cancellationToken = default)
        {
            if (maximumResults < 1 || maximumResults > m_limits.MaximumResults)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResults));
            }
            cancellationToken.ThrowIfCancellationRequested();
            filter ??= new ContentFilter();
            m_limits.Measure(Variant.FromStructure(filter), Session.MessageContext);
            m_limits.Measure(Variant.FromStructure(orderedBy), Session.MessageContext);
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            (uint handle, ArrayOf<string> ids, int error) = await management.GetResultIdListFilteredAsync(
                filter, orderedBy, (uint)maximumResults, 0, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(management, handle, () =>
            {
                ThrowIfDomainError(error, "GetResultIdListFiltered");
                if (ids.Count > maximumResults)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded, "The server exceeded the requested result count.");
                }
                m_limits.Measure(Variant.From(ids), Session.MessageContext);
                return ids.IsNull ? ArrayOf<string>.Empty : new ArrayOf<string>(ids.ToArray()!);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acknowledges bounded identifiers, retaining one unmodified domain result per identifier.
        /// </summary>
        public async ValueTask<ArrayOf<int>> AcknowledgeAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default)
        {
            if (resultIds.IsNull || resultIds.IsEmpty || resultIds.Count > m_limits.MaximumResults)
            {
                throw new ArgumentOutOfRangeException(nameof(resultIds));
            }
            m_limits.Measure(Variant.From(resultIds), Session.MessageContext);
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            (ArrayOf<int> errors, int error) =
                await management.AcknowledgeResultsAsync(resultIds, cancellationToken).ConfigureAwait(false);
            ThrowIfDomainError(error, "AcknowledgeResults");
            if (errors.Count != resultIds.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError, "Acknowledgment returned an invalid per-result status count.");
            }
            return errors;
        }

        /// <summary>
        /// Opens the generated proxy on the actual GMS-qualified result-management child.
        /// </summary>
        private async ValueTask<GMSResultManagementTypeClient> ResultsAsync(CancellationToken cancellationToken)
        {
            return await Root.GetResultManagementAsync(m_telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported, "The GMS instance does not expose required result management.");
        }

        /// <summary>
        /// Validates successful metadata without inventing acquisition quality for a method response.
        /// </summary>
        private ResultDataType ValidateResult(ResultDataType? result)
        {
            if (result?.ResultMetaData == null || string.IsNullOrWhiteSpace(result.ResultMetaData.ResultId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError, "A successful result response requires an identified result.");
            }
            m_limits.Measure(Variant.FromStructure(result), Session.MessageContext);
            return (ResultDataType)result.Clone();
        }

        /// <summary>
        /// Projects a response and releases its known handle independently of caller cancellation.
        /// </summary>
        /// <typeparam name="T">The detached response type.</typeparam>
        private static async ValueTask<T> CompleteAsync<T>(
            GMSResultManagementTypeClient management,
            uint handle,
            Func<T> project,
            CancellationToken cancellationToken)
        {
            Exception? failure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return project();
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                await ReleaseAsync(management, handle, failure).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases exactly the supplied owned handle and retains both errors if projection and cleanup fail.
        /// </summary>
        private static async ValueTask ReleaseAsync(
            GMSResultManagementTypeClient management,
            uint handle,
            Exception? failure)
        {
            if (handle == 0)
            {
                return;
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                ThrowIfDomainError(
                    await management.ReleaseResultHandleAsync(handle, cleanup.Token).ConfigureAwait(false),
                    "ReleaseResultHandle");
            }
            catch (Exception error) when (failure != null)
            {
                throw new AggregateException("The GMS result operation and metadata release both failed.", failure, error);
            }
        }

        /// <summary>
        /// Keeps domain errors distinct from UA service and operation status failures.
        /// </summary>
        private static void ThrowIfDomainError(int error, string operation)
        {
            if (error != 0)
            {
                throw new GmsResultDomainException(operation, error);
            }
        }

        /// <summary>
        /// Rejects unspecified result identities before using the session.
        /// </summary>
        private static void RequireIdentifier(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
            {
                throw new ArgumentException("A result identifier is required.", nameof(resultId));
            }
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly GmsResultLimits m_limits;
    }
}
