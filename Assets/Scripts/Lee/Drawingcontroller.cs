using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawingController : MonoBehaviour
{
    [Header("°üÀý (j0 ~ j6 ÀüºÎ ¿¬°á)")]
    public Transform j0, j1, j2, j3, j4, j5, j6;

    [Header("ÂüÁ¶")]
    public Transform brushTip;
    public Transform canvas;
    public CanvasPainter canvasPainter;
    public Material brushMaterial;
    public ReacherRobot reacherRobot;

    [Header("Å¸°Ù ÀÌ¹ÌÁö (´ÜÀÏ Å×½ºÆ®¿ë)")]
    public Texture2D targetImage;

    [Header("IK ¼³Á¤")]
    [Range(1, 30)] public int ikIterations = 8;
    public float arrivalThreshold = 0.15f;
    public float maxWaitTime = 0.5f;
    [Range(0.01f, 0.5f)] public float ikSpeed = 0.1f;

    [Header("È¹ ¼³Á¤")]
    public int samplingStep = 8;
    [Range(0f, 0.5f)] public float colorTolerance = 0.15f;
    [Range(0.5f, 1f)] public float backgroundThreshold = 0.95f;
    [Range(0.01f, 0.5f)] public float liftSpeed = 0.4f;
    [Range(0.01f, 0.5f)] public float strokeSpeed = 0.08f;
    [Range(2, 20)] public int interpolationSteps = 5;

    [Header("Äµ¹ö½º ¿ùµå Å©±â")]
    public float canvasWorldWidth = 4f;
    public float canvasWorldHeight = 3f;

    [Header("EEG °¨Á¤ »óÅÂ (0~1)")]
    [Range(0f, 1f)] public float eegJoy = 0f;
    [Range(0f, 1f)] public float eegSadness = 0f;
    [Range(0f, 1f)] public float eegExcited = 0f;
    [Range(0f, 1f)] public float eegCalm = 0f;

    private Transform[] joints;
    private Vector3[] jointAxes;
    private Rigidbody[] allRigidbodies;
    private Transform[] originalParents;

    private Vector3 currentTarget;
    private bool isMoving = false;
    private bool isPainting = false;
    private float baseStrokeSpeed;

    private class StrokeGroup
    {
        public Color color;
        public List<List<Vector2>> paths = new List<List<Vector2>>();
    }

    void Start()
    {
        baseStrokeSpeed = strokeSpeed;
        joints = new Transform[] { j6, j5, j4, j3, j2, j1 };
        jointAxes = new Vector3[]
        {
            Vector3.up, Vector3.right, Vector3.up,
            Vector3.right, Vector3.right, Vector3.up,
        };

        var rbList = new List<Rigidbody>();
        foreach (var t in new Transform[] { j0, j1, j2, j3, j4, j5, j6 })
        {
            if (t == null) continue;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) rbList.Add(rb);
        }
        allRigidbodies = rbList.ToArray();

        originalParents = new Transform[]
        {
            j0?.parent, j1?.parent, j2?.parent, j3?.parent,
            j4?.parent, j5?.parent, j6?.parent,
        };

        Debug.Log("[DrawingController] ÃÊ±âÈ­ ¿Ï·á");
    }

    void Update()
    {
        if (isMoving && brushTip != null)
            SolveIK(currentTarget);

        if (Input.GetKeyDown(KeyCode.Space) && !isPainting)
        {
            if (targetImage == null) { Debug.LogError("targetImage ¾øÀ½!"); return; }
            StartCoroutine(StartDrawing(new List<Texture2D> { targetImage }));
        }
    }

    // ´ÜÀÏ ÀÌ¹ÌÁö Å×½ºÆ®¿ë
    public void StartDrawingExternal()
    {
        if (isPainting || targetImage == null) return;
        StartCoroutine(StartDrawing(new List<Texture2D> { targetImage }));
    }

    // CreativeDrawingManager¿¡¼­ ²Þ ÀÌ¹ÌÁöµé Àü´Þ
    public void StartDreamDrawing(List<Texture2D> dreamImages)
    {
        if (isPainting || dreamImages == null || dreamImages.Count == 0) return;
        StartCoroutine(StartDrawing(dreamImages));
    }

    public void SetEEG(float joy, float sadness, float excited, float calm)
    {
        eegJoy = Mathf.Clamp01(joy);
        eegSadness = Mathf.Clamp01(sadness);
        eegExcited = Mathf.Clamp01(excited);
        eegCalm = Mathf.Clamp01(calm);
    }

    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    // ¸ÞÀÎ µå·ÎÀ× ÄÚ·çÆ¾
    // ÀÌ¹ÌÁö°¡ ¿©·¯ ÀåÀÌ¸é ²Þ ÀüÈ¯ È¿°ú Àû¿ë
    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    IEnumerator StartDrawing(List<Texture2D> images)
    {
        isPainting = true;
        baseStrokeSpeed = strokeSpeed;
        Debug.Log("=== ²Þ µå·ÎÀ× ½ÃÀÛ (" + images.Count + "Àå) ===");

        if (reacherRobot != null) reacherRobot.enabled = false;
        SetKinematic(true);
        yield return new WaitForSeconds(0.1f);

        BuildJointChain();
        yield return null;

        yield return StartCoroutine(MoveSmoothly(
            canvas.position + canvas.up * 0.1f, liftSpeed));

        for (int imgIdx = 0; imgIdx < images.Count; imgIdx++)
        {
            Texture2D currentImage = images[imgIdx];
            Texture2D nextImage = imgIdx + 1 < images.Count ? images[imgIdx + 1] : null;

            Debug.Log("=== ²Þ ÀÌ¹ÌÁö " + (imgIdx + 1) + "/" + images.Count + " ±×¸®±â ===");

            // ÇöÀç ÀÌ¹ÌÁö È¹ ±×·ì »ý¼º
            List<StrokeGroup> currentGroups = BuildStrokeGroups(currentImage);
            List<List<Vector2>> allCurrentPaths = GetAllPathsSorted();

            // ´ÙÀ½ ÀÌ¹ÌÁö È¹ ±×·ì »ý¼º (ÀüÈ¯¿ë)
            List<List<Vector2>> allNextPaths = null;
            if (nextImage != null)
            {
                BuildStrokeGroups(nextImage);
                allNextPaths = GetAllPathsSorted();
            }

            // ÇöÀç ÀÌ¹ÌÁö ±×¸®±â (·£´ý ±×·È´Ù Áö¿ì±â Æ÷ÇÔ)
            yield return StartCoroutine(DrawWithChildlikeStyle(currentGroups, allCurrentPaths));

            // ´ÙÀ½ ÀÌ¹ÌÁö°¡ ÀÖÀ¸¸é ÀüÈ¯ È¿°ú
            if (nextImage != null && allNextPaths != null)
            {
                Debug.Log("=== ²Þ ÀÌ¹ÌÁö ÀüÈ¯ ===");
                yield return StartCoroutine(TransitionToNextImage(
                    currentGroups, allCurrentPaths,
                    BuildStrokeGroups(nextImage), allNextPaths));
            }
        }

        isMoving = false;
        Debug.Log("=== ²Þ µå·ÎÀ× ¿Ï·á ===");

        RestoreJointChain();
        SetKinematic(false);
        if (reacherRobot != null) reacherRobot.enabled = true;
        isPainting = false;
    }

    // ÀÓ½Ã ÀúÀå¿ë
    private List<StrokeGroup> lastBuiltGroups = new List<StrokeGroup>();

    List<StrokeGroup> BuildStrokeGroups(Texture2D image)
    {
        lastBuiltGroups = new List<StrokeGroup>();
        int imgW = image.width;
        int imgH = image.height;
        Color[] pixels = image.GetPixels();
        Color bgColor = DetectBackgroundColor(pixels, imgW, imgH);

        // ¦¡¦¡ 1´Ü°è: Sobel ¿§Áö °¨Áö ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
        // °¢ ÇÈ¼¿ÀÇ ¹à±â º¯È­(gradient)¸¦ °è»êÇØ¼­ ¿§Áö(À±°û¼±) ÇÈ¼¿ °¨Áö
        float[,] edgeMap = new float[imgW, imgH];
        float maxEdge = 0f;

        for (int y = 1; y < imgH - 1; y++)
        {
            for (int x = 1; x < imgW - 1; x++)
            {
                Color c = pixels[y * imgW + x];
                if (IsBackground(c, bgColor)) continue;

                // ÁÖº¯ ÇÈ¼¿ ¹à±â
                float tl = GetBrightness(pixels[(y - 1) * imgW + (x - 1)]);
                float tm = GetBrightness(pixels[(y - 1) * imgW + x]);
                float tr = GetBrightness(pixels[(y - 1) * imgW + (x + 1)]);
                float ml = GetBrightness(pixels[y * imgW + (x - 1)]);
                float mr = GetBrightness(pixels[y * imgW + (x + 1)]);
                float bl2 = GetBrightness(pixels[(y + 1) * imgW + (x - 1)]);
                float bm = GetBrightness(pixels[(y + 1) * imgW + x]);
                float br2 = GetBrightness(pixels[(y + 1) * imgW + (x + 1)]);

                // Sobel ÇÊÅÍ
                float gx = -tl - 2 * ml - bl2 + tr + 2 * mr + br2;
                float gy = -tl - 2 * tm - tr + bl2 + 2 * bm + br2;
                float g = Mathf.Sqrt(gx * gx + gy * gy);

                edgeMap[x, y] = g;
                if (g > maxEdge) maxEdge = g;
            }
        }

        // ¦¡¦¡ 2´Ü°è: ¿§Áö ÇÈ¼¿À» ¿¬°áµÈ È¹À¸·Î ¹­±â ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
        float edgeThreshold = maxEdge * 0.3f; // »óÀ§ 70% ¿§Áö¸¸ »ç¿ë
        bool[,] visited = new bool[imgW, imgH];

        // ¿§Áö ÇÈ¼¿ ¼öÁý
        List<Vector2Int> edgePixels = new List<Vector2Int>();
        for (int y = 1; y < imgH - 1; y += samplingStep)
            for (int x = 1; x < imgW - 1; x += samplingStep)
                if (edgeMap[x, y] > edgeThreshold && !IsBackground(pixels[y * imgW + x], bgColor))
                    edgePixels.Add(new Vector2Int(x, y));

        Debug.Log("¿§Áö ÇÈ¼¿ ¼ö: " + edgePixels.Count);

        // ¿§Áö ÇÈ¼¿À» ¿¬°áµÈ °æ·Î·Î ¹­±â (°¡±î¿î ÇÈ¼¿³¢¸® ¿¬°á)
        bool[] used = new bool[edgePixels.Count];
        int searchRadius = samplingStep * 3;

        for (int i = 0; i < edgePixels.Count; i++)
        {
            if (used[i]) continue;

            List<Vector2> path = new List<Vector2>();
            Color pathColor = pixels[edgePixels[i].y * imgW + edgePixels[i].x];
            int current = i;
            used[current] = true;
            path.Add(new Vector2((float)edgePixels[current].x / imgW,
                                 (float)edgePixels[current].y / imgH));

            // °¡Àå °¡±î¿î ¹Ì»ç¿ë ¿§Áö ÇÈ¼¿·Î ¿¬°á
            for (int step = 0; step < 500; step++)
            {
                int nearest = -1;
                float minDist = searchRadius;
                for (int j = 0; j < edgePixels.Count; j++)
                {
                    if (used[j]) continue;
                    float dist = Vector2Int.Distance(edgePixels[current], edgePixels[j]);
                    Color jColor = pixels[edgePixels[j].y * imgW + edgePixels[j].x];
                    if (dist < minDist && ColorSimilar(jColor, pathColor))
                    {
                        minDist = dist;
                        nearest = j;
                    }
                }
                if (nearest == -1) break;
                used[nearest] = true;
                current = nearest;
                path.Add(new Vector2((float)edgePixels[current].x / imgW,
                                     (float)edgePixels[current].y / imgH));
            }

            if (path.Count > 2)
                AddPathToGroup(lastBuiltGroups, pathColor, path);
        }

        // ¦¡¦¡ 3´Ü°è: ³»ºÎ »ö»ó Ã¤¿ì±â (»ö»ó ¿µ¿ªº° °¡·Î È¹) ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
        for (int y = 0; y < imgH; y += samplingStep * 2)
        {
            List<Vector2> fillPath = null;
            Color fillColor = Color.clear;

            for (int x = 0; x < imgW; x += samplingStep)
            {
                Color c = pixels[y * imgW + x];
                if (IsBackground(c, bgColor) || edgeMap[Mathf.Clamp(x, 1, imgW - 2), Mathf.Clamp(y, 1, imgH - 2)] > edgeThreshold)
                {
                    if (fillPath != null && fillPath.Count > 1)
                        AddPathToGroup(lastBuiltGroups, fillColor, fillPath);
                    fillPath = null;
                    continue;
                }

                Vector2 uv = new Vector2((float)x / imgW, (float)y / imgH);
                if (fillPath == null) { fillPath = new List<Vector2> { uv }; fillColor = c; }
                else if (ColorSimilar(c, fillColor)) fillPath.Add(uv);
                else
                {
                    if (fillPath.Count > 1) AddPathToGroup(lastBuiltGroups, fillColor, fillPath);
                    fillPath = new List<Vector2> { uv }; fillColor = c;
                }
            }
            if (fillPath != null && fillPath.Count > 1)
                AddPathToGroup(lastBuiltGroups, fillColor, fillPath);
        }

        Debug.Log("»ö»ó ±×·ì: " + lastBuiltGroups.Count + "°³");

        // ¾îµÎ¿î »ö(°ËÀº À±°û¼±) ¸ÕÀú, ¹àÀº »ö ³ªÁß¿¡ Á¤·Ä
        lastBuiltGroups.Sort((a, b) => {
            float ha, sa, va, hb, sb, vb;
            Color.RGBToHSV(a.color, out ha, out sa, out va);
            Color.RGBToHSV(b.color, out hb, out sb, out vb);
            return (sa + va).CompareTo(sb + vb);
        });

        return lastBuiltGroups;
    }

    float GetBrightness(Color c) => (c.r + c.g + c.b) / 3f;

    // ÇöÀç º× À§Ä¡¿¡¼­ °¡±î¿î È¹ ¼ø¼­·Î Á¤·Ä
    List<List<Vector2>> GetAllPathsSorted()
    {
        List<List<Vector2>> allPaths = new List<List<Vector2>>();
        foreach (var g in lastBuiltGroups)
            allPaths.AddRange(g.paths);

        // Greedy: ÇöÀç À§Ä¡¿¡¼­ °¡Àå °¡±î¿î È¹ºÎÅÍ
        List<List<Vector2>> sorted = new List<List<Vector2>>();
        List<bool> used = new List<bool>(new bool[allPaths.Count]);
        Vector2 currentPos = new Vector2(0.5f, 0.5f);

        while (sorted.Count < allPaths.Count)
        {
            float minDist = float.MaxValue;
            int nearest = 0;
            for (int i = 0; i < allPaths.Count; i++)
            {
                if (used[i] || allPaths[i].Count == 0) continue;
                float dist = Vector2.Distance(currentPos, allPaths[i][0]);
                if (dist < minDist) { minDist = dist; nearest = i; }
            }
            sorted.Add(allPaths[nearest]);
            currentPos = allPaths[nearest][allPaths[nearest].Count - 1];
            used[nearest] = true;
        }

        return sorted;
    }

    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    // ¾î¸°¾ÆÀÌÃ³·³ ±×¸®±â (·£´ý ±×·È´Ù Áö¿ì±â)
    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    IEnumerator DrawWithChildlikeStyle(List<StrokeGroup> groups, List<List<Vector2>> sortedPaths)
    {
        Dictionary<List<Vector2>, Color> pathColorMap = new Dictionary<List<Vector2>, Color>();
        foreach (var g in groups)
            foreach (var path in g.paths)
                pathColorMap[path] = g.color;

        foreach (var path in sortedPaths)
        {
            if (path.Count == 0) continue;

            Color pathColor = pathColorMap.ContainsKey(path) ? pathColorMap[path] : Color.black;
            Color eegColor = ApplyEEGToColor(pathColor);
            if (brushMaterial != null)
                brushMaterial.SetColor("_BrushColor", eegColor);
            if (canvasPainter != null)
                canvasPainter.inkColor = eegColor;

            float currentStrokeSpeed = GetEEGStrokeSpeed();
            yield return StartCoroutine(DrawPath(path, currentStrokeSpeed, eegColor));
        }
    }

    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    // ÀÌ¹ÌÁö ÀüÈ¯ È¿°ú
    // Áö¿ì±â ¾øÀÌ ´ÙÀ½ ÀÌ¹ÌÁö¸¦ ¹Ù·Î µ¡±×¸®±â
    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    IEnumerator TransitionToNextImage(
        List<StrokeGroup> curGroups, List<List<Vector2>> curPaths,
        List<StrokeGroup> nextGroups, List<List<Vector2>> nextPaths)
    {
        Dictionary<List<Vector2>, Color> nextColorMap = new Dictionary<List<Vector2>, Color>();
        foreach (var g in nextGroups)
            foreach (var path in g.paths)
                nextColorMap[path] = g.color;

        // Áö¿ì±â ¾øÀÌ ¹Ù·Î µ¡±×¸®±â
        foreach (var path in nextPaths)
        {
            Color nextColor = nextColorMap.ContainsKey(path) ? nextColorMap[path] : Color.black;
            Color eegColor = ApplyEEGToColor(nextColor);
            if (brushMaterial != null)
                brushMaterial.SetColor("_BrushColor", eegColor);
            if (canvasPainter != null)
                canvasPainter.inkColor = eegColor;
            yield return StartCoroutine(DrawPath(path, GetEEGStrokeSpeed(), eegColor));
        }
    }

    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    // È¹ ±×¸®±â
    // ¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡¦¡
    IEnumerator DrawPath(List<Vector2> path, float speed, Color color)
    {
        if (path.Count == 0) yield break;

        if (brushMaterial != null)
            brushMaterial.SetColor("_BrushColor", color);
        if (canvasPainter != null)
            canvasPainter.inkColor = color;

        // º× µé±â
        yield return StartCoroutine(MoveSmoothly(
            UVToWorld(path[0]) - canvas.up * 0.2f, liftSpeed));

        // º× ³»¸®±â
        yield return StartCoroutine(MoveSmoothly(UVToWorld(path[0]), speed));

        // È¹ ±×¸®±â
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 from = UVToWorld(path[i - 1]);
            Vector3 to = UVToWorld(path[i]);

            for (int s = 1; s <= interpolationSteps; s++)
            {
                float t = (float)s / interpolationSteps;
                float smooth = t * t * (3f - 2f * t);
                currentTarget = Vector3.Lerp(from, to, smooth);
                isMoving = true;

                float elapsed = 0f;
                while (elapsed < maxWaitTime)
                {
                    if (Vector3.Distance(brushTip.position, currentTarget) < arrivalThreshold) break;
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }

        isMoving = false;
    }

    Color DetectBackgroundColor(Color[] pixels, int imgW, int imgH)
    {
        // 1. Åõ¸í ¹è°æ Ã¼Å©
        if (pixels[0].a < 0.1f || pixels[imgW - 1].a < 0.1f)
        {
            Debug.Log("Åõ¸í ¹è°æ °¨Áö");
            return Color.clear;
        }

        Color tl = pixels[0];
        Color tr = pixels[imgW - 1];
        Color bl = pixels[(imgH - 1) * imgW];
        Color br = pixels[(imgH - 1) * imgW + imgW - 1];

        // 2. ¸ð¼­¸® 4°³°¡ ºñ½ÁÇÑ »öÀÎÁö È®ÀÎ
        float diffH = ColorDistance(tl, tr);
        float diffV = ColorDistance(tl, bl);
        float diffD = ColorDistance(tl, br);
        float maxDiff = Mathf.Max(diffH, Mathf.Max(diffV, diffD));

        if (maxDiff < 0.25f)
        {
            // ¸ð¼­¸®°¡ ºñ½ÁÇÑ »ö ¡æ ±×°Ô ¹è°æ
            Color avg = new Color(
                (tl.r + tr.r + bl.r + br.r) / 4f,
                (tl.g + tr.g + bl.g + br.g) / 4f,
                (tl.b + tr.b + bl.b + br.b) / 4f, 1f);
            Debug.Log("´Ü»ö ¹è°æ °¨Áö: " + avg);
            return avg;
        }

        // 3. ¸ð¼­¸®°¡ ´Ù ´Ù¸¥ »ö ¡æ ¹è°æ ¾øÀ½ (ÀüÃ¼ ±×¸®±â)
        Debug.Log("¹è°æ ¾øÀ½ ¡æ ÀüÃ¼ ±×¸®±â");
        return Color.clear;
    }

    float ColorDistance(Color a, Color b)
    {
        return Mathf.Sqrt(
            (a.r - b.r) * (a.r - b.r) +
            (a.g - b.g) * (a.g - b.g) +
            (a.b - b.b) * (a.b - b.b));
    }

    bool IsBackground(Color pixel, Color bgColor)
    {
        if (pixel.a < 0.1f) return true;

        // ¹è°æ ¾øÀ½ or Åõ¸í ¹è°æ ¡æ ¹à±â·Î ÆÇ´Ü
        if (bgColor.a < 0.1f)
            return (pixel.r + pixel.g + pixel.b) / 3f > backgroundThreshold;

        float diff = ColorDistance(pixel, bgColor);

        // ¹è°æÀÌ ¾îµÎ¿ì¸é ´õ ¾ö°ÝÇÏ°Ô (°ËÀº À±°û¼± ¿ÀÀÎ ¹æÁö)
        float bgBrightness = (bgColor.r + bgColor.g + bgColor.b) / 3f;
        float threshold = bgBrightness < 0.2f ? 0.25f : 0.15f;

        return diff < threshold;
    }

    void AddPathToGroup(List<StrokeGroup> groups, Color color, List<Vector2> path)
    {
        StrokeGroup target = null;
        foreach (var g in groups)
            if (ColorSimilar(g.color, color)) { target = g; break; }
        if (target == null) { target = new StrokeGroup { color = color }; groups.Add(target); }
        target.paths.Add(new List<Vector2>(path));
    }

    Color ApplyEEGToColor(Color originalColor)
    {
        float h, s, v;
        Color.RGBToHSV(originalColor, out h, out s, out v);
        s += eegExcited * 0.3f;
        s -= eegCalm * 0.2f;
        v += eegJoy * 0.2f;
        h = Mathf.Lerp(h, 0.6f, eegSadness * 0.4f);
        s = Mathf.Clamp01(s); v = Mathf.Clamp01(v);
        Color result = Color.HSVToRGB(h, s, v);
        result.a = originalColor.a;
        return result;
    }

    float GetEEGStrokeSpeed()
    {
        float speed = baseStrokeSpeed;
        speed += eegExcited * 0.15f;
        speed -= eegCalm * 0.03f;
        speed -= eegSadness * 0.02f;
        speed += eegJoy * 0.05f;
        return Mathf.Clamp(speed, 0.01f, 0.5f);
    }

    void BuildJointChain()
    {
        if (j1 != null) j1.SetParent(j0, true);
        if (j2 != null) j2.SetParent(j1, true);
        if (j3 != null) j3.SetParent(j2, true);
        if (j4 != null) j4.SetParent(j3, true);
        if (j5 != null) j5.SetParent(j4, true);
        if (j6 != null) j6.SetParent(j5, true);
    }

    void RestoreJointChain()
    {
        Transform[] joints7 = { j0, j1, j2, j3, j4, j5, j6 };
        for (int i = 0; i < joints7.Length; i++)
            if (joints7[i] != null)
                joints7[i].SetParent(originalParents[i], true);
    }

    void SolveIK(Vector3 target)
    {
        for (int iter = 0; iter < ikIterations; iter++)
        {
            if (Vector3.Distance(brushTip.position, target) < arrivalThreshold * 0.5f) break;
            for (int i = 0; i < joints.Length; i++)
            {
                Transform joint = joints[i];
                if (joint == null) continue;
                Vector3 axis = jointAxes[i];
                Vector3 toTip = brushTip.position - joint.position;
                Vector3 toTarget = target - joint.position;
                Vector3 projTip = Vector3.ProjectOnPlane(toTip, axis);
                Vector3 projTarget = Vector3.ProjectOnPlane(toTarget, axis);
                if (projTip.magnitude < 0.001f || projTarget.magnitude < 0.001f) continue;
                float angle = Vector3.SignedAngle(projTip, projTarget, axis);
                float step = Mathf.Clamp(angle, -45f, 45f) * ikSpeed;
                joint.Rotate(axis, step, Space.World);
            }
        }
    }

    IEnumerator MoveSmoothly(Vector3 target, float speed)
    {
        isMoving = true;
        float elapsed = 0f;
        float timeout = 3f;
        float prevIkSpeed = ikSpeed;

        while (elapsed < timeout)
        {
            float dist = Vector3.Distance(brushTip.position, target);
            ikSpeed = Mathf.Lerp(speed * 0.3f, speed, Mathf.Clamp01(dist));
            currentTarget = target;
            if (dist < arrivalThreshold) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        ikSpeed = prevIkSpeed;
        isMoving = false;
    }

    Vector3 UVToWorld(Vector2 uv)
    {
        float localX = (uv.x - 0.5f) * canvasWorldWidth;
        float localZ = (uv.y - 0.5f) * canvasWorldHeight;
        return canvas.transform.position
             + canvas.transform.right * localX
             + canvas.transform.forward * localZ
             + canvas.transform.up * 0.1f;
    }

    void SetKinematic(bool value)
    {
        foreach (var rb in allRigidbodies)
            if (rb != null) rb.isKinematic = value;
    }

    bool ColorSimilar(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < colorTolerance
        && Mathf.Abs(a.g - b.g) < colorTolerance
        && Mathf.Abs(a.b - b.b) < colorTolerance;
}