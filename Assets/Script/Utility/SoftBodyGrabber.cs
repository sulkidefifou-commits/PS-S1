using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Utilise le premier contact choisi par ton InputManager (doigt ou souris simulee).
// Un rayon choisit la surface ; le geste deplace ensuite un sommet dans un plan face camera.
[DisallowMultipleComponent]
public class SoftBodyGrabber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private SoftBodyEntity[] targets = new SoftBodyEntity[0];

    [Header("Selection")]
    [SerializeField, Min(0.01f)] private float maximumPickDistance = 100f;
    [SerializeField] private bool ignoreTouchesOverUI = true;

    [Header("Lancer au relachement")]
    [SerializeField, Min(0f)] private float throwMultiplier = 1f;
    [SerializeField, Min(0f)] private float maximumThrowSpeed = 12f;
    [SerializeField, Min(0f)] private float velocitySmoothing = 0.04f;

    private InputManager _subscribedInput;
    private SoftBodyEntity _body;
    private bool _dragging;
    private UnityEngine.Plane _dragPlane;
    private Vector3 _grabOffset;
    private Vector3 _lastTarget;
    private double _lastSampleTime;
    private Vector3 _estimatedVelocity;

    private EventSystem _pointerEventSystem;
    private PointerEventData _pointerData;
    private readonly List<RaycastResult> _uiHits = new List<RaycastResult>(16);

    private void Awake()
    {
        if (gameplayCamera == null) gameplayCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (inputManager == null || gameplayCamera == null)
        {
            Debug.LogError(name + " : pas d'input manager ou de gamemanager dans SoftBodyGrabber", this);
            enabled = false;
            return;
        }
        if (targets == null || targets.Length == 0)
            Debug.LogWarning(name + " : Attention aucun softbody n'est renseigner dans la list du physics manager.", this);

        _subscribedInput = inputManager;
        _subscribedInput.PrimaryContactStarted += ContactStarted;
        _subscribedInput.PrimaryContactMoved += ContactMoved;
        _subscribedInput.PrimaryContactEnded += ContactEnded;
    }

    private void OnDisable()
    {
        if (_subscribedInput != null)
        {
            _subscribedInput.PrimaryContactStarted -= ContactStarted;
            _subscribedInput.PrimaryContactMoved -= ContactMoved;
            _subscribedInput.PrimaryContactEnded -= ContactEnded;
        }
        _subscribedInput = null;
        Release(true);
    }

    private void Update()
    {
        if (!_dragging) return;
        if (_body == null || !_body.isActiveAndEnabled || !_body.IsGrabbed || gameplayCamera == null || !gameplayCamera.isActiveAndEnabled || _subscribedInput == null || !_subscribedInput.isActiveAndEnabled || Time.timeScale <= 0f) Release(true);
    }

    private void ContactStarted(Vector2 screenPosition, double sampleTime)
    {
        Release(true);
        if (Time.timeScale <= 0f || gameplayCamera == null || !gameplayCamera.isActiveAndEnabled || !gameplayCamera.pixelRect.Contains(screenPosition) || targets == null || (ignoreTouchesOverUI && IsOverUI(screenPosition))) return;

        Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
        float closest = Mathf.Max(0.01f, maximumPickDistance);
        SoftBodyEntity selected = null;
        Vector3 selectedHit = Vector3.zero;
        int selectedParticle = -1;
        for (int i = 0; i < targets.Length; i++)
        {
            SoftBodyEntity candidate = targets[i];
            if (candidate == null || (gameplayCamera.cullingMask & (1 << candidate.gameObject.layer)) == 0) continue;
            if (!candidate.TryRaycast(ray, out float distance, out Vector3 point, out int particle) || distance >= closest) continue;
            selected = candidate;
            selectedHit = point;
            selectedParticle = particle;
            closest = distance;
        }
        if (selected == null || !selected.TryBeginGrab(selectedParticle, out Vector3 particlePosition)) return;

        _body = selected;
        _dragging = true;
        _dragPlane = new Plane(gameplayCamera.transform.forward, selectedHit);

        _grabOffset = particlePosition - selectedHit;
        _lastTarget = particlePosition;
        _lastSampleTime = sampleTime;
        _estimatedVelocity = Vector3.zero;
    }

    private void ContactMoved(Vector2 screenPosition, double sampleTime)
    {
        if (!_dragging) return;
        if (_body == null || !_body.isActiveAndEnabled || !_body.IsGrabbed || gameplayCamera == null
            || Time.timeScale <= 0f)
        {
            Release(true);
            return;
        }

        Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
        if (!_dragPlane.Raycast(ray, out float distance)) return;
        Vector3 target = _body.SetGrabTarget(ray.GetPoint(distance) + _grabOffset);
        double elapsed = sampleTime - _lastSampleTime;

        if (elapsed <= 0.000001) return;

        float dt = (float)elapsed;
        if (dt > 0.25f)
        {
            _estimatedVelocity = Vector3.zero;
        }
        else
        {
            Vector3 measuredVelocity = (target - _lastTarget) / dt;
            float blend = velocitySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / velocitySmoothing);
            _estimatedVelocity = Vector3.Lerp(_estimatedVelocity, measuredVelocity, blend);
        }
        _lastTarget = target;
        _lastSampleTime = sampleTime;
    }

    private void ContactEnded(bool canceled)
    {
        Release(canceled);
    }

    private void Release(bool canceled)
    {
        if (_dragging && _body != null && _body.IsGrabbed)
        {

            bool stale = Time.realtimeSinceStartupAsDouble - _lastSampleTime > 0.12;
            Vector3 velocity = canceled || stale ? Vector3.zero : Vector3.ClampMagnitude(_estimatedVelocity * Mathf.Max(0f, throwMultiplier), Mathf.Max(0f, maximumThrowSpeed));
            _body.EndGrab(velocity, canceled);
        }
        _body = null;
        _dragging = false;
        _estimatedVelocity = Vector3.zero;
    }

    private bool IsOverUI(Vector2 screenPosition)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (_pointerData == null || _pointerEventSystem != events)
        {
            _pointerEventSystem = events;
            _pointerData = new PointerEventData(events);
        }
        _pointerData.Reset();
        _pointerData.position = screenPosition;
        _uiHits.Clear();
        events.RaycastAll(_pointerData, _uiHits);
        for (int i = 0; i < _uiHits.Count; i++) if (_uiHits[i].module is GraphicRaycaster) return true;
        return false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) Release(true);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) Release(true);
    }
}
