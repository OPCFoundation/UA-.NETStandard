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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings.Modbus
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
            ModbusWotBindingOptions options,
            ModbusAddressing addressing)
        {
            m_client = client;
            Form = form;
            m_options = options;

            m_operation = addressing.Operation;
            m_address = addressing.Address;
            m_quantity = addressing.Quantity;
            m_unitId = addressing.UnitId;
            m_type = addressing.Type;
            m_msbFirst = addressing.MsbFirst;
            m_mswFirst = addressing.MswFirst;
        }

        public WotCompiledForm Form { get; }

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Variant value;
                if (m_operation == ModbusOperation.ReadCoils)
                {
                    bool[] bits = await m_client
                        .ReadCoilsAsync(m_unitId, m_address, m_quantity, cancellationToken).ConfigureAwait(false);
                    value = ToBitVariant(bits);
                }
                else if (m_operation == ModbusOperation.ReadDiscreteInputs)
                {
                    bool[] bits = await m_client
                        .ReadDiscreteInputsAsync(m_unitId, m_address, m_quantity, cancellationToken)
                        .ConfigureAwait(false);
                    value = ToBitVariant(bits);
                }
                else if (m_operation == ModbusOperation.ReadInputRegisters)
                {
                    ushort[] regs = await m_client
                        .ReadInputRegistersAsync(m_unitId, m_address, m_quantity, cancellationToken)
                        .ConfigureAwait(false);
                    value = ModbusDataConverter.ToVariant(regs, m_type, m_msbFirst, m_mswFirst);
                }
                else if (m_operation == ModbusOperation.ReadHoldingRegisters)
                {
                    ushort[] regs = await m_client
                        .ReadHoldingRegistersAsync(m_unitId, m_address, m_quantity, cancellationToken)
                        .ConfigureAwait(false);
                    value = ModbusDataConverter.ToVariant(regs, m_type, m_msbFirst, m_mswFirst);
                }
                else
                {
                    return new WotReadResult(
                        StatusCodes.BadNotSupported,
                        DataValue.FromStatusCode(StatusCodes.BadNotSupported),
                        $"The Modbus operation '{m_operation}' is not readable.");
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
                    StatusCodes.BadTimeout,
                    DataValue.FromStatusCode(StatusCodes.BadTimeout),
                    "The Modbus request timed out.");
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
            if (m_operation is
                ModbusOperation.ReadCoils or
                ModbusOperation.ReadDiscreteInputs or
                ModbusOperation.ReadHoldingRegisters or
                ModbusOperation.ReadInputRegisters)
            {
                return new WotWriteResult(StatusCodes.BadNotWritable, "The Modbus operation is read-only.");
            }
            try
            {
                if (m_operation == ModbusOperation.WriteSingleCoil)
                {
                    if (!value.WrappedValue.TryGetValue(out bool on))
                    {
                        return new WotWriteResult(
                            StatusCodes.BadTypeMismatch,
                            "The Modbus single-coil write requires a Boolean scalar.");
                    }
                    await m_client.WriteSingleCoilAsync(m_unitId, m_address, on, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (m_operation == ModbusOperation.WriteMultipleCoils)
                {
                    bool[] coilValues;
                    if (m_quantity == 1)
                    {
                        if (!value.WrappedValue.TryGetValue(out bool on))
                        {
                            return new WotWriteResult(
                                StatusCodes.BadTypeMismatch,
                                "The Modbus multiple-coil write with quantity 1 requires a Boolean scalar.");
                        }
                        coilValues = [on];
                    }
                    else if (value.WrappedValue.TryGetValue(out ArrayOf<bool> bits))
                    {
                        if (bits.Count != m_quantity)
                        {
                            return new WotWriteResult(
                                StatusCodes.BadInvalidArgument,
                                $"The Modbus multiple-coil write requires exactly {m_quantity} Boolean values; " +
                                $"the payload contains {bits.Count}.");
                        }
                        coilValues = bits.Memory.ToArray();
                    }
                    else
                    {
                        return new WotWriteResult(
                            StatusCodes.BadTypeMismatch,
                            $"The Modbus multiple-coil write requires an array of {m_quantity} Boolean values.");
                    }
                    await m_client
                        .WriteMultipleCoilsAsync(
                            m_unitId, m_address, coilValues, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (m_operation is
                    ModbusOperation.WriteSingleHoldingRegister or
                    ModbusOperation.WriteMultipleHoldingRegisters)
                {
                    ushort[] registers = ModbusDataConverter.ToRegisters(
                        value.WrappedValue, m_type, m_msbFirst, m_mswFirst);
                    if (m_operation == ModbusOperation.WriteSingleHoldingRegister)
                    {
                        if (registers.Length != 1)
                        {
                            return new WotWriteResult(
                                StatusCodes.BadTypeMismatch,
                                "The Modbus single-register write requires a value encoded in one register.");
                        }
                        await m_client
                            .WriteSingleRegisterAsync(m_unitId, m_address, registers[0], cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        if (registers.Length != m_quantity)
                        {
                            return new WotWriteResult(
                                StatusCodes.BadInvalidArgument,
                                $"The Modbus multiple-register write requires exactly {m_quantity} registers; " +
                                $"the payload encodes {registers.Length}.");
                        }
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
            catch (Exception ex) when (
                ex is FormatException or InvalidCastException or OverflowException)
            {
                return new WotWriteResult(StatusCodes.BadTypeMismatch, ex.Message);
            }
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotInvokeResult>(new WotInvokeResult(
                        StatusCodes.BadNotSupported, null, "Modbus does not support action invocation."));
        }

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
                Form,
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
        {
            return ObserveAsync(onEvent, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            m_client.Dispose();
            return default;
        }

        private Variant ToBitVariant(bool[] bits)
        {
            return m_quantity == 1
                ? new Variant(bits[0])
                : new Variant((ArrayOf<bool>)bits);
        }

        private readonly ModbusTcpClient m_client;
        private readonly ModbusWotBindingOptions m_options;
        private readonly ModbusOperation m_operation;
        private readonly ushort m_address;
        private readonly ushort m_quantity;
        private readonly byte m_unitId;
        private readonly string m_type;
        private readonly bool m_msbFirst;
        private readonly bool m_mswFirst;
    }
}
