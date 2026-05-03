using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

[RequireComponent(typeof(CanvasPainter))]
[RequireComponent(typeof(CanvasMeshGenerator))]

public class PaintingAgent : Agent
{
    private CanvasPainter canvasPainter;
    private CanvasMeshGenerator canvasMeshGenerator;
    private Vector2 strokeStart;

    [Header("목표 이미지")]
    public Texture2D targetImage;

    [Header("붓 설정")]
    public Material brushMaterial;
    public float maxBrushMoveSpeed = 0.05f;

    //캔버스를 다운 스케일링할 해상도
    //고해상도 그대로 입력하면 관찰공간이 너무 많아 수렴 불가능
    [Header("Observation 설정")]
    public int observationResolution = 32;

    //획을 그릴때마다 아주 작은 페널티를 줌
    [Header("보상 설정")]
    public float strokePenaltyWeight = 0.001f;

    //내부 상태 변수

    //0,0이 좌하단 1,1이 우상단
    private Vector2 brushUV;

    //이전 스텝의 캔버스-목표 이미지 차이값, 매 스텝마다 이전보다 나아졌는가를 비교해서 보상을 줌
    private float prevDistance;

    //현재 에피소드에서 사용한 획 수
    private int strokeCount;

    //캔버스 상태를 32*32로 다운스케일한 픽셀 배열
    //ML-Agent에 전달할 Observation 데이터
    private Color[] canvasPixels;
    private Color[] targetPixels;

    private RenderTexture observationRT;

    //유니티 초기화
    public override void Initialize()
    {
        Debug.Log("에이전트 Initialize 호출");
        canvasPainter = GetComponent<CanvasPainter>();
        canvasMeshGenerator = GetComponent<CanvasMeshGenerator>();

        //다운 스케일링용 RenderTexture생성
        //매 스텝마다 새로 만들면 메모리 낭비이므로 여기서 1회만 생성
        observationRT = new RenderTexture(
            observationResolution,
            observationResolution,
            0,
            RenderTextureFormat.ARGB32
            );
        observationRT.Create();

        //목표 이미지를 32*32로 미리 다운스케일링 해서 캐싱
        //매 스텝마다 계산하면 낭비이므로 여기서 1회만 처리
        if (targetImage != null)
        {
            targetPixels = DownscaleTexture(targetImage);
        }
        else
        {
            Debug.LogWarning("PaintingAgent: 타깃이미지 연결 안됨");
        }
    }

    //에피소드 시작 시 호출
    //캔버스 초기화 + 붓 위치 리셋
    public override void OnEpisodeBegin()
    {
        Debug.Log("에이전트 OnEpisodeBegin 호출");

        canvasPainter.ClearCanvas();
        //붓 시작 위치 랜덤
        brushUV = new Vector2(
    Random.Range(0.1f, 0.9f),
    Random.Range(0.1f, 0.9f)
);
        strokeCount = 0;

        // canvasRenderTexture가 준비됐을 때만 계산
        if (canvasPainter.GetCanvasTexture() != null)
        {
            canvasPixels = GetCanvasPixels();
            prevDistance = CalcL2Distance(canvasPixels, targetPixels);
        }
        else
        {
            // 아직 준비 안됐으면 최대값으로 초기화
            prevDistance = float.MaxValue;
            Debug.LogWarning("캔버스 텍스처 아직 준비 안됨, prevDistance 최대값으로 초기화");
        }
    }

    //에이전트가 환경을 관찰하는 함수, 여기서 넣은 값들이 신경망의 입력이 됨
    public override void CollectObservations(VectorSensor sensor)
    {
        Debug.Log("CollectObservations 호출됨!");

        // canvasPixels가 null이면 0으로 채우기
        // null 상태에서 return하면 observation 수가 6146과 안 맞아서 ML-Agents가 멈춤
        if (canvasPixels == null)
            canvasPixels = new Color[observationResolution * observationResolution];

        if (targetPixels == null)
            targetPixels = new Color[observationResolution * observationResolution];

        foreach (Color pixel in canvasPixels)
        {
            sensor.AddObservation(pixel.r);
            sensor.AddObservation(pixel.g);
            sensor.AddObservation(pixel.b);
        }

        foreach (Color pixel in targetPixels)
        {
            sensor.AddObservation(pixel.r);
            sensor.AddObservation(pixel.g);
            sensor.AddObservation(pixel.b);
        }

        sensor.AddObservation(brushUV.x);
        sensor.AddObservation(brushUV.y);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 논문 방식: 한 액션 = 획 하나 완성
        // 시작점, 끝점, 굵기를 한 번에 출력
        float startX = Mathf.Clamp01(Sigmoid(actions.ContinuousActions[0]));
        float startY = Mathf.Clamp01(Sigmoid(actions.ContinuousActions[1]));
        float endX = Mathf.Clamp01(Sigmoid(actions.ContinuousActions[2]));
        float endY = Mathf.Clamp01(Sigmoid(actions.ContinuousActions[3]));
        float width = Mathf.Clamp(Sigmoid(actions.ContinuousActions[4]) * 0.1f, 0.01f, 0.1f);

        if (brushMaterial != null)
        {
            brushMaterial.SetColor("_BrushColor", new Color(0f, 0f, 0f, 1f));
            brushMaterial.SetVector("_StrokeStart", new Vector4(startX, startY, 0, 0));
            brushMaterial.SetVector("_StrokeEnd", new Vector4(endX, endY, 0, 0));
            brushMaterial.SetFloat("_StrokeWidth", width);

            float paperHeight = canvasMeshGenerator.GetHeightAtUV(new Vector2(startX, startY));
            brushMaterial.SetFloat("_PaperHeight", paperHeight);

            canvasPainter.Paint(new Vector2(startX, startY), brushMaterial);
            strokeCount++;
        }

        canvasPixels = GetCanvasPixels();
        float currentDistance = CalcL2Distance(canvasPixels, targetPixels);

        float reward = (prevDistance - currentDistance) + 0.001f;
        AddReward(reward);

        prevDistance = currentDistance;

        if (currentDistance < 0.001f)
        {
            SetReward(1.0f);
            EndEpisode();
        }
    }

    //직접 조작
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continuous = actionsOut.ContinuousActions;

        // 랜덤 획 테스트
        continuous[0] = Random.Range(-1f, 1f); // 시작 x
        continuous[1] = Random.Range(-1f, 1f); // 시작 y
        continuous[2] = Random.Range(-1f, 1f); // 끝 x
        continuous[3] = Random.Range(-1f, 1f); // 끝 y
        continuous[4] = Random.Range(-1f, 1f); // 굵기
    }

    // ─────────────────────────────────────────
    // 현재 캔버스를 32x32로 다운스케일 후 픽셀 배열 반환
    // ─────────────────────────────────────────
    private Color[] GetCanvasPixels()
    {
        RenderTexture canvasRT = canvasPainter.GetCanvasTexture();
        if (canvasRT == null) return new Color[observationResolution * observationResolution];

        // 원본 캔버스  32x32 observationRT로 다운스케일
        // DirectX의 StretchRect와 동일한 개념
        Graphics.Blit(canvasRT, observationRT);

        // GPU  CPU로 픽셀 데이터 읽기
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = observationRT;

        Texture2D tempTex = new Texture2D(
            observationResolution,
            observationResolution,
            TextureFormat.RGB24,
            false
        );
        tempTex.ReadPixels(new Rect(0, 0, observationResolution, observationResolution), 0, 0);
        tempTex.Apply();

        RenderTexture.active = prev;

        Color[] pixels = tempTex.GetPixels();
        Destroy(tempTex); // 메모리 누수 방지

        return pixels;
    }

    // Texture2D를 32x32로 다운스케일
    // targetImage 전처리에 사용 (Initialize에서 1회만 호출)
    private Color[] DownscaleTexture(Texture2D source)
    {
        RenderTexture rt = new RenderTexture(observationResolution, observationResolution, 0);
        Graphics.Blit(source, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(
            observationResolution,
            observationResolution,
            TextureFormat.RGB24,
            false
        );
        result.ReadPixels(new Rect(0, 0, observationResolution, observationResolution), 0, 0);
        result.Apply();

        RenderTexture.active = prev;
        rt.Release();

        return result.GetPixels();
    }

    // 두 픽셀 배열의 L2 거리 계산
    private float CalcL2Distance(Color[] a, Color[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return float.MaxValue;

        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float dr = a[i].r - b[i].r;
            float dg = a[i].g - b[i].g;
            float db = a[i].b - b[i].b;
            sum += dr * dr + dg * dg + db * db;
        }

        // 픽셀 수로 나눠서 정규화
        return sum / a.Length;
    }

    // Sigmoid 함수 (  0~1)
    private float Sigmoid(float x)
    {
        return 1f / (1f + Mathf.Exp(-x));
    }

    // 오브젝트 삭제 시 RenderTexture 메모리 해제
    // GC가 자동 해제하지 않는 네이티브 메모리이므로 명시적 해제 필요
    private void OnDestroy()
    {
        if (observationRT != null)
        {
            observationRT.Release();
            Destroy(observationRT);
        }
    }
}
