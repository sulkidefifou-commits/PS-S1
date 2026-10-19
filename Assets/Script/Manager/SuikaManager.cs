using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


public class SuikaManager : MonoBehaviour
{
    private static SuikaManager _instance;
    public static SuikaManager Instance
    {
        get
        {
            return _instance;
        }
    }


    [Serializable]
    public class GatshaBallState
    {
        public Mesh ModelMesh;
        public Material ModelMatriel;
        [Min(0.001f)] public float Scale = 1f;
        [Min(0.001f)] public float Mass = 1f;
        public float Radius
        {
            get
            {
                return Scale / 2 ;
            }
        }
    }

    [Header("Gatsha Ball Settings")]
    public GatshaBallEntity BaseEntity;
    [SerializeField] private List<GatshaBallState> ballStates = new List<GatshaBallState>();

    public int LevelCount
    {
        get
        {
            if (ballStates == null)
            {
                return 0;
            }
            else
            {
                return ballStates.Count;
            }
        }
    }

    [Header("Gatsha Actions")]
    [SerializeField] private List<GatshaActions> gatshaActions = new List<GatshaActions>();
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Transform ballParent;
    [SerializeField] private bool startAutomatically = true;
    [SerializeField, Min(0f)] private float startDelay;

    [Header("Delai entre deux sequences")]
    [Min(0.01f)] public float NextActionTimer = 2f;
    [Min(0f)] public float NextActionDecrement = 0.1f;
    [Min(1)] public int NextActionEvery = 10;
    [Min(0)] public int NextActionLimit = 100;
    [SerializeField, Min(0.01f)] private float minimumNextActionTimer = 0.2f;

    [Header("Reutilisation des balles")]
    [SerializeField] private bool usePooling = true;
    [SerializeField, Min(0)] private int prewarmCount = 16;
    [SerializeField, Min(0)] private int maximumPooledBalls = 64;
    [SerializeField, Min(0f)] private float recycleMargin = 1f;
    [SerializeField, Min(0f)] private float maximumBallLifetime = 30f;

    [Header("Etat de la partie (lecture pendant Play)")]
    [SerializeField] private long completedActions;
    [SerializeField] private float gameTime;
    [SerializeField] private float currentNextActionTimer;
    [SerializeField] private float actionCountdown;

    public long CompletedActions => completedActions;
    public float GameTime => gameTime;
    public float CurrentNextActionTimer => currentNextActionTimer;
    public bool IsGameRunning { get; private set; }
    public bool IsSpawning { get; private set; }

    private GatshaActions _currentActions;
    private int _currentActionIndex;
    private bool _hasStarted;
    private bool _restartAfterReload;
    private Transform _poolRoot;
    private readonly Stack<GatshaBallEntity> _pool = new Stack<GatshaBallEntity>();
    private readonly List<GatshaBallEntity> _liveBalls = new List<GatshaBallEntity>(64);
    private readonly List<GatshaBallEntity> _retiredBalls = new List<GatshaBallEntity>(32);
    private readonly Plane[] _frustumPlanes = new Plane[6];

    private void Start()
    {
        _hasStarted = true;
        if (startAutomatically) StartGame();
    }

    public void StartGame()
    {
        StopGame();
        for (int i = _liveBalls.Count - 1; i >= 0; i--)
        {
            if (_liveBalls[i] != null && _liveBalls[i].IsActionBall) RecycleBall(_liveBalls[i]);
        }

        completedActions = 0;
        gameTime = 0f;
        UpdateNextActionTimer();
        actionCountdown = Mathf.Max(0f, startDelay);
        IsGameRunning = true;

        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (BaseEntity == null || gameplayCamera == null || PhysicsManager.Instance == null)
        {
            StopSpawning("aucun Base Entity, une camera et un PhysicsManager avant StartGame");
            return;
        }

        PreparePool();
        IsSpawning = true;
    }

    public void StopGame()
    {
        IsGameRunning = false;
        IsSpawning = false;
        _currentActions = null;
        _currentActionIndex = 0;
    }

    private void Update()
    {
        if (!IsGameRunning || Time.deltaTime <= 0f) return;
        gameTime += Time.deltaTime;
        if (!IsSpawning) return;

        actionCountdown -= Time.deltaTime;
        if (actionCountdown > 0f) return;

        if (_currentActions == null)
        {
            _currentActions = PickNextActions();
            if (_currentActions == null)
            {
                StopSpawning("Aucun GatshaActions disponible au compteur " + completedActions);
                return;
            }
            _currentActionIndex = 0;
            actionCountdown = GetActionDelay();
            if (!IsSpawning || actionCountdown > 0f) return;
        }


        if (!_currentActions.CanExecute(completedActions))
        {
            FinishSequence();
            return;
        }

        GatshaAction action = _currentActions.Actions[_currentActionIndex];
        if (!TryLaunchAction(action))
        {
            StopSpawning("Action " + (_currentActionIndex + 1) + " de " + _currentActions.name + " impossible ! verifier les reglage indiques dans la Consol");
            return;
        }

        completedActions++;
        UpdateNextActionTimer();
        _currentActionIndex++;
        if (_currentActionIndex >= _currentActions.Actions.Count || !_currentActions.CanExecute(completedActions))
            FinishSequence();
        else
            actionCountdown = GetActionDelay();
    }

    private GatshaActions PickNextActions()
    {
        GatshaActions selected = null;
        int eligibleCount = 0;
        if (gatshaActions == null) return null;
        for (int i = 0; i < gatshaActions.Count; i++)
        {
            GatshaActions candidate = gatshaActions[i];
            if (candidate == null || !candidate.CanExecute(completedActions)) continue;
            eligibleCount++;
            if (UnityEngine.Random.Range(0, eligibleCount) == 0) selected = candidate;
        }
        return selected;
    }

    private float GetActionDelay()
    {
        GatshaAction action = _currentActions.Actions[_currentActionIndex];
        if (action == null || action.DelayBefore == null)
        {
            StopSpawning("Une action ou son Delay Before est manquant dans " + _currentActions.name);
            return 0f;
        }
        return action.DelayBefore.Sample();
    }

    private void FinishSequence()
    {
        _currentActions = null;
        actionCountdown = currentNextActionTimer;
    }

    private void UpdateNextActionTimer()
    {
        currentNextActionTimer = GatshaLaunchUtility.GetNextDelay(NextActionTimer, minimumNextActionTimer, NextActionDecrement, NextActionEvery, NextActionLimit, completedActions);
    }

    private void StopSpawning(string reason)
    {
        IsSpawning = false;
        Debug.LogWarning(reason, this);
    }

    private bool TryLaunchAction(GatshaAction action)
    {
        if (action == null || action.InitialLevel == null || action.SpawnOffset == null || action.InertiaPoint == null)
            return false;

        PhysicsManager physics = PhysicsManager.Instance;
        if (physics == null) return false;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        int level = action.InitialLevel.Sample(LevelCount);
        if (!TryGetValidBallState(level, out GatshaBallState state)) return false;

        bool fromLeft = action.Side == GatshaSpawnSide.Left || (action.Side == GatshaSpawnSide.Random && UnityEngine.Random.Range(0, 2) == 0);
        float sideSign = fromLeft ? -1f : 1f;


        if (!GatshaLaunchUtility.TryGetSpawnPosition(gameplayCamera, _frustumPlanes, false,
            state.Radius, action.SpawnOffset.Sample(), out Vector3 start))
        {
            Debug.LogError("La camera doit regarder le plan Z = 0, avec la gauche et la droite suivant X", this);
            return false;
        }

        Vector2 apex = action.InertiaPoint.Sample();
        start.x *= sideSign;
        apex.x *= sideSign;
        
        if ((fromLeft ? apex.x <= start.x : apex.x >= start.x) || !GatshaLaunchUtility.TryGetLaunchVelocity(start, apex, physics.Gravity, physics.SubstepDeltaTime, out Vector3 velocity))
        {
            Debug.LogError("Le point d'inertie doit etre au dessus de Y = 0, vers l'interieur de l'ecran, la gravite doit etre verticale et dirigee vers le bas", this);
            return false;
        }

        GatshaBallEntity ball = SpawnBall(level, start, ballParent);
        if (ball == null) return false;
        ball.IsActionBall = true;
        ball.Velocity = velocity;
        return true;
    }

    private void EnsurePoolRoot()
    {
        if (_poolRoot != null) return;
        GameObject root = new GameObject("Gatsha Ball Pool");
        root.SetActive(false);
        DontDestroyOnLoad(root);
        _poolRoot = root.transform;
    }

    private GatshaBallEntity CreateInactiveBall()
    {
        EnsurePoolRoot();
        GatshaBallEntity ball = Instantiate(BaseEntity, Vector3.zero, Quaternion.identity, _poolRoot);
        ball.gameObject.SetActive(false);
        return ball;
    }

    private void PreparePool()
    {
        if (!usePooling) return;
        int count = Mathf.Clamp(prewarmCount, 0, maximumPooledBalls);
        while (_pool.Count < count)
        {
            GatshaBallEntity ball = CreateInactiveBall();
            ball.IsInPool = true;
            _pool.Push(ball);
        }
    }

    public void RecycleBall(GatshaBallEntity ball)
    {
        if (ball == null || ball.Owner != this || ball.IsInPool) return;
        ball.IsInPool = true;
        ball.SetMergePending(true);
        ball.Velocity = Vector3.zero;
        ball.gameObject.SetActive(false);
        _liveBalls.Remove(ball);
        if (!usePooling)
        {
            Destroy(ball.gameObject);
            return;
        }
        EnsurePoolRoot();
        ball.transform.SetParent(_poolRoot, true);
        _retiredBalls.Add(ball);
    }

    private void LateUpdate()
    {
        if (gameplayCamera != null && _liveBalls.Count > 0)
            GeometryUtility.CalculateFrustumPlanes(gameplayCamera, _frustumPlanes);

        for (int i = _liveBalls.Count - 1; i >= 0; i--)
        {
            GatshaBallEntity ball = _liveBalls[i];
            if (ball == null) { _liveBalls.RemoveAt(i); continue; }
            if (!ball.IsActionBall || ball.IsContained || ball.IsSelected) continue;
            float margin = ball.Radius + Mathf.Max(0f, recycleMargin);
            bool belowScreen = ball.Position.y < -margin && ball.Velocity.y < 0f &&
                (gameplayCamera == null || _frustumPlanes[2].GetDistanceToPoint(ball.Position) < -margin);
            bool tooOld = maximumBallLifetime > 0f && Time.time - ball.SpawnedAt >= maximumBallLifetime;
            if (belowScreen || tooOld) RecycleBall(ball);
        }

        for (int i = 0; i < _retiredBalls.Count; i++)
        {
            GatshaBallEntity ball = _retiredBalls[i];
            if (ball == null) continue;
            if (usePooling && _pool.Count < maximumPooledBalls) _pool.Push(ball);
            else Destroy(ball.gameObject);
        }
        _retiredBalls.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_hasStarted || mode != LoadSceneMode.Single) return;
        StopGame();
        if (startAutomatically || _restartAfterReload) StartGame();
        _restartAfterReload = false;
    }

    private void OnDisable()
    {
        StopGame();
    }

    [Header("Debug")] 
    public GameObject object1;
    public GameObject object2;
    

    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        else
        {
            _instance = this;
            
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }
    

    public void ActivateObject1()
    {
        object1.SetActive(!object1.activeSelf);
    }

    public void ActivateObject2()
    {
        object2.SetActive(!object2.activeSelf);
    }

    public void ReloadCurrentScene()
    {
        _restartAfterReload = IsGameRunning;
        StopGame();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
    
    public void RegisterDebugObject1(GameObject gameObject)
    {
        object1 = gameObject;
    }
    
    public void RegisterDebugObject2(GameObject gameObject)
    {
        object2 = gameObject;
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_poolRoot != null) Destroy(_poolRoot.gameObject);
        _instance = null;
    }

    public bool TryGetBallState(int level, out GatshaBallState state)
    {
        state = null;
        if (level < 1 || level > LevelCount) return false;
        state = ballStates[level - 1];
        return state != null;
    }

    internal bool TryGetValidBallState(int level, out GatshaBallState state)
    {
        if (!TryGetBallState(level, out state) || state.ModelMesh == null || state.ModelMatriel == null || !IsPositiveFinite(state.Scale) || !IsPositiveFinite(state.Mass) || !IsPositiveFinite(state.Radius))
        {
            Debug.LogError("La gatshaBall de niveau " + level + " est invalide : verifier Model Mesh, Model Renderer, Scale, Mass et Radius", this);
            return false;
        }
        
        return true;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }
    
    public GatshaBallEntity SpawnBall(int level, Vector3 position, Transform parent)
    {
        if (!TryGetValidBallState(level, out _)) return null;
        

        if (BaseEntity == null)
        {
            Debug.LogError("il faut renseigner Base Entity avec le GatshaBallEntity du prefab de base", this);
            return null;
        }

        GatshaBallEntity ball = null;
        if (usePooling)
            while (_pool.Count > 0 && ball == null) ball = _pool.Pop();
        if (ball == null) ball = CreateInactiveBall();
        GameObject instance = ball.gameObject;
        if (!ball.Initialize(this, level))
        {
            instance.SetActive(false);
            Destroy(instance);
            return null;
        }
        
        ball.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(instance, parent != null ? parent.gameObject.scene : SceneManager.GetActiveScene());
        ball.transform.SetParent(parent, true);
        ball.transform.position = new Vector3(position.x, position.y, 0f);
        ball.transform.rotation = Quaternion.identity;
        ball.enabled = true;
        instance.SetActive(true);
        ball.Velocity = Vector3.zero;
        ball.SpawnedAt = Time.time;
        _liveBalls.Add(ball);
        return ball;
    }

    public bool TryMerge(GatshaBallEntity first, GatshaBallEntity second, Vector3 contactPoint)
    {
        if (first == null || second == null || first == second) return false;
        if (!first.CanMerge || !second.CanMerge) return false;
        if (first.Owner != this || second.Owner != this) return false;
        if (first.Level != second.Level || first.Level < 1 || first.Level > LevelCount) return false;

        int nextLevel = first.Level + 1;
        
        first.SetMergePending(true);
        second.SetMergePending(true);

        if (nextLevel <= LevelCount)
        {
            GatshaBallEntity merged = SpawnBall(nextLevel, contactPoint, first.gameObject.transform.parent);
            if (merged == null)
            {
                first.SetMergePending(false);
                second.SetMergePending(false);
                return false;
            }
            merged.IsActionBall = first.IsActionBall || second.IsActionBall;
            merged.IsContained = first.IsContained || second.IsContained;
            merged.Velocity = (first.Velocity * first.Mass + second.Velocity * second.Mass) /
                (first.Mass + second.Mass);
        }
        
        RecycleBall(first);
        RecycleBall(second);
        return true;
    }
}
