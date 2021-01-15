/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;
using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// EventArgs class for <see cref="PubSubState"/> Change events
    /// </summary>
    public class PubSubStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Reference to the object whose <see cref="PubSubState"/> was changed
        /// </summary>
        public object ConfigurationObject { get; set; }

        /// <summary>
        /// Configuration Id of the object whose <see cref="PubSubState"/> was changed
        /// </summary>
        public uint ConfigurationObjectId { get; set; }

        /// <summary>
        /// New <see cref="PubSubState"/> 
        /// </summary>
        public PubSubState NewState { get; set; }

        /// <summary>
        /// Old <see cref="PubSubState"/> 
        /// </summary>
        public PubSubState OldState { get; set; }
    }    
}
