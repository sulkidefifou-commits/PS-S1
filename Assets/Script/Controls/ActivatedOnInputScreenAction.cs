using UnityEngine;

[RequireComponent(typeof(ControllableEntity))]
[DisallowMultipleComponent]
public class ActivatedOnInputScreenAction : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Camera inputCamera;
    [Tooltip("MeshRenderer de la sphere visible, facultatif.")]
    [SerializeField] private Renderer sphereRenderer;

    private readonly Plane _interactionPlane = new Plane(Vector3.forward, Vector3.zero);
    private ControllableEntity _entity;
    private double _previousSampleTime;

    private void Awake()
    {
        _entity = GetComponent<ControllableEntity>();
        if (inputCamera == null) inputCamera = Camera.main;
        if (sphereRenderer == null) sphereRenderer = GetComponentInChildren<Renderer>();
        StopControl(true);
    }

    private void OnEnable()
    {
        if (inputManager == null || inputCamera == null)
        {
            Debug.LogError(name + " : renseigner Input Manager et Input Camera.", this);
            enabled = false;
            return;
        }

        inputManager.PrimaryContactStarted += StartControl;
        inputManager.PrimaryContactMoved += MoveControl;
        inputManager.PrimaryContactEnded += StopControl;
    }

    private void OnDisable()
    {
        if (inputManager != null)
        {
            inputManager.PrimaryContactStarted -= StartControl;
            inputManager.PrimaryContactMoved -= MoveControl;
            inputManager.PrimaryContactEnded -= StopControl;
        }
        StopControl(true);
    }

    private bool TryGetWorldPosition(Vector2 screenPosition, out Vector3 position)
    {
        position = Vector3.zero;
        if (inputCamera == null) return false;

        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        if (!_interactionPlane.Raycast(ray, out float distance)) return false;

        position = ray.GetPoint(distance);
        position.z = 0f;
        return true;
    }

    private void StartControl(Vector2 screenPosition, double sampleTime)
    {
        if (Time.timeScale <= 0f || !TryGetWorldPosition(screenPosition, out Vector3 position)) return;
        if (!_entity.BeginControl(position)) return;

        _previousSampleTime = sampleTime;
        SetVisible(true);
    }

    private void MoveControl(Vector2 screenPosition, double sampleTime)
    {
        if (!_entity.IsControlled) return;
        if (Time.timeScale <= 0f || !TryGetWorldPosition(screenPosition, out Vector3 position))
        {
            StopControl(true);
            return;
        }
        
        float elapsedTime = (float)(sampleTime - _previousSampleTime);
        _entity.MoveControl(position, elapsedTime);
        if (sampleTime > _previousSampleTime) _previousSampleTime = sampleTime;
    }

    private void StopControl(bool canceled)
    {
        if (_entity != null) _entity.EndControl(canceled);
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (sphereRenderer != null) sphereRenderer.enabled = visible;
    }
}
