using UnityEngine;

[DisallowMultipleComponent]
public class ActiveEntity : AbstractEntity
{
    [Header("Entity Physics Settings")]
    [SerializeField] private Vector3 initialVelocity = Vector3.zero;
    [SerializeField, Min(0.001f)] private float mass = 1f;
    public float Mass
    {
        get
        {
            return Mathf.Max(0.001f, mass);
        }
        protected set
        {
            mass = Mathf.Max(0.001f, value);
        }
    }
    
    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer visualRenderer;
    public MeshRenderer VisualRenderer
    {
        get
        {
            return visualRenderer;
        }
    }
    
    [SerializeField] private MeshFilter visualFilter;
    public MeshFilter VisualFilter
    {
        get
        {
            return visualFilter;
        }
    }


    protected bool SetVisual(Mesh sourceMesh, Material sourceMaterial, float scale)
    {
        if (visualFilter == null || visualRenderer == null)
        {
            Debug.LogError(name + " :il faut renseigner Visual Filter et Visual Renderer sur le prefab de boule", this);
            return false;
        }

        if (sourceMesh == null || sourceMaterial == null)
        {
            Debug.LogError(name + " : il faut fournir un Mesh et un Renderer de base", this);
            return false;
        }

        Transform visualTransform = visualFilter.transform;
        if (visualRenderer.transform != visualTransform || (visualTransform != transform && !visualTransform.IsChildOf(transform)))
        {
            Debug.LogError(name + " : les composant visuel doivent etre sur le meme objet de cette boule", this);
            return false;
        }

        visualFilter.sharedMesh = sourceMesh;
        visualRenderer.material = sourceMaterial;
        visualTransform.localScale = Vector3.one * scale;
        visualRenderer.enabled = true;
        return true;
    }

    public Vector3 Velocity { get; set; }

    protected override void Awake()
    {
        base.Awake();
        
        Velocity = initialVelocity;
    }

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
            Debug.LogError(name + " : aucun PhysicsManager trouve", this);
            enabled = false;
            return;
        }

        _physicsManager.Register(this);
    }

    protected virtual void OnDisable()
    {
        if (_physicsManager != null) _physicsManager.Unregister(this);
    }


}