// Assets/ScriptsECS/Utils/NativeMultiCounter.cs
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using OptIn.Voxel;

namespace OptIn.Voxel.Meshing
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct NativeMultiCounter : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        private int* _Counters;
        private int _Capacity;
        private Allocator _AllocatorLabel;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle _Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel _DisposeSentinel;
#endif

        public NativeMultiCounter(int capacity, Allocator label)
        {
            _AllocatorLabel = label;
            _Capacity = capacity;
            int sizeOfInt = UnsafeUtility.SizeOf<int>();
            _Counters = (int*)UnsafeUtility.Malloc(sizeOfInt * capacity, 4, label);
            UnsafeUtility.MemClear(_Counters, sizeOfInt * capacity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out _Safety, out _DisposeSentinel, 0, label);
#endif
        }

        // [修复] 添加 IsCreated 属性
        public bool IsCreated => _Counters != null;

        public int Sum()
        {
            int sum = 0;
            for (int i = 0; i < _Capacity; i++)
            {
                sum += _Counters[i];
            }
            return sum;
        }

        public int[] ToArray()
        {
            int[] arr = new int[_Capacity];
            for (int i = 0; i < _Capacity; i++)
            {
                arr[i] = _Counters[i];
            }
            return arr;
        }


        public void Reset()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
#endif
            UnsafeUtility.MemClear(_Counters, UnsafeUtility.SizeOf<int>() * _Capacity);
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref _Safety, ref _DisposeSentinel);
#endif
            if (IsCreated)
            {
                UnsafeUtility.Free(_Counters, _AllocatorLabel);
                _Counters = null;
            }
        }

        public Concurrent ToConcurrent()
        {
            Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
            concurrent._Safety = _Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent._Safety);
#endif
            concurrent._Counters = _Counters;
            concurrent._Capacity = _Capacity;
            return concurrent;
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public unsafe struct Concurrent
        {
            [NativeDisableUnsafePtrRestriction]
            internal int* _Counters;
            internal int _Capacity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle _Safety;
#endif

            public int Increment(int index)
            {
                if (index >= _Capacity || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
#endif
                return Interlocked.Increment(ref *(_Counters + index)) - 1;
            }

            public NativeCounter.Concurrent ToConcurrentNativeCounter(int index)
            {
                if (index >= _Capacity || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                NativeCounter.Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
                concurrent._Safety = _Safety;
#endif

                concurrent._Counter = _Counters + index;
                return concurrent;
            }
        }
    }
}