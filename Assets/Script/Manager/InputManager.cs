using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;


[DefaultExecutionOrder(-1)]
public class InputManager : MonoBehaviour
{

    public delegate void StartTouchEvent(Vector2 position, float time);
    public event StartTouchEvent OnStartTouch;
    public delegate void EndTouchEvent(Vector2 position, float time);
    public event EndTouchEvent OnEndTouch;
    
    private PlayerInputActions playerInputActions;

    private void Awake()
    {
       playerInputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        playerInputActions.Enable();
        TouchSimulation.Enable();
    }

    private void OnDisable()
    {
        playerInputActions.Disable();
        TouchSimulation.Disable();
    }

    private void Start()
    {
        playerInputActions.Player.TouchPress.started += StartTouch;
        playerInputActions.Player.TouchPress.canceled += EndTouch;
    }
    
    private void StartTouch(InputAction.CallbackContext ctx)
    {
        if (OnStartTouch != null) OnStartTouch(playerInputActions.Player.TouchPosition.ReadValue<Vector2>(),(float)ctx.startTime);
        
        Debug.Log("StartTouch " + playerInputActions.Player.TouchPosition.ReadValue<Vector2>());
    }

    private void EndTouch(InputAction.CallbackContext ctx)
    {
        if (OnStartTouch != null) OnStartTouch(playerInputActions.Player.TouchPosition.ReadValue<Vector2>(),(float)ctx.time); 
        
        
    }
    

    private void Update()
    {

    }
}
