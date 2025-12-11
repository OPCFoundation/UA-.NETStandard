/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Opc.Ua
{
    /// <summary>
    /// A reusable value task source.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class ManualResetValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<T> m_core;

        public bool RunContinuationsAsynchronously
        {
            get => m_core.RunContinuationsAsynchronously;
            set => m_core.RunContinuationsAsynchronously = value;
        }

        public short Version => m_core.Version;

        public void Reset()
        {
            m_core.Reset();
        }

        public void SetResult(T result)
        {
            m_core.SetResult(result);
        }

        public void SetException(Exception error)
        {
            m_core.SetException(error);
        }

        public T GetResult(short token)
        {
            return m_core.GetResult(token);
        }

        void IValueTaskSource.GetResult(short token)
        {
            m_core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return m_core.GetStatus(token);
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            m_core.OnCompleted(continuation, state, token, flags);
        }

        public ValueTask<T> Task => new(this, m_core.Version);
        public ValueTask SourceTask => new(this, m_core.Version);
    }
}
