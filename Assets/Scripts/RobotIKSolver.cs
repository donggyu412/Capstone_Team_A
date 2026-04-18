using UnityEngine;
using UnityEditor;



#if UNITY_EDITOR
[CustomEditor(typeof(RobotIKSolver))]
public class RobotIKSolverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        RobotIKSolver solver = (RobotIKSolver)target;
        if (GUILayout.Button("Solve"))
            solver.Solve();
    }
}
#endif


public class RobotIKSolver : MonoBehaviour
{
    [Header("링크 길이")]
    public float L4 = 0.3f;   // 첫번째 팔 길이
    public float L6 = 0.2f;   // 두번째 팔 길이
    public float L7 = 0.1f;   // 손목~붓끝 길이
    public float L8 = 0.06f;  // 붓 길이

    [Header("목표값 입력")]
    public Vector3 targetPosition;  // 붓끝 목표 위치 (x, y, z)
    public float yaw   = 0f;        // 붓 방향 (도)
    public float pitch = -90f;      // 붓 기울기 (도)

    [Header("각도 수정")]
    public float theta1;  // Joint1 각도
    public float theta2;  // Joint2 각도
    public float theta3;  // Joint3 YAW 각도
    public float theta4;  // Joint5 PITCH 각도
    public float d;       // PrismaticJoint 이동거리

    public void Solve()
    {
        float yawRad   = yaw   * Mathf.Deg2Rad;
        float pitchRad = pitch * Mathf.Deg2Rad;

        float pwX = targetPosition.x - L7 * Mathf.Sin(yawRad);
        float pwY = targetPosition.y;
        float pwZ = targetPosition.z - L7 * Mathf.Cos(yawRad);

        float r2 = pwX * pwX + pwZ * pwZ;
        float r  = Mathf.Sqrt(r2);

        float D = (r2 - L4 * L4 - L6 * L6) / (2f * L4 * L6);
        D = Mathf.Clamp(D, -1f, 1f);
        theta2 = Mathf.Atan2(Mathf.Sqrt(1f - D * D), D) * Mathf.Rad2Deg;

        float delta1 = Mathf.Atan2(pwZ, pwX);
        float delta2 = Mathf.Atan2(
            L6 * Mathf.Sin(theta2 * Mathf.Deg2Rad),
            L4 + L6 * Mathf.Cos(theta2 * Mathf.Deg2Rad)
        );
        theta1 = (delta1 - delta2) * Mathf.Rad2Deg;

        theta3 = yaw   - theta1 - theta2;
        theta4 = pitch;

        float maxD = L8 * Mathf.Sin(Mathf.Abs(pitchRad));
        d = targetPosition.y - pwY + maxD * 0.05f;
        d = Mathf.Clamp(d, 0f, maxD);

        Debug.Log($"θ1:{theta1:F2} θ2:{theta2:F2} θ3:{theta3:F2} θ4:{theta4:F2} d:{d:F4}");
    }

    
}
