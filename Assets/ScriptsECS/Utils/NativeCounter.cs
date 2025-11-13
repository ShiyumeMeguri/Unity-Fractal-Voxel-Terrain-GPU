// Assets/ScriptsECS/Utils/NativeCounter.cs
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ruri.Voxel
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct NativeCounter : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        private int* _Counter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif

        private Allocator _AllocatorLabel;

        public NativeCounter(Allocator label)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsBlittable<int>())
                throw new ArgumentException($"{typeof(int)} used in NativeCounter must be blittable");
#endif
            _AllocatorLabel = label;
            _Counter = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), 4, label);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, label);
#endif
            Count = 0;
        }

        public int Count
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return *_Counter;
            }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                *_Counter = value;
            }
        }

        public bool IsCreated => _Counter != null;

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            if (_Counter != null)
            {
                UnsafeUtility.Free(_Counter, _AllocatorLabel);
                _Counter = null;
            }
        }

        public Concurrent ToConcurrent()
        {
            Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            concurrent.m_Safety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
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
            internal AtomicSafetyHandle m_Safety;
#endif

            public int Increment()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return Interlocked.Increment(ref *_Counter) - 1;
            }

            public int Add(int value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return Interlocked.Add(ref *_Counter, value) - value;
            }
        }
    }
}