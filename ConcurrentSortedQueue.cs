using System;
using System.Collections.Generic;

namespace LightNet
{
    /// <summary>
    /// This is a thread safe sorted queue. 
    /// Enqueued elements will be sorted into their approprivate place immediately using their key.
    /// </summary>
    public class ConcurrentSortedQueue<TKey, TValue> where TKey : struct where TValue : class
    {
        SortedList<TKey, TValue> List = new SortedList<TKey, TValue>();
        readonly object Lock = new object();

        public bool Enqueue(TKey key, TValue value)
        {
            lock (Lock)
            {
                try
                {
                    List.Add(key, value);
                    return true;
                }
                catch (ArgumentException)
                {
                    LogQueue.LogError("Cannot add element ({}, {}) to list, key already exists!", new object[]{ key, value });
                    return false;
                }
            }
        }

        public bool TryDequeue(out TValue value)
        {
            lock (Lock)
            {
                if (List.Values.Count == 0)
                {
                    value = null;
                    return false;
                }

                value = List.Values[0];
                List.RemoveAt(0);
            }
            return true;
        }

        public void Clear()
        {
            lock (Lock)
            {
                List.Clear();
            }
        }

        public int GetCount()
        {
            int count;
            lock (Lock)
            {
                count = List.Count;
            }
            return count;
        }
    }
}