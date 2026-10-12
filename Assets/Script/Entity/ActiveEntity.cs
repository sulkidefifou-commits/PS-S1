using System;
using UnityEngine;

[DisallowMultipleComponent]
public class ActiveEntity : AbstractEntity
{
    [Serializable]
    public class Constrain
    {
        public bool X;
        public bool Y;
        public bool Z;

        public bool HasNoConstrain()
        {
            if (!X && !Y && !Z) return true;
            return false;
        }
        
        public void ResetConstrain()
        {
            X = false;
            Y = false;
            Z = false;
        }

        public void SetConstrain(bool x, bool y, bool z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        
    }
    
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
    
    [SerializeField] private Constrain positionConstraint = new Constrain();

    public Constrain PositionConstraint
    {
        get
        {
            return positionConstraint;
        }
        set
        {
            positionConstraint = value;
            
            
        }
    }
    
    [SerializeField] private Constrain velocityConstraint = new Constrain();

    public Constrain VelocityConstraint
    {
        get
        {
            return velocityConstraint;
        }
        set
        {
            velocityConstraint = value;
        }
    }
    

    private Vector3 _velocity;
    public Vector3 Velocity {
        get
        {
            return _velocity;
        }
        set
        {
            _velocity = new Vector3(
                VelocityConstraint.X ? 0 : value.x,
                VelocityConstraint.Y ? 0 : value.y,
                VelocityConstraint.Z ? 0 : value.z
            );
        } 
    }

    public override Vector3 Position
    {
        get
        {
            return transform.position;
        }
        set
        {
            transform.position = new Vector3(
                PositionConstraint.X ? transform.position.x : value.x,
                PositionConstraint.Y ? transform.position.y : value.y,
                PositionConstraint.Z ? transform.position.z : value.z
                );
            
        }
    }

    public virtual bool UsesGravity => true;
    public virtual bool IsKinematic => false;
    public virtual bool CollidesWithWorld => true;
    
    public bool IsContained { get; internal set; }

    protected override void Awake()
    {
        base.Awake();
        
        Velocity = initialVelocity;
    }

    protected virtual void OnEnable()
    {
        IsContained = false;
        RefreshPosition();
        if (_physicsManager == null) _physicsManager = PhysicsManager.Instance;
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

    protected virtual void SetObjectAllConstaint(bool x, bool y, bool z)
    {
        SetObjectConstaint(PositionConstraint, x, y, z);
        
        SetObjectConstaint(VelocityConstraint, x, y, z);
        
        
    }

    protected virtual void SetObjectConstaint(Constrain constrain, bool x, bool y, bool z)
    {
        constrain.SetConstrain(x, y, z);
    }
}
