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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PlasticsRubber;
using static Opc.Ua.GranulateDrying.Client.GranulateDryingClientContext;

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Maintenance identity, status, intervals, attributed reset, and bounded read-only instructions.
    /// </summary>
    public sealed class GranulateDryingMaintenanceClient
    {
        /// <summary>
        /// Opens a selected maintenance category.
        /// </summary>
        internal GranulateDryingMaintenanceClient(GranulateDryingClientContext context, NodeId node)
        {
            m_context = context;
            Proxy = new GDDMaintenanceTypeClient(context.Session, node, context.Telemetry);
        }

        /// <summary>
        /// Gets the generated GDDMaintenanceReset method with its exact attribution arguments.
        /// Use it rather than the inherited parameterless common Reset method.
        /// </summary>
        public GDDMaintenanceTypeClient Proxy { get; }

        /// <summary>
        /// Gets the category identifier.
        /// </summary>
        public GranulateDryingVariable<string> Id =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.String, false, Gdd("Id"));

        /// <summary>
        /// Gets the common maintenance status.
        /// </summary>
        public GranulateDryingVariable<MaintenanceStatusEnumeration> Status =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Enumeration<MaintenanceStatusEnumeration>(),
                false, Common("Status"));

        /// <summary>
        /// Gets the maintenance interval in its declared engineering unit.
        /// </summary>
        public GranulateDryingVariable<double> Interval => Number("Interval");

        /// <summary>
        /// Gets the remaining interval in the same unit.
        /// </summary>
        public GranulateDryingVariable<double> RemainingInterval => Number("RemainingInterval");

        /// <summary>
        /// Gets lifetime operation; maintenance reset does not reset this quantity.
        /// </summary>
        public GranulateDryingVariable<double> TotalOperation => Number("TotalOperation");

        /// <summary>
        /// Gets optional maintenance guidance.
        /// </summary>
        public GranulateDryingVariable<string> AdditionalInformation =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.String, false, Common("AdditionalInformation"));

        /// <summary>
        /// Reads a selected instruction file using generated FileType methods.
        /// An absent file raises BadNotSupported. The session-owned handle is closed on success or cancellation.
        /// </summary>
        public async ValueTask<ByteString> ReadInstructionsAsync(
            int maximumBytes = 16 * 1024 * 1024,
            CancellationToken cancellationToken = default)
        {
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            FileTypeClient file = await Proxy.GetMaintenanceInstructionsAsync(m_context.Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
            uint handle = await file.OpenAsync(1, cancellationToken).ConfigureAwait(false);
            bool readingFailed = false;
            try
            {
                using var content = new MemoryStream();
                while (true)
                {
                    ByteString bytes = await file.ReadAsync(handle, Math.Min(64 * 1024, maximumBytes),
                        cancellationToken).ConfigureAwait(false);
                    if (bytes.IsNull || bytes.Span.Length == 0)
                    {
                        return ByteString.From(content.ToArray());
                    }
                    if (bytes.Span.Length > maximumBytes - content.Length)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    byte[] buffer = bytes.Span.ToArray();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await content.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
                    await content.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#endif
                }
            }
            catch
            {
                readingFailed = true;
                throw;
            }
            finally
            {
                try
                {
                    await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException) when (readingFailed)
                {
                    // A lost session already releases its handles; preserve the original read/cancellation failure.
                }
            }
        }

        /// <summary>
        /// Creates an inherited common maintenance number.
        /// </summary>
        private GranulateDryingVariable<double> Number(string member) =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false, Common(member));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }
}
