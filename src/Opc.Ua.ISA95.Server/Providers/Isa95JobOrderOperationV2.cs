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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The Job Control V2 job order operations. Unlike Job Control V1, the V2
    /// information model exposes each operation as a separate method on the job
    /// order receiver state machine; this enumeration provides a single cohesive
    /// facet argument that maps one-to-one to those operations.
    /// </summary>
    public enum Isa95JobOrderOperationV2
    {
        /// <summary>
        /// Store a new job order without starting it.
        /// </summary>
        Store = 1,

        /// <summary>
        /// Store a new job order and mark it AllowedToStart.
        /// </summary>
        StoreAndStart = 2,

        /// <summary>
        /// Mark a stored NotAllowedToStart job order as AllowedToStart.
        /// </summary>
        Start = 3,

        /// <summary>
        /// Update a stored job order that has not yet started.
        /// </summary>
        Update = 4,

        /// <summary>
        /// Stop a running or interrupted job order (ends it).
        /// </summary>
        Stop = 5,

        /// <summary>
        /// Cancel and remove a stored job order that has not yet started.
        /// </summary>
        Cancel = 6,

        /// <summary>
        /// Clear a terminal job order from the store.
        /// </summary>
        Clear = 7,

        /// <summary>
        /// Pause a running job order (interrupts it).
        /// </summary>
        Pause = 8,

        /// <summary>
        /// Resume an interrupted job order.
        /// </summary>
        Resume = 9,

        /// <summary>
        /// Abort a non-terminal job order.
        /// </summary>
        Abort = 10,

        /// <summary>
        /// Revoke the permission to start a job order that is allowed to start.
        /// </summary>
        RevokeStart = 11
    }
}
