using UnityEngine;

[DisallowMultipleComponent]
public class PassiveEntity : AbstractEntity
{
    public enum ColliderShape { Sphere, Box }

    [Header("Passive Collider")]
    [SerializeField] private ColliderShape shape = ColliderShape.Sphere;
    
    [SerializeField, Min(0.001f)] private float width = 1f;
    [SerializeField, Min(0.001f)] private float height = 1f;
    [SerializeField, Min(0.001f)] private float length = 1f;

    public ColliderShape Shape => shape;
    public Vector3 BoxSize => new Vector3(Mathf.Max(0.001f, width), Mathf.Max(0.001f, height), Mathf.Max(0.001f, length));

    private Vector3 _halfSize;
    private Vector3 _aabbHalfSize;
    private Quaternion _rotation;
    private Quaternion _inverseRotation;
    
    internal void RefreshCollider()
    {
        RefreshPosition();
        _halfSize = BoxSize * 0.5f;
        _rotation = transform.rotation;
        _inverseRotation = Quaternion.Inverse(_rotation);

        if (shape == ColliderShape.Sphere)
        {
            _aabbHalfSize = Vector3.one * Radius;
            return;
        }

        Vector3 x = _rotation * Vector3.right;
        Vector3 y = _rotation * Vector3.up;
        Vector3 z = _rotation * Vector3.forward;
        _aabbHalfSize = new Vector3(
            Mathf.Abs(x.x) * _halfSize.x + Mathf.Abs(y.x) * _halfSize.y + Mathf.Abs(z.x) * _halfSize.z,
            Mathf.Abs(x.y) * _halfSize.x + Mathf.Abs(y.y) * _halfSize.y + Mathf.Abs(z.y) * _halfSize.z,
            Mathf.Abs(x.z) * _halfSize.x + Mathf.Abs(y.z) * _halfSize.y + Mathf.Abs(z.z) * _halfSize.z);
    }

    internal bool TryGetContact(Vector3 sphereCenter, float sphereRadius,
        Vector3 sphereVelocity, Vector3 freePositionAxes,
        out Vector3 normal, out float penetration, out Vector3 contactPoint)
    {
        normal = Vector3.zero;
        penetration = 0f;
        contactPoint = Vector3.zero;

        Vector3 delta = sphereCenter - Position;
        Vector3 broadHalfSize = _aabbHalfSize + Vector3.one * sphereRadius;
        if (Mathf.Abs(delta.x) > broadHalfSize.x || Mathf.Abs(delta.y) > broadHalfSize.y ||
            Mathf.Abs(delta.z) > broadHalfSize.z) return false;

        if (shape == ColliderShape.Sphere)
        {
            float distanceSquared = delta.sqrMagnitude;
            float radiusSum = sphereRadius + Radius;
            if (distanceSquared > radiusSum * radiusSum) return false;
            float distance = Mathf.Sqrt(distanceSquared);
            if (distance > 0.000001f)
                normal = delta / distance;
            else
            {
                normal = Vector3.Scale(-sphereVelocity, freePositionAxes).normalized;
                if (normal.sqrMagnitude < 0.000001f)
                    normal = freePositionAxes.y > 0f ? Vector3.up :
                        (freePositionAxes.x > 0f ? Vector3.right : Vector3.forward);
            }
            penetration = radiusSum - distance;
            contactPoint = Position + normal * Radius;
            return true;
        }
        
        Vector3 localCenter = _inverseRotation * delta;
        Vector3 closest = new Vector3(
            Mathf.Clamp(localCenter.x, -_halfSize.x, _halfSize.x),
            Mathf.Clamp(localCenter.y, -_halfSize.y, _halfSize.y),
            Mathf.Clamp(localCenter.z, -_halfSize.z, _halfSize.z));
        Vector3 localDelta = localCenter - closest;
        float squaredDistance = localDelta.sqrMagnitude;
        if (squaredDistance > sphereRadius * sphereRadius) return false;

        if (squaredDistance > 0.000000000001f)
        {
            float distance = Mathf.Sqrt(squaredDistance);
            normal = _rotation * (localDelta / distance);
            penetration = sphereRadius - distance;
            contactPoint = Position + _rotation * closest;
            if (Vector3.Dot(Vector3.Scale(normal, freePositionAxes), normal) > 0.000001f)
                return true;
        }


        return TryGetEscapeFace(localCenter, sphereRadius, sphereVelocity, freePositionAxes,
            out normal, out penetration, out contactPoint);
    }

    private bool TryGetEscapeFace(Vector3 localCenter, float sphereRadius, Vector3 sphereVelocity, Vector3 freePositionAxes, out Vector3 normal, out float penetration, out Vector3 contactPoint)
    {
        normal = Vector3.zero;
        penetration = 0f;
        contactPoint = Vector3.zero;
        float bestScore = float.PositiveInfinity;
        float bestSpeed = float.PositiveInfinity;
        if (freePositionAxes.sqrMagnitude < 0.000001f) freePositionAxes = Vector3.one;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 localNormal = Vector3.zero;
                localNormal[axis] = sign;
                Vector3 worldNormal = _rotation * localNormal;
                float response = Vector3.Dot(Vector3.Scale(worldNormal, freePositionAxes), worldNormal);
                if (response <= 0.000001f) continue;

                float depth = _halfSize[axis] - sign * localCenter[axis] + sphereRadius;
                float score = depth * depth / response;
                float speed = Vector3.Dot(sphereVelocity, worldNormal);
                if (score > bestScore || (score == bestScore && speed >= bestSpeed)) continue;
                bestScore = score;
                bestSpeed = speed;
                normal = worldNormal;
                penetration = Mathf.Max(0f, depth);
                Vector3 point = new Vector3(
                    Mathf.Clamp(localCenter.x, -_halfSize.x, _halfSize.x),
                    Mathf.Clamp(localCenter.y, -_halfSize.y, _halfSize.y),
                    Mathf.Clamp(localCenter.z, -_halfSize.z, _halfSize.z));
                point[axis] = sign * _halfSize[axis];
                contactPoint = Position + _rotation * point;
            }
        }
        return !float.IsInfinity(bestScore);
    }

    protected virtual void OnEnable()
    {
        RefreshCollider();
        if (_physicsManager != null) _physicsManager.Register(this);
    }

    protected virtual void Start()
    {
        _physicsManager = PhysicsManager.Instance;
        if (_physicsManager == null)
        {
            Debug.LogError(name + " : aucun PhysicsManager trouve.", this);
            enabled = false;
            return;
        }

        _physicsManager.Register(this);
    }

    protected virtual void OnDisable()
    {
        if (_physicsManager != null) _physicsManager.Unregister(this);
    }
    
    protected virtual void Reset()
    {
        colliderGizmoColor = Color.blue;
    }

    protected override void OnDrawGizmos()
    {
        if (!DebugEnabled) return;
        Color oldColor = Gizmos.color;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.color = colliderGizmoColor;
        if (shape == ColliderShape.Sphere)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
        else
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, BoxSize);
        }
        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }
}
