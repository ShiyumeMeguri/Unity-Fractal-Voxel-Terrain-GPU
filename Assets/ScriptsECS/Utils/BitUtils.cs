// Assets/ScriptsECS/Utils/BitUtils.cs

using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace OptIn.Voxel
{
    public static class BitUtils
    {
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBitSet(uint backing, int index)
        {
            return ((backing >> index) & 1) == 1;
        }
    }
}