using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace OptIn.Voxel.Meshing
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct NativeMultiCounter : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        private int* m_Counters;
        private int m_Capacity;
        private Allocator m_AllocatorLabel;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif

        public NativeMultiCounter(int capacity, Allocator label)
        {
            m_AllocatorLabel = label;
            m_Capacity = capacity;
            int sizeOfInt = UnsafeUtility.SizeOf<int>();
            m_Counters = (int*)UnsafeUtility.Malloc(sizeOfInt * capacity, 4, label);
            UnsafeUtility.MemClear(m_Counters, sizeOfInt * capacity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, label);
#endif
        }

        public int Sum()
        {
            int sum = 0;
            for (int i = 0; i < m_Capacity; i++)
            {
                sum += m_Counters[i];
            }
            return sum;
        }
        
        public int[] ToArray()
        {
            int[] arr = new int[m_Capacity];
            for(int i = 0; i < m_Capacity; i++)
            {
                arr[i] = m_Counters[i];
            }
            return arr;
        }


        public void Reset()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            UnsafeUtility.MemClear(m_Counters, UnsafeUtility.SizeOf<int>() * m_Capacity);
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            UnsafeUtility.Free(m_Counters, m_AllocatorLabel);
            m_Counters = null;
        }

        public Concurrent ToConcurrent()
        {
            Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            concurrent.m_Safety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
#endif
            concurrent.m_Counters = m_Counters;
            concurrent.m_Capacity = m_Capacity;
            return concurrent;
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public unsafe struct Concurrent
        {
            [NativeDisableUnsafePtrRestriction]
            internal int* m_Counters;
            internal int m_Capacity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public int Increment(int index)
            {
                if (index >= m_Capacity || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return Interlocked.Increment(ref *(m_Counters + index)) - 1;
            }
            
            public NativeCounter.Concurrent ToConcurrentNativeCounter(int index)
            {
                if (index >= m_Capacity || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                NativeCounter.Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                concurrent.m_Safety = m_Safety;
#endif

                concurrent.m_Counter = m_Counters + index;
                return concurrent;
            }
        }
    }
}