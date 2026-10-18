using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class DetectionEntity : AbstractEntity
{
    public enum DetectionShape { Sphere, Box }

    [Flags]
    public enum EntityTypes
    {
        None = 0,
        Active = 1,
        Passive = 2,
        SoftBody = 4,
        Detection = 8,
        Everything = Active | Passive | SoftBody | Detection
    }

    // SoftBodyEntity derive de MonoBehaviour, contrairement aux autres entites.
    [Serializable]
    public class EntityEvent : UnityEvent<MonoBehaviour> { }

    [Header("Detection Shape")]
    [SerializeField] private DetectionShape shape = DetectionShape.Sphere;
    [SerializeField, Min(0.001f)] private float width = 1f;
    [SerializeField, Min(0.001f)] private float height = 1f;
    [SerializeField, Min(0.001f)] private float length = 1f;

    [Header("Detection Filters")]
    [SerializeField] private EntityTypes detectedTypes = EntityTypes.Active;
    [SerializeField] private LayerMask detectedLayers = ~0;
    [Tooltip("Liste vide : tous les tags. Sinon, correspondance exacte avec au moins une entree.")]
    [SerializeField] private string[] allowedTags = new string[0];
    [Tooltip("Liste vide : tous les noms. Comparaison exacte, sensible aux majuscules.")]
    [SerializeField] private string[] allowedNames = new string[0];
    [Tooltip("Retire uniquement le suffixe (Clone) avant de comparer le nom.")]
    [SerializeField] private bool ignoreCloneSuffix = true;

    [Header("Detection Events")]
    [SerializeField] private EntityEvent onEnter = new EntityEvent();
    [SerializeField] private EntityEvent onStay = new EntityEvent();
    [SerializeField] private EntityEvent onExit = new EntityEvent();

    public EntityEvent OnEnter => onEnter;
    public EntityEvent OnStay => onStay;
    public EntityEvent OnExit => onExit;
    public EntityTypes DetectedTypes { get => detectedTypes; set => detectedTypes = value; }
    public LayerMask DetectedLayers { get => detectedLayers; set => detectedLayers = value; }
    public int DetectedCount => _inside.Count;
    public bool Contains(MonoBehaviour entity) => entity != null && _inside.Contains(entity);
    public DetectionShape Shape => shape;
    public Vector3 BoxSize => new Vector3(Mathf.Max(0.001f, width), Mathf.Max(0.001f, height), Mathf.Max(0.001f, length));

    private HashSet<MonoBehaviour> _inside = new HashSet<MonoBehaviour>();
    private HashSet<MonoBehaviour> _next = new HashSet<MonoBehaviour>();
    private readonly HashSet<MonoBehaviour> _notified = new HashSet<MonoBehaviour>();
    private readonly List<PendingEvent> _events = new List<PendingEvent>(32);
    private DetectionGeometry.Shape _geometry;
    private int _generation;
    private int _evaluatedGeneration;

    private enum EventKind { Enter, Stay, Exit }
    private struct PendingEvent
    {
        public MonoBehaviour Entity;
        public EventKind Kind;
    }

    protected virtual void OnEnable()
    {
        RefreshDetectionShape();
        if (_physicsManager != null) _physicsManager.Register(this);
    }

    protected virtual void Start()
    {
        _physicsManager = PhysicsManager.Instance;
        if (_physicsManager == null)
        {
            Debug.LogError(name + " : aucun PhysicsManager trouve pour la detection.", this);
            enabled = false;
            return;
        }
        _physicsManager.Register(this);
    }

    protected virtual void OnDisable()
    {
        if (_physicsManager != null) _physicsManager.Unregister(this);
        // Une zone desactivee cesse d'emettre. Reactivation : nouveaux Enter.
        _generation++;
        _inside.Clear();
        _next.Clear();
        _notified.Clear();
        _events.Clear();
    }

    internal void RefreshDetectionShape()
    {
        RefreshPosition();
        // Comme PassiveEntity : dimensions en unites monde, scale ignore.
        _geometry = shape == DetectionShape.Box
            ? DetectionGeometry.Shape.Box(Position, transform.rotation, BoxSize)
            : DetectionGeometry.Shape.Sphere(Position, Radius);
    }

    internal void Evaluate(List<MonoBehaviour> candidates)
    {
        _evaluatedGeneration = _generation;
        _events.Clear();
        _next.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            MonoBehaviour other = candidates[i];
            if (!Accepts(other) || !Overlaps(other) || !_next.Add(other)) continue;
            _events.Add(new PendingEvent { Entity = other, Kind = _notified.Contains(other) ? EventKind.Stay : EventKind.Enter });
        }
        foreach (MonoBehaviour other in _notified)
            if (!_next.Contains(other)) _events.Add(new PendingEvent { Entity = other, Kind = EventKind.Exit });

        HashSet<MonoBehaviour> previous = _inside;
        _inside = _next;
        _next = previous;
        _next.Clear();
    }

    internal void DispatchEvents()
    {
        // Aucune callback pendant le parcours des registres ou les calculs physiques.
        for (int i = 0; i < _events.Count; i++)
        {
            if (this == null || !isActiveAndEnabled || _generation != _evaluatedGeneration) break;
            PendingEvent entry = _events[i];
            if (entry.Kind != EventKind.Exit && (entry.Entity == null || !entry.Entity.isActiveAndEnabled)) continue;
            try
            {
                switch (entry.Kind)
                {
                    case EventKind.Enter:
                        _notified.Add(entry.Entity);
                        onEnter.Invoke(entry.Entity);
                        break;
                    case EventKind.Stay: onStay.Invoke(entry.Entity); break;
                    case EventKind.Exit:
                        _notified.Remove(entry.Entity);
                        onExit.Invoke(entry.Entity);
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
        _events.Clear();
    }

    private bool Accepts(MonoBehaviour other)
    {
        if (other == null || other == this || !other.isActiveAndEnabled) return false;
        EntityTypes type;
        if (other is DetectionEntity) type = EntityTypes.Detection;
        else if (other is SoftBodyEntity) type = EntityTypes.SoftBody;
        else if (other is ActiveEntity) type = EntityTypes.Active;
        else if (other is PassiveEntity) type = EntityTypes.Passive;
        else return false;
        if ((detectedTypes & type) == 0 || (detectedLayers.value & (1 << other.gameObject.layer)) == 0) return false;
        if (allowedTags != null && allowedTags.Length > 0 && !Matches(allowedTags, other.gameObject.tag, false)) return false;
        return allowedNames == null || allowedNames.Length == 0 || Matches(allowedNames, other.gameObject.name, ignoreCloneSuffix);
    }

    private static bool Matches(string[] allowed, string value, bool stripClone)
    {
        const string suffix = "(Clone)";
        int count = value.Length;
        if (stripClone && value.EndsWith(suffix, StringComparison.Ordinal)) count -= suffix.Length;
        for (int i = 0; i < allowed.Length; i++)
        {
            string entry = allowed[i];
            if (!string.IsNullOrEmpty(entry) && entry.Length == count &&
                string.CompareOrdinal(value, 0, entry, 0, count) == 0) return true;
        }
        return false;
    }

    private bool Overlaps(MonoBehaviour other)
    {
        if (other is SoftBodyEntity softBody) return softBody.OverlapsDetection(_geometry);
        if (other is DetectionEntity detection) return DetectionGeometry.Overlaps(_geometry, detection._geometry);
        if (other is PassiveEntity passive)
        {
            DetectionGeometry.Shape target = passive.Shape == PassiveEntity.ColliderShape.Box
                ? DetectionGeometry.Shape.Box(passive.transform.position, passive.transform.rotation, passive.BoxSize)
                : DetectionGeometry.Shape.Sphere(passive.transform.position, passive.Radius);
            return DetectionGeometry.Overlaps(_geometry, target);
        }
        if (other is ActiveEntity active)
            return DetectionGeometry.Overlaps(_geometry, DetectionGeometry.Shape.Sphere(active.Position, active.Radius));
        return false;
    }

    protected virtual void Reset() => colliderGizmoColor = Color.green;

    protected override void OnDrawGizmos()
    {
        if (!DebugEnabled) return;
        Color oldColor = Gizmos.color;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.color = colliderGizmoColor;
        if (shape == DetectionShape.Sphere)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
        else
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, BoxSize);
        }
        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }
}
