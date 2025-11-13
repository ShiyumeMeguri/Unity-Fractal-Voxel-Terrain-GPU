// Utils/BitUtils.cs
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
                throw new System.Exception("There must exactly be one bool set in the bool2 mask");
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DebugCheckOnlyOneBitMask(bool3 mask)
        {
            if (CountTrue(mask) != 1)
                throw new System.Exception("There must exactly be one bool set in the bool3 mask");
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DebugCheckOnlyOneBitMask(bool4 mask)
        {
            if (CountTrue(mask) != 1)
                throw new System.Exception("There must exactly be one bool set in the bool4 mask");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool2 b) => math.countbits(math.bitmask(new bool4(b, false, false)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool3 b) => math.countbits(math.bitmask(new bool4(b, false)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountTrue(bool4 b) => math.countbits(math.bitmask(b));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBitSet(byte backing, int index) => ((backing >> index) & 1) == 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBit(ref byte backing, int index, bool value)
        {
            if (value) backing |= (byte)(1 << index);
            else backing &= (byte)~(1 << index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackUInt4ToUInt(uint4 bytes)
        {
            return (bytes.x & 0xFFu) | ((bytes.y & 0xFFu) << 8) | ((bytes.z & 0xFFu) << 16) | ((bytes.w & 0xFFu) << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackSnorm8(float4 value)
        {
            float4 n = math.clamp(value, -1f, 1f) * 127f;
            uint4 bytes = (uint4)(int4)math.round(n);
            return PackUInt4ToUInt(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackUnorm8(float4 value)
        {
            float4 n = math.saturate(value) * 255f;
            return PackUInt4ToUInt((uint4)math.round(n));
        }
    }
}