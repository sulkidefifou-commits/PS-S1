using System.Collections.Generic;
using UnityEngine;

public sealed class SoftBodyMeshData
{
    public Vector3[] Positions { get; }
    public int[] Tetrahedra { get; }     
    public int[] Edges { get; }       
    public int[] SurfaceTriangles { get; } 

    private SoftBodyMeshData(Vector3[] positions, int[] tetrahedra,
        int[] edges, int[] surfaceTriangles)
    {
        Positions = positions;
        Tetrahedra = tetrahedra;
        Edges = edges;
        SurfaceTriangles = surfaceTriangles;
    }

    public static SoftBodyMeshData CreateCube(float size, int subdivisions)
    {
        int n = Mathf.Clamp(subdivisions, 1, 6);
        int side = n + 1;
        float length = Mathf.Max(0.05f, size);
        var points = new Vector3[side * side * side];
        int Index(int x, int y, int z) => x + side * (y + side * z);

        for (int z = 0; z <= n; z++)
        for (int y = 0; y <= n; y++)
        for (int x = 0; x <= n; x++) points[Index(x, y, z)] = new Vector3((float)x / n - 0.5f, 0.5f - (float)y / n, (float)z / n - 0.5f) * length;

        var tetrahedra = new List<int>(n * n * n * 24);
        void AddTet(int a, int b, int c, int d)
        {

            if (Vector3.Dot(points[b] - points[a], Vector3.Cross(points[c] - points[a], points[d] - points[a])) < 0f)
            {
                (b, c) = (c, b);
            }
            tetrahedra.Add(a);
            tetrahedra.Add(b);
            tetrahedra.Add(c);
            tetrahedra.Add(d);
        }

        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int a = Index(x, y, z);
            int b = Index(x + 1, y, z);
            int c = Index(x, y + 1, z);
            int d = Index(x + 1, y + 1, z);
            int e = Index(x, y, z + 1);
            int f = Index(x + 1, y, z + 1);
            int g = Index(x, y + 1, z + 1);
            int h = Index(x + 1, y + 1, z + 1);
            AddTet(a, b, d, h);
            AddTet(a, d, c, h);
            AddTet(a, c, g, h);
            AddTet(a, g, e, h);
            AddTet(a, e, f, h);
            AddTet(a, f, b, h);
        }

        var edges = new List<int>();
        var knownEdges = new HashSet<(int, int)>();
        var faces = new List<(int a, int b, int c)>();
        var faceCounts = new Dictionary<(int, int, int), int>();

        void AddEdge(int a, int b)
        {
            var key = (Mathf.Min(a, b), Mathf.Max(a, b));
            if (!knownEdges.Add(key)) return;
            edges.Add(key.Item1);
            edges.Add(key.Item2);
        }

        void AddFace(int a, int b, int c)
        {
            var key = FaceKey(a, b, c);
            if (faceCounts.TryGetValue(key, out int count)) faceCounts[key] = count + 1;
            else
            {
                faceCounts.Add(key, 1);
                faces.Add((a, b, c)); 
            }
        }

        for (int i = 0; i < tetrahedra.Count; i += 4)
        {
            int a = tetrahedra[i], b = tetrahedra[i + 1];
            int c = tetrahedra[i + 2], d = tetrahedra[i + 3];
            AddEdge(a, b); AddEdge(a, c); AddEdge(a, d);
            AddEdge(b, c); AddEdge(b, d); AddEdge(c, d);
            AddFace(b, c, d); AddFace(a, d, c);
            AddFace(a, b, d); AddFace(a, c, b);
        }

        var surface = new List<int>();
        foreach (var face in faces)
        {
            if (faceCounts[FaceKey(face.a, face.b, face.c)] != 1) continue;
            surface.Add(face.a);
            surface.Add(face.b);
            surface.Add(face.c);
        }

        return new SoftBodyMeshData(points, tetrahedra.ToArray(), edges.ToArray(), surface.ToArray());
    }

    private static (int, int, int) FaceKey(int a, int b, int c)
    {
        int minimum = Mathf.Min(a, Mathf.Min(b, c));
        int maximum = Mathf.Max(a, Mathf.Max(b, c));
        return (minimum, a + b + c - minimum - maximum, maximum);
    }
}
