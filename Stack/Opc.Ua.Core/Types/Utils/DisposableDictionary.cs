using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Types.Utils
{
    internal class DisposableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDisposable where TValue : IDisposable
    {
        private bool m_disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposedValue)
            {
                // we need to make sure all the items in values will be disposed
                
                if (this.Count > 0)
                {
                    foreach (IDisposable entry in this.Values)
                    {
                        entry.Dispose();
                    }
                }
                m_disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~DisposableDictionary()
        {
            Dispose(disposing: false);
        }

        public new void Clear()
        {
            if (this.Count > 0)
            {
                foreach (IDisposable entry in this.Values)
                {
                    entry?.Dispose();
                }
                base.Clear();
            }
        }
    }
}
