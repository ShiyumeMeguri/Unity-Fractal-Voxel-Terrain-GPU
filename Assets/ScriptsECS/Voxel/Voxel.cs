using Unity.Mathematics;
using UnityEngine;
using System.Runtime.InteropServices;

namespace OptIn.Voxel
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Voxel
    {
        /// <summary>
        /// ID of the voxel.
        /// > 0: Block type ID.
        /// < 0: Isosurface material ID (absolute value).
        /// = 0: Air.
        /// </summary>
        public short voxelID;

        /// <summary>
        /// For isosurface voxels (voxelID <= 0), this stores density, scaled to the range of a short.
        /// For block voxels (voxelID > 0), this can be used for metadata (e.g., orientation, damage).
        /// </summary>
        public short metadata;

        public static Voxel Empty => new Voxel { voxelID = 0, metadata = 0 };

        public float Density
        {
            get
            {
                if (voxelID > 0) return 1f; // Blocks are always "full"
                // metadata stores density scaled from [-1, 1] to [-32767, 32767]
                return metadata / 32767f;
            }
            set
            {
                // Scale float from [-1, 1] to short [-32767, 32767]
                metadata = (short)(math.clamp(value, -1f, 1f) * 32767f);
            }
        }

        public bool IsBlock => voxelID > 0;
        public bool IsIsosurface => voxelID <= 0; // Air is part of the isosurface field
        public bool IsAir => voxelID == 0 && Density <= 0;
        public bool IsSolid => IsBlock || Density > 0;

        public ushort GetMaterialID()
        {
            // Returns a positive ID for texturing, regardless of type.
            return (ushort)Mathf.Abs(voxelID);
        }
    }
}