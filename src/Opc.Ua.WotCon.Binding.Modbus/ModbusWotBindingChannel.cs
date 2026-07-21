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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding.Modbus
{
    /// <summary>
    /// A live Modbus TCP binding channel. It reads coils / discrete inputs /
    /// holding / input registers and writes coils and holding registers with the
    /// data type and byte / word order compiled from the form, mapping Modbus
    /// exceptions and timeouts to OPC UA status codes.
    /// </summary>
    internal sealed class ModbusWotBindingChannel : IWotBindingChannel
    {
        public ModbusWotBindingChannel(
            ModbusTcpClient client,
            WotCompiledForm form,
            WotExecutorContext context,
            ModbusWotBindingOptions options)
        {
            m_client = client;
            m_form = form;
            m_context = context;
            m_options = options;

            ImmutableDictionary<string, string> address = form.Addressing.Metadata;
            m_entity = Get(address, "entity", "holdingRegister");
            m_address = (ushort)ParseInt(address, "address", 0);
            m_quantity = (ushort)Math.Max(1, ParseInt(address, "quantity", 1));
            m_unitId = (byte)ParseInt(address, "unitId", 1);
            m_type = Get(form.Payload.Metadata, "type", "uint16");
            m_msbFirst = ParseBool(form.Payload.Metadata, "mostSignificantByte", true);
            m_mswFirst = ParseBool(form.Payload.Metadata, "mostSignificantWord", true);
        }

        public WotCompiledForm Form => m_form;

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Variant value;
                if (IsCoil())
                {
                    bool[] bits = await m_client
                        .ReadCoilsAsync(m_unitId, m_address, 1, cancellationToken).ConfigureAwait(false);
                    value = new Variant(bits.Length > 0 && bits[0]);
                }
                else if (IsDiscreteInput())
                {
                    bool[] bits = await m_client
                        .ReadDiscreteInputsAsync(m_unitId, m_address, 1, cancellationToken).ConfigureAwait(false);
                    value = new Variant(bits.Length > 0 && bits[0]);
                }
                else if (IsInputRegister())
                {
                    ushort[] regs = await m_client
                        .ReadInputRegistersAsync(m_unitId, m_address, m_quantity, cancellationToken).ConfigureAwait(false);
                    value = ModbusDataConverter.ToVariant(regs, m_type, m_msbFirst, m_mswFirst);
                }
                else
                {
                    ushort[] regs = await m_client
                        .ReadHoldingRegistersAsync(m_unitId, m_address, m_quantity, cancellationToken).ConfigureAwait(false);
                    value = ModbusDataConverter.ToVariant(regs, m_type, m_msbFirst, m_mswFirst);
                }
                return new WotReadResult(
                    StatusCodes.Good, new DataValue(value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now));
            }
            catch (ModbusException ex)
            {
                StatusCode status = ModbusStatusMapper.Map(ex);
                return new WotReadResult(status, DataValue.FromStatusCode(status), ex.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new WotReadResult(
                    StatusCodes.BadTimeout, DataValue.FromStatusCode(StatusCodes.BadTimeout), "The Modbus request timed out.");
            }
            catch (System.IO.IOException ex)
            {
                return new WotReadResult(
                    StatusCodes.BadCommunicationError,
                    DataValue.FromStatusCode(StatusCodes.BadCommunicationError), ex.Message);
            }
        }

        public async ValueTask<WotWriteResult> WriteAsync(
            DataValue value, CancellationToken cancellationToken = default)
        {
            if (IsDiscreteInput() || IsInputRegister())
            {
                return new WotWriteResult(StatusCodes.BadNotWritable, "The Modbus entity is read-only.");
            }
            try
            {
                if (IsCoil())
                {
                    bool on = Convert.ToBoolean(value.WrappedValue.AsBoxedObject() ?? false, CultureInfo.InvariantCulture);
                    await m_client.WriteSingleCoilAsync(m_unitId, m_address, on, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    ushort[] registers = ModbusDataConverter.ToRegisters(value.WrappedValue, m_type, m_msbFirst, m_mswFirst);
                    if (registers.Length == 1)
                    {
                        await m_client
                            .WriteSingleRegisterAsync(m_unitId, m_address, registers[0], cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await m_client
                            .WriteMultipleRegistersAsync(m_unitId, m_address, registers, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                return new WotWriteResult(StatusCodes.Good);
            }
            catch (ModbusException ex)
            {
                return new WotWriteResult(ModbusStatusMapper.Map(ex), ex.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new WotWriteResult(StatusCodes.BadTimeout, "The Modbus request timed out.");
            }
            catch (System.IO.IOException ex)
            {
                return new WotWriteResult(StatusCodes.BadCommunicationError, ex.Message);
            }
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
            => new ValueTask<WotInvokeResult>(new WotInvokeResult(
                StatusCodes.BadNotSupported, null, "Modbus does not support action invocation."));

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller, who disposes it.")]
        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            var subscription = new PollingWotSubscription(
                m_form,
                async token =>
                {
                    WotReadResult result = await ReadAsync(token).ConfigureAwait(false);
                    if (result.Success)
                    {
                        onNotification(new WotNotification(result.Value));
                    }
                },
                m_options.ObserveInterval,
                // A transient poll fault is reported as a Bad-status notification
                // so consumers observe the fault without the poll loop faulting.
                onError: _ => onNotification(new WotNotification(
                    DataValue.FromStatusCode(StatusCodes.BadCommunicationError))));
            return new ValueTask<IWotSubscription>(subscription);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            => ObserveAsync(onEvent, cancellationToken);

        public ValueTask DisposeAsync()
        {
            m_client.Dispose();
            return default;
        }

        private bool IsCoil() => string.Equals(m_entity, "coil", StringComparison.OrdinalIgnoreCase);

        private bool IsDiscreteInput()
            => string.Equals(m_entity, "discreteInput", StringComparison.OrdinalIgnoreCase);

        private bool IsInputRegister()
            => string.Equals(m_entity, "inputRegister", StringComparison.OrdinalIgnoreCase);

        private static string Get(ImmutableDictionary<string, string> map, string key, string fallback)
            => map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : fallback;

        private static int ParseInt(ImmutableDictionary<string, string> map, string key, int fallback)
            => map.TryGetValue(key, out string? value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result : fallback;

        private static bool ParseBool(ImmutableDictionary<string, string> map, string key, bool fallback)
            => map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) ? result : fallback;

        private readonly ModbusTcpClient m_client;
        private readonly WotCompiledForm m_form;
        private readonly WotExecutorContext m_context;
        private readonly ModbusWotBindingOptions m_options;
        private readonly string m_entity;
        private readonly ushort m_address;
        private readonly ushort m_quantity;
        private readonly byte m_unitId;
        private readonly string m_type;
        private readonly bool m_msbFirst;
        private readonly bool m_mswFirst;
    }
}
