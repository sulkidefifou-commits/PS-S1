using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GameManager : MonoBehaviour
{
    private static GameManager _instance;
    public static GameManager Instance => _instance;


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

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
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
            Debug.LogError("La gatshaBall de niveau " + level + " est invalide : verifier Model Mesh, Model Renderer, Scale, Mass et Radius.", this);
            return false;
        }
        
        return true;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }
    
    public GatshaBallEntity SpawnBall(int level, Vector3 position)
    {
        if (!TryGetValidBallState(level, out _)) return null;
        

        if (BaseEntity == null)
        {
            Debug.LogError("il faut renseigner Base Entity avec le GatshaBallEntity du prefab de base", this);
            return null;
        }

        GatshaBallEntity ball = Instantiate(BaseEntity, position, Quaternion.identity);
        GameObject instance = ball.gameObject;
        if (!ball.Initialize(this, level))
        {
            instance.SetActive(false);
            Destroy(instance);
            return null;
        }

        ball.enabled = true;
        instance.SetActive(true);
        ball.Position = position;
        ball.Velocity = Vector3.zero;
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
            GatshaBallEntity merged = SpawnBall(nextLevel, contactPoint);
            if (merged == null)
            {
                first.SetMergePending(false);
                second.SetMergePending(false);
                return false;
            }
        }
        
        first.gameObject.SetActive(false);
        second.gameObject.SetActive(false);
        Destroy(first.gameObject);
        Destroy(second.gameObject);
        return true;
    }
}
