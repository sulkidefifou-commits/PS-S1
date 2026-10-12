using UnityEngine;
using UnityEngine.Rendering;

// A placer sur un enfant visuel du SoftBodyEntity, avec un MeshFilter et un MeshRenderer.
// Chaque sommet visuel suit quatre particules, avec des poids calcules une seule fois.
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SoftBodySkin : MonoBehaviour
{
    [SerializeField] private SoftBodyEntity body;
    [SerializeField] private bool hideSimulationMesh = true;
    [Tooltip("Utile si le materiau utilise une normal map. Necessite des UV sur le mesh.")]
    [SerializeField] private bool recalculateTangents = false;
    [Header("Debug - lecture pendant Play")]
    [SerializeField] private int boundVertexCount;
    [SerializeField] private int outsideVertexCount;

    private SoftBodyEntity _attachedBody;
    private MeshFilter _filter;
    private Mesh _sourceMesh;
    private Mesh _deformedMesh;
    private SoftBodyEntity.SkinBinding[] _bindings;
    private Vector3[] _worldVertices;
    private Vector3[] _localVertices;
    private int[] _triangles;
    private bool _hasUV;

    public bool IsBound { get; private set; }
    internal bool HideSimulationMesh => hideSimulationMesh;

    private void OnEnable()
    {
        _filter = GetComponent<MeshFilter>();
        if (body == null) body = GetComponentInParent<SoftBodyEntity>();
        if (body == null || body.transform == transform || !transform.IsChildOf(body.transform))
        {
            Debug.LogError(name + " : SoftBodySkin doit etre sur un enfant visuel du SoftBodyEntity.", this);
            return;
        }
        if (!body.AttachSkin(this))
        {
            Debug.LogError(name + " : un seul SoftBodySkin actif est accepte par SoftBodyEntity.", this);
            return;
        }
        _attachedBody = body;
        // Si le parent n'a pas encore execute Awake, son ResetSimulation fera la liaison.
        if (_attachedBody.IsInitialized) Rebind();
    }

    [ContextMenu("Rebind visual mesh")]
    public void Rebind()
    {
        if (!Application.isPlaying || _attachedBody == null || !_attachedBody.IsInitialized) return;
        ReleaseGeneratedMesh();
        boundVertexCount = 0;
        outsideVertexCount = 0;
        _sourceMesh = _filter.sharedMesh;
        if (_sourceMesh == null || !_sourceMesh.isReadable)
        {
            Debug.LogError(name + " : fournir un mesh lisible (Read/Write Enabled dans ses reglages d'import).", this);
            _attachedBody.RefreshSkinVisibility();
            return;
        }
        for (int submesh = 0; submesh < _sourceMesh.subMeshCount; submesh++)
        {
            if (_sourceMesh.GetTopology(submesh) == MeshTopology.Triangles) continue;
            Debug.LogError(name + " : le mesh visuel doit contenir uniquement des triangles.", this);
            _attachedBody.RefreshSkinVisibility();
            return;
        }

        Vector3[] sourceVertices = _sourceMesh.vertices;
        _triangles = _sourceMesh.triangles;
        if (sourceVertices.Length == 0 || _triangles.Length == 0)
        {
            Debug.LogError(name + " : le mesh visuel est vide.", this);
            _attachedBody.RefreshSkinVisibility();
            return;
        }
        _bindings = new SoftBodyEntity.SkinBinding[sourceVertices.Length];
        Matrix4x4 visualToBody = _attachedBody.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        for (int i = 0; i < sourceVertices.Length; i++)
        {
            Vector3 restLocalPoint = visualToBody.MultiplyPoint3x4(sourceVertices[i]);
            if (!_attachedBody.TryCreateSkinBinding(restLocalPoint, out _bindings[i])) outsideVertexCount++;
        }
        if (outsideVertexCount > 0)
        {
            Debug.LogError(name + " : " + outsideVertexCount
                + " sommets sortent de la cage. Agrandir Size sur le parent ou reduire/recentrer l'enfant,"
                + " puis utiliser Reset simulation sur le parent.", this);
            _attachedBody.RefreshSkinVisibility();
            return;
        }

        // Clone prive : l'asset importe et les autres instances restent intacts.
        _deformedMesh = Instantiate(_sourceMesh);
        _deformedMesh.name = _sourceMesh.name + " - Soft body instance";
        _deformedMesh.MarkDynamic();
        _filter.sharedMesh = _deformedMesh;
        _worldVertices = new Vector3[sourceVertices.Length];
        _localVertices = new Vector3[sourceVertices.Length];
        _hasUV = _sourceMesh.HasVertexAttribute(VertexAttribute.TexCoord0);
        boundVertexCount = sourceVertices.Length;
        IsBound = true;
        UpdateVisual();
        _attachedBody.RefreshSkinVisibility();
    }

    private void UpdateWorldVertices()
    {
        for (int i = 0; i < _bindings.Length; i++)
            _worldVertices[i] = _attachedBody.EvaluateSkinBinding(_bindings[i]);
    }

    internal void UpdateVisual()
    {
        if (!IsBound || _attachedBody == null || _deformedMesh == null) return;
        UpdateWorldVertices();
        Matrix4x4 worldToVisual = transform.worldToLocalMatrix;
        for (int i = 0; i < _localVertices.Length; i++)
            _localVertices[i] = worldToVisual.MultiplyPoint3x4(_worldVertices[i]);
        _deformedMesh.vertices = _localVertices;
        _deformedMesh.RecalculateNormals();
        if (recalculateTangents && _hasUV) _deformedMesh.RecalculateTangents();
        _deformedMesh.RecalculateBounds();
    }

    internal bool TryRaycast(UnityEngine.Ray ray, out float hitDistance, out Vector3 hitPoint)
    {
        hitDistance = float.PositiveInfinity;
        hitPoint = Vector3.zero;
        if (!IsBound || _attachedBody == null) return false;
        // Lire la pose physique actuelle, meme si FixedUpdate n'a pas encore rafraichi le rendu.
        UpdateWorldVertices();
        Vector3 direction = ray.direction.normalized;
        for (int i = 0; i < _triangles.Length; i += 3)
        {
            if (!SoftBodyEntity.RayTriangle(ray.origin, direction,
                _worldVertices[_triangles[i]], _worldVertices[_triangles[i + 1]],
                _worldVertices[_triangles[i + 2]], out float distance) || distance >= hitDistance) continue;
            hitDistance = distance;
        }
        if (float.IsPositiveInfinity(hitDistance)) return false;
        hitPoint = ray.origin + direction * hitDistance;
        return true;
    }

    internal void ClearBinding()
    {
        ReleaseGeneratedMesh();
        boundVertexCount = 0;
        outsideVertexCount = 0;
        if (_attachedBody != null) _attachedBody.RefreshSkinVisibility();
    }

    private void ReleaseGeneratedMesh()
    {
        IsBound = false;
        if (_deformedMesh == null) return;
        if (_filter != null && _filter.sharedMesh == _deformedMesh) _filter.sharedMesh = _sourceMesh;
        Destroy(_deformedMesh);
        _deformedMesh = null;
    }

    private void OnDisable()
    {
        if (_attachedBody != null) _attachedBody.DetachSkin(this);
        _attachedBody = null;
        ReleaseGeneratedMesh();
        boundVertexCount = 0;
    }

    private void OnDestroy()
    {
        ReleaseGeneratedMesh();
    }
}
