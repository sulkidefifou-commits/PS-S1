using UnityEngine;

// XPBD : maintient une distance entre deux particules avec une souplesse reglable.
// Une seule instance par liaison, reutilisee pendant toute la simulation.
public sealed class DistanceConstraint
{
    public int FirstIndex { get; }
    public int SecondIndex { get; }
    public float RestLength { get; }
    public float Compliance { get; set; }

    private float _lambda;
    private Vector3 _lastDirection = Vector3.right;

    public DistanceConstraint(int firstIndex, int secondIndex, float restLength, float compliance)
    {
        FirstIndex = firstIndex;
        SecondIndex = secondIndex;
        RestLength = Mathf.Max(0f, restLength);
        Compliance = Mathf.Max(0f, compliance);
    }

    public void BeginSubstep()
    {
        // Reinitialise une fois par sous-pas, et non a chaque iteration.
        _lambda = 0f;
    }

    public void Solve(Vector3[] positions, float[] inverseMasses, float dt)
    {
        if (dt <= 0f) return;

        float firstWeight = inverseMasses[FirstIndex];
        float secondWeight = inverseMasses[SecondIndex];
        float weightSum = firstWeight + secondWeight;
        if (weightSum <= 0f) return;

        Vector3 difference = positions[SecondIndex] - positions[FirstIndex];
        float distance = difference.magnitude;

        // Une direction de secours evite une division par zero si les points se superposent.
        if (distance > 0.000001f)
            _lastDirection = difference / distance;

        float error = distance - RestLength;
        float alpha = Mathf.Max(0f, Compliance) / (dt * dt);

        // La lambda accumulee conserve la souplesse au fil des iterations XPBD.
        float deltaLambda = (-error - alpha * _lambda) / (weightSum + alpha);
        _lambda += deltaLambda;

        Vector3 correction = _lastDirection * deltaLambda;
        positions[FirstIndex] -= correction * firstWeight;
        positions[SecondIndex] += correction * secondWeight;
    }
}
