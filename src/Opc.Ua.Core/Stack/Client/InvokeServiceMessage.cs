// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// The message contract for the InvokeService service.
    /// </summary>
    public class InvokeServiceMessage
    {
        /// <summary>
        /// The body of the message.
        /// </summary>
        public byte[]? InvokeServiceRequest;

        /// <summary>
        /// Initializes an empty message.
        /// </summary>
        public InvokeServiceMessage()
        {
        }

        /// <summary>
        /// Initializes the message with the body.
        /// </summary>
        /// <param name="invokeServiceRequest">The invoke service request.</param>
        public InvokeServiceMessage(byte[] invokeServiceRequest)
        {
            InvokeServiceRequest = invokeServiceRequest;
        }
    }

    /// <summary>
    /// The message contract for the InvokeService service response.
    /// </summary>
    public class InvokeServiceResponseMessage
    {
        /// <summary>
        /// The body of the message.
        /// </summary>
        public byte[]? InvokeServiceResponse { get; set; }

        /// <summary>
        /// Initializes an empty message.
        /// </summary>
        public InvokeServiceResponseMessage()
        {
        }

        /// <summary>
        /// Initializes the message with the body.
        /// </summary>
        /// <param name="invokeServiceResponse">The invoke service response.</param>
        public InvokeServiceResponseMessage(byte[] invokeServiceResponse)
        {
            InvokeServiceResponse = invokeServiceResponse;
        }
    }
}
