using UnityEngine;

// XPBD : conserve le volume signe d'un tetraedre.
// Le signe distingue la forme normale d'un tetraedre retourne.
public sealed class VolumeConstraint
{
    public float RestVolume { get; }
    public float Compliance { get; set; }

    private readonly int _first;
    private readonly int _second;
    private readonly int _third;
    private readonly int _fourth;
    private float _lambda;

    public VolumeConstraint(int first, int second, int third, int fourth,
        float restVolume, float compliance)
    {
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
        RestVolume = restVolume;
        Compliance = Mathf.Max(0f, compliance);
    }

    public static float GetSignedVolume(Vector3 first, Vector3 second,
        Vector3 third, Vector3 fourth)
    {
        return Vector3.Dot(second - first, Vector3.Cross(third - first, fourth - first)) / 6f;
    }

    public void BeginSubstep()
    {
        _lambda = 0f;
    }

    public void Solve(Vector3[] positions, float[] inverseMasses, float dt)
    {
        if (dt <= 0f) return;

        Vector3 first = positions[_first];
        Vector3 second = positions[_second];
        Vector3 third = positions[_third];
        Vector3 fourth = positions[_fourth];

        // Chaque gradient indique comment le volume varie quand ce point bouge.
        Vector3 secondGradient = Vector3.Cross(third - first, fourth - first) / 6f;
        Vector3 thirdGradient = Vector3.Cross(fourth - first, second - first) / 6f;
        Vector3 fourthGradient = Vector3.Cross(second - first, third - first) / 6f;
        Vector3 firstGradient = -secondGradient - thirdGradient - fourthGradient;

        float firstWeight = inverseMasses[_first];
        float secondWeight = inverseMasses[_second];
        float thirdWeight = inverseMasses[_third];
        float fourthWeight = inverseMasses[_fourth];
        float weightSum = firstWeight * firstGradient.sqrMagnitude
            + secondWeight * secondGradient.sqrMagnitude
            + thirdWeight * thirdGradient.sqrMagnitude
            + fourthWeight * fourthGradient.sqrMagnitude;
        if (weightSum <= 0.000000000001f) return;

        float error = GetSignedVolume(first, second, third, fourth) - RestVolume;
        float alpha = Mathf.Max(0f, Compliance) / (dt * dt);
        float deltaLambda = (-error - alpha * _lambda) / (weightSum + alpha);
        _lambda += deltaLambda;

        positions[_first] += firstGradient * (firstWeight * deltaLambda);
        positions[_second] += secondGradient * (secondWeight * deltaLambda);
        positions[_third] += thirdGradient * (thirdWeight * deltaLambda);
        positions[_fourth] += fourthGradient * (fourthWeight * deltaLambda);
    }
}
