using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;


namespace OpcUaWebTelemetry.CircularBuffer
{
    public class CircularBuffer<T> : ICircularBuffer<T>, IEnumerable<T>

    {
        private T[] _buffer;
        private int _head;
        private int _tail;

        public CircularBuffer(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException("capacity", "must be positive");
            _buffer = new T[capacity];
            _head = capacity - 1;
        }

        public CircularBuffer()
        {
            int capacity = 10;
            _buffer = new T[capacity];
            _head = capacity - 1;
        }

        public int Count { get; private set; }

        public int Capacity
        {
            get { return _buffer.Length; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "must be positive");

                if (value == _buffer.Length)
                    return;

                var buffer = new T[value];
                var count = 0;
                while (Count > 0 && count < value)
                    buffer[count++] = Dequeue();

                _buffer = buffer;
                Count = count;
                _head = count - 1;
                _tail = 0;
            }
        }

        public T Enqueue(T item)
        {
            _head = (_head + 1) % Capacity;
            var overwritten = _buffer[_head];
            _buffer[_head] = item;
            if (Count == Capacity)
                _tail = (_tail + 1) % Capacity;
            else
                ++Count;
            return overwritten;
        }

        public T Dequeue()
        {
            if (Count == 0)
                throw new InvalidOperationException("queue exhausted");

            var dequeued = _buffer[_tail];
            _buffer[_tail] = default(T);
            _tail = (_tail + 1) % Capacity;
            --Count;
            return dequeued;
        }

        public void Clear()
        {
            _head = Capacity - 1;
            _tail = 0;
            Count = 0;
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException("index");

                return _buffer[(_tail + index) % Capacity];
            }
            set
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException("index");

                _buffer[(_tail + index) % Capacity] = value;
            }
        }

        public T getElement(int index)
        {
            
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index");

             return _buffer[(_tail + index) % Capacity];
        }

        public int IndexOf(T item)
        {
            for (var i = 0; i < Count; ++i)
                if (Equals(item, this[i]))
                    return i;
            return -1;
        }

        public void Insert(int index, T item)
        {
            if (index < 0 || index > Count)
                throw new ArgumentOutOfRangeException("index");

            if (Count == index)
                Enqueue(item);
            else
            {
                var last = this[Count - 1];
                for (var i = index; i < Count - 2; ++i)
                    this[i + 1] = this[i];
                this[index] = item;
                Enqueue(last);
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index");

            for (var i = index; i > 0; --i)
                this[i] = this[i - 1];
            Dequeue();
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (Count == 0 || Capacity == 0)
                yield break;

            for (var i = 0; i < Count; ++i)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}