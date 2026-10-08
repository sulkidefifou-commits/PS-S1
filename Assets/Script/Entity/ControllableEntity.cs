using UnityEngine;

[DisallowMultipleComponent]
public class ControllableEntity : ActiveEntity
{
    [Header("Finger Hit Settings")]
    [SerializeField, Range(0f, 1f)] private float hitRestitution = 0.8f;
    [SerializeField, Min(0.01f)] private float maxHitSpeed = 20f;

    public override bool UsesGravity => false;
    public override bool IsKinematic => true;
    public float HitRestitution => Mathf.Clamp01(hitRestitution);
    public bool IsControlled { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        SetObjectAllConstaint(false, false, true);
        Velocity = Vector3.zero;
    }

    public bool BeginControl(Vector3 worldPosition)
    {
        if (_physicsManager == null) _physicsManager = PhysicsManager.Instance;
        if (_physicsManager == null) return false;

        
        worldPosition.z = 0f;
        transform.position = worldPosition;
        Velocity = Vector3.zero;
        IsControlled = true;
        enabled = true;
        _physicsManager.Register(this);
        return true;
    }

    public void MoveControl(Vector3 worldPosition, float elapsedTime)
    {
        if (!IsControlled || !isActiveAndEnabled || _physicsManager == null) return;

        worldPosition.z = 0f;
        Vector3 from = Position;
        Vector3 displacement = worldPosition - from;
        Velocity = Vector3.ClampMagnitude(
            displacement / Mathf.Max(0.001f, elapsedTime),
            Mathf.Max(0.01f, maxHitSpeed));

        if (displacement.sqrMagnitude > 1e-12f)
            _physicsManager.QueueControlledMotion(this, from, worldPosition, Velocity);
        
        Position = worldPosition;
    }

    public void EndControl(bool cancelPendingMotion = false)
    {
        if (cancelPendingMotion && _physicsManager != null)
            _physicsManager.CancelControlledMotions(this);

        IsControlled = false;
        Velocity = Vector3.zero;
        enabled = false;
    }

    protected override void OnDisable()
    {
        IsControlled = false;
        Velocity = Vector3.zero;
        base.OnDisable();
    }
}
