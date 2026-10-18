using UnityEngine;

// Etape 6 : cage physique et maillage visuel detaille lie aux tetraedres.
// Le PhysicsManager pilote la simulation. Le maillage suit les points physiques.
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SoftBodyEntity : MonoBehaviour
{
    [Header("Cube initial")]

    [SerializeField, Min(0.05f)] private float size = 1f;

    [SerializeField, Range(1, 6)] private int subdivisions = 2;

    [SerializeField, Min(0.001f)] private float particleMass = 1f;
    [SerializeField] private Vector3 initialVelocity = Vector3.zero;

    [SerializeField] private bool pinFirstPoint = false;

    [Header("Souplesse XPBD")]

    [SerializeField, Min(0f)] private float edgeCompliance = 0.005f;

    [SerializeField, Min(0f)] private float volumeCompliance = 0f;

    [SerializeField, Min(0f)] private float damping = 0.5f;

    [Header("Contacts avec le decor")]
    [SerializeField] private bool collideWithWorld = true;
    [SerializeField] private bool collideWithPassives = true;

    [SerializeField, Min(0.001f)] private float particleRadius = 0.025f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField, Min(0.001f)] private float pointGizmoRadius = 0.04f;

    [SerializeField] private float currentVolumeRatio = 1f;


    [SerializeField] private float minimumVolumeRatio = 1f;
    [SerializeField] private int particleCount;
    [SerializeField] private int tetrahedronCount;

    private SoftBodyMeshData _data;
    private SoftBodyMeshData _previewData;
    private float _previewSize;
    private int _previewSubdivisions;

    private PhysicsManager _physicsManager;
    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private Mesh _previousMesh;
    private bool _initialized;
    private Vector3 _pinLocalPosition;


    private Vector3[] _positions = new Vector3[0];
    private Vector3[] _previousPositions = new Vector3[0];
    private Vector3[] _velocities = new Vector3[0];
    private float[] _inverseMasses = new float[0];
    private Vector3[] _meshVertices = new Vector3[0];
    private DistanceConstraint[] _distanceConstraints = new DistanceConstraint[0];
    private VolumeConstraint[] _volumeConstraints = new VolumeConstraint[0];

    private int _grabbedParticle = -1;
    private Vector3 _grabTarget;
    private bool _grabVisualDirty;
    public bool IsGrabbed => _grabbedParticle >= 0;
    public bool IsInitialized => _initialized;

    private SoftBodySkin _skin;
    private bool _cageHiddenBySkin;
    private bool _cageRendererWasEnabled;

    internal struct SkinBinding
    {
        public int A, B, C, D;
        public Vector4 Weights;
    }

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null || GetComponent<MeshRenderer>() == null)
        {
            Debug.LogError(name + " : ajouter MeshFilter et MeshRenderer au GameObject du soft body.", this);
            enabled = false;
            return;
        }

        if (TryGetComponent(out LineRenderer oldLine)) oldLine.enabled = false;
        _previousMesh = _meshFilter.sharedMesh;
        ResetSimulation();
    }

    private void OnEnable()
    {
        if (_initialized && _physicsManager != null) _physicsManager.Register(this);
    }

    private void Start()
    {
        _physicsManager = PhysicsManager.Instance;
        if (_physicsManager == null)
        {
            Debug.LogError(name + " : aucun PhysicsManager trouve pour le soft body.", this);
            enabled = false;
            return;
        }

        if (_initialized) _physicsManager.Register(this);
    }

    private void OnDisable()
    {
        EndGrab(Vector3.zero, true);
        if (_physicsManager != null) _physicsManager.Unregister(this);
    }

    [ContextMenu("Reset simulation")]
    public void ResetSimulation()
    {
        if (!Application.isPlaying || _meshFilter == null) return;
        EndGrab(Vector3.zero, true);
        if (_skin != null) _skin.ClearBinding();
        _initialized = false;
        _data = SoftBodyMeshData.CreateCube(size, subdivisions);
        particleCount = _data.Positions.Length;
        tetrahedronCount = _data.Tetrahedra.Length / 4;
        _positions = new Vector3[particleCount];
        _previousPositions = new Vector3[particleCount];
        _velocities = new Vector3[particleCount];
        _inverseMasses = new float[particleCount];
        _meshVertices = new Vector3[particleCount];
        _distanceConstraints = new DistanceConstraint[_data.Edges.Length / 2];
        _volumeConstraints = new VolumeConstraint[tetrahedronCount];

        for (int i = 0; i < _positions.Length; i++)
        {
            Vector3 localPoint = _data.Positions[i];
            _positions[i] = transform.TransformPoint(localPoint);
            _previousPositions[i] = _positions[i];
            _velocities[i] = initialVelocity;
        }
        _pinLocalPosition = _data.Positions[0];
        RefreshInverseMasses();
        if (pinFirstPoint) _velocities[0] = Vector3.zero;

        for (int i = 0; i < tetrahedronCount; i++)
        {
            int offset = i * 4;
            int a = _data.Tetrahedra[offset], b = _data.Tetrahedra[offset + 1];
            int c = _data.Tetrahedra[offset + 2], d = _data.Tetrahedra[offset + 3];
            float restVolume = VolumeConstraint.GetSignedVolume(
                _positions[a], _positions[b], _positions[c], _positions[d]);
            if (Mathf.Abs(restVolume) <= 0.0000000001f)
            {
                Debug.LogError(name + " :cellule trop petite.", this);
                enabled = false;
                return;
            }
            _volumeConstraints[i] = new VolumeConstraint(a, b, c, d, restVolume, volumeCompliance);
        }

        for (int i = 0; i < _distanceConstraints.Length; i++)
        {
            int first = _data.Edges[i * 2];
            int second = _data.Edges[i * 2 + 1];
            float restLength = Vector3.Distance(_positions[first], _positions[second]);
            _distanceConstraints[i] = new DistanceConstraint(first, second, restLength, edgeCompliance);
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "Soft body - spawn auto !" };
            _mesh.MarkDynamic();
        }
        for (int i = 0; i < _meshVertices.Length; i++)
            _meshVertices[i] = transform.InverseTransformPoint(_positions[i]);

        _mesh.Clear(); // Retire les anciens indices avant un changement de subdivision.
        _mesh.vertices = _meshVertices;
        _mesh.triangles = _data.SurfaceTriangles;
        _meshFilter.sharedMesh = _mesh;
        _initialized = true;
        if (_skin != null && _skin.isActiveAndEnabled) _skin.Rebind();
        UpdateVisual();
    }

    private void RefreshInverseMasses()
    {
        float inverseMass = 1f / Mathf.Max(0.001f, particleMass);
        for (int i = 0; i < _inverseMasses.Length; i++)
            _inverseMasses[i] = (i == 0 && pinFirstPoint) || i == _grabbedParticle ? 0f : inverseMass;
    }

    internal void BeginSubstep(float dt, Vector3 gravity)
    {
        if (pinFirstPoint && _grabbedParticle == 0) EndGrab(Vector3.zero, true);
        RefreshInverseMasses();
        if (pinFirstPoint)
        {
            _positions[0] = transform.TransformPoint(_pinLocalPosition);
            _velocities[0] = Vector3.zero;
        }
        if (IsGrabbed)
        {
            _positions[_grabbedParticle] = _grabTarget;
            _velocities[_grabbedParticle] = Vector3.zero;
        }

        for (int i = 0; i < _distanceConstraints.Length; i++)
        {
            _distanceConstraints[i].Compliance = edgeCompliance;
            _distanceConstraints[i].BeginSubstep();
        }
        for (int i = 0; i < _volumeConstraints.Length; i++)
        {
            _volumeConstraints[i].Compliance = volumeCompliance;
            _volumeConstraints[i].BeginSubstep();
        }

        for (int i = 0; i < _positions.Length; i++)
        {
            _previousPositions[i] = _positions[i];
            if (_inverseMasses[i] == 0f) continue;
            _velocities[i] += gravity * dt;
            _positions[i] += _velocities[i] * dt;
        }
    }

    internal void SolveConstraints(float dt)
    {
        for (int i = 0; i < _distanceConstraints.Length; i++)
            _distanceConstraints[i].Solve(_positions, _inverseMasses, dt);
        for (int i = 0; i < _volumeConstraints.Length; i++)
            _volumeConstraints[i].Solve(_positions, _inverseMasses, dt);
    }

    internal void SolveEnvironmentContacts()
    {
        if (!collideWithWorld && !collideWithPassives) return;
        float radius = Mathf.Max(0.001f, particleRadius);
        for (int i = 0; i < _positions.Length; i++)
        {
            if (_inverseMasses[i] == 0f && i != _grabbedParticle) continue;
            _positions[i] = _physicsManager.ResolveSoftBodyMotion(
                _previousPositions[i], _positions[i], _velocities[i], radius,
                collideWithWorld, collideWithPassives);
            if (i == _grabbedParticle) _grabTarget = _positions[i];
        }
    }

    internal void EndSubstep(float dt)
    {
        if (dt <= 0f) return;
        float dampingFactor = Mathf.Exp(-Mathf.Max(0f, damping) * dt);
        float radius = Mathf.Max(0.001f, particleRadius);
        for (int i = 0; i < _positions.Length; i++)
        {
            if (_inverseMasses[i] == 0f) continue; _velocities[i] = (_positions[i] - _previousPositions[i]) / dt;
            if (collideWithPassives) _physicsManager.ClipSoftBodyPassiveVelocity(_positions[i], ref _velocities[i], radius);
            if (collideWithWorld) _physicsManager.ClipSoftBodyVelocity(_positions[i], ref _velocities[i], radius); 
            _velocities[i] *= dampingFactor;
        }
    }

    // Detection du volume de la cage deformee, sans modifier la simulation.
    internal bool OverlapsDetection(DetectionGeometry.Shape zone)
    {
        if (!_initialized || _data == null || _positions.Length == 0) return false;
        Vector3 min = _positions[0], max = _positions[0];
        for (int i = 1; i < _positions.Length; i++)
        {
            min = Vector3.Min(min, _positions[i]);
            max = Vector3.Max(max, _positions[i]);
        }
        if (!zone.Bounds.Intersects(new Bounds((min + max) * 0.5f, max - min))) return false;
        for (int offset = 0; offset < _data.Tetrahedra.Length; offset += 4)
        {
            if (DetectionGeometry.OverlapsTetrahedron(zone,
                _positions[_data.Tetrahedra[offset]], _positions[_data.Tetrahedra[offset + 1]],
                _positions[_data.Tetrahedra[offset + 2]], _positions[_data.Tetrahedra[offset + 3]])) return true;
        }
        return false;
    }

    internal void UpdateVisual()
    {
        if (!_initialized || _mesh == null) return;
        for (int i = 0; i < _meshVertices.Length; i++) _meshVertices[i] = transform.InverseTransformPoint(_positions[i]);
        
        _mesh.vertices = _meshVertices;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        float totalRestVolume = 0f;
        float totalOrientedVolume = 0f;
        minimumVolumeRatio = float.PositiveInfinity;
        for (int i = 0; i < _volumeConstraints.Length; i++)
        {
            int offset = i * 4;
            float volume = VolumeConstraint.GetSignedVolume(_positions[_data.Tetrahedra[offset]], _positions[_data.Tetrahedra[offset + 1]], _positions[_data.Tetrahedra[offset + 2]], _positions[_data.Tetrahedra[offset + 3]]);
            float rest = _volumeConstraints[i].RestVolume;
            minimumVolumeRatio = Mathf.Min(minimumVolumeRatio, volume / rest);
            totalRestVolume += Mathf.Abs(rest);
            totalOrientedVolume += volume * Mathf.Sign(rest);
        }
        currentVolumeRatio = totalRestVolume > 0f ? totalOrientedVolume / totalRestVolume : 0f;
        _grabVisualDirty = false;
        if (_skin != null && _skin.IsBound && _skin.isActiveAndEnabled)
        {
            _skin.UpdateVisual();
            RefreshSkinVisibility();
        }
    }

    private void LateUpdate()
    {
        if (_grabVisualDirty) UpdateVisual();
    }

    public bool TryBeginGrab(int particleIndex, out Vector3 pointPosition)
    {
        pointPosition = Vector3.zero;
        if (!_initialized || !isActiveAndEnabled || _physicsManager == null || IsGrabbed || particleIndex < 0 || particleIndex >= _positions.Length || (particleIndex == 0 && pinFirstPoint)) return false;

        _grabbedParticle = particleIndex;
        _grabTarget = _positions[particleIndex];
        pointPosition = _grabTarget;
        _velocities[particleIndex] = Vector3.zero;
        RefreshInverseMasses();
        return true;
    }

    public Vector3 SetGrabTarget(Vector3 target)
    {
        if (!IsGrabbed) return target;
        if (_physicsManager != null)
        {
            if (collideWithPassives) _physicsManager.RefreshSoftBodyPassiveColliders();
            target = _physicsManager.ResolveSoftBodyMotion(_positions[_grabbedParticle], target, target - _positions[_grabbedParticle], Mathf.Max(0.001f, particleRadius), collideWithWorld, collideWithPassives);
        }

        _grabTarget = target;
        _positions[_grabbedParticle] = target;
        _grabVisualDirty = true;
        return target;
    }

    public void EndGrab(Vector3 releaseVelocity, bool canceled = false)
    {
        if (!IsGrabbed) return;
        int index = _grabbedParticle;
        _grabbedParticle = -1;
        RefreshInverseMasses();
        _velocities[index] = canceled || _inverseMasses[index] == 0f ? Vector3.zero : releaseVelocity;
        if (_physicsManager != null)
        {
            float radius = Mathf.Max(0.001f, particleRadius);
            if (collideWithPassives)
            {
                _physicsManager.RefreshSoftBodyPassiveColliders();
                _physicsManager.ClipSoftBodyPassiveVelocity(_positions[index], ref _velocities[index], radius);
            }
            if (collideWithWorld) _physicsManager.ClipSoftBodyVelocity(_positions[index], ref _velocities[index], radius);
        }
        _grabVisualDirty = true;
    }

    public bool TryRaycast(Ray ray, out float hitDistance, out Vector3 hitPoint, out int particleIndex)
    {
        hitDistance = float.PositiveInfinity;
        hitPoint = Vector3.zero;
        particleIndex = -1;
        if (!_initialized || !isActiveAndEnabled || IsGrabbed) return false;

        if (_skin != null && _skin.IsBound && _skin.isActiveAndEnabled)
        {
            if (!_skin.TryRaycast(ray, out hitDistance, out hitPoint)) return false;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < _positions.Length; i++)
            {
                if (i == 0 && pinFirstPoint) continue;
                float squared = (_positions[i] - hitPoint).sqrMagnitude;
                if (squared >= closest) continue;
                closest = squared;
                particleIndex = i;
            }
            return particleIndex >= 0;
        }

        Vector3 direction = ray.direction.normalized;
        for (int i = 0; i < _data.SurfaceTriangles.Length; i += 3)
        {
            int first = _data.SurfaceTriangles[i];
            int second = _data.SurfaceTriangles[i + 1];
            int third = _data.SurfaceTriangles[i + 2];
            if (!RayTriangle(ray.origin, direction, _positions[first], _positions[second], _positions[third], out float distance) || distance >= hitDistance) continue;

            Vector3 point = ray.origin + direction * distance;
            int selected = -1;
            float closestSquared = float.PositiveInfinity;
            for (int corner = 0; corner < 3; corner++)
            {
                int index = _data.SurfaceTriangles[i + corner];
                if (index == 0 && pinFirstPoint) continue;
                float squared = (_positions[index] - point).sqrMagnitude;
                if (squared >= closestSquared) continue;
                closestSquared = squared;
                selected = index;
            }
            if (selected < 0) continue;
            hitDistance = distance;
            hitPoint = point;
            particleIndex = selected;
        }
        return particleIndex >= 0;
    }

    internal static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 first, Vector3 second, Vector3 third, out float distance)
    {
        distance = 0f;
        Vector3 edge1 = second - first;
        Vector3 edge2 = third - first;
        Vector3 cross = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, cross);
        if (Mathf.Abs(determinant) < 0.00000001f) return false;

        float inverse = 1f / determinant;
        Vector3 offset = origin - first;
        float u = Vector3.Dot(offset, cross) * inverse;
        if (u < 0f || u > 1f) return false;
        Vector3 secondCross = Vector3.Cross(offset, edge1);
        float v = Vector3.Dot(direction, secondCross) * inverse;
        if (v < 0f || u + v > 1f) return false;
        distance = Vector3.Dot(edge2, secondCross) * inverse;
        return distance >= 0f;
    }

    internal bool AttachSkin(SoftBodySkin skin)
    {
        if (_skin != null && _skin != skin) return false;
        _skin = skin;
        return true;
    }

    internal void DetachSkin(SoftBodySkin skin)
    {
        if (_skin != skin) return;
        _skin = null;
        RefreshSkinVisibility();
    }

    internal void RefreshSkinVisibility()
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        bool hide = _skin != null && _skin.IsBound && _skin.isActiveAndEnabled && _skin.HideSimulationMesh;
        if (hide && !_cageHiddenBySkin)
        {
            _cageRendererWasEnabled = renderer.enabled;
            renderer.enabled = false;
            _cageHiddenBySkin = true;
        }
        else if (!hide && _cageHiddenBySkin)
        {
            renderer.enabled = _cageRendererWasEnabled;
            _cageHiddenBySkin = false;
        }
    }

    internal bool TryCreateSkinBinding(Vector3 restLocalPoint, out SkinBinding binding)
    {
        binding = default;
        if (!_initialized) return false;
        for (int offset = 0; offset < _data.Tetrahedra.Length; offset += 4)
        {
            int a = _data.Tetrahedra[offset], b = _data.Tetrahedra[offset + 1];
            int c = _data.Tetrahedra[offset + 2], d = _data.Tetrahedra[offset + 3];
            Vector3 first = _data.Positions[a];
            Vector3 e1 = _data.Positions[b] - first;
            Vector3 e2 = _data.Positions[c] - first;
            Vector3 e3 = _data.Positions[d] - first;
            Vector3 relative = restLocalPoint - first;
            float determinant = Vector3.Dot(e1, Vector3.Cross(e2, e3));
            if (Mathf.Abs(determinant) <= 0.000000000001f) continue;
            float w1 = Vector3.Dot(relative, Vector3.Cross(e2, e3)) / determinant;
            float w2 = Vector3.Dot(e1, Vector3.Cross(relative, e3)) / determinant;
            float w3 = Vector3.Dot(e1, Vector3.Cross(e2, relative)) / determinant;
            float w0 = 1f - w1 - w2 - w3;
            const float tolerance = 0.0001f;
            if (w0 < -tolerance || w1 < -tolerance || w2 < -tolerance || w3 < -tolerance) continue;
            binding = new SkinBinding { A = a, B = b, C = c, D = d,
                Weights = new Vector4(w0, w1, w2, w3) };
            return true;
        }
        return false;
    }

    internal Vector3 EvaluateSkinBinding(SkinBinding binding)
    {
        return _positions[binding.A] * binding.Weights.x
            + _positions[binding.B] * binding.Weights.y
            + _positions[binding.C] * binding.Weights.z
            + _positions[binding.D] * binding.Weights.w;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        SoftBodyMeshData data;
        bool simulated = Application.isPlaying && _initialized;
        if (simulated) data = _data;
        else
        {
            // Cache de la geometrie de previsualisation : aucune reconstruction par image.
            if (_previewData == null || _previewSize != size || _previewSubdivisions != subdivisions)
            {
                _previewData = SoftBodyMeshData.CreateCube(size, subdivisions);
                _previewSize = size;
                _previewSubdivisions = subdivisions;
            }
            data = _previewData;
        }
        Vector3 Point(int index) => simulated ? _positions[index] : transform.TransformPoint(data.Positions[index]);
        Color previousColor = Gizmos.color;
        Gizmos.color = Color.white;
        for (int i = 0; i < data.Edges.Length; i += 2) Gizmos.DrawLine(Point(data.Edges[i]), Point(data.Edges[i + 1]));
        for (int i = 0; i < data.Positions.Length; i++)
        {
            Gizmos.color = i == _grabbedParticle ? Color.magenta : i == 0 && pinFirstPoint ? Color.yellow : Color.cyan;
            Gizmos.DrawSphere(Point(i), Mathf.Max(0.001f, pointGizmoRadius));
        }
        Gizmos.color = previousColor;
    }

    private void OnDestroy()
    {
        if (_mesh == null) return;
        if (_meshFilter != null && _meshFilter.sharedMesh == _mesh)
            _meshFilter.sharedMesh = _previousMesh;
        Destroy(_mesh);
    }
}
