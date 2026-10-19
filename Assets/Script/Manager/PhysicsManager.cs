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

    public Vector3 Gravity => gravity;
    public float SubstepDeltaTime => Time.fixedDeltaTime / Substeps;
    
    [SerializeField, Range(0f, 1f)] private float restitution = 1f;
    public float Restitution
    {
        get
        {
            return restitution;
        }
    }

    [SerializeField, Range(1, 20)] private int substeps = 3; 
    private int Substeps
    {
        get
        {
            return Mathf.Clamp(substeps, 1, 20);
        }
    }

    [SerializeField, Range(1, 20)] private int solverIterations = 6;
    private int SolverIterations
    {
        get
        {
            return Mathf.Clamp(solverIterations, 1, 20);
        }
    }
    
    [Header("Fast Ball Collisions")]
    [SerializeField] private bool adaptiveSubsteps = true;
    [SerializeField, Range(20, 256)] private int maximumAdaptiveSubsteps = 128;
    [SerializeField, Range(0.1f, 1f)] private float maximumTravelPerRadius = 0.5f;
    [SerializeField] private int lastSubstepCount;

    [SerializeField, Min(0f)] private float bounceThreshold = 0.5f;
    private float BounceThreshold
    {
        get
        {
            return Mathf.Abs(bounceThreshold);
        }
    }

    [Header("World Collider Settings")]

    [SerializeField] private bool useWorldBounds = true;

    [SerializeField] private bool captureIncomingEntities = true;

    [SerializeField] private Vector2 worldCenterXZ = Vector2.zero;
    
    [SerializeField] private bool useSpatialHashing = true;
    
    [SerializeField, Min(0.001f)] private float halfWidth = 1.5f;
    
    [SerializeField, Min(0.001f)] private float halfDepth = 2.5f;
    
    [SerializeField] private float floorY = 0f;

    [SerializeField] private bool useCeiling = false;

    [SerializeField, Min(0.001f)] private float containerHeight = 6f;
    

    [Header("Debug")]
    
    [SerializeField] private bool enableDebug = true;

    [SerializeField] private Color floorGizmoColor = Color.cyan;
    
    [SerializeField] private float lastCellSize;

    [SerializeField] private long lastCandidatePairTests;

    [SerializeField] private long lastAllPairTests;
    
    
    private readonly List<ActiveEntity> _allActiveEntitiesRegistered = new List<ActiveEntity>(); 
    // Lecture seule, utilisee pour choisir une boule au debut du toucher.
    public IReadOnlyList<ActiveEntity> ActiveEntities => _allActiveEntitiesRegistered;

    private readonly List<ActiveEntity> _simulationEntities = new List<ActiveEntity>(128); 
    private readonly List<PassiveEntity> _allPassiveEntitiesRegistered = new List<PassiveEntity>();
    private readonly List<PassiveEntity> _passiveEntities = new List<PassiveEntity>(128);
    private readonly List<SoftBodyEntity> _allSoftBodyEntitiesRegistered = new List<SoftBodyEntity>();
    private readonly List<SoftBodyEntity> _softBodyEntities = new List<SoftBodyEntity>(8);
    private readonly List<DetectionEntity> _allDetectionEntitiesRegistered = new List<DetectionEntity>();
    private readonly List<DetectionEntity> _detectionEntities = new List<DetectionEntity>(16);
    private readonly List<MonoBehaviour> _detectionCandidates = new List<MonoBehaviour>(128);

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
    
    public void Register(SoftBodyEntity entity)
    {
        if (entity == null || _allSoftBodyEntitiesRegistered.Contains(entity)) return;
        _allSoftBodyEntitiesRegistered.Add(entity);
    }

    public void Unregister(SoftBodyEntity entity)
    {
        _allSoftBodyEntitiesRegistered.Remove(entity);
    }

    public void Register(DetectionEntity entity)
    {
        if (entity == null || _allDetectionEntitiesRegistered.Contains(entity)) return;
        _allDetectionEntitiesRegistered.Add(entity);
    }

    public void Unregister(DetectionEntity entity)
    {
        _allDetectionEntitiesRegistered.Remove(entity);
    }

    private void ProcessDetections()
    {
        if (_allDetectionEntitiesRegistered.Count == 0) return;
        _detectionEntities.Clear();
        _detectionCandidates.Clear();
        for (int i = 0; i < _allDetectionEntitiesRegistered.Count; i++)
        {
            DetectionEntity zone = _allDetectionEntitiesRegistered[i];
            if (zone == null || !zone.isActiveAndEnabled) continue;
            zone.RefreshDetectionShape();
            _detectionEntities.Add(zone);
            _detectionCandidates.Add(zone);
        }
        AddDetectionCandidates(_allActiveEntitiesRegistered);
        AddDetectionCandidates(_allPassiveEntitiesRegistered);
        AddDetectionCandidates(_allSoftBodyEntitiesRegistered);

        // Calculer toutes les zones avant les callbacks, qui peuvent detruire,
        // creer ou desactiver des entites et donc modifier les registres.
        for (int i = 0; i < _detectionEntities.Count; i++)
            _detectionEntities[i].Evaluate(_detectionCandidates);
        for (int i = 0; i < _detectionEntities.Count; i++)
        {
            DetectionEntity zone = _detectionEntities[i];
            if (zone != null && zone.isActiveAndEnabled) zone.DispatchEvents();
        }
        // Ne pas conserver les references aux objets detruits par un evenement.
        _detectionCandidates.Clear();
        _detectionEntities.Clear();
    }

    private void AddDetectionCandidates<T>(List<T> entities) where T : MonoBehaviour
    {
        for (int i = 0; i < entities.Count; i++)
        {
            T entity = entities[i];
            if (entity != null && entity.isActiveAndEnabled) _detectionCandidates.Add(entity);
        }
    }

    void FixedUpdate()
    {
        _collisionPairs.Clear();
        _pendingContacts.Clear();

        int iterationCount = SolverIterations;
        float maxRadius = PrepareSimulationEntities();
        int stepCount = GetAdaptiveSubstepCount();
        lastSubstepCount = stepCount;
        int entityCount = _simulationEntities.Count;
        PrepareSoftBodyEntities();
        int softBodyCount = _softBodyEntities.Count;
        
        lastCellSize = entityCount > 0 ? (2f * maxRadius) : 0f;
        lastCandidatePairTests = 0;
        lastAllPairTests = ((long)entityCount * (entityCount - 1) / 2 + (long)entityCount * _passiveEntities.Count) * stepCount * iterationCount;

        if (entityCount == 0 && softBodyCount == 0)
        {
            ProcessDetections();
            return;
        }
        
        float dt = Time.fixedDeltaTime / stepCount;

        for (int step = 0; step < stepCount; step++)
        {
            for (int i = 0; i < entityCount; i++)
            {
                ActiveEntity entity = _simulationEntities[i];

                if (entity.IsKinematic) continue;
                if (entity.UsesGravity) entity.Velocity += gravity * dt;
                entity.Position += entity.Velocity * dt;
            }
            
            for (int i = 0; i < softBodyCount; i++) _softBodyEntities[i].BeginSubstep(dt, gravity);

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

                for (int i = 0; i < softBodyCount; i++)
                {
                    _softBodyEntities[i].SolveConstraints(dt);
                    _softBodyEntities[i].SolveEnvironmentContacts();
                }
            }

            for (int i = 0; i < softBodyCount; i++) _softBodyEntities[i].EndSubstep(dt);
        }

        for (int i = 0; i < entityCount; i++)
        {
            ActiveEntity entity = _simulationEntities[i];
            entity.transform.position = entity.Position;
        }


        for (int i = 0; i < softBodyCount; i++) _softBodyEntities[i].UpdateVisual();

        DispatchCollisions();
        ProcessDetections();
    }
    
    private int GetAdaptiveSubstepCount()
    {
        int baseline = Substeps;
        if (!adaptiveSubsteps || _simulationEntities.Count == 0) return baseline;
        float minRadius = float.PositiveInfinity;
        float maxSpeed = 0f;
        for (int i = 0; i < _simulationEntities.Count; i++)
        {
            ActiveEntity entity = _simulationEntities[i];
            minRadius = Mathf.Min(minRadius, entity.Radius);
            if (!entity.IsKinematic) maxSpeed = Mathf.Max(maxSpeed, entity.Velocity.magnitude);
        }
        // Facteur 2 : deux boules peuvent se rapprocher simultanement.
        float travel = (2f * maxSpeed + gravity.magnitude * Time.fixedDeltaTime) * Time.fixedDeltaTime;
        float allowedTravel = Mathf.Max(0.0001f, minRadius * maximumTravelPerRadius);
        int limit = Mathf.Max(baseline, maximumAdaptiveSubsteps);
        float required = travel / allowedTravel;
        if (required >= limit) return limit;
        return Mathf.Clamp(Mathf.CeilToInt(required), baseline, limit);
    }

    private void PrepareSoftBodyEntities()
    {
        _softBodyEntities.Clear();
        for (int i = 0; i < _allSoftBodyEntitiesRegistered.Count; i++)
        {
            SoftBodyEntity entity = _allSoftBodyEntitiesRegistered[i];
            if (entity == null || !entity.isActiveAndEnabled) continue;
            _softBodyEntities.Add(entity);
        }
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

            entity.RefreshCollider();
            _passiveEntities.Add(entity);
        }

        int colliderCount = _simulationEntities.Count;
        if (_hashPositions.Length < colliderCount)
        {
            Array.Resize(ref _hashPositions, Mathf.NextPowerOfTwo(colliderCount));
        }

        return maxRadius;
    }
    
    private void SolveSpatialContacts(float maxRadius)
    {
        int count = _simulationEntities.Count;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            _hashPositions[i] = _simulationEntities[i].Position;
        }
        
        _spatialHash.Build(_hashPositions, count, lastCellSize);

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
                HandleActiveCollision(firstEntity, _simulationEntities[j]);
            }
            
            for (int j = 0; j < _passiveEntities.Count; j++)
            {
                lastCandidatePairTests++;
                HandlePassiveCollision(firstEntity, _passiveEntities[j]);
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
        if (active.IsKinematic) return;
        Vector3 freePositionAxes = new Vector3(active.PositionConstraint.X ? 0f : 1f, active.PositionConstraint.Y ? 0f : 1f, active.PositionConstraint.Z ? 0f : 1f);
        if (!passive.TryGetContact(active.Position, active.Radius, active.Velocity, freePositionAxes, out Vector3 normal, out float penetration, out Vector3 contactPoint)) return;

        Vector3 correction = Vector3.Scale(normal, freePositionAxes);
        float positionResponse = Vector3.Dot(correction, normal);
        if (positionResponse > 0.000001f) active.Position += correction * (penetration / positionResponse);
        RecordCollision(active, passive, contactPoint);

        float normalVelocity = Vector3.Dot(active.Velocity, normal);
        
        if (normalVelocity >= 0f) return;
        Vector3 velocityResponse = new Vector3(active.VelocityConstraint.X ? 0f : normal.x, active.VelocityConstraint.Y ? 0f : normal.y, active.VelocityConstraint.Z ? 0f : normal.z);
        float normalResponse = Vector3.Dot(velocityResponse, normal);
        if (normalResponse <= 0.000001f) return;
        float bounce = -normalVelocity < BounceThreshold ? 0f : restitution;
        active.Velocity -= velocityResponse * ((1f + bounce) * normalVelocity / normalResponse);
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

        // Une boule tenue a une masse inverse nulle : elle ne recoit
        // ni correction de position ni impulsion. L'autre reagit normalement.
        float inverseMass1 = firstEntity.IsKinematic ? 0f : 1f / firstEntity.Mass;
        float inverseMass2 = nextEntity.IsKinematic ? 0f : 1f / nextEntity.Mass;
        Vector3 positionResponse1 = FreeAxes(normal, firstEntity.PositionConstraint) * inverseMass1;
        Vector3 positionResponse2 = FreeAxes(normal, nextEntity.PositionConstraint) * inverseMass2;
        float positionResponse = Vector3.Dot(normal, positionResponse1 + positionResponse2);
        float penetration = radiusSum - distance;
        if (positionResponse > 0.000001f)
        {
            firstEntity.Position -= positionResponse1 * (penetration / positionResponse);
            nextEntity.Position += positionResponse2 * (penetration / positionResponse);
        }

        Vector3 contactPoint = firstEntity.Position + normal * firstEntity.Radius;
        RecordCollision(firstEntity, nextEntity, contactPoint);

        float closingSpeed = Vector3.Dot(firstEntity.Velocity - nextEntity.Velocity, normal);
        if (closingSpeed <= 0f) return;
        Vector3 velocityResponse1 = FreeAxes(normal, firstEntity.VelocityConstraint) * inverseMass1;
        Vector3 velocityResponse2 = FreeAxes(normal, nextEntity.VelocityConstraint) * inverseMass2;
        float velocityResponse = Vector3.Dot(normal, velocityResponse1 + velocityResponse2);
        if (velocityResponse <= 0.000001f) return;

        float bounce = closingSpeed < BounceThreshold ? 0f : Mathf.Clamp01(restitution);
        float impulse = (1f + bounce) * closingSpeed / velocityResponse;
        if (!firstEntity.IsKinematic) firstEntity.Velocity -= velocityResponse1 * impulse;
        if (!nextEntity.IsKinematic) nextEntity.Velocity += velocityResponse2 * impulse;
    }

    private static Vector3 FreeAxes(Vector3 value, ActiveEntity.Constrain constraint)
    {
        return new Vector3(constraint.X ? 0f : value.x,
            constraint.Y ? 0f : value.y, constraint.Z ? 0f : value.z);
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
        if (receiver == null || other == null || !receiver.isActiveAndEnabled) return;

        if (!other.isActiveAndEnabled) return;

        receiver.OnCollision(other, point);
        
    }

    private void HandleWorldCollision(ActiveEntity entity)
    {
        if (!useWorldBounds || entity.IsKinematic) return;

        if (!entity.CollidesWithWorld && !entity.IsContained)
        {
            if (!captureIncomingEntities || !FitsInsideWorld(entity.Position, entity.Radius)) return;
        }
        entity.IsContained = true;

        Vector3 position = entity.Position;
        Vector3 velocity = entity.Velocity;

        
        float radius = entity.Radius;
        
        float minX = worldCenterXZ.x - halfWidth + radius;
        float maxX = worldCenterXZ.x + halfWidth - radius;
        
        float minZ = worldCenterXZ.y - halfDepth + radius;
        float maxZ = worldCenterXZ.y + halfDepth - radius;
        
        float minY = floorY + radius;
        
        ConstrainWorldAxis(ref position.x, ref velocity.x, minX, maxX);
        ConstrainWorldAxis(ref position.z, ref velocity.z, minZ, maxZ);
        if (useCeiling)
        {
            float maxY = floorY + Mathf.Max(0.001f, containerHeight) - radius;
            ConstrainWorldAxis(ref position.y, ref velocity.y, minY, maxY);
        }
        else if (position.y < minY)
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
    
    private const float SoftBodyContactSkin = 0.0001f;

    internal void RefreshSoftBodyPassiveColliders()
    {
        for (int i = 0; i < _allPassiveEntitiesRegistered.Count; i++)
        {
            PassiveEntity passive = _allPassiveEntitiesRegistered[i];
            if (passive != null && passive.isActiveAndEnabled) passive.RefreshCollider();
        }
    }

    internal Vector3 ResolveSoftBodyMotion(Vector3 from, Vector3 target, Vector3 velocity,
        float radius, bool withWorld, bool withPassives)
    {
        if (!withWorld && !withPassives) return target;
        Vector3 position = from;
        ProjectSoftBodyOverlaps(ref position, velocity, radius, withWorld, withPassives);
        Vector3 remaining = target - position;

        if (withPassives)
        {
            for (int slide = 0; slide < 4 && remaining.sqrMagnitude > 0.000000000001f; slide++)
            {
                float firstFraction = 1f;
                Vector3 firstNormal = Vector3.zero;
                bool found = false;
                for (int i = 0; i < _allPassiveEntitiesRegistered.Count; i++)
                {
                    PassiveEntity passive = _allPassiveEntitiesRegistered[i];
                    if (passive == null || !passive.isActiveAndEnabled) continue;
                    if (!SweepSoftBodyParticle(passive, position, remaining, radius,
                        out float fraction, out Vector3 normal) || fraction > firstFraction) continue;
                    firstFraction = fraction;
                    firstNormal = normal;
                    found = true;
                }

                position += remaining * firstFraction;
                if (!found) { remaining = Vector3.zero; break; }
                remaining *= 1f - firstFraction;
                float inward = Vector3.Dot(remaining, firstNormal);
                if (inward < 0f) remaining -= firstNormal * inward;
                else break;
            }
        }
        else position = target;

        ProjectSoftBodyOverlaps(ref position, velocity, radius, withWorld, withPassives);
        return position;
    }

    private void ProjectSoftBodyOverlaps(ref Vector3 position, Vector3 velocity, float radius, bool withWorld, bool withPassives)
    {

        for (int pass = 0; pass < 4; pass++)
        {
            Vector3 before = position;
            if (withPassives)
            {
                for (int i = 0; i < _allPassiveEntitiesRegistered.Count; i++)
                {
                    PassiveEntity passive = _allPassiveEntitiesRegistered[i];
                    if (passive == null || !passive.isActiveAndEnabled) continue;
                    if (passive.TryGetContact(position, radius, velocity, Vector3.one,
                        out Vector3 normal, out float depth, out _) && depth > 0f)
                        position += normal * (depth + SoftBodyContactSkin);
                }
            }
            if (withWorld) position = ProjectSoftBodyPoint(position, radius);
            if ((position - before).sqrMagnitude < 0.000000000001f) break;
        }
    }

    private static bool SweepSoftBodyParticle(PassiveEntity passive, Vector3 from, Vector3 movement, float radius, out float fraction, out Vector3 normal)
    {
        fraction = 0f;
        normal = Vector3.zero;
        float travel = movement.magnitude;
        if (travel <= 0.000001f) return false;
        
        for (int iteration = 0; iteration < 20; iteration++)
        {
            float extraRadius = travel * (1f - fraction) + SoftBodyContactSkin;
            if (!passive.TryGetContact(from + movement * fraction, radius + extraRadius, movement, Vector3.one, out normal, out float depth, out _)) return false;

            float gap = extraRadius - depth;
            float approach = -Vector3.Dot(movement, normal);
            if (approach <= 0.0000001f) return false;
            if (gap <= SoftBodyContactSkin * 1.5f) return true;

            float next = fraction + (gap - SoftBodyContactSkin) / approach;
            if (next > 1f) return false;
            fraction = Mathf.Clamp01(next);
        }
        return true;
    }

    internal void ClipSoftBodyPassiveVelocity(Vector3 position, ref Vector3 velocity, float radius)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _allPassiveEntitiesRegistered.Count; i++)
            {
                PassiveEntity passive = _allPassiveEntitiesRegistered[i];
                if (passive == null || !passive.isActiveAndEnabled) continue;
                if (!passive.TryGetContact(position, radius + SoftBodyContactSkin * 2f, velocity, Vector3.one, out Vector3 normal, out _, out _)) continue;
                float inward = Vector3.Dot(velocity, normal);
                if (inward < 0f) velocity -= normal * inward;
            }
        }
    }
    
    internal Vector3 ProjectSoftBodyPoint(Vector3 position, float radius)
    {
        if (!useWorldBounds) return position;
        GetSoftBodyWorldLimits(radius, out Vector3 min, out Vector3 max);
        position.x = ClampSoftBodyAxis(position.x, min.x, max.x);
        position.y = ClampSoftBodyAxis(position.y, min.y, max.y);
        position.z = ClampSoftBodyAxis(position.z, min.z, max.z);
        return position;
    }

    internal void ClipSoftBodyVelocity(Vector3 position, ref Vector3 velocity, float radius)
    {
        if (!useWorldBounds) return;
        GetSoftBodyWorldLimits(radius, out Vector3 min, out Vector3 max);
        ClipSoftBodyAxisVelocity(position.x, ref velocity.x, min.x, max.x);
        ClipSoftBodyAxisVelocity(position.y, ref velocity.y, min.y, max.y);
        ClipSoftBodyAxisVelocity(position.z, ref velocity.z, min.z, max.z);
    }

    private void GetSoftBodyWorldLimits(float radius, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(worldCenterXZ.x - halfWidth + radius,
            floorY + radius, worldCenterXZ.y - halfDepth + radius);
        max = new Vector3(worldCenterXZ.x + halfWidth - radius,
            useCeiling ? floorY + Mathf.Max(0.001f, containerHeight) - radius : float.PositiveInfinity,
            worldCenterXZ.y + halfDepth - radius);
    }

    private static float ClampSoftBodyAxis(float position, float min, float max)
    {
        return min > max ? (min + max) * 0.5f : Mathf.Clamp(position, min, max);
    }

    private static void ClipSoftBodyAxisVelocity(float position, ref float velocity, float min, float max)
    {
        const float contactTolerance = 0.00001f;
        if (min > max || (position <= min + contactTolerance && velocity < 0f)
            || (position >= max - contactTolerance && velocity > 0f))
            velocity = 0f;
    }

    private bool FitsInsideWorld(Vector3 position, float radius)
    {
        if (radius > halfWidth || radius > halfDepth) return false;
        if (Mathf.Abs(position.x - worldCenterXZ.x) > halfWidth - radius ||
            Mathf.Abs(position.z - worldCenterXZ.y) > halfDepth - radius ||
            position.y < floorY + radius) return false;
        return !useCeiling || position.y <= floorY + Mathf.Max(0.001f, containerHeight) - radius;
    }

    private void ConstrainWorldAxis(ref float position, ref float velocity, float min, float max)
    {
        if (min > max)
        {
            position = (min + max) * 0.5f;
            velocity = 0f;
        }
        else if (position < min)
        {
            position = min;
            if (velocity < 0f) velocity = Bounce(velocity);
        }
        else if (position > max)
        {
            position = max;
            if (velocity > 0f) velocity = Bounce(velocity);
        }
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
        if (!enableDebug || !useWorldBounds) return;
        Color oldColor = Gizmos.color;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.color = floorGizmoColor;
        Gizmos.matrix = Matrix4x4.identity;
        float height = Mathf.Max(0.001f, containerHeight);
        if (useCeiling)
        {
            Gizmos.DrawWireCube(new Vector3(worldCenterXZ.x, floorY + height * 0.5f, worldCenterXZ.y),
                new Vector3(halfWidth * 2f, height, halfDepth * 2f));
        }
        else
        {
            Vector3 a = new Vector3(worldCenterXZ.x - halfWidth, floorY, worldCenterXZ.y - halfDepth);
            Vector3 b = new Vector3(worldCenterXZ.x + halfWidth, floorY, worldCenterXZ.y - halfDepth);
            Vector3 c = new Vector3(worldCenterXZ.x + halfWidth, floorY, worldCenterXZ.y + halfDepth);
            Vector3 d = new Vector3(worldCenterXZ.x - halfWidth, floorY, worldCenterXZ.y + halfDepth);
            Vector3 up = Vector3.up * height;
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            Gizmos.DrawLine(a, a + up); Gizmos.DrawLine(b, b + up);
            Gizmos.DrawLine(c, c + up); Gizmos.DrawLine(d, d + up);
        }
        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

}
