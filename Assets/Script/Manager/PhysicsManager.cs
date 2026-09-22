using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PhysicsManager : MonoBehaviour
{
    private static PhysicsManager _instance;
    public static PhysicsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                Debug.LogWarning("!!! PhysicsManager est null !!!");
            }
            
            return _instance;
        }
    }
    
    [Header("Physics Settings")]
    
    [SerializeField] private Vector3 gravity = new Vector3(0, -9.81f, 0);
    
    [SerializeField, Range(0f, 1f)] private float restitution = 1f;
    public float Restitution
    {
        get
        {
            return restitution;
        }
    }

    [SerializeField, Range(1, 20)] private int substeps = 3; //Limitation du nombre de pas effectuer tout les secondes
    private int Substeps
    {
        get
        {
            return Mathf.Clamp(substeps, 1, 20);
        }
    }

    [SerializeField, Range(1, 20)] private int solverIterations = 6; //Nombre de collision tester tout les pas par entity
    private int SolverIterations
    {
        get
        {
            return Mathf.Clamp(solverIterations, 1, 20);
        }
    }
    
    [SerializeField, Min(0f)] private float bounceThreshold = 0.5f;
    private float BounceThreshold
    {
        get
        {
            return Mathf.Abs(bounceThreshold);
        }
    }

    [Header("World Collider Settings")]
    
    [SerializeField] private bool useSpatialHashing = true;
    
    [SerializeField, Min(0.001f)] private float halfWidth = 1.5f;
    
    [SerializeField, Min(0.001f)] private float halfDepth = 2.5f;
    
    [SerializeField] private float floorY = 0f;
    

    [Header("Debug")]
    
    [SerializeField] private bool enableDebug = true;

    [SerializeField] private Color floorGizmoColor = Color.cyan;
    
    [SerializeField] private float lastCellSize;

    [SerializeField] private long lastCandidatePairTests;

    [SerializeField] private long lastAllPairTests;
    
    
    private readonly List<ActiveEntity> _allActiveEntitiesRegistered = new List<ActiveEntity>(); //toute les entiter enregrister au pret du manager
    private readonly List<ActiveEntity> _simulationEntities = new List<ActiveEntity>(128); //toute les entiter retenue, verifier, et active pour l'execussion dans l'update
    private readonly List<PassiveEntity> _allPassiveEntitiesRegistered = new List<PassiveEntity>();
    private readonly List<PassiveEntity> _passiveEntities = new List<PassiveEntity>(128);
    private readonly SpatialHash3D _spatialHash = new SpatialHash3D(128);
    private Vector3[] _hashPositions = new Vector3[128];

    private readonly HashSet<ulong> _collisionPairs = new HashSet<ulong>();
    private readonly List<CollisionContact> _pendingContacts = new List<CollisionContact>(128);

    private struct CollisionContact
    {
        public AbstractEntity First;
        public AbstractEntity Second;
        public Vector3 Point;
    }


    
    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            DestroyImmediate(gameObject);
        }
        else
        {
            _instance = this;
            
            DontDestroyOnLoad(gameObject);
        }
    }

    public void Register(ActiveEntity entity)
    {
        if (entity == null || _allActiveEntitiesRegistered.Contains(entity)) return;
        
        _allActiveEntitiesRegistered.Add(entity);
    }
    
    public void Unregister(ActiveEntity entity)
    {
        _allActiveEntitiesRegistered.Remove(entity);
    }

    public void Register(PassiveEntity entity)
    {
        if (entity == null || _allPassiveEntitiesRegistered.Contains(entity)) return;
        _allPassiveEntitiesRegistered.Add(entity);
    }

    public void Unregister(PassiveEntity entity)
    {
        _allPassiveEntitiesRegistered.Remove(entity);
    }
    
    void FixedUpdate()
    {
        _collisionPairs.Clear();
        _pendingContacts.Clear();

        int stepCount = Substeps;
        int iterationCount = SolverIterations;
        float maxRadius = PrepareSimulationEntities();
        int entityCount = _simulationEntities.Count;
        
        lastCellSize = entityCount > 0 ? (2f * maxRadius) : 0f;
        lastCandidatePairTests = 0;
        lastAllPairTests = ((long)entityCount * (entityCount - 1) / 2 + (long)entityCount * _passiveEntities.Count) * stepCount * iterationCount;

        if (entityCount == 0) return;
        
        float dt = Time.fixedDeltaTime/Substeps;

        for (int step = 0; step < stepCount; step++)
        {
            for (int i = 0; i < entityCount; i++)
            {
                ActiveEntity entity = _simulationEntities[i];

                entity.Velocity += gravity * dt;
                entity.Position += entity.Velocity * dt;
            }
            
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                if (useSpatialHashing)
                {
                    SolveSpatialContacts(maxRadius);
                }
                else
                {
                    SolveAllPairs();
                }
                
                for (int i = 0; i < entityCount; i++)
                {
                    HandleWorldCollision(_simulationEntities[i]);
                }
            }
        }

        for (int i = 0; i < entityCount; i++)
        {
            ActiveEntity entity = _simulationEntities[i];
            entity.transform.position = entity.Position;
        }


        DispatchCollisions();
    }
    
    private float PrepareSimulationEntities()
    {
        _simulationEntities.Clear();
        float maxRadius = 0f;

        for (int i = 0; i < _allActiveEntitiesRegistered.Count; i++)
        {
            ActiveEntity entity = _allActiveEntitiesRegistered[i];
            if (!IsSimulated(entity)) continue;

            _simulationEntities.Add(entity);
            maxRadius = Mathf.Max(maxRadius, entity.Radius);
        }

        _passiveEntities.Clear();
        for (int i = 0; i < _allPassiveEntitiesRegistered.Count; i++)
        {
            PassiveEntity entity = _allPassiveEntitiesRegistered[i];
            if (entity == null || !entity.isActiveAndEnabled) continue;

            entity.RefreshPosition();
            _passiveEntities.Add(entity);
            maxRadius = Mathf.Max(maxRadius, entity.Radius);
        }

        int colliderCount = _simulationEntities.Count + _passiveEntities.Count;
        if (_hashPositions.Length < colliderCount)
        {
            Array.Resize(ref _hashPositions, Mathf.NextPowerOfTwo(colliderCount));
        }

        return maxRadius;
    }
    
    private void SolveSpatialContacts(float maxRadius)
    {
        int count = _simulationEntities.Count;
        int colliderCount = count + _passiveEntities.Count;
        if (count == 0 || colliderCount < 2) return;

        for (int i = 0; i < count; i++)
        {
            _hashPositions[i] = _simulationEntities[i].Position;
        }
        
        for (int i = 0; i < _passiveEntities.Count; i++)
        {
            _hashPositions[count + i] = _passiveEntities[i].Position;
        }

        _spatialHash.Build(_hashPositions, colliderCount, lastCellSize);

        for (int i = 0; i < count; i++)
        {
            ActiveEntity firstEntity = _simulationEntities[i];
            
            float searchRadius = firstEntity.Radius + maxRadius;
            int neighbourCount = _spatialHash.Query(i, searchRadius);

            for (int neighbour = 0; neighbour < neighbourCount; neighbour++)
            {
                int j = _spatialHash.QueryIds[neighbour];
                
                if (j <= i) continue;

                lastCandidatePairTests++;
                if (j < count)
                {
                    HandleActiveCollision(firstEntity, _simulationEntities[j]);
                }
                else
                {
                    HandlePassiveCollision(firstEntity, _passiveEntities[j - count]);
                }
            }
        }
    }
    
    private void SolveAllPairs()
    {
        for (int i = 0; i < _simulationEntities.Count; i++)
        {
            for (int j = i + 1; j < _simulationEntities.Count; j++)
            {
                lastCandidatePairTests++;
                HandleActiveCollision(_simulationEntities[i], _simulationEntities[j]);
            }

            for (int j = 0; j < _passiveEntities.Count; j++)
            {
                lastCandidatePairTests++;
                HandlePassiveCollision(_simulationEntities[i], _passiveEntities[j]);
            }
        }
    }

    private void HandlePassiveCollision(ActiveEntity active, PassiveEntity passive)
    {
        Vector3 delta = active.Position - passive.Position;
        float distanceSquared = delta.sqrMagnitude;
        float radiusSum = active.Radius + passive.Radius;
        if (distanceSquared > radiusSum * radiusSum) return;

        float distance;
        Vector3 normal;
        if (distanceSquared > 1e-12f)
        {
            distance = Mathf.Sqrt(distanceSquared);
            normal = delta / distance;
        }
        else
        {
   
            distance = 0f;
            
            float speedSquared = active.Velocity.sqrMagnitude;
            
            normal = speedSquared > 1e-12f ? -active.Velocity / Mathf.Sqrt(speedSquared) : Vector3.up;
        }
        
        active.Position += normal * (radiusSum - distance);
        
        Vector3 contactPoint = passive.Position + normal * passive.Radius;
        RecordCollision(active, passive, contactPoint);

        float normalVelocity = Vector3.Dot(active.Velocity, normal);
        
        if (normalVelocity >= 0f) return;

        float bounce = -normalVelocity < BounceThreshold ? 0f : restitution;
        
        active.Velocity -= normal * ((1f + bounce) * normalVelocity);
    }
    
    private void HandleActiveCollision(ActiveEntity firstEntity, ActiveEntity nextEntity)
    {
        Vector3 delta = nextEntity.Position - firstEntity.Position;
        float distanceSquared = delta.sqrMagnitude;
        float radiusSum = firstEntity.Radius + nextEntity.Radius;
        
        if (distanceSquared > radiusSum * radiusSum) return;

        float distance;
        Vector3 normal;

        if (distanceSquared > 1e-12f)
        {
            distance = Mathf.Sqrt(distanceSquared);
            normal = delta / distance;
        }
        else
        {
            distance = 0f;
            
            Vector3 relativeVelocity = firstEntity.Velocity - nextEntity.Velocity;
            
            float relativeSpeedSquared = relativeVelocity.sqrMagnitude;
            
            if (relativeSpeedSquared > 1e-12f)
            {
                normal = relativeVelocity / Mathf.Sqrt(relativeSpeedSquared);
            }
            else
            {
                normal = Vector3.right;
            }
        }

        float m1 = firstEntity.Mass;
        float m2 = nextEntity.Mass;
        
        float penetration = radiusSum - distance;
        
        float weight1 = m2 / (m1 + m2);
        float weight2 = m1 / (m1 + m2);
        
        firstEntity.Position -= normal * (penetration * weight1);
        nextEntity.Position += normal * (penetration * weight2);

        Vector3 contactPoint = firstEntity.Position + normal * firstEntity.Radius;
        RecordCollision(firstEntity, nextEntity, contactPoint);
        
        float v1 = Vector3.Dot(firstEntity.Velocity, normal);
        float v2 = Vector3.Dot(nextEntity.Velocity, normal);
        
        float closingSpeed = v1 - v2;
        
        if (closingSpeed <= 0f) return;
        
        float seuil = closingSpeed < bounceThreshold ? 0f : restitution;
        
        float newV1 = (m1 * v1 + m2 * v2 - m2 * (v1 - v2) * seuil) / (m1 + m2);
        float newV2 = (m1 * v1 + m2 * v2 - m1 * (v2 - v1) * seuil) / (m1 + m2);
        
        firstEntity.Velocity += normal * (newV1 - v1);
        nextEntity.Velocity += normal * (newV2 - v2);
    }

    private void RecordCollision(AbstractEntity first, AbstractEntity second, Vector3 point)
    {
        
        uint firstId = unchecked((uint)first.GetInstanceID());
        uint secondId = unchecked((uint)second.GetInstanceID());
        uint minId = firstId < secondId ? firstId : secondId;
        uint maxId = firstId < secondId ? secondId : firstId;
        ulong pairKey = ((ulong)minId << 32) | maxId;


        if (!_collisionPairs.Add(pairKey)) return;

        _pendingContacts.Add(new CollisionContact { First = first, Second = second, Point = point });
    }

    private void DispatchCollisions()
    {
        for (int i = 0; i < _pendingContacts.Count; i++)
        {
            CollisionContact contact = _pendingContacts[i];
            NotifyCollision(contact.First, contact.Second, contact.Point);
            NotifyCollision(contact.Second, contact.First, contact.Point);
        }

        _pendingContacts.Clear();
    }

    private static void NotifyCollision(AbstractEntity receiver, AbstractEntity other, Vector3 point)
    {
        if (receiver == null || other == null || !receiver.isActiveAndEnabled || !other.isActiveAndEnabled) return;

        receiver.OnCollision(other, point);
        
    }

    private void HandleWorldCollision(ActiveEntity entity)
    {
        Vector3 position = entity.Position;
        Vector3 velocity = entity.Velocity;
        float radius = entity.Radius;
        
        float minX = -halfWidth + radius;
        float maxX = halfWidth - radius;
        
        float minZ = -halfDepth + radius;
        float maxZ = halfDepth - radius;
        
        float minY = floorY + radius;
        
        if (position.x < minX)
        {
            position.x = minX;
            
            if (velocity.x < 0f)
            {
                velocity.x = Bounce(velocity.x);
            }
        }
        else if (position.x > maxX)
        {
            position.x = maxX;
            
            if (velocity.x > 0f)
            {
                velocity.x = Bounce(velocity.x);
            }
        }

        if (position.z < minZ)
        {
            position.z = minZ;
            
            if (velocity.z < 0f)
            {
                velocity.z = Bounce(velocity.z);
            }
        }
        else if (position.z > maxZ)
        {
            position.z = maxZ;
            
            if (velocity.z > 0f)
            {
                velocity.z = Bounce(velocity.z);
            }
        }
        
        if (position.y < minY)
        {
            position.y = minY;
            
            if (velocity.y < 0f)
            {
                velocity.y = Bounce(velocity.y);
            }
        }

        entity.Position = position;
        entity.Velocity = velocity;
        
    }
    
    private static bool IsSimulated(ActiveEntity entity)
    {
        return entity && entity.isActiveAndEnabled;
    }
    
    private float Bounce(float normalVelocity)
    {
        float seuil = Mathf.Abs(normalVelocity) < bounceThreshold ? 0f : restitution;
        return -normalVelocity * seuil;
    }

    
    
    private void OnDrawGizmos()
    {
        if (!enableDebug) return;
        
        Gizmos.color = floorGizmoColor;
        Gizmos.DrawWireCube(new Vector3(0f, floorY, 0f), new Vector3(halfWidth * 2f, 0f, halfDepth * 2f));
    }

}
