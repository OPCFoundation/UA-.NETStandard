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

<<<<<<<< HEAD:Applications/Quickstarts.Servers/ReferenceServer/Namespaces.cs
namespace Quickstarts.ReferenceServer
========
using System;
using System.Collections.Generic;
using System.Text;

namespace AggregatingServer.Servers
>>>>>>>> 0641bfb846daba4d4386fc89c5fc9e4b765a12a4:SampleApplications/Workshop/AggregatingServer/Namespaces.cs
{
    /// <summary>
    /// Defines constants for namespaces used by the servers.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The namespace for the nodes provided by the reference server.
        /// </summary>
<<<<<<<< HEAD:Applications/Quickstarts.Servers/ReferenceServer/Namespaces.cs
        public const string ReferenceServer = "http://opcfoundation.org/Quickstarts/ReferenceServer";
========
        public const string AggregatingServer = "http://phi-ware.com/AggregatingServer";
>>>>>>>> 0641bfb846daba4d4386fc89c5fc9e4b765a12a4:SampleApplications/Workshop/AggregatingServer/Namespaces.cs
    }
}
