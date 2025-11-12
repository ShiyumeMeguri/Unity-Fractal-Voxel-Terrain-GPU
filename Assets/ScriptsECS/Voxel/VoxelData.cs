using Unity.Mathematics;
using UnityEngine;
using System.Runtime.InteropServices;

namespace OptIn.Voxel
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData
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

        public static VoxelData Empty => new VoxelData { voxelID = 0, metadata = 0 };

        public float Density
        {
            get
            {
                if (voxelID > 0) return 1f; // Blocks are always "full"
                return metadata / 32767f;
            }
            set
            {
                metadata = (short)(math.clamp(value, -1f, 1f) * 32767f);
            }
        }

        public bool IsBlock => voxelID > 0;
        public bool IsIsosurface => voxelID <= 0; // Air is part of the isosurface field
        public bool IsAir => voxelID == 0 && Density <= 0;
        public bool IsSolid => IsBlock || Density > 0;

        public ushort GetMaterialID()
        {
            return (ushort)Mathf.Abs(voxelID);
        }
    }
}