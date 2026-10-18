using UnityEngine;

// Geometrie sans Collider Unity et sans correction de position/vitesse.
internal static class DetectionGeometry
{
    internal struct Shape
    {
        public bool IsBox;
        public Vector3 Center, HalfSize, X, Y, Z;
        public float Radius;
        public Bounds Bounds;

        public static Shape Sphere(Vector3 center, float radius)
        {
            return new Shape { Center = center, Radius = radius,
                Bounds = new Bounds(center, Vector3.one * (2f * radius)) };
        }

        public static Shape Box(Vector3 center, Quaternion rotation, Vector3 size)
        {
            Shape s = new Shape { IsBox = true, Center = center, HalfSize = size * 0.5f,
                X = rotation * Vector3.right, Y = rotation * Vector3.up, Z = rotation * Vector3.forward };
            Vector3 extent = Abs(s.X) * s.HalfSize.x + Abs(s.Y) * s.HalfSize.y + Abs(s.Z) * s.HalfSize.z;
            s.Bounds = new Bounds(center, extent * 2f);
            return s;
        }

        public Vector3 Axis(int i) => i == 0 ? X : i == 1 ? Y : Z;
        public float ProjectRadius(Vector3 axis) =>
            Mathf.Abs(Vector3.Dot(X, axis)) * HalfSize.x +
            Mathf.Abs(Vector3.Dot(Y, axis)) * HalfSize.y +
            Mathf.Abs(Vector3.Dot(Z, axis)) * HalfSize.z;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    internal static bool Overlaps(Shape a, Shape b)
    {
        if (!a.Bounds.Intersects(b.Bounds)) return false;
        if (!a.IsBox && !b.IsBox)
        {
            float radius = a.Radius + b.Radius;
            return (a.Center - b.Center).sqrMagnitude <= radius * radius;
        }
        if (!a.IsBox) return SphereBox(a, b);
        if (!b.IsBox) return SphereBox(b, a);
        Vector3 delta = b.Center - a.Center;
        for (int i = 0; i < 3; i++)
        {
            if (Separated(a, b, delta, a.Axis(i)) || Separated(a, b, delta, b.Axis(i))) return false;
            for (int j = 0; j < 3; j++)
                if (Separated(a, b, delta, Vector3.Cross(a.Axis(i), b.Axis(j)))) return false;
        }
        return true;
    }

    private static bool SphereBox(Shape sphere, Shape box)
    {
        Vector3 delta = sphere.Center - box.Center;
        Vector3 closest = box.Center;
        for (int i = 0; i < 3; i++)
            closest += box.Axis(i) * Mathf.Clamp(Vector3.Dot(delta, box.Axis(i)), -box.HalfSize[i], box.HalfSize[i]);
        return (sphere.Center - closest).sqrMagnitude <= sphere.Radius * sphere.Radius;
    }

    private static bool Separated(Shape a, Shape b, Vector3 delta, Vector3 axis)
    {
        if (axis.sqrMagnitude < 1e-12f) return false;
        return Mathf.Abs(Vector3.Dot(delta, axis)) > a.ProjectRadius(axis) + b.ProjectRadius(axis);
    }

    internal static bool OverlapsTetrahedron(Shape zone, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 min = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
        Vector3 max = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));
        if (!zone.Bounds.Intersects(new Bounds((min + max) * 0.5f, max - min))) return false;
        if (!zone.IsBox)
        {
            Vector3 p = zone.Center;
            float determinant = Vector3.Dot(b - a, Vector3.Cross(c - a, d - a));
            if (Mathf.Abs(determinant) > 1e-12f)
            {
                float u = Vector3.Dot(p - a, Vector3.Cross(c - a, d - a)) / determinant;
                float v = Vector3.Dot(b - a, Vector3.Cross(p - a, d - a)) / determinant;
                float w = Vector3.Dot(b - a, Vector3.Cross(c - a, p - a)) / determinant;
                if (u >= 0f && v >= 0f && w >= 0f && u + v + w <= 1f) return true;
            }
            float r2 = zone.Radius * zone.Radius;
            return TriangleDistanceSquared(p, a, b, c) <= r2 || TriangleDistanceSquared(p, a, b, d) <= r2 ||
                TriangleDistanceSquared(p, a, c, d) <= r2 || TriangleDistanceSquared(p, b, c, d) <= r2;
        }

        // SAT : 3 axes de boite, 4 normales de faces, 6 aretes x 3 axes.
        a -= zone.Center; b -= zone.Center; c -= zone.Center; d -= zone.Center;
        for (int i = 0; i < 3; i++)
            if (TetraSeparated(zone, a, b, c, d, zone.Axis(i))) return false;
        if (TetraSeparated(zone, a, b, c, d, Vector3.Cross(b - a, c - a)) ||
            TetraSeparated(zone, a, b, c, d, Vector3.Cross(b - a, d - a)) ||
            TetraSeparated(zone, a, b, c, d, Vector3.Cross(c - a, d - a)) ||
            TetraSeparated(zone, a, b, c, d, Vector3.Cross(c - b, d - b))) return false;
        for (int edge = 0; edge < 6; edge++)
        {
            Vector3 e = edge == 0 ? b - a : edge == 1 ? c - a : edge == 2 ? d - a :
                edge == 3 ? c - b : edge == 4 ? d - b : d - c;
            for (int i = 0; i < 3; i++)
                if (TetraSeparated(zone, a, b, c, d, Vector3.Cross(e, zone.Axis(i)))) return false;
        }
        return true;
    }

    private static bool TetraSeparated(Shape box, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 axis)
    {
        if (axis.sqrMagnitude < 1e-20f) return false;
        float pa = Vector3.Dot(a, axis), pb = Vector3.Dot(b, axis);
        float pc = Vector3.Dot(c, axis), pd = Vector3.Dot(d, axis);
        float min = Mathf.Min(Mathf.Min(pa, pb), Mathf.Min(pc, pd));
        float max = Mathf.Max(Mathf.Max(pa, pb), Mathf.Max(pc, pd));
        float radius = box.ProjectRadius(axis);
        return min > radius || max < -radius;
    }

    private static float TriangleDistanceSquared(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, normal = Vector3.Cross(ab, ac);
        float normal2 = normal.sqrMagnitude;
        if (normal2 > 1e-20f)
        {
            Vector3 projected = p - normal * (Vector3.Dot(p - a, normal) / normal2);
            if (Vector3.Dot(Vector3.Cross(b - a, projected - a), normal) >= 0f &&
                Vector3.Dot(Vector3.Cross(c - b, projected - b), normal) >= 0f &&
                Vector3.Dot(Vector3.Cross(a - c, projected - c), normal) >= 0f)
                return (p - projected).sqrMagnitude;
        }
        return Mathf.Min(SegmentDistanceSquared(p, a, b),
            Mathf.Min(SegmentDistanceSquared(p, b, c), SegmentDistanceSquared(p, c, a)));
    }

    private static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        float t = edge.sqrMagnitude > 1e-20f ? Mathf.Clamp01(Vector3.Dot(p - a, edge) / edge.sqrMagnitude) : 0f;
        return (p - (a + edge * t)).sqrMagnitude;
    }
}
