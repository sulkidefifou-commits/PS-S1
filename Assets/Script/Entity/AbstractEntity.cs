using UnityEngine;

public abstract class AbstractEntity : MonoBehaviour
{    
    protected PhysicsManager _physicsManager;

    [Header("Entity Collider Settings")]
    [SerializeField, Min(0.001f)] private float _radius = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool enableDebug = true;
    [SerializeField] private bool debugOnContact = false;
    [SerializeField] protected Color colliderGizmoColor = Color.red;

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

    public Vector3 Position { get; set; }

    protected virtual void Awake()
    {
        RefreshPosition();
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