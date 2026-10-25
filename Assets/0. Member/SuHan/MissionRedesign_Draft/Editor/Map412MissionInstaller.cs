using System.Collections.Generic;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [역할 · 에디터 전용, 1클릭 도구] 4~12 맵 씬(Assets/Scenes/4~12.unity)에 미션 장치를 놓고 1층 아이템 NavMesh 를 굽는다 (9a 명세).
/// [자리 — MP1 · MP2 · MP6 · MP7] 하나짜리 장치 8개(밸브 · 차단기 · 전선 · 필터+청소기 · 안테나 · 압력 · 코드 · 장비 조립)와 방화문은
///  맵 중심 (0.35, −20.8) 둘레 가운데 구역(x −10 ~ 10 · z −30 ~ −12) 안, 발전기 3대 · 생명 유지 본체 3대는 맵 곳곳.
///  좌표는 씬의 바닥 · 벽 좌표를 분석해 정한 임시값이다 (벽에서 0.15m · 장치끼리 0.4m · 스폰에서 3m — 검사 스크립트 `MissionDesign/Verification~/Layout/`).
///  기획자가 옮긴다 → 같은 이름이 이미 있으면 건너뛴다 (MP3).
/// [공유 데이터 — MP5] 미션 풀 · TG 데이터를 건드리지 않는다. 씬에 이미 있는 MissionManager(실제 게임 풀) · PlayerHealthActorGate 를 그대로 쓴다.
/// [공유 씬 — MP11] 4~12.unity 는 맵 담당이 만든 씬 — 이 메뉴로만 바꾼다 (미션 루트 하나 추가 · NavMesh · 미션 목록 설정 · 저장).
/// [미션 목록 — 10 명세 ML12] 왼쪽 미션 목록 패널을 왼쪽 위 글자 칸 하나로 바꾼다 (공통 단계 SetupMissionList — 글자 칸이 없을 때만).
/// [근거] 9a 명세 MP1 ~ MP11 (질문 1 · 2 · 3 사용자 결정, 접근 1), 10 명세 ML10 ~ ML12
/// </summary>
public static class Map412MissionInstaller
{
    private const string ScenePath = "Assets/Scenes/4~12.unity";

    // 맵 중심 = 바닥 범위(x −25.65 ~ 26.35, z −42.8 ~ 1.2)의 가운데 (9a 명세 1-1)
    private static readonly Vector3 MapCenter = new Vector3(0.35f, 0f, -20.8f);

    // 9a 명세 3장 배치표. Yaw = 장치 앞(+Z)이 보는 쪽 — 넓은 공간을 보게 돌렸다.
    //  벽걸이(밸브 · 차단기 · 필터)는 바로 뒤에 벽이 있는 면에 붙인다 (밸브 · 차단기 높이는 GamePlay 와 같게 1.18 · 1.25m). 벽과 닿는 것이 정상이라 겹침 검사를 끈다
    private static readonly MissionPlacement[] Placements =
    {
        // ── 가운데 구역 (맵 중앙 — MP1 · MP2) ──
        // 압력: 조작대는 북쪽 통로 입구 앞, 피스톤 3개는 남쪽 6.7m 뒤 (z −27.1 ~ −28.9) — 가운데 넓은 곳을 차지하는 가장 큰 장치
        new MissionPlacement { Name = "Pressure", PrefabPath = LegacyMissionConverter.PressureStationPath, Position = new Vector3(3.4f, 0f, -21.3f), Yaw = 0f, CheckOverlap = true },
        new MissionPlacement { Name = "Assembly", PrefabPath = LegacyMissionConverter.AssemblyStationPath, Position = new Vector3(8.7f, 0f, -22f), Yaw = -90f, CheckOverlap = true },
        // 안테나 1대 (MP6 — 단체 TG009 는 1번이면 완료)
        new MissionPlacement { Name = "Antenna", PrefabPath = LegacyMissionConverter.AntennaStationPath, Position = new Vector3(9f, 0f, -29.2f), Yaw = 0f, CheckOverlap = true },
        // 방화문: 북쪽 통로의 동쪽 벽(x 5.85)에 등을 대고 서쪽을 본다. 폭 약 8m 라 북쪽 끝이 구역 선을 1m쯤 넘는다
        new MissionPlacement { Name = "FireDoor", PrefabPath = LegacyMissionConverter.FireDoorStationPath, Position = new Vector3(5.6f, 0f, -14.8f), Yaw = -90f, CheckOverlap = true },
        // 차단기: 오른쪽 작은 방의 서쪽 벽(동쪽 면 x 6.85)에 걸기
        new MissionPlacement { Name = "Breaker", PrefabPath = LegacyMissionConverter.BreakerStationPath, Position = new Vector3(6.9f, 1.25f, -15f), Yaw = 90f },
        // 코드: 스폰 방 서쪽 (스폰 지점에서 4.5m)
        new MissionPlacement { Name = "Code", PrefabPath = LegacyMissionConverter.CodeStationPath, Position = new Vector3(-8f, 0f, -20.5f), Yaw = 90f, CheckOverlap = true },
        // 밸브: 스폰 방 남동쪽 벽의 가운데 쪽 면(x −2.82)에 걸기
        new MissionPlacement { Name = "Valve", PrefabPath = LegacyMissionConverter.ValveStationPath, Position = new Vector3(-2.72f, 1.18f, -20.6f), Yaw = 90f },
        // 필터: 1.1 × 1.1m 얇은 판이고 기준점이 판 가운데라 벽걸이로 다룬다 — 스폰 방 남쪽 벽(남쪽 면 z −23.3)에 등을 대고, 아래 끝이 바닥 바로 위(0.05m)가 되게
        //  (9a 테스트 후: 바닥 높이 0.03 에 놓았더니 아래 절반이 바닥에 묻혔다 — 진단 로그로 모델 범위 확인, MP12)
        new MissionPlacement { Name = "Filter", PrefabPath = LegacyMissionConverter.FilterStationPath, Position = new Vector3(-5.6f, 0.6f, -23.38f), Yaw = 180f },
        // 청소기: 필터 옆 바닥 (필터를 하려면 먼저 든다)
        new MissionPlacement { Name = "Vacuum", PrefabPath = LegacyMissionConverter.VacuumToolPath, Position = new Vector3(-4f, 0.15f, -25.4f), Yaw = 0f, CheckOverlap = true },
        new MissionPlacement { Name = "Wiring", PrefabPath = LegacyMissionConverter.WiringStationPath, Position = new Vector3(-7.5f, 0f, -25.9f), Yaw = 0f, CheckOverlap = true },

        // ── 맵 곳곳 (기획서 "시설 곳곳에 있는 발전기 3대", 생명 유지 "본체는 총 3개" — MP1) ──
        // 발전기 모델은 4 × 2m (로컬 X 4m, 기준점 가운데 — 9a 테스트 후 진단 로그로 확인, MP12). 벽에서 1.3m 이상 떨어진 자리를 검사 스크립트로 골랐다
        new MissionPlacement { Name = "Generator_A", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-20f, 0f, -12f), Yaw = 90f, CheckOverlap = true },
        new MissionPlacement { Name = "Generator_B", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(12.5f, 0f, -4f), Yaw = 180f, CheckOverlap = true },
        new MissionPlacement { Name = "Generator_C", PrefabPath = LegacyMissionConverter.GeneratorStationPath, Position = new Vector3(-3f, 0f, -39f), Yaw = -90f, CheckOverlap = true },
        new MissionPlacement { Name = "LifeSupport", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(23.5f, 0f, -27f), Yaw = -90f, CheckOverlap = true },
        new MissionPlacement { Name = "LifeSupport_B", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(-20.5f, 0f, -22f), Yaw = 90f, CheckOverlap = true },
        new MissionPlacement { Name = "LifeSupport_C", PrefabPath = LegacyMissionConverter.LifeSupportStationPath, Position = new Vector3(4f, 0f, -40f), Yaw = 0f, CheckOverlap = true },
    };

    // 1층 아이템 NavMesh (MP8): 바닥 전체(x −25.65 ~ 26.35, z −42.8 ~ 1.2) + 여유, 높이 지면 −0.5 ~ +2.0m (6b 보강 NS2 — "1층").
    //  서쪽 바깥벽 밖 2m 바닥 띠도 구워질 수 있지만, 아이템은 "장치에서 걸어갈 수 있는 곳"만 고르므로(NS5) 거기 놓이지 않는다
    private static readonly Bounds NavMeshVolume = new Bounds(new Vector3(0.35f, 0.75f, -20.8f), new Vector3(54f, 2.5f, 46f));
    private const string NavMeshPath = MissionMapInstaller.GameDataFolder + "/Map4to12_ItemNavMesh.asset";

    // 배치표 · NavMesh 값보다 뒤에 둔다 — 정적 필드는 적힌 순서로 초기화된다
    private static readonly MissionMapSpec Spec = new MissionMapSpec
    {
        Label = "4~12",
        ScenePath = ScenePath,
        Placements = Placements,
        FaceFocus = MapCenter,
        NavMeshVolume = NavMeshVolume,
        NavMeshAssetPath = NavMeshPath,
    };

    // ═════════════════════════════════════════════════════════════
    //  메뉴
    // ═════════════════════════════════════════════════════════════

    [MenuItem("SuHan/Apply Missions To 4~12")]
    public static void Apply()
    {
        if (!MissionMapInstaller.TryOpenScene(Spec, out Scene scene))
            return;

        // 실제 게임 풀은 읽기만 한다 (MP5) — 씬에 MissionManager 가 없을 때 새로 만들면서 넣는 용도
        MissionPool pool = AssetDatabase.LoadAssetAtPath<MissionPool>(MissionMapInstaller.GamePoolPath);

        if (pool == null)
        {
            Debug.LogError($"[Map412MissionInstaller] 실제 게임 미션 풀이 없습니다 ({MissionMapInstaller.GamePoolPath}). 미션 데이터가 지워졌는지 확인하세요.");
            return;
        }

        Transform root = MissionMapInstaller.FindOrCreateRoot(scene);
        MissionManager manager = MissionMapInstaller.EnsureMissionManager(root, pool, out bool managerCreated);
        MissionMapInstaller.WarnIfPoolDiffers(manager, pool, Spec.Label);
        bool gateCreated = MissionMapInstaller.EnsureActorGate(root);

        List<GameObject> placedNow = MissionMapInstaller.PlaceMissing(Spec, root, out int skipped);
        bool navMeshBaked = MissionMapInstaller.BakeNavMesh(Spec, root);
        int overlaps = MissionMapInstaller.CheckOverlaps(Spec, placedNow, root);
        // 미션 목록: 왼쪽 위 글자 칸 하나 (10 명세 ML12 — 글자 칸이 없는 목록만)
        int listsSetUp = MissionMapInstaller.SetupMissionList(scene);

        // 저장할 때 Fusion 이 씬 오브젝트의 정렬 키를 굽는다 (호스트 · 손님이 같은 오브젝트로 짝을 맞추는 번호)
        MissionMapInstaller.SaveScene(scene);

        Debug.Log($"[Map412MissionInstaller] 완료 — 새 장치 {placedNow.Count}개 놓음 · {skipped}개 이미 있어 건너뜀, " +
                  $"MissionManager {(managerCreated ? "새로 만듦" : "있음")}, PlayerHealthActorGate {(gateCreated ? "새로 만듦" : "있음")}, " +
                  $"NavMesh {(navMeshBaked ? "구움" : "실패")}, 겹침 경고 {overlaps}개, 미션 목록 {listsSetUp}개 설정. 멀티 테스트 전에는 SuHan/Build Full Game (Windows) 로 다시 빌드하세요.");
    }

    /// <summary>테스트 동안만 4~12 씬에 디버그 패널(호스트 F8 = 방화문 빼고 단체 미션 즉시 완료 등)을 넣는다 (MP9). 커밋 전에 끄기.</summary>
    [MenuItem("SuHan/4~12 테스트 표시 켜기")]
    public static void AddTestOverlay()
    {
        MissionMapInstaller.AddTestOverlay(Spec);
    }

    [MenuItem("SuHan/4~12 테스트 표시 끄기")]
    public static void RemoveTestOverlay()
    {
        MissionMapInstaller.RemoveTestOverlay(Spec);
    }
}
