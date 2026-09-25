using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;


[DefaultExecutionOrder(-1)]
public class InputManager : MonoBehaviour
{
    public TMP_Text text;

    private PlayerInputActions playerInputActions;

    private void Awake()
    {
        playerInputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        playerInputActions.Enable();

        EnhancedTouchSupport.Enable();

        Touch.onFingerDown += FingerDown;
        Touch.onFingerUp += FingerUp;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= FingerDown;
        Touch.onFingerUp -= FingerUp;
        
        EnhancedTouchSupport.Disable();

        playerInputActions.Disable();
    }

    private void FingerDown(Finger finger)
    {
        Vector2 position = finger.currentTouch.screenPosition;

        ContactDetected(finger.index, position);
    }

    private void FingerUp(Finger finger)
    {
        Vector2 position = finger.currentTouch.screenPosition;

        ContactReleased(finger.index, position);
    }

    private void ContactDetected(int fingerIndex, Vector2 position)
    {
        Debug.Log($"Contact {fingerIndex} détecté : {position}");
    }

    private void ContactReleased(int fingerIndex, Vector2 position)
    {
        Debug.Log($"Contact {fingerIndex} relâché : {position}");
    }

    private void Update()
    {
        var fingers = Touch.activeFingers;

        string display = $"Doigts posés : {fingers.Count}\n\n";

        for (int i = 0; i < fingers.Count; i++)
        {
            Finger finger = fingers[i];

            Vector2 position = finger.currentTouch.screenPosition;

            display +=
                $"Finger {finger.index} : {position}\n";
        }

        text.text = display;
    }
}