using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum GatshaValueMode { Fixed, RandomRange }
public enum GatshaSpawnSide { Left, Right, Random }

[Serializable]
public class GatshaIntValue
{
    public GatshaValueMode Mode;
    [Min(1)] public int Value = 1;
    [Min(1)] public int Min = 1;
    [Min(1)] public int Max = 2;

    public int Sample(int maximumLevel)
    {
        if (maximumLevel < 1) return 0;
        if (Mode == GatshaValueMode.Fixed) return Mathf.Clamp(Value, 1, maximumLevel);
        int min = Mathf.Clamp(Mathf.Min(Min, Max), 1, maximumLevel);
        int max = Mathf.Clamp(Mathf.Max(Min, Max), 1, maximumLevel);
        return Random.Range(min, max + 1);
    }
}

[Serializable]
public class GatshaFloatValue
{
    public GatshaValueMode Mode;
    [Min(0f)] public float Value;
    [Min(0f)] public float Min;
    [Min(0f)] public float Max = 0.5f;

    public GatshaFloatValue(float value) { Value = value; }

    public float Sample()
    {
        float result = Mode == GatshaValueMode.Fixed
            ? Value : Random.Range(Mathf.Min(Min, Max), Mathf.Max(Min, Max));
        return float.IsNaN(result) || float.IsInfinity(result) ? 0f : Mathf.Max(0f, result);
    }
}

[Serializable]
public class GatshaPointValue
{
    public GatshaValueMode Mode;
    public Vector2 Value = new Vector2(0f, 3f);
    public Vector2 Min = new Vector2(-1f, 2f);
    public Vector2 Max = new Vector2(1f, 4f);

    public Vector2 Sample()
    {
        if (Mode == GatshaValueMode.Fixed) return Value;
        return new Vector2(Random.Range(Mathf.Min(Min.x, Max.x), Mathf.Max(Min.x, Max.x)), Random.Range(Mathf.Min(Min.y, Max.y), Mathf.Max(Min.y, Max.y)));
    }
}

[Serializable]
public class GatshaAction
{

    public GatshaFloatValue DelayBefore = new GatshaFloatValue(0f);

    public GatshaIntValue InitialLevel = new GatshaIntValue();

    public GatshaSpawnSide Side = GatshaSpawnSide.Random;
    
    public GatshaFloatValue SpawnOffset = new GatshaFloatValue(0.2f);
    
    public GatshaPointValue InertiaPoint = new GatshaPointValue();
}

[CreateAssetMenu(fileName = "GatshaActions", menuName = "Gatsha/Actions")]
public class GatshaActions : ScriptableObject
{
    [Header("Disponibilite selon les balles deja lancees")]
    public bool UseMinimumActionCount;
    [Min(0)] public int MinimumActionCount;
    public bool UseMaximumActionCount;
    [Min(1)] public int MaximumActionCount = 20;

    [Header("Actions executees dans l'ordre")]
    public List<GatshaAction> Actions = new List<GatshaAction> { new GatshaAction() };

    public bool CanExecute(long completedActions)
    {
        if (Actions == null || Actions.Count == 0) return false;
        if (UseMinimumActionCount && completedActions < MinimumActionCount) return false;
        if (UseMaximumActionCount && completedActions >= MaximumActionCount) return false;
        return true;
    }
}
