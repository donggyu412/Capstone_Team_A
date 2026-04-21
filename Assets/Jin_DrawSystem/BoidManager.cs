using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour
{
    [Header("Boid Settings")]
    public GameObject boidPrefab; // 아까 만든 파란색 프리팹을 넣을 곳
    public int spawnCount = 50;   // 생성할 붓의 개수

    [Header("Canvas Connection")]
    public CanvasManager canvasManager; // 도화지 연결

    // 생성된 모든 에이전트를 담아둘 리스트
    public List<BoidAgent> agents = new List<BoidAgent>();

    void Start()
    {
        // 화면 크기에 맞춰 무작위 위치에 에이전트들을 소환합니다.
        for (int i = 0; i < spawnCount; i++)
        {
            // 카메라 화면 안의 무작위 월드 좌표 계산
            Vector3 randomPos = Camera.main.ScreenToWorldPoint(new Vector3(Random.Range(0, Screen.width), Random.Range(0, Screen.height), 10f));
            randomPos.z = 0; // 2D이므로 Z축은 0으로 고정

            // 프리팹 생성
            GameObject go = Instantiate(boidPrefab, randomPos, Quaternion.identity);
            
            // 생성된 에이전트에게 기본 정보 전달
            BoidAgent agent = go.GetComponent<BoidAgent>();
            agent.manager = this;
            agent.canvasManager = canvasManager;
            
            agents.Add(agent); // 리스트에 추가
        }
    }
}