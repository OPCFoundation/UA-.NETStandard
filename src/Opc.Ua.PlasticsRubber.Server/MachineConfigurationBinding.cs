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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Commits device configuration and retrieves device-rendered images.
    /// </summary>
    public interface IMachineConfigurationProvider
    {
        /// <summary>
        /// Commits the operator's machine name.
        /// </summary>
        ValueTask SetMachineNameAsync(
            PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the machine's location label.
        /// </summary>
        ValueTask SetLocationNameAsync(
            PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the device's local timezone independently of its UTC clock.
        /// </summary>
        ValueTask SetTimeZoneAsync(
            PlasticsRubberRequestContext context,
            TimeZoneDataType timeZone,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the device's UTC time and timezone together; implementations must not change a shared host clock.
        /// </summary>
        ValueTask SetMachineTimeAsync(
            PlasticsRubberRequestContext context,
            DateTimeUtc utcTime,
            TimeZoneDataType timeZone,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a named device page as BMP, GIF, JPEG or PNG bytes.
        /// </summary>
        ValueTask<ByteString> GetPageAsync(
            PlasticsRubberRequestContext context, string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a visualization unit's current page; zero identifies the default unit.
        /// </summary>
        ValueTask<ByteString> GetCurrentPageAsync(
            PlasticsRubberRequestContext context,
            uint visualizationUnit,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds common machine configuration without substituting host settings for device settings.
    /// </summary>
    public sealed class MachineConfigurationBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the mandatory configuration and selected optional image methods.
        /// </summary>
        public MachineConfigurationBinding(
            MachineConfigurationState node,
            ISystemContext context,
            IMachineConfigurationProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            int maximumPageBytes = 16 * 1024 * 1024)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (maximumPageBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPageBytes));
            }
            if (node.UserMachineName == null || node.LocationName == null ||
                node.TimeZoneOffset == null || node.SetMachineTime == null)
            {
                throw new ArgumentException("Mandatory machine configuration members are missing.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            m_binding.BindWrite(node.UserMachineName, (request, value, ct) =>
                provider.SetMachineNameAsync(request, ReadString(value), ct));
            m_binding.BindWrite(node.LocationName, (request, value, ct) =>
                provider.SetLocationNameAsync(request, ReadString(value), ct));
            m_binding.BindWrite(node.TimeZoneOffset, (request, value, ct) =>
            {
                if (!value.TryGetStructure<TimeZoneDataType>(out TimeZoneDataType? timeZone) || timeZone == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch, "A TimeZoneDataType is required.");
                }
                return provider.SetTimeZoneAsync(request, timeZone, ct);
            });
            node.SetMachineTime.OnCallAsync = async (call, method, target, time, zone, ct) =>
                new SetMachineTimeMethodStateResult
                {
                    ServiceResult = await m_binding.ExecuteAsync(
                        call, target, method.NodeId,
                        async (request, token) =>
                        {
                            if (zone == null)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadInvalidArgument, "A timezone is required.");
                            }
                            await provider.SetMachineTimeAsync(request, time, zone, token).ConfigureAwait(false);
                            node.TimeZoneOffset.Value = zone;
                            node.TimeZoneOffset.ClearChangeMasks(context, false);
                        },
                        ct).ConfigureAwait(false)
                };
            m_binding.Track(node.SetMachineTime);
            if (node.GetPage != null)
            {
                node.GetPage.OnCallAsync = async (call, method, target, id, ct) =>
                {
                    ByteString page = default;
                    ServiceResult result = await m_binding.ExecuteAsync(
                        call, target, method.NodeId,
                        async (request, token) =>
                        {
                            if (string.IsNullOrEmpty(id))
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadInvalidArgument, "A page identifier is required.");
                            }
                            page = ValidatePage(
                                await provider.GetPageAsync(request, id, token).ConfigureAwait(false),
                                maximumPageBytes);
                        },
                        ct).ConfigureAwait(false);
                    return new GetPageMethodStateResult { ServiceResult = result, Page = page };
                };
                m_binding.Track(node.GetPage);
            }
            if (node.GetCurrentPage != null)
            {
                node.GetCurrentPage.OnCallAsync = async (call, method, target, unit, ct) =>
                {
                    ByteString page = default;
                    ServiceResult result = await m_binding.ExecuteAsync(
                        call, target, method.NodeId,
                        async (request, token) =>
                        {
                            page = ValidatePage(
                                await provider.GetCurrentPageAsync(request, unit, token).ConfigureAwait(false),
                                maximumPageBytes);
                        },
                        ct).ConfigureAwait(false);
                    return new GetCurrentPageMethodStateResult { ServiceResult = result, Page = page };
                };
                m_binding.Track(node.GetCurrentPage);
            }
        }

        /// <summary>
        /// Gets the generated common configuration instance.
        /// </summary>
        public MachineConfigurationState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Preserves nullable OPC UA String values without coercing other variants.
        /// </summary>
        private static string? ReadString(Variant value)
        {
            if (value.IsNull)
            {
                return null;
            }
            return value.TryGetValue(out string? text)
                ? text
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A String is required.");
        }

        /// <summary>
        /// Refuses empty images and oversized responses explicitly.
        /// </summary>
        private static ByteString ValidatePage(ByteString page, int maximumBytes)
        {
            if (page.IsNull || page.Span.IsEmpty)
            {
                throw ServiceResultException.Create(StatusCodes.BadNoDataAvailable, "No page image is available.");
            }
            if (page.Span.Length > maximumBytes)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded, "The page exceeds the configured image limit.");
            }
            return page;
        }

        /// <summary>
        /// The authorized operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
