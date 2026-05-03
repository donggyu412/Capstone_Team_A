using UnityEngine;

public class BoidAgent : MonoBehaviour
{
    [HideInInspector] public BoidManager manager;
    [HideInInspector] public CanvasManager canvasManager;

    [Header("Movement Rules")]
    public float maxSpeed = 3f;
    public float perceptionRadius = 2f; // 주변 인식하는 반경

    public float separationWeight = 1.5f; // 분리 가중치
    public float alignmentWeight = 1.0f;  // 정렬 가중치
    public float cohesionWeight = 1.0f;   // 응집 가중치

    private Vector2 velocity;
    public Color myColor = new Color(0, 0, 0, 0.05f);

    void Start()
    {
        // 처음 태어날 때 무작위 방향으로 출발
        velocity = Random.insideUnitCircle.normalized * maxSpeed;
    }

    void Update()
    {
        ApplyBoidsRules();
        
        // 위치 이동
        transform.position += (Vector3)velocity * Time.deltaTime;

        WrapAround(); // 화면 밖으로 나가면 반대쪽에서 나오게 처리
        DrawOnCanvas(); // 도화지에 궤적 그리기
    }

    void ApplyBoidsRules()
    {
        Vector2 separation = Vector2.zero;
        Vector2 alignment = Vector2.zero;
        Vector2 cohesion = Vector2.zero;
        int neighborCount = 0;

        // 매 프레임마다 다른 에이전트들과의 거리 측정 (O(N^2) 방식, 프로토타입용)
        foreach (BoidAgent other in manager.agents)
        {
            if (other == this) continue;

            float dist = Vector2.Distance(transform.position, other.transform.position);
            if (dist < perceptionRadius)
            {
                // 1. 분리: 가까울수록 강하게 밀어냄
                Vector2 diff = transform.position - other.transform.position;
                separation += diff.normalized / dist; 

                // 2. 정렬: 친구들의 속도 벡터를 더함
                alignment += other.velocity;

                // 3. 응집: 친구들의 위치를 더함
                cohesion += (Vector2)other.transform.position;

                neighborCount++;
            }
        }

        if (neighborCount > 0)
        {
            separation /= neighborCount;
            
            alignment /= neighborCount;
            alignment = (alignment.normalized * maxSpeed) - velocity;

            cohesion /= neighborCount;
            cohesion = (cohesion - (Vector2)transform.position).normalized * maxSpeed - velocity;
        }

        // 3가지 힘을 가중치에 맞게 합산하여 현재 속도에 적용
        Vector2 force = (separation * separationWeight) + (alignment * alignmentWeight) + (cohesion * cohesionWeight);
        velocity += force * Time.deltaTime;
        velocity = Vector2.ClampMagnitude(velocity, maxSpeed); // 최고 속도 제한
    }

    // 화면 밖으로 나가면 반대편에서 나타나게 하는 함수
    void WrapAround()
    {
        Vector3 viewportPos = Camera.main.WorldToViewportPoint(transform.position);
        bool wrapped = false;

        if (viewportPos.x < 0) { viewportPos.x = 1; wrapped = true; }
        else if (viewportPos.x > 1) { viewportPos.x = 0; wrapped = true; }
        
        if (viewportPos.y < 0) { viewportPos.y = 1; wrapped = true; }
        else if (viewportPos.y > 1) { viewportPos.y = 0; wrapped = true; }
        
        if (wrapped)
        {
            viewportPos.z = Mathf.Abs(Camera.main.transform.position.z);
            transform.position = Camera.main.ViewportToWorldPoint(viewportPos);
        }
    }

    // AgentTest에서 썼던 캔버스 그리기 함수를 그대로 이식
    void DrawOnCanvas()
    {
        if (canvasManager == null) return;

        // 에이전트의 월드 좌표를 화면 픽셀 좌표로 변환
        Vector2 screenPos = Camera.main.WorldToScreenPoint(transform.position);
        
        float ratioX = canvasManager.canvasTexture.width / (float)Screen.width;
        float ratioY = canvasManager.canvasTexture.height / (float)Screen.height;

        float exactX = screenPos.x * ratioX;
        float exactY = (Screen.height - screenPos.y) * ratioY;

        canvasManager.DrawStamp(new Vector2(exactX, exactY), myColor, 10f); // 붓 크기를 10으로 축소
    }
}