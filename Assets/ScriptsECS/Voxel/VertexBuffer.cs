using Aether;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class VertexBuffer
{
    public struct Vertex
    {
        public Vector3 pos;
        public Vector2 uv;
        public Vector3 norm;

        public override bool Equals(object obj)
        {
            if (obj is Vertex vertex)
            {
                if (pos.Equals(vertex.pos) && uv.Equals(vertex.uv))
                {
                    return norm.Equals(vertex.norm);
                }
                return false;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return ((17 * 31 + pos.GetHashCode()) * 31 + uv.GetHashCode()) * 31 + norm.GetHashCode();
        }

        public Vertex SetNorm(Vector3 norm)
        {
            this.norm = norm;
            return this;
        }
    }

    public List<Vertex> Vertices;
    public List<int> Indices;

    public VertexBuffer(int initialCapacity = 0)
    {
        Vertices = new List<Vertex>(initialCapacity);
        Indices = new List<int>(initialCapacity);
    }

    public void PushVertex(Vector3 pos, Vector2 uv, Vector3 norm)
    {
        Vertices.Add(new Vertex
        {
            pos = pos,
            uv = uv,
            norm = norm
        });
    }

    public void RemoveVertex(int index, int count = 1)
    {
        Vertices.RemoveRange(index, count);
    }

    public void Clear()
    {
        Vertices.Clear();
        Indices.Clear();
    }

    public bool IsIndexed()
    {
        return Indices.Count > 0;
    }

    public int VertexCount()
    {
        return IsIndexed() ? Indices.Count : Vertices.Count;
    }

    public int NumTriangles()
    {
        return VertexCount() / 3;
    }

    public Vertex GetVertex(int idx)
    {
        return IsIndexed() ? Vertices[Indices[idx]] : Vertices[idx];
    }

    public void ComputeNormalsFlat()
    {
        int num = NumTriangles();
        for (int i = 0; i < num; i++)
        {
            int num2 = i * 3;
            Vector3 pos = Vertices[num2].pos;
            Vector3 pos2 = Vertices[num2 + 1].pos;
            Vector3 normalized = Vector3.Cross(Vertices[num2 + 2].pos - pos, pos2 - pos).normalized;
            Vertices[num2] = Vertices[num2].SetNorm(normalized);
            Vertices[num2 + 1] = Vertices[num2 + 1].SetNorm(normalized);
            Vertices[num2 + 2] = Vertices[num2 + 2].SetNorm(normalized);
        }
    }

    public VertexBuffer ComputeIndexed()
    {
        VertexBuffer vertexBuffer = new VertexBuffer();
        Dictionary<Vertex, int> dictionary = new Dictionary<Vertex, int>();
        foreach (Vertex vertex in Vertices)
        {
            if (dictionary.TryGetValue(vertex, out var value))
            {
                vertexBuffer.Indices.Add(value);
                continue;
            }
            value = (dictionary[vertex] = vertexBuffer.Vertices.Count);
            vertexBuffer.Indices.Add(value);
            vertexBuffer.Vertices.Add(vertex);
        }
        return vertexBuffer;
    }

    public Mesh ToMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] array = new Vector3[Vertices.Count];
        Vector3[] array2 = new Vector3[Vertices.Count];
        Vector2[] array3 = new Vector2[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            array[i] = Vertices[i].pos;
            array2[i] = Vertices[i].norm;
            array3[i] = Vertices[i].uv;
        }
        mesh.vertices = array;
        mesh.normals = array2;
        mesh.uv = array3;
        if (IsIndexed())
        {
            mesh.triangles = Indices.ToArray();
        }
        else
        {
            mesh.triangles = Utility.Sequence(Vertices.Count);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
