using System;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    [Serializable]
    public struct OctreeNode : IEquatable<OctreeNode>
    {
        public int3 position;
        public int depth;
        public int size;
        public int index;
        public int parentIndex;
        public bool atMaxDepth;
        public int childBaseIndex;

        public float3 Center => math.float3(position) + math.float3(size) / 2.0F;
        public Unity.Mathematics.Geometry.MinMaxAABB Bounds => new Unity.Mathematics.Geometry.MinMaxAABB { Min = position, Max = position + size };

        public bool Equals(OctreeNode other)
        {
            return math.all(this.position == other.position) &&
                   this.depth == other.depth &&
                   this.size == other.size &&
                   (this.childBaseIndex == -1) == (other.childBaseIndex == -1);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + position.GetHashCode();
                hash = hash * 23 + depth.GetHashCode();
                hash = hash * 23 + childBaseIndex.GetHashCode();
                hash = hash * 23 + size.GetHashCode();
                return hash;
            }
        }
    }
}