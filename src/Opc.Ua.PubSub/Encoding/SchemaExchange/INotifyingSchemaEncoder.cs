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

using System.Collections.Generic;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Implemented by a NetworkMessage encoder that produces a per-DataSet schema and can report,
    /// after each encode, the per-DataSet schema changes it produced — a not-yet-announced SchemaId
    /// for a DataSet. The publisher uses this to drive the schema lifecycle (advance the DataSet
    /// ConfigurationVersion and, optionally, register the schema) through an
    /// <see cref="ISchemaLifecycleObserver"/>.
    /// </summary>
    public interface INotifyingSchemaEncoder
    {
        /// <summary>
        /// The per-DataSet schema changes produced by the most recent encode call. Empty when the
        /// encode produced no not-yet-announced schema (every DataSet's schema was already
        /// announced to the destination).
        /// </summary>
        IReadOnlyList<SchemaChangeNotification> LastSchemaChanges { get; }
    }
}
