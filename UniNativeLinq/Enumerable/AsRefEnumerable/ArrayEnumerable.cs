using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UniNativeLinq
{
    [CanFastCountAttribute]
    public unsafe struct
        ArrayEnumerable<T>
        : IRefEnumerable<ArrayEnumerable<T>.Enumerator, T>
        where T : unmanaged
    {
        private readonly T[] array;
        private readonly long offset;
        internal readonly long Length;

        internal readonly T* GetPointer() => (T*)Unsafe.AsPointer(ref array[offset]);
        private readonly T* GetPinPointer(out ulong gcHandle) => (T*)UnsafeUtility.PinGCArrayAndGetDataAddress(array, out gcHandle) + offset;

        public ArrayEnumerable(T[] array)
        {
            this.array = array ?? throw new ArgumentNullException();
            Length = array.LongLength;
            offset = 0;
        }

        public ArrayEnumerable(ArraySegment<T> segment)
        {
            array = segment.Array ?? throw new ArgumentNullException();
            Length = segment.Count;
            offset = segment.Offset;
        }

        public ArrayEnumerable(T[] array, long offset, long count)
        {
            this.array = array ?? throw new ArgumentNullException();
            Length = count;
            this.offset = offset;
        }

        public struct Enumerator : IRefEnumerator<T>
        {
            private readonly T* ptr;
            private readonly long length;
            private readonly ulong gcHandle;
            private long index;

            internal Enumerator(T* ptr, long length, ulong gcHandle)
            {
                this.ptr = ptr;
                this.length = length;
                this.gcHandle = gcHandle;
                index = -1;
            }

            public bool MoveNext() => ++index < length;
            public void Reset() => index = -1;
            public readonly ref T Current => ref ptr[index];
            readonly T IEnumerator<T>.Current => Current;
            readonly object IEnumerator.Current => Current;

            public void Dispose()
            {
                if (ptr != null)
                    UnsafeUtility.ReleaseGCObject(gcHandle);
                this = default;
            }

            public ref T TryGetNext(out bool success)
            {
                success = ++index < length;
                if (success)
                    return ref ptr[index];
                index = length;
                return ref Unsafe.AsRef<T>(null);
            }

            public bool TryMoveNext(out T value)
            {
                if (++index < length)
                {
                    value = ptr[index];
                    return true;
                }
                else
                {
                    index = length;
                    value = default;
                    return false;
                }
            }
        }

        public struct ReverseEnumerator : IRefEnumerator<T>
        {
            private readonly T* ptr;
            private readonly long length;
            private readonly ulong gcHandle;
            private long index;

            internal ReverseEnumerator(T* ptr, long length, ulong gcHandle)
            {
                this.ptr = ptr;
                this.length = length;
                this.gcHandle = gcHandle;
                index = length;
            }

            public bool MoveNext() => --index >= 0;
            public void Reset() => index = length;
            public ref T Current => ref ptr[index];
            T IEnumerator<T>.Current => Current;
            object IEnumerator.Current => Current;

            public void Dispose()
            {
                if (ptr != null)
                    UnsafeUtility.ReleaseGCObject(gcHandle);
                this = default;
            }

            public ref T TryGetNext(out bool success)
            {
                success = --index >= 0;
                if (success)
                    return ref ptr[index];
                index = 0;
                return ref Unsafe.AsRef<T>(null);
            }

            public bool TryMoveNext(out T value)
            {
                if(--index >= 0)
                {
                    value = ptr[index];
                    return true;
                }
                else
                {
                    index = 0;
                    value = default;
                    return false;
                }
            }
        }

        public readonly Enumerator GetEnumerator()
        {
            if (array is null || array.Length == 0)
                return default;
            return new Enumerator(GetPinPointer(out var gcHandle), Length, gcHandle);
        }

        public readonly ArrayEnumerable<T> Slice(long length)
        {
            if (length > Length)
                length = Length;
            return length <= 0 ? new ArrayEnumerable<T>(Array.Empty<T>(), 0, 0) : new ArrayEnumerable<T>(array, offset, length);
        }
        public readonly ArrayEnumerable<T> Slice(long offset, long length)
        {
            if (array.Length == 0) return this;
            var rest = this.offset + Length - offset;
            if (length <= 0 || rest <= 0) return new ArrayEnumerable<T>(Array.Empty<T>(), 0, 0);
            if (length > rest)
                length = rest;
            return new ArrayEnumerable<T>(array, offset + this.offset, length);
        }

        #region Interface Implementation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool CanFastCount() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Any() => Length != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Count() => (int)Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long LongCount() => Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void CopyTo(T* dest) => UnsafeUtilityEx.MemCpy(dest, GetPointer(), Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T[] ToArray()
        {
            var count = LongCount();
            if (count == 0) return Array.Empty<T>();
            var answer = new T[count];
            CopyTo((T*)Unsafe.AsPointer(ref answer[0]));
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
