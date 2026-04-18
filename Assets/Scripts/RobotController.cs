using UnityEngine;

public class RobotController : MonoBehaviour
{
    [Header("관절 연결")]
    public Transform joint1;         
    public Transform joint2;         
    public Transform joint3;         

    public Transform prismaticJoint; 
    public Transform joint4;        
        
    public Transform joint5;        
    public Transform brushTip;     

    [Header("IK Solver 연결")]
    public RobotIKSolver ikSolver;

    [Header("회전 속도")]
    public float rotateSpeed = 5f;

    private float _currentTheta1;
    private float _currentTheta2;
    private float _currentTheta3;
    private float _currentTheta4;
    private float _currentD;

    void Start()
    {
        
        if (ikSolver == null)
            ikSolver = GetComponent<RobotIKSolver>();
    }

    void Update()
    {
        if (ikSolver == null) return;

        
        _currentTheta1 = Mathf.LerpAngle(_currentTheta1, ikSolver.theta1, Time.deltaTime * rotateSpeed);
        _currentTheta2 = Mathf.LerpAngle(_currentTheta2, ikSolver.theta2, Time.deltaTime * rotateSpeed);
        _currentTheta3 = Mathf.LerpAngle(_currentTheta3, ikSolver.theta3, Time.deltaTime * rotateSpeed);
        _currentTheta4 = Mathf.LerpAngle(_currentTheta4, ikSolver.theta4, Time.deltaTime * rotateSpeed);
        _currentD      = Mathf.Lerp(_currentD, ikSolver.d, Time.deltaTime * rotateSpeed);

        if (joint1) joint1.localEulerAngles = new Vector3(0, _currentTheta1, 0);
        if (joint2) joint2.localEulerAngles = new Vector3(0, _currentTheta2, 0);
        if (joint3) joint3.localEulerAngles = new Vector3(0, _currentTheta3, 0);
        if (joint5) joint5.localEulerAngles = new Vector3(_currentTheta4, 0, 0);

        if (joint4)
        {
            Vector3 pos = prismaticJoint.localPosition;
            pos.y = -_currentD;
            prismaticJoint.localPosition = pos;
        }
    }
    public void SetTarget(Vector3 position, float yaw, float pitch)
    {
        ikSolver.targetPosition = position;
        ikSolver.yaw = yaw;
        ikSolver.pitch = pitch;
        ikSolver.Solve();
    }
}