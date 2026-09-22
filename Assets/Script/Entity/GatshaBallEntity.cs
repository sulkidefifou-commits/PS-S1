using UnityEngine;

public class GatshaBallEntity : ActiveEntity
{
    [SerializeField, Min(1)] private int level = 1;
    public int Level
    {
        get { return level; }
    }

    private GameManager _owner;
    internal GameManager Owner
    {
        get
        {
            return _owner;
        }
    }
    
    private bool _initialized;
    private bool _mergePending;
    
    internal bool CanMerge
    {
        get
        {
            return _initialized && !_mergePending && isActiveAndEnabled;
        }
    }

    protected override void Start()
    {
        if (!_initialized && !Initialize(GameManager.Instance, Mathf.Max(1, level)))
        {
            enabled = false;
            return;
        }

        base.Start();
    }

    internal bool Initialize(GameManager manager, int newLevel)
    {
        if (manager == null)
        {
            Debug.LogError(name + " : aucun GameManager trouver ", this);
            return false;
        }

        if (!manager.TryGetValidBallState(newLevel, out GameManager.GatshaBallState state)) return false;
        
        if (!SetVisual(state.ModelMesh, state.ModelMatriel, state.Scale)) return false;

        _owner = manager;
        level = newLevel;
        Mass = state.Mass;
        Radius = state.Radius;
        _mergePending = false;
        _initialized = true;
        return true;
    }

    internal void SetMergePending(bool pending)
    {
        _mergePending = pending;
    }

    public override void OnCollision(AbstractEntity other, Vector3 contactPoint)
    {
        base.OnCollision(other, contactPoint);
        
        if (!CanMerge || _owner == null) return;
        
        if (other is GatshaBallEntity otherBall)
        {
            _owner.TryMerge(this, otherBall, contactPoint);
        }
    }
}