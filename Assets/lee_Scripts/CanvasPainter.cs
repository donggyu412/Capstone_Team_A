using UnityEngine;

// 캔버스에 페인팅하는 스크립트
// 붓 팀원이 Paint() 함수를 호출하면 Render Texture에 획을 기록한다
// CanvasMeshGenerator : 캔버스 형태 담당
// CanvasPainter       : 캔버스 위에 그림 기록 담당
[RequireComponent(typeof(CanvasMeshGenerator))]
public class CanvasPainter : MonoBehaviour
{
    [Header("Render Texture 설정")]
    // Inspector에서 CanvasRenderTexture를 드래그 앤 드롭
    public RenderTexture canvasRenderTexture;

    // 더블 버퍼링용 임시 Render Texture
    // Graphics.Blit은 입력과 출력이 같은 텍스처면 안되므로
    // 임시 텍스처에 먼저 결과를 기록하고 캔버스로 복사하는 방식 사용
    // DirectX의 백버퍼 개념과 유사
    private RenderTexture tempRenderTexture;

    void Start()
    {
        if (canvasRenderTexture == null)
        {
            Debug.LogError("CanvasPainter: canvasRenderTexture가 연결되지 않았습니다!");
            return;
        }

        // 캔버스와 동일한 크기로 임시 Render Texture 생성
        tempRenderTexture = new RenderTexture(
            canvasRenderTexture.width,
            canvasRenderTexture.height,
            0,
            canvasRenderTexture.format
        );
        tempRenderTexture.Create();
    }

    // ─────────────────────────────────────────────────────
    // 붓 팀원이 호출하는 공개 함수
    //
    // uv          : Raycast hit.textureCoord로 얻은 UV 좌표 (0~1)
    // brushMaterial : 붓 팀원이 만든 BrushShader Material
    //                 (붓 모양, 크기, 색상, 필압 등이 이미 설정된 상태로 넘어옴)
    // ─────────────────────────────────────────────────────
    public void Paint(Vector2 uv, Material brushMaterial)
    {
        if (canvasRenderTexture == null || brushMaterial == null) return;

        // UV 좌표를 셰이더에 전달
        // 붓 팀원의 BrushShader에서 _BrushUV 프로퍼티로 받아서 사용
        brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));

        // Graphics.Blit 흐름:
        // 1. canvasRenderTexture (현재 캔버스 상태) 를 입력으로
        // 2. brushMaterial 셰이더로 처리해서
        // 3. tempRenderTexture (임시 버퍼) 에 결과 저장
        Graphics.Blit(canvasRenderTexture, tempRenderTexture, brushMaterial);

        // 임시 버퍼 -> 실제 캔버스로 복사
        Graphics.Blit(tempRenderTexture, canvasRenderTexture);
    }

    // 캔버스를 아이보리색(종이색)으로 초기화
    // 붓 팀원 또는 게임 시작 시 호출 가능
    public void ClearCanvas()
    {
        if (canvasRenderTexture == null) return;

        // 현재 렌더 타겟을 canvasRenderTexture로 변경
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = canvasRenderTexture;

        // 아이보리색으로 초기화
        GL.Clear(true, true, new Color(1f, 0.96f, 0.86f));

        // 렌더 타겟 복구
        RenderTexture.active = previous;
    }

    // 현재 캔버스 Render Texture를 반환
    // 붓 팀원 또는 AI 에이전트가 캔버스 상태를 읽을 때 사용
    public RenderTexture GetCanvasTexture()
    {
        return canvasRenderTexture;
    }

    // 오브젝트 삭제 시 임시 텍스처 메모리 해제
    // GC가 자동 해제하지 않는 Unity 네이티브 메모리이므로 명시적 해제 필요
    void OnDestroy()
    {
        if (tempRenderTexture != null)
        {
            tempRenderTexture.Release();
            Destroy(tempRenderTexture);
        }
    }
}