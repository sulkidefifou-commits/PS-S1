using UnityEngine;

[DisallowMultipleComponent]
public class PassiveEntity : AbstractEntity
{
    protected virtual void OnEnable()
    {
        RefreshPosition();
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
}