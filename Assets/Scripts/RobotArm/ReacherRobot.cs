using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class ReacherRobot : Agent
{
    public GameObject pendulumA;
    public GameObject pendulumB;
    public GameObject pendulumC;
    public GameObject pendulumD;
    public GameObject pendulumE;
    public GameObject pendulumF;

    Rigidbody m_RbA, m_RbB, m_RbC, m_RbD, m_RbE, m_RbF;

    public GameObject brushTip;
    public GameObject canvas;
    public float torqueStrength = 150f;

    RobotBrush robotBrush;

    public override void Initialize()
    {
        m_RbA = pendulumA.GetComponent<Rigidbody>();
        m_RbB = pendulumB.GetComponent<Rigidbody>();
        m_RbC = pendulumC.GetComponent<Rigidbody>();
        m_RbD = pendulumD.GetComponent<Rigidbody>();
        m_RbE = pendulumE.GetComponent<Rigidbody>();
        m_RbF = pendulumF.GetComponent<Rigidbody>();

        robotBrush = brushTip.GetComponent<RobotBrush>();
    }

    public override void OnEpisodeBegin()
    {
        ResetJoint(pendulumA, m_RbA, new Vector3(-1.937307f, 0.6f,   1.24f));
        ResetJoint(pendulumB, m_RbB, new Vector3(-2.087307f, 0.6f,   1.24f));
        ResetJoint(pendulumC, m_RbC, new Vector3(-2.087307f, 1.425f, 1.24f));
        ResetJoint(pendulumD, m_RbD, new Vector3(-2.087307f, 1.425f, 1.24f));
        ResetJoint(pendulumE, m_RbE, new Vector3(-2.087307f, 2.05f,  1.24f));
        ResetJoint(pendulumF, m_RbF, new Vector3(-2.087307f, 2.16f,  1.24f));
    }

    void ResetJoint(GameObject joint, Rigidbody rb, Vector3 localPos)
    {
        joint.transform.position = localPos + transform.position;
        joint.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 관절 rotation (4 x 6 = 24)
        sensor.AddObservation(pendulumA.transform.localRotation);
        sensor.AddObservation(pendulumB.transform.localRotation);
        sensor.AddObservation(pendulumC.transform.localRotation);
        sensor.AddObservation(pendulumD.transform.localRotation);
        sensor.AddObservation(pendulumE.transform.localRotation);
        sensor.AddObservation(pendulumF.transform.localRotation);

        // 관절 각속도 (3 x 6 = 18)
        sensor.AddObservation(m_RbA.angularVelocity);
        sensor.AddObservation(m_RbB.angularVelocity);
        sensor.AddObservation(m_RbC.angularVelocity);
        sensor.AddObservation(m_RbD.angularVelocity);
        sensor.AddObservation(m_RbE.angularVelocity);
        sensor.AddObservation(m_RbF.angularVelocity);

        // BrushTip → Canvas 방향벡터 (3)
        Vector3 toCanvas = canvas.transform.position - brushTip.transform.position;
        sensor.AddObservation(toCanvas.normalized);

        // BrushTip → Canvas 거리 (1)
        sensor.AddObservation(toCanvas.magnitude);

        // 총합 = 46
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var act = actions.ContinuousActions;

        m_RbA.AddTorque(pendulumA.transform.forward * act[0] * torqueStrength);
        m_RbB.AddTorque(pendulumB.transform.up      * act[1] * torqueStrength);
        m_RbC.AddTorque(pendulumC.transform.up      * act[2] * torqueStrength);
        m_RbD.AddTorque(pendulumD.transform.forward * act[3] * torqueStrength);
        m_RbE.AddTorque(pendulumE.transform.up      * act[4] * torqueStrength);
        m_RbF.AddTorque(pendulumF.transform.forward * act[5] * torqueStrength);

        // 바닥 뚫기 감지
        if (StepCount > 10)
        {
            bool fallen =
                brushTip.transform.position.y < 0f ||
                pendulumA.transform.position.y < 0f ||
                pendulumB.transform.position.y < 0f ||
                pendulumC.transform.position.y < 0f ||
                pendulumD.transform.position.y < 0f ||
                pendulumE.transform.position.y < 0f ||
                pendulumF.transform.position.y < 0f;

            if (fallen)
            {
                AddReward(-1.0f);
                EndEpisode();
                return;
            }
        }

        // Reward: 캔버스에 가까울수록
        float dist = Vector3.Distance(brushTip.transform.position, canvas.transform.position);
        AddReward(1f / (1f + dist) * 0.01f);

        // 캔버스에 닿으면 성공
        if (robotBrush != null && robotBrush.IsTouching)
        {
            AddReward(1.0f);
            //EndEpisode();
        }

        // 관절 과속 패널티
        foreach (var rb in new[] { m_RbA, m_RbB, m_RbC, m_RbD, m_RbE, m_RbF })
        {
            if (rb.angularVelocity.magnitude > 10f)
                AddReward(-0.05f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var act = actionsOut.ContinuousActions;
        act[0] = Input.GetAxis("Horizontal");
        act[1] = Input.GetAxis("Vertical");
        act[2] = Input.GetKey(KeyCode.Q) ? 1f :
                 Input.GetKey(KeyCode.E) ? -1f : 0f;
        act[3] = Input.GetKey(KeyCode.R) ? 1f :
                 Input.GetKey(KeyCode.F) ? -1f : 0f;
        act[4] = Input.GetKey(KeyCode.T) ? 1f :
                 Input.GetKey(KeyCode.G) ? -1f : 0f;
        act[5] = Input.GetKey(KeyCode.Y) ? 1f :
                 Input.GetKey(KeyCode.H) ? -1f : 0f;
    }
}