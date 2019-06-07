using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;

namespace UniNativeLinq
{
    public readonly unsafe struct
        SelectEnumerable<TPrevEnumerable, TPrevEnumerator, TPrev, T, TAction>
        : IRefEnumerable<SelectEnumerable<TPrevEnumerable, TPrevEnumerator, TPrev, T, TAction>.Enumerator, T>
        where TPrev : unmanaged
        where TPrevEnumerable : struct, IRefEnumerable<TPrevEnumerator, TPrev>
        where TPrevEnumerator : struct, IRefEnumerator<TPrev>
        where T : unmanaged
        where TAction : struct, IRefAction<TPrev, T>
    {
        private readonly TPrevEnumerable enumerable;
        private readonly TAction action;

        public SelectEnumerable(in TPrevEnumerable enumerable, in TAction action)
        {
            this.enumerable = enumerable;
            this.action = action;
        }

        public readonly Enumerator GetEnumerator() => new Enumerator(enumerable.GetEnumerator(), action);

        [LocalRefReturn]
        public struct Enumerator : IRefEnumerator<T>
        {
            private TPrevEnumerator enumerator;
            private T element;
            private TAction action;

            internal Enumerator(in TPrevEnumerator enumerator, in TAction action)
            {
                this.enumerator = enumerator;
                element = default;
                this.action = action;
            }

            public readonly ref T Current => throw new NotImplementedException();
            readonly T IEnumerator<T>.Current => Current;
            readonly object IEnumerator.Current => Current;

            public void Dispose()
            {
                enumerator.Dispose();
                this = default;
            }

            public bool MoveNext()
            {
                if (!enumerator.MoveNext()) return false;
                action.Execute(ref enumerator.Current, ref element);
                return true;
            }

            public void Reset() => throw new InvalidOperationException();

            public ref T TryGetNext(out bool success)
            {
                ref var value = ref enumerator.TryGetNext(out success);
                if (!success) return ref Pseudo.AsRefNull<T>();
                action.Execute(ref value, ref element);
                throw new NotImplementedException();
            }

            public bool TryMoveNext(out T value)
            {
                if(!enumerator.TryMoveNext(out var prevValue))
                {
                    value = default;
                    return false;
                }
                action.Execute(ref prevValue, ref element);
                value = element;
                return true;
            }
        }

        #region Interface Implementation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool CanFastCount() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Any() => enumerable.Any();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Count() => enumerable.Count();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long LongCount() => enumerable.LongCount();

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
            var answer = new T[count];
            CopyTo(Pseudo.AsPointer<T>(ref answer[0]));
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
            CopyTo(UnsafeUtilityEx.GetPointer(answer));
            return answer;
        }
        #endregion
    }
}