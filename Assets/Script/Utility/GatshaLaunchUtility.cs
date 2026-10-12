using UnityEngine;

// Calculs separes du GameManager pour garder l'ordonnancement lisible.
public static class GatshaLaunchUtility
{
    public static float GetNextDelay(float initialDelay, float minimumDelay,
        float decrement, int every, int limit, long completed)
    {
        long counted = limit > 0 ? System.Math.Min(completed, limit) : completed;
        long reductions = counted / System.Math.Max(1, every);
        double delay = initialDelay - reductions * (double)Mathf.Max(0f, decrement);
        return (float)System.Math.Max(Mathf.Max(0.01f, minimumDelay), delay);
    }

    public static bool TryGetLaunchVelocity(Vector3 start, Vector2 apex,
        Vector3 gravity, float substep, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        float height = apex.y - start.y;
        if (!IsFinite(height) || !IsFinite(apex.x) || !IsFinite(gravity.y) ||
            !IsFinite(gravity.x) || Mathf.Abs(gravity.x) > 0.0001f || gravity.y >= -0.0001f ||
            height <= 0f || !IsFinite(substep) || substep <= 0f) return false;

        float riseTime = Mathf.Sqrt(2f * height / -gravity.y);
        velocity = new Vector3((apex.x - start.x) / riseTime,
            -gravity.y * riseTime, 0f);

        // Le PhysicsManager ajoute la gravite AVANT de deplacer la balle.
        // Cette correction compense la derive de cet Euler semi-implicite.
        velocity.y -= gravity.y * substep * 0.5f;
        return IsFinite(velocity.x) && IsFinite(velocity.y);
    }

    public static bool TryGetSpawnPosition(Camera camera, Plane[] frustumPlanes,
        bool fromLeft, float radius, float offset, out Vector3 position)
    {
        position = Vector3.zero;
        if (camera == null) return false;
        GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
        Plane edge = frustumPlanes[fromLeft ? 0 : 1];
        if ((fromLeft && edge.normal.x <= 0.0001f) ||
            (!fromLeft && edge.normal.x >= -0.0001f)) return false;

        // Distance signee au plan lateral = -(rayon + marge).
        // Toute la sphere est hors ecran, aussi avec une camera perspective.
        position.x = (-edge.distance - radius - Mathf.Max(0.001f, offset)) / edge.normal.x;
        return IsFinite(position.x) && camera.WorldToViewportPoint(position).z > 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
