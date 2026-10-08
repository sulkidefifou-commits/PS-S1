using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

[DefaultExecutionOrder(-100)]
public class InputManager : MonoBehaviour
{
    public TMP_Text text;
    [SerializeField] private bool simulateMouseInEditor = true;

    public event Action<Vector2, double> PrimaryContactStarted;
    public event Action<Vector2, double> PrimaryContactMoved;
    public event Action<bool> PrimaryContactEnded;

    private PlayerInputActions playerInputActions;
    private Finger _primaryFinger;
    private int _primaryTouchId;
    private int _lastSampleFrame = -1;
    private bool _waitForAllReleased;
    private bool _acceptInput = true;
    private bool _ownsMouseSimulation;
    private readonly StringBuilder _display = new StringBuilder(256);

    private void Awake()
    {
        playerInputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        playerInputActions.Enable();
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += FingerDown;
        Touch.onFingerMove += FingerMove;
        Touch.onFingerUp += FingerUp;

#if UNITY_EDITOR
        if (simulateMouseInEditor && TouchSimulation.instance == null)
        {
            TouchSimulation.Enable();
            _ownsMouseSimulation = true;
        }
#endif
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= FingerDown;
        Touch.onFingerMove -= FingerMove;
        Touch.onFingerUp -= FingerUp;
        EndPrimaryContact(true);

#if UNITY_EDITOR
        if (_ownsMouseSimulation)
        {
            TouchSimulation.Disable();
            _ownsMouseSimulation = false;
        }
#endif
        EnhancedTouchSupport.Disable();
        playerInputActions.Disable();
    }

    private void OnDestroy()
    {
        playerInputActions?.Dispose();
    }

    private void FingerDown(Finger finger)
    {
        if (!_acceptInput || Time.timeScale <= 0f || _primaryFinger != null || _waitForAllReleased) return;

        Touch touch = finger.currentTouch;
        _primaryFinger = finger;
        _primaryTouchId = touch.touchId;
        _lastSampleFrame = Time.frameCount;
        PrimaryContactStarted?.Invoke(touch.screenPosition, touch.time);
    }

    private bool IsPrimary(Finger finger)
    {
        return finger == _primaryFinger && finger.currentTouch.valid &&
               finger.currentTouch.touchId == _primaryTouchId;
    }

    private void FingerMove(Finger finger)
    {
        if (!IsPrimary(finger)) return;
        Touch touch = finger.currentTouch;
        _lastSampleFrame = Time.frameCount;
        PrimaryContactMoved?.Invoke(touch.screenPosition, touch.time);
    }

    private void FingerUp(Finger finger)
    {
        if (!IsPrimary(finger)) return;
        Touch touch = finger.currentTouch;
        bool canceled = touch.phase == TouchPhase.Canceled;
        
        if (!canceled) PrimaryContactMoved?.Invoke(touch.screenPosition, touch.time);
        EndPrimaryContact(canceled);
    }

    private void EndPrimaryContact(bool canceled)
    {
        if (_primaryFinger == null)
        {
            if (canceled) PrimaryContactEnded?.Invoke(true);
            return;
        }
        _primaryFinger = null;
        _waitForAllReleased = true;
        PrimaryContactEnded?.Invoke(canceled);
    }

    private void Update()
    {
        if (_primaryFinger != null)
        {
            Touch touch = _primaryFinger.currentTouch;
            if (!touch.valid || touch.touchId != _primaryTouchId || !touch.isInProgress || Time.timeScale <= 0f)
            {
                EndPrimaryContact(true);
            }
            else if (_lastSampleFrame != Time.frameCount)
            {
                PrimaryContactMoved?.Invoke(touch.screenPosition, Time.realtimeSinceStartupAsDouble);
                _lastSampleFrame = Time.frameCount;
            }
        }

        var fingers = Touch.activeFingers;
        bool anyFingerDown = false;
        for (int i = 0; i < fingers.Count; i++)
        {
            Touch touch = fingers[i].currentTouch;
            if (touch.valid && touch.isInProgress) anyFingerDown = true;
        }
        if (!anyFingerDown) _waitForAllReleased = false;

        if (text == null) return;
        _display.Clear();
        _display.Append("Doigts poses : ").Append(fingers.Count).Append('\n');
        for (int i = 0; i < fingers.Count; i++)
            _display.Append("Finger ").Append(fingers[i].index).Append(" : ")
                .Append(fingers[i].currentTouch.screenPosition).Append('\n');
        text.text = _display.ToString();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        _acceptInput = hasFocus;
        if (!hasFocus) EndPrimaryContact(true);
    }

    private void OnApplicationPause(bool paused)
    {
        _acceptInput = !paused;
        if (paused) EndPrimaryContact(true);
    }
}
