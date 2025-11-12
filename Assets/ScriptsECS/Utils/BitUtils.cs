// Assets/ScriptsECS/Utils/BitUtils.cs
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace OptIn.Voxel
{
    public static class BitUtils
    {
        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DebugCheckOnlyOneBitMask(bool2 mask)
        {
            if (CountTrue(mask) != 1)
            {
                throw new System.Exception("There must exactly be one bool set in the bool2 mask");
            }
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DebugCheckOnlyOneBitMask(bool3 mask)
        {
            if (CountTrue(mask) != 1)
            {
                throw new System.Exception("There must exactly be one bool set in the bool3 mask");
            }
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DebugCheckOnlyOneBitMask(bool4 mask)
        {
            if (CountTrue(mask) != 1)
            {
                throw new System.Exception("There must exactly be one bool set in the bool4 mask");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool2 b)
        {
            return math.countbits(math.bitmask(new bool4(b, false, false)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool3 b)
        {
            return math.countbits(math.bitmask(new bool4(b, false)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool4 b)
        {
            return math.countbits(math.bitmask(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBitSet(byte backing, int index)
        {
            return ((backing >> index) & 1) == 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBit(ref byte backing, int index, bool value)
        {
            if (value)
            {
                backing |= (byte)(1 << index);
            }
            else
            {
                backing &= (byte)(~(1 << index));
            }
        }
    }
}