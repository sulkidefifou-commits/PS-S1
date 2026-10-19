using UnityEngine;

public class GatshaBallEntity : ActiveEntity
{
    [Header("Gatsha Ball Settings")]
    [SerializeField, Min(1)] private int level = 1;
    public int Level
    {
        get { return level; }
    }

    private SuikaManager _owner;
    internal SuikaManager Owner
    {
        get
        {
            return _owner;
        }
    }
    
    public bool IsActionBall { get; internal set; }
    internal bool IsInPool { get; set; }
    internal float SpawnedAt { get; set; }

    public override bool CollidesWithWorld => !IsActionBall;

    public bool IsSelected { get; private set; }
    public override bool IsKinematic => IsSelected;
    public override bool UsesGravity => !IsSelected;
    internal bool CanSelect => _initialized && !_mergePending && !IsInPool &&
                               !IsSelected && isActiveAndEnabled;

    private Vector3 _aimAnchor;
    private float _aimStartedAt;
    private Transform _aimVisual;
    private Quaternion _visualRotationAtGrab;
    private Quaternion _defaultVisualLocalRotation;
    private bool _visualRotationCached;

    public override Vector3 Position
    {
        get => IsSelected ? _aimAnchor : base.Position;
        set { if (!IsSelected) base.Position = value; }
    }

    internal bool BeginAim()
    {
        if (!CanSelect) return false;
        _aimAnchor = Position;
        _aimStartedAt = Time.time;
        Velocity = Vector3.zero;
        IsSelected = true;
        _aimVisual = VisualFilter != null ? VisualFilter.transform : transform;
        // Reference fixe : reprendre une boule ne cumule pas les angles du tir precedent.
        _visualRotationAtGrab = _aimVisual.parent != null
            ? _aimVisual.parent.rotation * _defaultVisualLocalRotation
            : _defaultVisualLocalRotation;
        return true;
    }

    internal void Aim(Vector3 launchDirection, float visualAngleOffset)
    {
        if (!IsSelected) return;
        Velocity = Vector3.zero;
        transform.position = _aimAnchor;
        if (_aimVisual != null && launchDirection.sqrMagnitude > 0.000001f)
        {
            float angle = Mathf.Atan2(launchDirection.y, launchDirection.x) * Mathf.Rad2Deg;
            _aimVisual.rotation = Quaternion.AngleAxis(angle + visualAngleOffset, Vector3.forward) *
                                  _visualRotationAtGrab;
        }
    }

    internal void EndAim(Vector3 launchVelocity, bool canceled)
    {
        if (!IsSelected) return;
        transform.position = _aimAnchor;
        // Le temps passe a viser ne consomme pas la duree de vie de la boule.
        SpawnedAt += Mathf.Max(0f, Time.time - _aimStartedAt);
        IsSelected = false;
        Velocity = canceled ? Vector3.zero : launchVelocity;
        _aimVisual = null;
    }

    protected override void OnDisable()
    {
        EndAim(Vector3.zero, true);
        base.OnDisable();
    }

    protected override void Awake()
    {
        SetObjectAllConstaint(false, false, true);
        base.Awake();
        CacheVisualRotation();
        Vector3 position = transform.position;
        position.z = 0f;
        transform.position = position;
    }

    private bool _initialized;
    private bool _mergePending;

    private void CacheVisualRotation()
    {
        // Initialize peut preceder Awake pour un prefab instancie dans le pool inactif.
        if (_visualRotationCached) return;
        Transform visual = VisualFilter != null ? VisualFilter.transform : transform;
        _defaultVisualLocalRotation = visual.localRotation;
        _visualRotationCached = true;
    }
    
    internal bool CanMerge
    {
        get
        {
            return _initialized && !_mergePending && !IsSelected && !IsInPool && isActiveAndEnabled;
        }
    }

    protected override void Start()
    {
        if (!_initialized && !Initialize(SuikaManager.Instance, Mathf.Max(1, level)))
        {
            enabled = false;
            return;
        }

        SetObjectAllConstaint(false, false, true);
        
        base.Start();

        
    }

    internal bool Initialize(SuikaManager manager, int newLevel)
    {
        if (manager == null)
        {
            Debug.LogError(name + " : aucun GameManager trouver ", this);
            return false;
        }

        if (!manager.TryGetValidBallState(newLevel, out SuikaManager.GatshaBallState state)) return false;
        CacheVisualRotation();
        
        if (!SetVisual(state.ModelMesh, state.ModelMatriel, state.Scale)) return false;

        Transform visual = VisualFilter != null ? VisualFilter.transform : transform;
        visual.localRotation = _defaultVisualLocalRotation;
        _owner = manager;
        level = newLevel;
        Mass = state.Mass;
        Radius = state.Radius;
        _mergePending = false;
        IsActionBall = false;
        IsInPool = false;
        IsSelected = false;
        _aimVisual = null;
        SetObjectAllConstaint(false, false, true);
        Velocity = Vector3.zero;
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
