﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UniNativeLinq
{
    internal unsafe struct
        UnrolledLinkedList<T>
        : IRefEnumerable<UnrolledLinkedList<T>.Enumerator, T>
        where T : unmanaged
    {
        private Node First;
        private Node LastFull;
        private readonly Allocator alloc;

        public UnrolledLinkedList(Allocator allocator)
        {
            First = new Node(16L, allocator);
            LastFull = default;
            alloc = allocator;
        }

        public UnrolledLinkedList(long capacity, Allocator allocator)
        {
            First = new Node(16L > capacity ? 16L : capacity, allocator);
            LastFull = default;
            alloc = allocator;
        }

        public struct Node : IRefEnumerable<Node.Enumerator, T>
        {
            public IntPtr<Node> Next;
            public readonly IntPtr<T> Values;
            public readonly long Capacity;
            public long Count;

            public Node(long capacity, Allocator allocator)
            {
                Capacity = capacity;
                Count = 0L;
                Next = default;
                Values = UnsafeUtilityEx.Malloc<T>(capacity, allocator);
            }

            public readonly bool IsFull => Count == Capacity;
            public readonly bool IsEmpty => Count == 0;
            public readonly bool HasNext => Next;
            public readonly ref Node NextRef => ref *Next.Value;

            public bool TryAddConcurrent(in T value)
            {
                long index;
                do
                {
                    index = Count;
                    if (index >= Capacity)
                        return false;
                } while (Count != Interlocked.CompareExchange(ref Count, index + 1, index));
                Values[index] = value;
                return true;
            }

            public bool TryAddRangeConcurrent(T* values, long length)
            {
                long index;
                do
                {
                    index = Count;
                    if (index + length > Capacity)
                        return false;
                } while (Count != Interlocked.CompareExchange(ref Count, index + length, index));
                UnsafeUtilityEx.MemCpy(Values.Value + index, values, length);
                return true;
            }

            public void Clear() => Interlocked.Exchange(ref Count, 0);

            public readonly ref T this[long index] => ref Values[index];

            public struct Enumerator : IRefEnumerator<T>
            {
                private readonly T* values;
                private readonly long count;
                private long index;

                internal Enumerator(in Node parent)
                {
                    values = parent.Values;
                    count = parent.Count;
                    index = -1;
                }

                public readonly ref T Current => ref values[index];
                readonly T IEnumerator<T>.Current => Current;
                readonly object IEnumerator.Current => Current;
                public void Dispose() => this = default;
                public bool MoveNext() => ++index < count;
                public void Reset() => index = -1;

                public ref T TryGetNext(out bool success)
                {
                    if (!(success = ++index < count))
                        index = count;
                    if (success)
                        return ref values[index];
                    return ref Psuedo.AsRefNull<T>();
                }

                public bool TryMoveNext(out T value)
                {
                    var success = ++index < count;
                    if (success)
                    {
                        value = values[index];
                        return true;
                    }
                    else
                    {
                        value = default;
                        index = count;
                        return false;
                    }
                }
            }

            public readonly Enumerator GetEnumerator() => new Enumerator(this);
            readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public void Dispose(Allocator allocator)
            {
                if (UnsafeUtility.IsValidAllocator(allocator))
                {
                    if (Next)
                    {
                        Next.Value->Dispose(allocator);
                        UnsafeUtility.Free(Next.Value, allocator);
                    }
                    if (Values)
                        UnsafeUtility.Free(Values.Value, allocator);
                }
                this = default;
            }

            public readonly bool CanFastCount() => true;

            public readonly bool Any() => Count > 0;

            readonly int IRefEnumerable<Enumerator, T>.Count() => (int)Count;

            public readonly long LongCount() => Count;

            public readonly void CopyTo(T* dest) => UnsafeUtilityEx.MemCpy(dest, Values, Count);

            public readonly NativeEnumerable<T> ToNativeEnumerable(Allocator allocator)
            {
                var ptr = UnsafeUtilityEx.Malloc<T>(Count, allocator);
                CopyTo(ptr);
                return new NativeEnumerable<T>(ptr, Count);
            }

            public readonly NativeArray<T> ToNativeArray(Allocator allocator)
            {
                var answer = new NativeArray<T>((int)Count, allocator, NativeArrayOptions.UninitializedMemory);
                CopyTo(answer.GetPointer());
                return answer;
            }

            public readonly T[] ToArray()
            {
                var answer = new T[Count];
                CopyTo(Psuedo.AsPointer<T>(ref answer[0]));
                return answer;
            }

            public static implicit operator bool(in Node @this) => @this.Values.Value != null;
        }

        public void Add(in T value)
        {
            if (LastFull)
                AddStartFromLastFull(in value);
            else AddStartFromFirst(in value);
        }
        
        #region private Add
        private void AddStartFromFirst(in T value)
        {
            ref var seek = ref First;
            while (true)
            {
                if (seek.TryAddConcurrent(value)) return;
                LastFull = seek;
                if (!seek.HasNext)
                {
                    AddAndMakeNewNode(ref seek, value);
                    return;
                }
                seek = ref seek.NextRef;
            }
        }

        private void AddAndMakeNewNode(ref Node seek, in T value)
        {
            var ptr = UnsafeUtilityEx.Malloc<Node>(1, alloc);
            *ptr = new Node(seek.Capacity, alloc);
            *(ptr->Values.Value) = value;
            ptr->Count = 1;
            AddNewNodePtr(ref seek, new IntPtr(ptr));
        }

        private void AddNewNodePtr(ref Node seekOrigin, IntPtr nodePtr)
        {
            while (IntPtr.Zero != Interlocked.CompareExchange(ref seekOrigin.Next.Ptr, nodePtr, IntPtr.Zero))
                seekOrigin = ref seekOrigin.NextRef;
        }

        private void AddStartFromLastFull(in T value)
        {
            if (!LastFull.HasNext)
            {
                AddAndMakeNewNode(ref LastFull, in value);
                return;
            }
            ref var seek = ref LastFull.NextRef;
            while (true)
            {
                if (seek.TryAddConcurrent(value)) return;
                LastFull = seek;
                if (!seek.HasNext)
                {
                    AddAndMakeNewNode(ref seek, value);
                    return;
                }
                seek = ref seek.NextRef;
            }
        }
        #endregion

        public readonly Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
            : IRefEnumerator<T>
        {
            private NodeEnumerator nodeEnumerator;
            private Node.Enumerator enumerator;
            internal Enumerator(in UnrolledLinkedList<T> @this)
            {
                nodeEnumerator = new NodeEnumerator(@this.First);
                enumerator = default;
            }

            public ref T Current => ref enumerator.Current;
            T IEnumerator<T>.Current => Current;
            object IEnumerator.Current => Current;

            public void Dispose() => this = default;

            public bool MoveNext()
            {
                if (enumerator.MoveNext()) return true;
                while (nodeEnumerator.MoveNext())
                {
                    enumerator = nodeEnumerator.Current.GetEnumerator();
                    if (enumerator.MoveNext()) return true;
                }
                return false;
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }

            public ref T TryGetNext(out bool success)
            {
                ref var value = ref enumerator.TryGetNext(out success);
                if (success) return ref value;
                while (nodeEnumerator.MoveNext())
                {
                    enumerator = nodeEnumerator.Current.GetEnumerator();
                    value = ref enumerator.TryGetNext(out success);
                    if (success)
                        return ref value;
                }
                return ref value;
            }

            public bool TryMoveNext(out T value)
            {
                if (enumerator.TryMoveNext(out value))
                    return true;
                while (nodeEnumerator.MoveNext())
                {
                    enumerator = nodeEnumerator.Current.GetEnumerator();
                    if (enumerator.TryMoveNext(out value))
                        return true;
                }
                value = default;
                return false;
            }
        }

        public struct NodeEnumerator
        {
            private bool isFirst;
            public Node Current;

            public NodeEnumerator(in Node node)
            {
                isFirst = true;
                Current = node;
            }

            public bool MoveNext()
            {
                if (isFirst)
                {
                    isFirst = false;
                    return true;
                }
                if (!Current.HasNext) return false;
                Current = Current.NextRef;
                return true;
            }
        }

        #region Interface Implementation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool CanFastCount() => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Any()
        {
            var enumerator = GetEnumerator();
            if (enumerator.MoveNext())
            {
                enumerator.Dispose();
                return true;
            }
            enumerator.Dispose();
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Count()
            => (int)LongCount();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long LongCount()
        {
            var enumerator = GetEnumerator();
            var count = 0L;
            while (enumerator.MoveNext())
                ++count;
            enumerator.Dispose();
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void CopyTo(T* dest)
        {
            var enumerator = GetEnumerator();
            while (enumerator.MoveNext())
                *dest++ = enumerator.Current;
            enumerator.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T[] ToArray()
        {
            var count = LongCount();
            if (count == 0) return Array.Empty<T>();
            var answer = new T[LongCount()];
            CopyTo(Psuedo.AsPointer<T>(ref answer[0]));
            return answer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly NativeEnumerable<T> ToNativeEnumerable(Allocator allocator)
        {
            var count = LongCount();
            var ptr = UnsafeUtilityEx.Malloc<T>(count, allocator);
            CopyTo(ptr);
            return new NativeEnumerable<T>(ptr, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly NativeArray<T> ToNativeArray(Allocator allocator)
        {
            var count = Count();
            if (count == 0) return default;
            var answer = new NativeArray<T>(count, allocator, NativeArrayOptions.UninitializedMemory);
            CopyTo(answer.GetPointer());
            return answer;
        }
        #endregion
    }
}
