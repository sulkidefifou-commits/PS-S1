using UnityEngine;

public abstract class AbstractEntity : MonoBehaviour
{    
    protected PhysicsManager _physicsManager;

    [Header("Entity Collider Settings")]
    [SerializeField, Min(0.001f)] private float _radius = 0.2f;

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
    
    
    [Header("Debug")]
    [SerializeField] private bool enableDebug = true;
    [SerializeField] private bool debugOnContact = false;
    [SerializeField] protected Color colliderGizmoColor = Color.red;

    
    protected bool DebugEnabled => enableDebug;

    public float Radius
    {
        get
        {
            return Mathf.Max(0.001f, _radius);
        }
        protected set
        {
            _radius = Mathf.Max(0.001f, value);
        }
    }
    
    

    public virtual Vector3 Position { get; set; }

    protected virtual void Awake()
    {
        RefreshPosition();
    }

    
    
    protected bool SetVisual(Mesh sourceMesh, Material sourceMaterial, float scale)
    {
        if (!visualFilter || !visualRenderer)
        {
            return false;
        }

        if (!sourceMesh || !sourceMaterial)
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
        visualRenderer.sharedMaterial = sourceMaterial;
        visualTransform.localScale = Vector3.one * scale;
        visualRenderer.enabled = true;
        return true;
    }
    internal void RefreshPosition()
    {
        Position = transform.position;
    }

    public virtual void OnCollision(AbstractEntity other, Vector3 contactPoint)
    {
        if (debugOnContact && enableDebug)
        {
            Debug.Log(name + " est rentrer en contacte avec  " + other.name + " au point : " + contactPoint);
        }
        
 
    }

    protected virtual void OnDrawGizmos()
    {
        if (!enableDebug) return;

        Gizmos.color = colliderGizmoColor;
        Vector3 center = Application.isPlaying ? Position : transform.position;
        Gizmos.DrawWireSphere(center, Radius);
    }
    
}
