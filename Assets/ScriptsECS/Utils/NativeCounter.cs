using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace OptIn.Voxel
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct NativeCounter : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        private int* _Counter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle _Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel _DisposeSentinel;
#endif

        private Allocator _AllocatorLabel;

        public NativeCounter(Allocator label)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsBlittable<int>())
                throw new ArgumentException(string.Format("{0} used in NativeQueue<{0}> must be blittable", typeof(int)));
#endif
            _AllocatorLabel = label;
            _Counter = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), 4, label);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out _Safety, out _DisposeSentinel, 0, label);
#endif
            Count = 0;
        }

        public int Count
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(_Safety);
#endif
                return *_Counter;
            }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
#endif
                *_Counter = value;
            }
        }

        public bool IsCreated => _Counter != null;

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref _Safety, ref _DisposeSentinel);
#endif
            UnsafeUtility.Free(_Counter, _AllocatorLabel);
            _Counter = null;
        }

        public Concurrent ToConcurrent()
        {
            Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
            concurrent._Safety = _Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent._Safety);
#endif
            concurrent._Counter = _Counter;
            return concurrent;
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public unsafe struct Concurrent
        {
            [NativeDisableUnsafePtrRestriction]
            internal int* _Counter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle _Safety;
#endif

            public int Increment()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
#endif
                return Interlocked.Increment(ref *_Counter) - 1;
            }

            public int Add(int value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(_Safety);
#endif
                return Interlocked.Add(ref *_Counter, value) - value;
            }
        }
    }
}