using UnityEngine;

public class RobotBrush : MonoBehaviour
{
    [Header("연결 설정")]
    [Tooltip("캔버스에 있는 CanvasPainter 스크립트를 연결하세요")]
    public CanvasPainter canvasPainter;
    
    [Tooltip("붓 팀원이 만든 브러시 셰이더(Material)를 연결하세요")]
    public Material brushMaterial; 

    [Header("붓 설정")]
    [Tooltip("붓끝에서 레이저를 쏠 거리 (붓모의 길이)")]
    public float brushLength = 0.05f;

    // 매 프레임마다 붓끝의 위치를 확인합니다.
    void Update()
    {
        // 1. Raycast 쏘기
        // transform.position: 현재 붓끝 오브젝트의 위치
        // -transform.up: 붓끝이 아래를 향한다고 가정 (로봇 팔 축에 따라 transform.forward 등이 될 수 있음)
        if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, brushLength))
        {
            // 2. 부딪힌 물체가 우리가 만든 캔버스가 맞는지 확인
            CanvasPainter hitCanvas = hit.collider.GetComponent<CanvasPainter>();
            
            if (hitCanvas != null)
            {
                // 3. 충돌 지점의 UV 좌표를 가져와서 Paint 함수 실행!
                // hit.textureCoord가 바로 0~1 사이의 정확한 2D 캔버스 좌표입니다.
                hitCanvas.Paint(hit.textureCoord, brushMaterial);
                
                Debug.Log($"도화지에 물감이 묻었습니다! UV 좌표: {hit.textureCoord}");
            }
        }

        // (선택 사항) 에디터에서 레이저가 어떻게 나가는지 시각적으로 보기 위한 줄 긋기
        Debug.DrawRay(transform.position, -transform.up * brushLength, Color.red);
    }
}