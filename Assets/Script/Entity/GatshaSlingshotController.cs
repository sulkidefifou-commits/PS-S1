using UnityEngine;
using UnityEngine.Rendering;


[DisallowMultipleComponent]
[RequireComponent(typeof(InputManager))]
[DefaultExecutionOrder(-90)]
public class GatshaSlingshotController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private InputManager inputManager;

    [Header("Selection")]
    [SerializeField, Min(0f)] private float grabMarginPixels = 24f;
    [SerializeField, Min(0f)] private float minimumGrabRadiusPixels = 36f;
    [SerializeField, Min(0f)] private float motionAssistSeconds = 0.06f;
    [SerializeField, Min(0f)] private float maximumMotionMarginPixels = 60f;

    [Header("Launch")]
    [SerializeField, Min(0f)] private float launchSpeedPerUnit = 8f;
    [SerializeField, Min(0f)] private float dragDeadZonePixels = 6f;
    [SerializeField] private float visualAngleOffset;

    [Header("Aim Visuals")]
    [SerializeField] private Material aimMaterial;
    [SerializeField] private Color elasticColor = new Color(1f, 0.75f, 0.2f, 1f);
    [SerializeField] private Color arrowColor = new Color(0.2f, 1f, 0.65f, 1f);
    [SerializeField, Min(0.001f)] private float lineWidth = 0.025f;
    [SerializeField, Min(0.001f)] private float arrowHeadLength = 0.18f;

    public GatshaBallEntity SelectedBall => _selected;
    public Vector3 PreviewVelocity { get; private set; }

    private static GatshaSlingshotController _instance;
    private GatshaBallEntity _selected;
    private InputManager _subscribedInput;
    private Vector2 _startScreenPosition;
    private Vector2 _lastScreenPosition;
    private Vector3 _grabOffset;
    private Vector3 _fingerWorld;
    private Vector3 _pull;
    private Plane _aimPlane;
    private bool _gestureActive;
    private LineRenderer _elastic;
    private LineRenderer _shaft;
    private LineRenderer _head;
    private Material _elasticMaterial;
    private Material _arrowMaterial;

    private void Awake()
    {
        if (inputManager == null) inputManager = GetComponent<InputManager>();
        if (gameplayCamera == null) gameplayCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogError("Un seul GatshaSlingshotController doit etre actif.", this);
            enabled = false;
            return;
        }
        _instance = this;
        if (inputManager == null) inputManager = GetComponent<InputManager>();
        _subscribedInput = inputManager;
        if (_subscribedInput == null) return;
        _subscribedInput.PrimaryContactStarted += BeginContact;
        _subscribedInput.PrimaryContactMoved += MoveContact;
        _subscribedInput.PrimaryContactEnded += EndContact;
    }

    private void OnDisable()
    {
        if (_subscribedInput != null)
        {
            _subscribedInput.PrimaryContactStarted -= BeginContact;
            _subscribedInput.PrimaryContactMoved -= MoveContact;
            _subscribedInput.PrimaryContactEnded -= EndContact;
        }
        _subscribedInput = null;
        EndContact(true);
        if (_instance == this) _instance = null;
    }

    private void BeginContact(Vector2 screenPosition, double time)
    {
        if (_gestureActive || Time.timeScale <= 0f) return;
        _gestureActive = true;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null) return;

        GatshaBallEntity ball = PickBall(screenPosition);
        if (ball == null) return;
        _aimPlane = new Plane(Vector3.forward, ball.Position);
        if (!TryGetAimPoint(screenPosition, out Vector3 fingerWorld)) return;
        if (!ball.BeginAim()) return;

        _selected = ball;
        _startScreenPosition = screenPosition;
        _lastScreenPosition = screenPosition;
        _grabOffset = ball.Position - fingerWorld;
        _fingerWorld = fingerWorld;
        _pull = Vector3.zero;
        PreviewVelocity = Vector3.zero;
        EnsureVisuals();
    }

    private GatshaBallEntity PickBall(Vector2 screenPosition)
    {
        PhysicsManager physics = PhysicsManager.Instance;
        if (physics == null) return null;
        float pixelScale = PixelScale;
        GatshaBallEntity best = null;
        bool bestIsDirect = false;
        float bestScore = float.PositiveInfinity;
        var entities = physics.ActiveEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is GatshaBallEntity ball) || !ball.CanSelect) continue;
            Vector3 center = gameplayCamera.WorldToScreenPoint(ball.Position);
            if (center.z <= gameplayCamera.nearClipPlane || center.z >= gameplayCamera.farClipPlane) continue;
            Vector3 edgeX = gameplayCamera.WorldToScreenPoint(ball.Position + gameplayCamera.transform.right * ball.Radius);
            Vector3 edgeY = gameplayCamera.WorldToScreenPoint(ball.Position + gameplayCamera.transform.up * ball.Radius);
            float radiusPixels = Mathf.Max(Vector2.Distance(center, edgeX), Vector2.Distance(center, edgeY));
            Rect view = gameplayCamera.pixelRect;
            if (center.x + radiusPixels < view.xMin || center.x - radiusPixels > view.xMax ||
                center.y + radiusPixels < view.yMin || center.y - radiusPixels > view.yMax) continue;
            Vector3 movingPoint = gameplayCamera.WorldToScreenPoint(ball.Position + ball.Velocity * motionAssistSeconds);
            float motionMargin = Mathf.Min(Vector2.Distance(center, movingPoint), maximumMotionMarginPixels * pixelScale);
            float grabRadius = Mathf.Max(radiusPixels + grabMarginPixels * pixelScale,
                minimumGrabRadiusPixels * pixelScale) + motionMargin;
            float distance = Vector2.Distance(screenPosition, center);
            if (distance > grabRadius) continue;
            
            bool direct = distance <= radiusPixels;
            float score = direct ? distance / Mathf.Max(1f, radiusPixels) : distance - radiusPixels;
            if (best == null || (direct && !bestIsDirect) || (direct == bestIsDirect && score < bestScore))
            {
                best = ball;
                bestIsDirect = direct;
                bestScore = score;
            }
        }
        return best;
    }

    private float PixelScale => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height)) / 720f;

    private bool TryGetAimPoint(Vector2 screenPosition, out Vector3 point)
    {
        point = Vector3.zero;
        if (gameplayCamera == null) return false;
        Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
        if (!_aimPlane.Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    private void MoveContact(Vector2 screenPosition, double time)
    {
        if (!_gestureActive || _selected == null || !_selected.IsSelected) return;
        if (Time.timeScale <= 0f) { EndContact(true); return; }
        _lastScreenPosition = screenPosition;
        if (!TryGetAimPoint(screenPosition, out Vector3 fingerWorld))
        {
            EndContact(true);
            return;
        }
        _fingerWorld = fingerWorld;
        _pull = _selected.Position - (fingerWorld + _grabOffset);
        _pull.z = 0f;
        if (Vector2.Distance(_startScreenPosition, screenPosition) <= dragDeadZonePixels * PixelScale)
            _pull = Vector3.zero;
        PreviewVelocity = _pull * Mathf.Max(0f, launchSpeedPerUnit);
        _selected.Aim(_pull, visualAngleOffset);
    }

    private void EndContact(bool canceled)
    {
        GatshaBallEntity ball = _selected;
        _selected = null;
        _gestureActive = false;
        if (ball != null && ball.IsSelected)
            ball.EndAim(PreviewVelocity, canceled || Time.timeScale <= 0f);
        PreviewVelocity = Vector3.zero;
        _pull = Vector3.zero;
        ShowVisuals(false);
    }

    private void LateUpdate()
    {
        if (_selected == null || !_selected.IsSelected || !_selected.isActiveAndEnabled)
        {
            _selected = null;
            PreviewVelocity = Vector3.zero;
            ShowVisuals(false);
            return;
        }
        if (gameplayCamera == null || _subscribedInput == null || !_subscribedInput.isActiveAndEnabled || Time.timeScale <= 0f)
        {
            EndContact(true);
            return;
        }
        MoveContact(_lastScreenPosition, Time.realtimeSinceStartupAsDouble);
        DrawAim();
    }

    private void OnApplicationFocus(bool focus) { if (!focus) EndContact(true); }
    private void OnApplicationPause(bool paused) { if (paused) EndContact(true); }

    private void OnDestroy()
    {
        if (_elasticMaterial != null) Destroy(_elasticMaterial);
        if (_arrowMaterial != null) Destroy(_arrowMaterial);
        if (_elastic != null) Destroy(_elastic.gameObject);
        if (_shaft != null) Destroy(_shaft.gameObject);
        if (_head != null) Destroy(_head.gameObject);
    }

    private void EnsureVisuals()
    {
        if (_elastic != null) return;
        if (aimMaterial == null)
        {
            Debug.LogWarning("Assigner Aim Material (Unlit) pour afficher l'elastique et la fleche.", this);
            return;
        }
        _elasticMaterial = CreateMaterial(elasticColor);
        _arrowMaterial = CreateMaterial(arrowColor);
        _elastic = CreateLine("Slingshot Elastic", 2, _elasticMaterial);
        _shaft = CreateLine("Slingshot Arrow", 2, _arrowMaterial);
        _head = CreateLine("Slingshot Arrow Head", 3, _arrowMaterial);
    }

    private Material CreateMaterial(Color color)
    {
        Material material = new Material(aimMaterial);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        return material;
    }

    private LineRenderer CreateLine(string objectName, int count, Material material)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.layer = gameObject.layer;
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.positionCount = count;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.widthMultiplier = lineWidth;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void ShowVisuals(bool show)
    {
        if (_elastic != null) _elastic.enabled = show;
        if (_shaft != null) _shaft.enabled = show;
        if (_head != null) _head.enabled = show;
    }

    private void DrawAim()
    {
        if (_selected == null || _elastic == null || gameplayCamera == null || _pull.sqrMagnitude < 0.000001f)
        {
            ShowVisuals(false);
            return;
        }
        ShowVisuals(true);
        Vector3 center = _selected.Position - gameplayCamera.transform.forward * (_selected.Radius + 0.03f);
        Vector3 direction = _pull.normalized;
        Vector3 side = new Vector3(-direction.y, direction.x, 0f);
        Vector3 tip = center + _pull;
        float headLength = Mathf.Min(arrowHeadLength, _pull.magnitude * 0.4f);
        _elastic.SetPosition(0, center);
        _elastic.SetPosition(1, _fingerWorld + center - _selected.Position);
        _shaft.SetPosition(0, center);
        _shaft.SetPosition(1, tip);
        _head.SetPosition(0, tip - direction * headLength + side * headLength * 0.5f);
        _head.SetPosition(1, tip);
        _head.SetPosition(2, tip - direction * headLength - side * headLength * 0.5f);
    }
}
