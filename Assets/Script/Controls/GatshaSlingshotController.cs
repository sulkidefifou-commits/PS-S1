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
    [SerializeField] private Gradient powerGradient = CreateDefaultPowerGradient();
    [Tooltip("Vitesse initiale correspondant au rouge. Limite visuelle seulement.")]
    [SerializeField, Min(0.01f)] private float speedForRed = 24f;
    [SerializeField, Min(0.001f)] private float dashLength = 0.08f;
    [SerializeField, Min(0.001f)] private float dashGap = 0.06f;
    [Tooltip("0 : fondu sur toute la distance boule-doigt. 0.5 : fondu sur la seconde moitie.")]
    [SerializeField, Range(0f, 0.99f)] private float fadeStart = 0f;
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
    private Material _visualMaterial;
    private MaterialPropertyBlock _lineProperties;

    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int LineLengthId = Shader.PropertyToID("_LineLength");
    private static readonly int DashLengthId = Shader.PropertyToID("_DashLength");
    private static readonly int DashGapId = Shader.PropertyToID("_DashGap");
    private static readonly int DashedId = Shader.PropertyToID("_Dashed");
    private static readonly int FadeToEndId = Shader.PropertyToID("_FadeToEnd");
    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");

    private static Gradient CreateDefaultPowerGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.green, 0f),
                new GradientColorKey(Color.yellow, 0.5f),
                new GradientColorKey(Color.red, 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

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
        UnityEngine.Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
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
        // Meme puissance qu'avant (distance depuis l'appui initial), mais
        // un axe unique boule-doigt pour la propulsion et les deux lignes.
        Vector3 drag = _selected.Position - (fingerWorld + _grabOffset);
        drag.z = 0f;
        Vector3 awayFromFinger = _selected.Position - fingerWorld;
        awayFromFinger.z = 0f;
        _pull = awayFromFinger.sqrMagnitude > 0.000001f
            ? awayFromFinger.normalized * drag.magnitude
            : Vector3.zero;
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
        if (_visualMaterial != null) Destroy(_visualMaterial);
        if (_elastic != null) Destroy(_elastic.gameObject);
        if (_shaft != null) Destroy(_shaft.gameObject);
        if (_head != null) Destroy(_head.gameObject);
    }

    private void EnsureVisuals()
    {
        if (_elastic != null) return;
        if (aimMaterial == null || aimMaterial.shader == null ||
            aimMaterial.shader.name != "Gatsha/SlingshotDotted")
        {
            Debug.LogWarning("Aim Material doit utiliser le shader Gatsha/SlingshotDotted fourni.", this);
            return;
        }
        _visualMaterial = new Material(aimMaterial);
        _lineProperties = new MaterialPropertyBlock();
        _elastic = CreateLine("Slingshot Elastic", 2);
        _shaft = CreateLine("Slingshot Arrow", 2);
        _head = CreateLine("Slingshot Arrow Head", 3);
    }

    private LineRenderer CreateLine(string objectName, int count)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.layer = gameObject.layer;
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = _visualMaterial;
        line.positionCount = count;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.startWidth = 1f;
        line.endWidth = 1f;
        line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
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

    private void StyleLine(LineRenderer line, Color color, float length, bool dashed, bool fade)
    {
        // Unity copie le bloc : un seul objet est reutilise pour les trois lignes.
        _lineProperties.Clear();
        _lineProperties.SetColor(TintId, color);
        _lineProperties.SetFloat(LineLengthId, Mathf.Max(0.0001f, length));
        _lineProperties.SetFloat(DashLengthId, Mathf.Max(0.001f, dashLength));
        _lineProperties.SetFloat(DashGapId, Mathf.Max(0.001f, dashGap));
        _lineProperties.SetFloat(DashedId, dashed ? 1f : 0f);
        _lineProperties.SetFloat(FadeToEndId, fade ? 1f : 0f);
        _lineProperties.SetFloat(FadeStartId, Mathf.Clamp(fadeStart, 0f, 0.99f));
        line.SetPropertyBlock(_lineProperties);
        line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
    }

    private void DrawAim()
    {
        if (_selected == null || _elastic == null || gameplayCamera == null || _pull.sqrMagnitude < 0.000001f)
        {
            ShowVisuals(false);
            return;
        }
        ShowVisuals(true);
        Vector3 direction = _pull.normalized;
        Vector3 offset = -gameplayCamera.transform.forward * (_selected.Radius + 0.03f);
        Vector3 center = _selected.Position + offset;
        Vector3 finger = _fingerWorld + offset;
        Vector3 side = new Vector3(-direction.y, direction.x, 0f);
        Vector3 tip = center + _pull;
        float headLength = Mathf.Min(arrowHeadLength, _pull.magnitude * 0.4f);

        // Les deux segments partagent exactement le meme axe, sans coude au centre.
        _elastic.SetPosition(0, center);
        _elastic.SetPosition(1, finger);
        _shaft.SetPosition(0, center);
        _shaft.SetPosition(1, tip);
        Vector3 headLeft = tip - direction * headLength + side * headLength * 0.5f;
        Vector3 headRight = tip - direction * headLength - side * headLength * 0.5f;
        _head.SetPosition(0, headLeft);
        _head.SetPosition(1, tip);
        _head.SetPosition(2, headRight);

        float power = Mathf.Clamp01(PreviewVelocity.magnitude / Mathf.Max(0.01f, speedForRed));
        Color color = powerGradient.Evaluate(power);
        StyleLine(_elastic, color, Vector3.Distance(center, finger), true, true);
        StyleLine(_shaft, color, _pull.magnitude, true, false);
        // Pointe pleine et opaque pour garder la direction de tir lisible.
        StyleLine(_head, color, 2f * Vector3.Distance(headLeft, tip), false, false);
    }
}
