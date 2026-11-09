using System.Collections.Generic;
using Fusion;
using TMPro;
using TrustNoOne.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 옛 미션 프리팹(Assets/Prefabs/Mission)을 새 구조(MissionStation)로 바꾼 "복사본"을 만든다.
///  원본 프리팹은 수정하지 않는다 (stage1 명세 S1: 옛 코드·프리팹은 교체 단계까지 그대로).
///
/// [방식] 모델·월드 UI·NetworkObject 는 그대로 두고 스크립트만 교체한다. 옛 값은 SerializedObject 로 읽어 옮긴다.
///  옛 타입은 "이름(문자열)"으로 찾는다 → 교체 단계에서 옛 코드를 지워도 이 파일은 그대로 컴파일된다.
/// [공통 틀 — Convert] 원본 로드 → 복사본 생성 → 미니게임별 Build → 검증 2가지 → 저장 → 복사본 파괴.
///  (11a) Build 뒤 아이템 손 설정 되돌리기(ItemHandSettings — 팀원이 맞춘 값 유지) · 방화문을 뺀 장치에 막는 몸체(MissionBodyBuilder).
///  검증: ① 옛 스크립트 · Missing Script 가 남지 않았는가 ② 모든 콜라이더를 조준하면 입력을 받는 대상이 잡히는가.
///  하나라도 실패하면 저장하지 않는다.
///  공통 후처리: 조준 문구(MissionPrompt.promptObject)가 있으면 PromptBillboard 를 붙여 항상 보는 사람 쪽을 향하게 한다
///  (옛 밸브 문구가 방향 고정이라 배치에 따라 뒤집혀 보였음 — 2b 테스트에서 발견).
/// [조준 외곽선] MissionOutlineBuilder 로 붙인다 (1단계 사용자 요청). 부품마다 MissionPrompt 를 두면 조준한 부품만 켜진다 (2a 명세 P7).
/// [변환 목록] 1단계 발전기 / 2a 밸브 · 안테나 · 차단기 / 2b 전선 / 2c 필터 · 청소기(아이템) / 2d 압력(신규 — 옛 프리팹이 없어 원본 패널 · 피스톤 모델에서 조립) / 3a 코드 자판(신규 — 원본 자판 모델에서 조립) / 3b 생명 유지 장치 · 산소통(신규 — 모델이 없어 기본 도형으로 조립) / 3c 장비 조립(신규 — 원본 본체 · 부품 모델에서 조립) / 3d 대형 방화문(신규 — 원본 문 · 패널 · 레버 모델에서 조립) / 13a 배관 누수 차단 · 테이프(신규 — 15 에서 받은 배관 모델 4종 · 테이프 조각 5종 · 롤 모델로) / 13b 좌표 설정(신규 — 15 에서 받은 패널 모델로). 미니게임을 이식할 때마다 Build… 와 Convert… 를 하나씩 추가한다.
/// </summary>
public static class LegacyMissionConverter
{
    private const string OutputFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Missions";
    private const string LegacyFolder = "Assets/Prefabs/Mission";

    public const string GeneratorStationPath = OutputFolder + "/Generator_Station.prefab";
    public const string ValveStationPath = OutputFolder + "/Valve_Station.prefab";
    public const string AntennaStationPath = OutputFolder + "/Antenna_Station.prefab";
    public const string BreakerStationPath = OutputFolder + "/Breaker_Station.prefab";
    public const string WiringStationPath = OutputFolder + "/Wiring_Station.prefab";
    public const string FilterStationPath = OutputFolder + "/Filter_Station.prefab";

    private const string ItemPrefabFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Prefabs/Items";
    private const string ItemDataFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Items";
    private const string LegacyVacuumDataPath = "Assets/ScriptableObjects/Data/Item/VacuumData.asset";

    public const string VacuumToolPath = ItemPrefabFolder + "/Vacuum_Tool.prefab";
    public const string VacuumToolFirstPersonPath = ItemPrefabFolder + "/Vacuum_Tool_FP.prefab";
    public const string VacuumToolDataPath = ItemDataFolder + "/VacuumToolData.asset";

    // 2d 압력: 옛 프리팹이 없어 원본 모델(패널 프리팹 · 피스톤 FBX)에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string PressureStationPath = OutputFolder + "/Pressure_Station.prefab";
    // 원본 모델 · 재질 폴더 (읽기 전용 — 복사해서 쓰고 원본은 수정하지 않는다)
    private const string SourceModelFolder = "Assets/0. Member/Taewoo/Model";
    private const string PressurePanelPath = SourceModelFolder + "/Pressure_device_panel.prefab";
    private const string SourceMaterialFolder = "Assets/0. Member/Taewoo/Material";

    // 3a 코드: 옛 프리팹이 없어 원본 자판 모델(Match_code.fbx)에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string CodeStationPath = OutputFolder + "/Code_Station.prefab";
    private const string CodeModelPath = SourceModelFolder + "/Match_code.fbx";
    private const float CodeInteractRange = 3f;

    // 3a C13 화면: 패널 메시에서 화면 면을 찾는 아틀라스 칸 (TryFindScreen 참고)과 글꼴
    private static readonly Vector2 CodeScreenAtlasUv = new Vector2(0.3865f, 0.5745f);
    private const float CodeScreenUvTolerance = 0.004f;
    private const string TmpDefaultFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    // 한글 LCD (장비 화면 "부품 n / 4" · "정상 작동" — 기본 글꼴에는 한글이 없다). 팀원 UI 와 같은 글꼴
    private const string KoreanFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/NotoSansKR/NotoSansKR-Medium SDF.asset";

    // 3b 생명 유지 장치 · 산소통: 원본 본체 · 산소통 모델에서 조립한다 (3b 보강 명세 LM1 — 원본은 복사해서 쓰고 수정하지 않음)
    public const string LifeSupportStationPath = OutputFolder + "/LifeSupport_Station.prefab";
    public const string OxygenTankPath = ItemPrefabFolder + "/OxygenTank_Item.prefab";
    private const string OxygenTankFirstPersonPath = ItemPrefabFolder + "/OxygenTank_FP.prefab";
    private const string OxygenTankDataPath = ItemDataFolder + "/OxygenTankData.asset";
    private const string LifeSupportModelPath = SourceModelFolder + "/Life_support_system.fbx";
    private const string OxygenTankModelPath = SourceModelFolder + "/Life_support_oxygen_tank.fbx";
    // 12a 폭발음 (사운드 담당 e2024b6): 본체 위치 3D — 피해 반경(3m, LS5) 안 최대 크기, 25m 에서 0 (직선)
    private const string LifeSupportExplosionSoundPath = "Assets/Sound/Mission Sound/Oxygen Tank Mission/Explosion.mp3";
    private const float LifeSupportExplosionFullDistance = 3f;
    private const float LifeSupportExplosionMaxDistance = 25f;
    // 본체 모델 안의 빈 노드 = 앞면 세로 홈의 바닥 = 산소통이 서는 자리 (보강 명세 1장 · LM3)
    private const string LifeSupportTankSeatNode = "Life_support_oxygen_tank_axes";
    private const string DraftMaterialFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Materials";
    private const string DraftMeshFolder = "Assets/0. Member/SuHan/MissionRedesign_Draft/Meshes";
    private const float LifeSupportInteractRange = 3f;

    // 산소통 아이템 번호: 기존 아이템 데이터(10 청소기 · 11 · 12 · 13)와 겹치지 않게
    private const int OxygenTankItemId = 20;

    // 화면 상자 (보강 명세 LM4): 모델에 화면으로 쓸 면이 없어 꼭대기 위에 받침 + 상자를 얹고 뒤로 15° 눕힌다 (m)
    private static readonly Vector3 LifeSupportScreenBoxSize = new Vector3(0.5f, 0.26f, 0.05f);
    private static readonly Vector2 LifeSupportScreenSize = new Vector2(0.46f, 0.22f);
    private static readonly Vector3 LifeSupportScreenStandSize = new Vector3(0.08f, 0.06f, 0.06f);
    private const float LifeSupportScreenTilt = 15f;

    // 산소통 압력계 (보강 명세 LM10 — 기획서 그림처럼 밸브 옆 둥근 압력계 1개, 7d L3B14 부터 색이 바뀌지 않는 장식):
    // 높이 = 모델 높이의 90% (FBX 분석상 밸브 머리의 목, 약 0.69m), 그 높이 ±3cm 정점으로 붙일 면을 찾는다 (m)
    private const float OxygenTankGaugeHeightRatio = 0.9f;
    private const float OxygenTankGaugeBand = 0.03f;
    private const float OxygenTankGaugeRimDiameter = 0.075f;
    private const float OxygenTankGaugeRimThickness = 0.015f;
    private const float OxygenTankGaugeFaceDiameter = 0.06f;

    // 1인칭 산소통 (LM11): 모델 사본 0.5배를 화면 오른쪽 아래에 세운다 (위치는 에디터 확인 후 조정)
    private const float OxygenTankFirstPersonScale = 0.5f;
    private static readonly Vector3 OxygenTankFirstPersonPosition = new Vector3(0.25f, -0.44f, 0.55f);

    // 덧붙인 부품 색: 압력계 눈금판(회색 — 정상 · 위험 모두 같은 색, 7d L3B14) · 화면 상자 · 받침 · 압력계 테두리(짙은 회색)
    private static readonly Color GaugeOffColor = new Color(0.35f, 0.35f, 0.35f);
    private static readonly Color LifeSupportFrameColor = new Color(0.16f, 0.17f, 0.19f);

    // 3c 장비 조립: 원본 본체 · 부품 모델에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string AssemblyStationPath = OutputFolder + "/Assembly_Station.prefab";
    private const string AssemblyMachineModelPath = SourceModelFolder + "/Equipment_repair_machine_main.fbx";
    private const float AssemblyFlyStartGap = 1f;       // 날아오기 시작점: 장비 앞면에서 (m) — 7c AR9 (바닥 원이 없어짐)
    private const float AssemblyInteractRange = 4f;     // 공통 범위: 장비 루트에서 (m) — 7c AR6 (자리에서 3m 인 팀원 검사와 맞춤)

    // 부품 아이템 번호: 21 파랑 · 22 회색 · 23 빨강 · 24 노랑 (기존 10 ~ 13 · 20 과 겹치지 않게, EA10)
    private const int EquipmentPartFirstItemId = 21;

    // 색 순서 = AssemblyRules 색 번호. 모델 · 자리 노드 이름 / 프리팹 이름 / 로그 · 아이템 이름
    private static readonly string[] EquipmentPartColors = { "blue", "gray", "red", "yellow" };
    private static readonly string[] EquipmentPartNames = { "Blue", "Gray", "Red", "Yellow" };
    private static readonly string[] EquipmentPartKoreanNames = { "파랑", "회색", "빨강", "노랑" };

    // 3d 방화문: 원본 문 · 패널 · 레버 모델에서 조립한다 (원본은 복사해서 쓰고 수정하지 않음)
    public const string FireDoorStationPath = OutputFolder + "/FireDoor_Station.prefab";
    private const string FireDoorGateModelPath = SourceModelFolder + "/Fire_door_gate.fbx";
    private const string FireDoorPanelModelPath = SourceModelFolder + "/Fire_door_lever.panel.fbx";
    private const string FireDoorLeverModelPath = SourceModelFolder + "/Fire_door_lever.fbx";
    private const int FireDoorLeverItemId = 25;           // 기존 10 ~ 13 · 20 ~ 24 와 겹치지 않게 (FD18)
    private const float FireDoorPanelOffsetX = 3.2f;      // 문 중심에서 패널까지 (명세 4장)
    private const float FireDoorPanelHeight = 0.7f;       // 원본 모델 테스트 씬의 패널 높이 (0.69m)
    private const float FireDoorLeverPivotHeight = 0.2f;  // 패널 바닥에서 꽂힌 레버 회전축까지
    private const float FireDoorLandingDistance = 1f;     // 튕겨 나간 레버가 떨어지는 거리 (FD5)
    private const float FireDoorInteractRange = 5.5f;     // 문 중심에서 양쪽 패널까지 + 여유
    private static readonly Vector3 FireDoorWallSize = new Vector3(0.8f, 2f, 0.2f);     // 받침 벽 (테스트용 자리 표시, FD16)
    private static readonly Vector2 FireDoorScreenSize = new Vector2(0.5f, 0.2f);
    private static readonly Color FireDoorWallColor = new Color(0.3f, 0.32f, 0.35f);

    // 조준 감지 레이어 (ProjectSettings 의 Interactable). 원본 모델은 기본 레이어(0)로 들어와 있어 버튼을 이 레이어로 옮겨야 조준된다
    // 13a: 배관 누수 차단 (TG012) · 테이프 — 명세 PL13 · PL20, 15 명세 NM2 · NM3 · NM12 ~ NM15 · NM18 · NM19 (받은 모델 — 모델 담당 커밋 8d073f8)
    public const string PipeLeakStationPath = OutputFolder + "/Pipe_Station.prefab";
    // 배관 모델 4종: 0번 구멍 없음 · 1 · 2 · 3번 = 구멍 1 · 2 · 3개 — 모양은 넷이 같다 (15 명세 1-2)
    private static readonly string[] PipeModelPaths =
    {
        SourceModelFolder + "/Pipe_leak_Hole_0.fbx", SourceModelFolder + "/Pipe_leak_Hole_1.fbx",
        SourceModelFolder + "/Pipe_leak_Hole_2.fbx", SourceModelFolder + "/Pipe_leak_Hole_3.fbx",
    };
    // 테이프 조각 5종 V1 ~ V5: 기준점 = 관 축, 관 둘레(반지름 0.204 ~ 0.266m)에 휘어 있다 (15 명세 1-3)
    private static readonly string[] PipeTapePaths =
    {
        SourceModelFolder + "/Pipe_leak_Tape_V1.fbx", SourceModelFolder + "/Pipe_leak_Tape_V2.fbx", SourceModelFolder + "/Pipe_leak_Tape_V3.fbx",
        SourceModelFolder + "/Pipe_leak_Tape_V4.fbx", SourceModelFolder + "/Pipe_leak_Tape_V5.fbx",
    };
    public const string TapeItemPath = ItemPrefabFolder + "/Tape_Item.prefab";
    private const string TapeFirstPersonPath = ItemPrefabFolder + "/Tape_Item_FP.prefab";
    private const string TapeDataPath = ItemDataFolder + "/TapeData.asset";
    private const string TapeModelPath = SourceModelFolder + "/Tape.fbx";
    private const int TapeItemId = 26;                    // 기존 10 ~ 13 · 20 ~ 25 와 겹치지 않게 (PL20)
    private const float TapeFirstPersonScale = 0.5f;      // 1인칭 롤 = 받은 모델 0.5배 (지름 12cm) — 1배(24cm)는 손 옆에서 화면을 많이 가린다 (NM19)
    private const float TapeColliderPadding = 0.01f;      // 테이프 조준 상자 = 모델 범위 + 1cm (NM18)
    private const float PipeInteractRange = 3f;           // 배관 기준점(바닥 · 관 가운데)에서 — 가장 높은 구멍(1.58m)도 닿게
    private const float PipeHoldSeconds = 2f;             // F 를 누르고 있는 시간 (PL13)
    private const float PipeModelHeight = 2.994f;         // 배관 모델 높이 (FBX 분석 −0.008 ~ 2.986m) — 가져온 크기가 다르면 자리를 이 비율로 맞춘다
    private const float PipeSurfaceRadius = 0.235f;       // 관 반지름 0.232m (FBX 분석 — 15 명세 1-2) + 3mm
    private const float PipeHoleSurfaceRadius = 0.232f;   // 구멍 검사의 기준 (관 표면)
    private const float PipeHoleInnerRadius = 0.226f;     // 구멍 검사 (NM12): 관 축에서 이보다 가까운 점 = 표면보다 안쪽으로 들어간 구멍
    private const float PipeHoleCheckDistance = 0.06f;    // 구멍 검사: 자리에서 이 거리 안의 점만
    private const float PipeTapeOuterRadius = 0.236f;     // 조각 넓이 가운데 (NM13): 관 축에서 이보다 먼 면 = 바깥 면 (안쪽 · 두께 면 빼기)
    // 구멍 6자리 (높이 m, 정면 +Z 에서 잰 각도° — Unity 기준): 배관 모델 1 · 2 · 3번에만 있는 점 중 표면보다 안쪽 점 덩어리의 가운데 (15 명세 1-2 · NM12)
    // 자리 1 = 1번 모델 · 자리 2 · 3 = 2번 · 자리 4 · 5 · 6 = 3번 (PipeLeakRules.ModelOf)
    private static readonly Vector2[] PipeLeakSpots =
    {
        new Vector2(1.037f, -34.9f),
        new Vector2(0.806f, -17.2f), new Vector2(1.352f, 35.6f),
        new Vector2(0.811f, 54.4f), new Vector2(1.170f, -16.9f), new Vector2(1.576f, 53.7f),
    };
    // 자리마다 붙일 테이프 조각 (PipeTapePaths 번호): 자리 1 = V2 · 2 = V1 · 3 = V5 · 4 = V3 · 5 = V2 · 6 = V4 — 한 배관 안에서는 모두 다른 모양 (NM3 · 사용자 결정 A)
    private static readonly int[] PipeSpotTapes = { 1, 0, 4, 2, 1, 3 };
    private static readonly Vector3 PipeTargetSize = new Vector3(0.14f, 0.14f, 0.06f);   // 조준 상자 — 구멍(금 간 자국 포함 6 ~ 11cm)보다 넉넉하게
    // 조준 고리 (NM14): 렌더러 없는 납작한 양면 고리 — 외곽선(양쪽 2cm씩 커짐)이 구멍 둘레 반지름 약 6 ~ 7.5cm 에 흰 고리로 그려진다
    private const float PipeRingInnerRadius = 0.045f;
    private const float PipeRingOuterRadius = 0.055f;
    private const float PipeRingOffset = 0.004f;          // 자리(관 표면 + 3mm)보다 4mm 앞 — 외곽선 재질은 뒷면만 그려서(Cull Front) 표면에 두면 관 속에 묻힌다
    private const int PipeRingSegments = 32;
    private const string PipeRingMeshPath = DraftMeshFolder + "/PipeLeakRing.asset";
    private const float PipeDoneLabelHeight = 1.85f;      // 「수리 완료」 높이 — 구멍(0.81 ~ 1.58m) 위 · 위 링(2.1m) 아래 (NM15)
    private static readonly Color PipeDoneColor = new Color(0.49f, 0.99f, 0.49f);    // 미션 목록 완료 색 #7CFC7C 와 같게
    private const string PipeGasTexturePath = DraftMaterialFolder + "/PipeGasPuff.png";
    private const string PipeGasMaterialPath = DraftMaterialFolder + "/PipeGas.mat";

    // 13b: 좌표 설정 (TG013) — 명세 CO30 ~ CO32, 15 명세 NM7 ~ NM11 (받은 패널 모델 — 모델 담당 커밋 8d073f8). 앞 = +Z
    public const string CoordinateStationPath = OutputFolder + "/Coordinate_Station.prefab";
    private const string CoordinateModelPath = SourceModelFolder + "/Coordinate_setting_panel.fbx";
    private const string CoordGreenButtonName = "Coordinate_setting_panel_button_green";    // 모델 자식 — 앞에 선 사람의 왼쪽 (+X)
    private const string CoordYellowButtonName = "Coordinate_setting_panel_button_yellow";  // 앞에 선 사람의 오른쪽 (−X)
    private const float CoordInteractRange = 3f;                                         // 장치 기준점(바닥 가운데)에서 — 두 사람이 나란히 서도 닿게
    // 모델의 검은 화면 (FBX 분석 — 앞을 보는 검은 면 56개, 15 명세 1-1): 가운데 · 법선(뒤로 18.3°) · 크기
    private static readonly Vector3 CoordScreenCenter = new Vector3(0f, 1.164f, -0.024f);
    private static readonly Vector3 CoordScreenNormal = new Vector3(0f, 0.314f, 0.95f);
    private static readonly Vector2 CoordModelScreenSize = new Vector2(1.304f, 0.507f);
    private const float CoordScreenGap = 0.001f;                                         // 화면 그림을 모델 화면보다 1mm 앞에
    private static readonly Vector2 CoordScreenSize = new Vector2(0.92f, 0.38f);         // 화면 그림의 기준 크기 (13b 임시 화면) — 모델 화면 세로에 맞춰 키운다 (NM10)
    private const float CoordButtonPadding = 0.01f;                                      // 버튼 조준 상자 = 버튼 메시 범위 + 1cm (NM8)
    private const float CoordPromptHeight = 0.25f;                                       // 조준 문구: 버튼 가운데 위
    private const float CoordAreaRadius = 0.16f;                                         // 탐색 영역 반지름 (화면 위 m) — 화면 세로 0.38 안에 들어가게
    private const float CoordCursorRadius = 0.04f;                                       // 조작 원 고리 반지름
    private const float CoordTargetSize = 0.07f;                                         // 목표 그림 한 변
    private const float CoordLayerStep = 0.002f;                                         // 화면 그림을 보는 쪽으로 겹치는 간격 (깜빡임 방지)
    private const float CoordAxisLabelX = 0.33f;                                         // 축 글자 · 막대: 화면 가운데에서 좌우
    private static readonly Vector2 CoordBarSize = new Vector2(0.2f, 0.022f);
    private const float CoordBarY = -0.14f;
    private const float CoordBeepMinDistance = 1f;                                       // 신호음 3D 거리 (CO32)
    private const float CoordBeepMaxDistance = 8f;
    private static readonly Color CoordGreenColor = new Color(0.3f, 0.95f, 0.3f);       // 그림의 초록 버튼
    private static readonly Color CoordYellowColor = new Color(1f, 0.8f, 0.15f);        // 그림의 노란 버튼
    private static readonly Color CoordAreaColor = new Color(0.45f, 0.85f, 1f, 0.55f);  // 그림의 하늘색 원 (흐리게)
    private static readonly Color CoordLineColor = new Color(0.45f, 0.85f, 1f, 0.3f);
    private static readonly Color CoordTargetColor = new Color(1f, 0.86f, 0.3f, 1f);
    private static readonly Color CoordIdleColor = new Color(0.55f, 0.65f, 0.7f);
    private const string CoordRingTexturePath = DraftMaterialFolder + "/CoordRing.png";
    private const string CoordDiscTexturePath = DraftMaterialFolder + "/CoordDisc.png";
    private const string CoordTargetTexturePath = DraftMaterialFolder + "/CoordTarget.png";

    private const int InteractableLayer = 6;

    private static readonly string[] PressureLetters = { "A", "B", "C" };

    // 모델 폴더의 테스트 씬에 놓인 배치를 패널 기준 상대 위치로 옮긴 값 (패널을 마주 보면 왼쪽부터 A · B · C)
    private static readonly Vector3[] PistonOffsets =
    {
        new Vector3(3.486f, 0f, -6.761f),
        new Vector3(-0.034f, 0f, -6.731f),
        new Vector3(-3.324f, 0f, -6.621f),
    };

    // 옛 개인 미니게임 6종 공통 거리 (2a 명세 3장). 발전기는 1단계 값 3.5 유지.
    // 7a: 6종은 단체 미션 → 완료 후 잠금(Lock — 끝난 모습 그대로, 7a 명세 TC9)
    private const float PersonalInteractRange = 3f;

    private static readonly string[] LegacyTypeNames =
    {
        "MissionMiniGameBase", "GeneratorMission", "MissionButton",
        "ValveMission", "AntennaMission", "AntennaLockButton", "BreakerMission", "BreakerLever",
        "WiringMission", "WireStartPoint", "WireEndPoint", "WireConnectionVisual", "WiringLever",
        "FilterCleaningMission", "VacuumDust", "VacuumItem", "FirstPersonVacuumView",
    };

    [MenuItem("SuHan/Convert Legacy Mission Prefabs")]
    public static void ConvertAllFromMenu()
    {
        Report("발전기", ConvertGenerator());
        Report("밸브", ConvertValve());
        Report("안테나", ConvertAntenna());
        Report("차단기", ConvertBreaker());
        Report("전선", ConvertWiring());
        Report("필터", ConvertFilter());
        Report("청소기", ConvertVacuumTool());
        Report("압력", CreatePressureStation());
        Report("코드", CreateCodeStation());
        Report("생명 유지 장치", CreateLifeSupportStation());
        Report("장비 조립", CreateAssemblyStation());
        Report("방화문", CreateFireDoorStation());
        Report("테이프", CreateTapeItem());
        Report("배관", CreatePipeLeakStation());
        Report("좌표 장치", CreateCoordinateStation());
        AssetDatabase.SaveAssets();

        // 새 NetworkObject 프리팹을 Fusion 네트워크 프리팹 목록에 즉시 반영 (안 하면 Runner.Spawn 이 실패한다)
        Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();
    }

    private static void Report(string what, NetworkObject result)
    {
        if (result == null)
            Debug.LogError($"[LegacyMissionConverter] {what} 변환 실패 (위 로그 참고)");
    }

    public static NetworkObject ConvertGenerator() =>
        Convert("발전기", LegacyFolder + "/Generator_Prefab.prefab", "Generator_Station", GeneratorStationPath, BuildGenerator);

    public static NetworkObject ConvertValve() =>
        Convert("밸브", LegacyFolder + "/Valve_Prefab.prefab", "Valve_Station", ValveStationPath, BuildValve);

    public static NetworkObject ConvertAntenna() =>
        Convert("안테나", LegacyFolder + "/Antenna_Prefab.prefab", "Antenna_Station", AntennaStationPath, BuildAntenna);

    public static NetworkObject ConvertBreaker() =>
        Convert("차단기", LegacyFolder + "/CircuitBreaker_Prefab.prefab", "Breaker_Station", BreakerStationPath, BuildBreaker);

    public static NetworkObject ConvertWiring() =>
        Convert("전선", LegacyFolder + "/Wiring/Wiring_Prefab.prefab", "Wiring_Station", WiringStationPath, BuildWiring);

    public static NetworkObject ConvertFilter() =>
        Convert("필터", LegacyFolder + "/FilterCleaning_Prefab.prefab", "Filter_Station", FilterStationPath, BuildFilter);

    /// <summary>
    /// 청소기: 1인칭 모델 → 아이템 데이터(복사본) → 아이템 프리팹 순서로 만든다 (앞의 결과를 뒤에서 참조).
    /// 아이템은 스테이션이 아니라 조준 검증을 하지 않는다.
    /// </summary>
    public static NetworkObject ConvertVacuumTool()
    {
        GameObject firstPerson = ConvertPrefab("청소기 1인칭", LegacyFolder + "/Vacuum_FP_Prefab.prefab", "Vacuum_Tool_FP",
            VacuumToolFirstPersonPath, BuildVacuumFirstPerson, false);

        if (firstPerson == null)
            return null;

        Object itemData = CreateVacuumToolData(firstPerson);

        if (itemData == null)
            return null;

        return Convert("청소기", LegacyFolder + "/Vacuum_Prefab.prefab", "Vacuum_Tool", VacuumToolPath,
            copy => BuildVacuumTool(copy, itemData), false);
    }

    /// <summary>압력: 원본 패널 프리팹(Pressure_device_panel)을 복사해 조립한다 (명세 PR9). 공통 틀의 저장 · 검증 · 빌보드를 그대로 쓴다.</summary>
    public static NetworkObject CreatePressureStation() =>
        Convert("압력", PressurePanelPath, "Pressure_Station", PressureStationPath, BuildPressure);

    /// <summary>코드: 원본 자판 모델(Match_code.fbx)을 복사해 조립한다 (3a 명세 4장). 공통 틀의 저장 · 검증 · 빌보드를 그대로 쓴다.</summary>
    public static NetworkObject CreateCodeStation() =>
        Convert("코드", CodeModelPath, "Code_Station", CodeStationPath, BuildCode);

    /// <summary>
    /// 생명 유지 장치 본체 (3b 명세 4장 · 보강 명세 3장): 산소통 아이템을 먼저 만들고, 원본 본체 모델을 복사해 조립한다.
    /// 화면 상자 · 꽂힌 산소통 · 콜라이더 · 섬광을 덧붙인다 (LM2 ~ LM6).
    /// </summary>
    public static NetworkObject CreateLifeSupportStation()
    {
        NetworkObject tankPrefab = CreateOxygenTank();

        if (tankPrefab == null)
        {
            Debug.LogError("[LegacyMissionConverter] 생명 유지 장치: 산소통을 만들지 못해 본체를 조립하지 않습니다.");
            return null;
        }

        return Convert("생명 유지 장치", LifeSupportModelPath, "LifeSupport_Station", LifeSupportStationPath, copy => BuildLifeSupport(copy, tankPrefab));
    }

    /// <summary>산소통 아이템 (3b 명세 4장): 1인칭 모델 → 아이템 데이터 → 월드 아이템 순서로 만든다 (청소기와 같은 순서).</summary>
    public static NetworkObject CreateOxygenTank()
    {
        GameObject firstPerson = AssemblePrefab("산소통 1인칭", "OxygenTank_FP", OxygenTankFirstPersonPath, BuildOxygenTankFirstPerson, false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateOxygenTankData(firstPerson);

        if (data == null)
            return null;

        return Convert("산소통", OxygenTankModelPath, "OxygenTank_Item", OxygenTankPath, copy => BuildOxygenTank(copy, data), false);
    }

    /// <summary>
    /// 장비 (3c 명세 4장): 부품 4종을 먼저 만들고, 원본 본체 모델을 복사해 자리 · 조립 위치 · 연출을 조립한다.
    /// 장비에는 조준 입력이 없어 조준 검증은 끈다 (EA14).
    /// </summary>
    public static NetworkObject CreateAssemblyStation()
    {
        NetworkObject[] partPrefabs = new NetworkObject[AssemblyRules.PartCount];

        for (int c = 0; c < partPrefabs.Length; c++)
        {
            partPrefabs[c] = CreateEquipmentPart(c);

            if (partPrefabs[c] == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 장비: {EquipmentPartKoreanNames[c]} 부품을 만들지 못해 장비를 조립하지 않습니다.");
                return null;
            }
        }

        return Convert("장비", AssemblyMachineModelPath, "Assembly_Station", AssemblyStationPath, copy => BuildAssembly(copy, partPrefabs));
    }

    /// <summary>장비 부품 아이템 한 색 (3c 명세 4장): 1인칭 모델 → 아이템 데이터 → 월드 아이템 (산소통과 같은 순서).</summary>
    public static NetworkObject CreateEquipmentPart(int color)
    {
        string label = EquipmentPartNames[color];
        string koreanName = EquipmentPartKoreanNames[color];

        GameObject firstPerson = AssemblePrefab($"부품 1인칭 ({koreanName})", $"EquipmentPart_{label}_FP",
            $"{ItemPrefabFolder}/EquipmentPart_{label}_FP.prefab", copy => BuildEquipmentPartFirstPerson(copy, color), false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateItemData($"{ItemDataFolder}/EquipmentPart_{label}Data.asset", EquipmentPartFirstItemId + color,
            $"장비 부품 ({koreanName})", firstPerson);

        if (data == null)
            return null;

        return Assemble($"부품 ({koreanName})", $"EquipmentPart_{label}", $"{ItemPrefabFolder}/EquipmentPart_{label}.prefab",
            copy => BuildEquipmentPart(copy, color, data), false);
    }

    /// <summary>
    /// 방화문 (3d 명세 4장): 레버 아이템을 먼저 만들고, 원본 문 모델을 복사해 양쪽 패널 · 꽂힌 레버 · 화면 · 충돌을 조립한다.
    /// </summary>
    public static NetworkObject CreateFireDoorStation()
    {
        NetworkObject leverPrefab = CreateFireDoorLever();

        if (leverPrefab == null)
        {
            Debug.LogError("[LegacyMissionConverter] 방화문: 레버 아이템을 만들지 못해 방화문을 조립하지 않습니다.");
            return null;
        }

        return Convert("방화문", FireDoorGateModelPath, "FireDoor_Station", FireDoorStationPath, copy => BuildFireDoor(copy, leverPrefab));
    }

    /// <summary>방화문 레버 아이템 (3d 명세 4장): 1인칭 모델 → 아이템 데이터(번호 25) → 월드 아이템.</summary>
    public static NetworkObject CreateFireDoorLever()
    {
        GameObject firstPerson = AssemblePrefab("방화문 레버 1인칭", "FireDoorLever_FP", $"{ItemPrefabFolder}/FireDoorLever_FP.prefab",
            BuildFireDoorLeverFirstPerson, false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateItemData($"{ItemDataFolder}/FireDoorLeverData.asset", FireDoorLeverItemId, "방화문 레버", firstPerson);

        if (data == null)
            return null;

        return Assemble("방화문 레버", "FireDoorLever_Item", $"{ItemPrefabFolder}/FireDoorLever_Item.prefab", copy => BuildFireDoorLever(copy, data), false);
    }

    /// <summary>테이프 아이템 (13a 명세 PL20): 1인칭 모델 → 아이템 데이터(번호 26) → 월드 아이템 (레버 · 산소통과 같은 순서). 모델이 없어 기본 도형 롤로 조립한다.</summary>
    public static NetworkObject CreateTapeItem()
    {
        GameObject firstPerson = AssemblePrefab("테이프 1인칭", "Tape_Item_FP", TapeFirstPersonPath, BuildTapeFirstPerson, false);

        if (firstPerson == null)
            return null;

        ItemData data = CreateItemData(TapeDataPath, TapeItemId, "테이프", firstPerson);

        if (data == null)
            return null;

        return Assemble("테이프", "Tape_Item", TapeItemPath, copy => BuildTapeItem(copy, data), false);
    }

    /// <summary>배관 (13a 명세 PL13 · PL18 · PL21, 15 명세 NM6 · NM12 ~ NM15): 빈 루트에 받은 배관 모델 4개를 복사해 구멍 6자리 · 테이프 조각 · 「수리 완료」를 덧붙인다. 공통 틀의 저장 · 검증 · 빌보드 · 몸체를 그대로 쓴다.</summary>
    public static NetworkObject CreatePipeLeakStation() =>
        Assemble("배관", "Pipe_Station", PipeLeakStationPath, BuildPipeLeak);

    /// <summary>좌표 설정 장치 (13b 명세 CO30 ~ CO32, 15 명세 NM7 ~ NM11): 받은 패널 모델을 복사해 버튼 · 화면 그림 · 신호음 자리를 붙인다. 공통 틀의 저장 · 검증 · 빌보드 · 몸체를 그대로 쓴다.</summary>
    public static NetworkObject CreateCoordinateStation() =>
        Convert("좌표 장치", CoordinateModelPath, "Coordinate_Station", CoordinateStationPath, BuildCoordinate);

    // ═════════════════════════════════════════════════════════════
    //  공통 틀
    // ═════════════════════════════════════════════════════════════

    /// <param name="build">복사본을 새 구조로 바꾼다. 실패하면 오류를 남기고 false.</param>
    /// <param name="verifyTargets">조준 검증을 할지. 스테이션이 아닌 것(아이템 · 1인칭 모델)은 false.</param>
    private static NetworkObject Convert(string what, string legacyPath, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets = true)
    {
        GameObject saved = ConvertPrefab(what, legacyPath, outputName, outputPath, build, verifyTargets);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    /// <summary>Convert 의 본체. NetworkObject 가 없는 프리팹(1인칭 모델)도 변환할 수 있게 저장된 GameObject 를 돌려준다.</summary>
    private static GameObject ConvertPrefab(string what, string legacyPath, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(outputPath).Replace('\\', '/'));

        GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);

        if (legacy == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 옛 {what} 프리팹이 없습니다: {legacyPath}");
            return null;
        }

        GameObject copy = Object.Instantiate(legacy);
        copy.name = outputName;
        return FinishPrefab(what, copy, outputPath, build, verifyTargets);
    }

    /// <summary>원본 없이 빈 루트에서 조립한다 (3b — 모델이 없어 기본 도형으로). 검증 · 저장은 Convert 와 같다.</summary>
    private static NetworkObject Assemble(string what, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets = true)
    {
        GameObject saved = AssemblePrefab(what, outputName, outputPath, build, verifyTargets);
        return saved != null ? saved.GetComponent<NetworkObject>() : null;
    }

    private static GameObject AssemblePrefab(string what, string outputName, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(outputPath).Replace('\\', '/'));
        return FinishPrefab(what, new GameObject(outputName), outputPath, build, verifyTargets);
    }

    /// <summary>
    /// 공통 뒷부분: 조립 → 아이템 손 설정 되돌리기 → 안내 빌보드 → (장치) 막는 몸체 → 검증 2가지 → 저장 → 복사본 파괴. 하나라도 실패하면 저장하지 않는다.
    /// [11a] 손 설정: 저장 경로의 기존 프리팹에서 읽어 두었다가 되돌린다 — 팀원이 맞춘 값이 메뉴를 돌려도 남는다 (BC13 · BC14).
    ///  막는 몸체: 루트에 장치(MissionStation)가 있고 방화문이 아니면 붙인다 — 방화문은 열리면 지나가야 해서 BuildFireDoor 가 부품별로 막는다 (BC5 · BC11 · BC18).
    /// </summary>
    private static GameObject FinishPrefab(string what, GameObject copy, string outputPath,
        System.Func<GameObject, bool> build, bool verifyTargets)
    {
        // 덮어쓰기 전에 읽는다 (기존 프리팹이 없거나 아이템 표시가 없으면 null — 도구 기본값)
        ItemHandSettings hand = ItemHandSettings.Capture(outputPath);

        try
        {
            if (!build(copy))
                return null;

            if (hand != null)
            {
                hand.Apply(copy);
                Debug.Log($"[LegacyMissionConverter] {what}: 손 설정 유지 — {hand.Describe()}");
            }

            AddPromptBillboards(copy);

            if (copy.GetComponent<MissionStation>() != null && copy.GetComponent<FireDoorStation>() == null)
            {
                // 압력 장치는 둥근 콘솔 — 모델 범위 상자 하나면 오목한 가운데(노란 버튼)를 덮고 윗면에 올라선다 → 조각 상자 (11a BC19, 테스트 후 추가)
                // 배관도 조각 상자 — 바짝 다가가 위 · 아래 새는 곳을 조준하게 (13a PL21)
                int boxes = copy.GetComponent<PressureStation>() != null || copy.GetComponent<PipeLeakStation>() != null
                    ? MissionBodyBuilder.AttachSliced(copy, BodyBoxRules.TightPadding)
                    : MissionBodyBuilder.Attach(copy);
                Debug.Log($"[LegacyMissionConverter] {what}: 몸체 상자 {boxes}개 — {MissionBodyBuilder.Describe(copy)}");
            }

            if (!VerifyNoLegacyScripts(copy) || (verifyTargets && !VerifyTargetResolution(copy)))
                return null;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, outputPath);
            Debug.Log($"[LegacyMissionConverter] {what} 변환 완료: {outputPath}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    /// <summary>스테이션 공통 값 설정. 반환한 SerializedObject 에 미니게임별 값을 더 넣고 Apply 한다.</summary>
    private static SerializedObject ConfigureStation(MissionStation station, MissionEventType completionEvent, CompletionPolicy policy, float range)
    {
        SerializedObject so = new SerializedObject(station);
        // enum 은 intValue 로 넣는다: MissionEventType 값이 연속이 아니라 enumValueIndex 를 쓰면 엉뚱한 값이 들어간다
        so.FindProperty("completionEvent").intValue = (int)completionEvent;
        so.FindProperty("completionPolicy").intValue = (int)policy;
        so.FindProperty("interactRange").floatValue = range;
        return so;
    }

    // ═════════════════════════════════════════════════════════════
    //  1단계: 발전기
    // ═════════════════════════════════════════════════════════════

    private static bool BuildGenerator(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "GeneratorMission");
        MonoBehaviour oldButton = FindLegacy(copy, "MissionButton");

        if (oldMission == null || oldButton == null)
        {
            Debug.LogError("[LegacyMissionConverter] 발전기 프리팹에서 GeneratorMission 또는 MissionButton 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldMissionSO = new SerializedObject(oldMission);
        SerializedObject oldButtonSO = new SerializedObject(oldButton);
        GameObject buttonObject = oldButton.gameObject;

        // 루트: GeneratorMission → GeneratorStation
        GeneratorStation station = copy.AddComponent<GeneratorStation>();
        SerializedObject stationSO = ConfigureStation(station, MissionEventType.GeneratorRepaired, CompletionPolicy.Lock, 3.5f);
        CopyReference(oldMissionSO, "guideText", stationSO, "guideText");
        CopyReference(oldMissionSO, "gaugeFill", stationSO, "gaugeFill");
        CopyReference(oldMissionSO, "completeText", stationSO, "completeText");
        CopyFloat(oldMissionSO, "fillDuration", stationSO, "fillDuration");
        CopyString(oldMissionSO, "waitingText", stationSO, "waitingText");
        CopyString(oldMissionSO, "runningText", stationSO, "runningText");
        CopyColliders(oldMissionSO, stationSO, buttonObject);
        stationSO.ApplyModifiedPropertiesWithoutUndo();

        // 버튼: MissionButton → StationButton (partIndex 0)
        // 부품은 "자기 콜라이더가 있는 오브젝트"에 붙인다. 옛 MissionButton 은 루트에 있었지만, 새 GeneratorStation(루트)도
        // 조준 대상(ITargetable)이라 루트에 두면 감지기가 스테이션을 먼저 잡아서 클릭이 버튼에 전달되지 않는다.
        GameObject partObject = FindPartObject(stationSO, copy) ?? buttonObject;
        StationButton button = AddStationButton(partObject, station, 0);
        SerializedObject buttonSO = new SerializedObject(button);
        CopyReference(oldButtonSO, "buttonVisual", buttonSO, "buttonVisual");
        CopyVector3(oldButtonSO, "pressOffset", buttonSO, "pressOffset");
        CopyFloat(oldButtonSO, "pressTime", buttonSO, "pressTime");
        CopyFloat(oldButtonSO, "returnTime", buttonSO, "returnTime");
        buttonSO.ApplyModifiedPropertiesWithoutUndo();

        // 거리 기준점 = 버튼 (7a 테스트 후 추가 — 명세 TC17). 루트가 기준이면 씬에서 모델 · 버튼만 옮긴 발전기는
        // 버튼 바로 앞에서도 루트와 멀어 "범위 밖"으로 거부된다 (메인 씬 Generator_C: 자식만 약 6m 옮겨져 4.8 ~ 5.1m 로 거부)
        stationSO.Update();
        stationSO.FindProperty("rangeCenter").objectReferenceValue = partObject.transform;
        stationSO.ApplyModifiedPropertiesWithoutUndo();

        // 조준 시 외곽선: 버튼을 조준해서 누르는 미니게임이라 버튼 모델에 붙인다 (눌림 애니메이션을 따라 움직인다)
        Transform buttonVisual = buttonSO.FindProperty("buttonVisual").objectReferenceValue as Transform;
        MissionOutlineBuilder.Attach(copy, buttonVisual != null ? buttonVisual.gameObject : buttonObject);

        Object.DestroyImmediate(oldButton);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 밸브 (F 홀드로 회전)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildValve(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "ValveMission");

        if (oldMission == null)
        {
            Debug.LogError("[LegacyMissionConverter] 밸브 프리팹에서 ValveMission 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        // 루트: ValveMission → ValveStation. 부품 없음 — 모델 콜라이더에서 위로 올라가면 루트의 ValveStation(F 홀드)이 잡힌다.
        ValveStation station = copy.AddComponent<ValveStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.ValveClosed, CompletionPolicy.Lock, PersonalInteractRange);
        CopyReference(oldSO, "rotatingPart", so, "rotatingPart");
        CopyVector3(oldSO, "rotationAxis", so, "rotationAxis");
        CopyFloat(oldSO, "rotationSpeed", so, "rotationSpeed");
        CopyFloat(oldSO, "minTargetAngle", so, "minTargetAngle");
        CopyFloat(oldSO, "maxTargetAngle", so, "maxTargetAngle");
        CopyColliders(oldSO, so, copy);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 외곽선: 밸브 프리팹은 이미 MissionPrompt + 외곽선(ValveOutLine)을 갖고 있다 → 비어 있을 때만 만든다
        if (!HasHighlight(copy))
        {
            Transform part = so.FindProperty("rotatingPart").objectReferenceValue as Transform;
            MissionOutlineBuilder.Attach(copy, part != null ? part.gameObject : copy);
        }

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 안테나 (F 홀드로 회전 → 고정 버튼)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildAntenna(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "AntennaMission");
        MonoBehaviour oldLock = FindLegacy(copy, "AntennaLockButton");

        if (oldMission == null || oldLock == null)
        {
            Debug.LogError("[LegacyMissionConverter] 안테나 프리팹에서 AntennaMission 또는 AntennaLockButton 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);
        GameObject lockObject = oldLock.gameObject;

        if (lockObject == copy)
        {
            Debug.LogError("[LegacyMissionConverter] 안테나 고정 버튼이 루트에 있어 스테이션에 가려집니다.");
            return false;
        }

        AntennaStation station = copy.AddComponent<AntennaStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.AntennaAligned, CompletionPolicy.Lock, PersonalInteractRange);
        CopyReference(oldSO, "antennaTransform", so, "rotatingPart");
        CopyVector3(oldSO, "rotationAxis", so, "rotationAxis");
        CopyFloat(oldSO, "rotationSpeed", so, "rotationSpeed");
        // 옛 AntennaMission 은 목표 각도를 상수(MinTargetAngle 360 / MaxTargetAngle 720)로 가졌다
        so.FindProperty("minTargetAngle").floatValue = 360f;
        so.FindProperty("maxTargetAngle").floatValue = 720f;
        CopyReference(oldSO, "gaugeFill", so, "gaugeFill");
        CopyReference(oldSO, "percentText", so, "percentText");
        CopyColliders(oldSO, so, copy);

        // 고정 버튼 콜라이더도 "내 미션 아니면 끄기" 대상에 넣는다 (옛 목록에 빠져 있어도)
        foreach (Collider c in lockObject.GetComponents<Collider>())
            AddCollider(so, c);

        so.ApplyModifiedPropertiesWithoutUndo();

        // 고정 버튼: AntennaLockButton → StationButton(0). 버튼 모양은 캔버스 UI 라 눌림 애니메이션(buttonVisual)은 없다.
        AddStationButton(lockObject, station, AntennaStation.LockButtonPart);

        // 외곽선: 안테나 몸체. ButtonHitbox 근처에는 메시가 없어서(UI 버튼) 자기 MissionPrompt 를 두지 않는다
        //  → 버튼을 조준해도 GetComponentInParent 로 루트 프롬프트가 잡혀 몸체 외곽선이 켜진다 (2a 명세 6장 해소).
        Transform part = so.FindProperty("rotatingPart").objectReferenceValue as Transform;
        MissionOutlineBuilder.Attach(copy, part != null ? part.gameObject : copy);

        Object.DestroyImmediate(oldLock);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2a: 차단기 (레버 6개 클릭)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildBreaker(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "BreakerMission");
        List<MonoBehaviour> oldLevers = FindAllLegacy(copy, "BreakerLever");

        if (oldMission == null || oldLevers.Count == 0)
        {
            Debug.LogError("[LegacyMissionConverter] 차단기 프리팹에서 BreakerMission 또는 BreakerLever 를 찾지 못했습니다.");
            return false;
        }

        if (oldLevers.Count > BreakerStation.MaxLevers)
        {
            Debug.LogError($"[LegacyMissionConverter] 차단기 레버가 {oldLevers.Count}개입니다 (최대 {BreakerStation.MaxLevers}).");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        BreakerStation station = copy.AddComponent<BreakerStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.BreakerRestored, CompletionPolicy.Lock, PersonalInteractRange);
        CopyColliders(oldSO, so, copy);

        BreakerLeverVisual[] visuals = new BreakerLeverVisual[oldLevers.Count];

        foreach (MonoBehaviour oldLever in oldLevers)
        {
            SerializedObject leverSO = new SerializedObject(oldLever);
            int index = leverSO.FindProperty("leverIndex").intValue;

            // 레버 번호는 0 ~ (개수-1) 이고 겹치면 안 된다 (배열 인덱스 = partIndex)
            if (index < 0 || index >= visuals.Length || visuals[index] != null)
            {
                Debug.LogError($"[LegacyMissionConverter] 차단기 레버 '{oldLever.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            GameObject leverObject = oldLever.gameObject;

            // 모습: BreakerLever 의 연출 값을 BreakerLeverVisual 로
            BreakerLeverVisual visual = leverObject.AddComponent<BreakerLeverVisual>();
            SerializedObject visualSO = new SerializedObject(visual);
            CopyReference(leverSO, "leverTransform", visualSO, "leverTransform");
            CopyReference(leverSO, "leverRenderer", visualSO, "leverRenderer");
            CopyVector3(leverSO, "offRotation", visualSO, "offRotation");
            CopyVector3(leverSO, "onRotation", visualSO, "onRotation");
            CopyFloat(leverSO, "rotateDuration", visualSO, "rotateDuration");
            visualSO.ApplyModifiedPropertiesWithoutUndo();
            visuals[index] = visual;

            // 클릭: StationButton (레버는 회전 연출이 반응을 보여 주므로 눌림 애니메이션 없음)
            AddStationButton(leverObject, station, index);

            foreach (Collider c in leverObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선: 레버마다 MissionPrompt → 조준한 레버만 켜진다 (2a 명세 P7)
            Renderer leverRenderer = visualSO.FindProperty("leverRenderer").objectReferenceValue as Renderer;
            MissionOutlineBuilder.Attach(leverObject, leverRenderer != null ? leverRenderer.gameObject : leverObject);
        }

        SerializedProperty leverList = so.FindProperty("levers");
        leverList.ClearArray();

        for (int i = 0; i < visuals.Length; i++)
        {
            leverList.InsertArrayElementAtIndex(i);
            leverList.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (MonoBehaviour oldLever in oldLevers)
            Object.DestroyImmediate(oldLever);

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2b: 전선 (시작점 드래그 → 같은 색 도착점 → 레버)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildWiring(GameObject copy)
    {
        const int wireCount = WiringRules.WireCount;

        MonoBehaviour oldMission = FindLegacy(copy, "WiringMission");
        MonoBehaviour oldLever = FindLegacy(copy, "WiringLever");
        List<MonoBehaviour> oldStarts = FindAllLegacy(copy, "WireStartPoint");
        List<MonoBehaviour> oldEnds = FindAllLegacy(copy, "WireEndPoint");

        if (oldMission == null || oldLever == null || oldStarts.Count != wireCount || oldEnds.Count != wireCount)
        {
            Debug.LogError($"[LegacyMissionConverter] 전선 프리팹 구성이 다릅니다 (WiringMission · WiringLever 1개씩, 시작점 · 도착점 {wireCount}개씩 필요).");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);

        WiringStation station = copy.AddComponent<WiringStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.WiresConnected, CompletionPolicy.Lock, PersonalInteractRange);
        CopyColliders(oldSO, so, copy);

        WireVisual[] wires = new WireVisual[wireCount];
        Transform[] endAnchors = new Transform[wireCount];
        int[] endColors = new int[wireCount];
        bool[] endSeen = new bool[wireCount];
        List<MonoBehaviour> oldLines = new List<MonoBehaviour>();

        // 시작점: WireStartPoint → StationDragPart, 그 시작점의 WireConnectionVisual → WireVisual
        foreach (MonoBehaviour oldStart in oldStarts)
        {
            SerializedObject startSO = new SerializedObject(oldStart);
            int index = startSO.FindProperty("index").intValue;

            if (index < 0 || index >= wireCount || wires[index] != null)
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 시작점 '{oldStart.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            MonoBehaviour oldLine = startSO.FindProperty("connectionVisual").objectReferenceValue as MonoBehaviour;

            if (oldLine == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 시작점 '{oldStart.name}' 에 connectionVisual 이 없습니다.");
                return false;
            }

            SerializedObject lineSO = new SerializedObject(oldLine);
            WireVisual wire = oldLine.gameObject.AddComponent<WireVisual>();
            SerializedObject wireSO = new SerializedObject(wire);

            // 꽂이 색 (옛 WireStartPoint). anchor 가 비었으면 옛 코드처럼 시작점 자신
            if (startSO.FindProperty("anchor").objectReferenceValue != null)
                CopyReference(startSO, "anchor", wireSO, "startAnchor");
            else
                wireSO.FindProperty("startAnchor").objectReferenceValue = oldStart.transform;

            CopyReference(startSO, "colorRenderer", wireSO, "plugRenderer");
            CopyInt(startSO, "materialIndex", wireSO, "plugMaterialIndex");
            CopyString(startSO, "colorProperty", wireSO, "plugColorProperty");

            // 전선 메시 (옛 WireConnectionVisual). lengthAxis 는 옛 WireMeshAxis 와 같은 값
            CopyReference(lineSO, "wireMesh", wireSO, "wireMesh");
            CopyReference(lineSO, "wireMeshFilter", wireSO, "wireMeshFilter");
            CopyReference(lineSO, "wireRenderer", wireSO, "wireRenderer");
            CopyInt(lineSO, "lengthAxis", wireSO, "lengthAxis");
            CopyReference(lineSO, "wirePlane", wireSO, "wirePlane");
            CopyString(lineSO, "colorProperty", wireSO, "wireColorProperty");
            CopyFloat(lineSO, "extraLength", wireSO, "extraLength");
            CopyFloat(lineSO, "followSpeed", wireSO, "followSpeed");
            wireSO.ApplyModifiedPropertiesWithoutUndo();

            wires[index] = wire;
            oldLines.Add(oldLine);

            GameObject startObject = oldStart.gameObject;
            StationDragPart dragPart = startObject.AddComponent<StationDragPart>();
            SerializedObject dragSO = new SerializedObject(dragPart);
            dragSO.FindProperty("station").objectReferenceValue = station;
            dragSO.FindProperty("partIndex").intValue = index;
            dragSO.FindProperty("preview").objectReferenceValue = wire;
            dragSO.ApplyModifiedPropertiesWithoutUndo();

            foreach (Collider c in startObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선: 꽂이 오브젝트에는 자기 메시가 없고, 꽂이 모양은 패널 메시의 서브메시(= 색을 칠하는 재질 번호)다.
            //  그 서브메시만 떼어 외곽선을 만든다 → 조준한 줄만 빛난다 (2b 테스트 피드백, 명세 W8)
            Renderer plugRenderer = startSO.FindProperty("colorRenderer").objectReferenceValue as Renderer;
            MeshFilter plugMesh = plugRenderer != null ? plugRenderer.GetComponent<MeshFilter>() : null;
            MissionOutlineBuilder.AttachSubmesh(startObject, plugMesh, startSO.FindProperty("materialIndex").intValue, $"WirePlug_{index}");
        }

        // 도착점: WireEndPoint → StationDropTarget, 색 · 위치는 스테이션 배열로
        foreach (MonoBehaviour oldEnd in oldEnds)
        {
            SerializedObject endSO = new SerializedObject(oldEnd);
            int index = endSO.FindProperty("index").intValue;

            if (index < 0 || index >= wireCount || endSeen[index])
            {
                Debug.LogError($"[LegacyMissionConverter] 전선 도착점 '{oldEnd.name}' 의 번호 {index} 가 범위를 벗어나거나 중복입니다.");
                return false;
            }

            endSeen[index] = true;
            endColors[index] = endSO.FindProperty("wireColor").intValue;

            Transform anchor = endSO.FindProperty("anchor").objectReferenceValue as Transform;
            endAnchors[index] = anchor != null ? anchor : oldEnd.transform;

            GameObject endObject = oldEnd.gameObject;
            StationDropTarget dropTarget = endObject.AddComponent<StationDropTarget>();
            SerializedObject dropSO = new SerializedObject(dropTarget);
            dropSO.FindProperty("station").objectReferenceValue = station;
            dropSO.FindProperty("partIndex").intValue = index;
            dropSO.ApplyModifiedPropertiesWithoutUndo();

            foreach (Collider c in endObject.GetComponents<Collider>())
                AddCollider(so, c);

            // 외곽선 없음: 도착점은 따로 뗄 모양이 없다. 빈 프롬프트를 둬서 패널 전체가 빛나지 않게 한다 (W8)
            MissionOutlineBuilder.AttachEmpty(endObject);
        }

        // 레버: WiringLever → StationButton(LeverPart). 당겨졌다 돌아오는 연출은 pressRotation (W7)
        SerializedObject oldLeverSO = new SerializedObject(oldLever);
        GameObject leverObject = oldLever.gameObject;
        Transform leverTransform = oldLeverSO.FindProperty("leverTransform").objectReferenceValue as Transform;

        if (leverTransform == null)
            leverTransform = leverObject.transform;

        StationButton lever = AddStationButton(leverObject, station, WiringStation.LeverPart);
        SerializedObject leverSO = new SerializedObject(lever);
        leverSO.FindProperty("buttonVisual").objectReferenceValue = leverTransform;

        Vector3 current = leverTransform.localEulerAngles;
        Vector3 completed = oldLeverSO.FindProperty("completedRotation").vector3Value;
        leverSO.FindProperty("pressRotation").vector3Value = new Vector3(
            Mathf.DeltaAngle(current.x, completed.x),
            Mathf.DeltaAngle(current.y, completed.y),
            Mathf.DeltaAngle(current.z, completed.z));

        CopyFloat(oldLeverSO, "rotateDuration", leverSO, "pressTime");
        CopyFloat(oldLeverSO, "rotateDuration", leverSO, "returnTime");
        leverSO.ApplyModifiedPropertiesWithoutUndo();

        foreach (Collider c in leverObject.GetComponents<Collider>())
            AddCollider(so, c);

        AttachPartOutline(leverObject);

        // 스테이션 배열 채우기 (인덱스 = 부품 번호)
        SerializedProperty wireList = so.FindProperty("wires");
        SerializedProperty anchorList = so.FindProperty("endAnchors");
        SerializedProperty colorList = so.FindProperty("endColors");
        wireList.ClearArray();
        anchorList.ClearArray();
        colorList.ClearArray();

        for (int i = 0; i < wireCount; i++)
        {
            wireList.InsertArrayElementAtIndex(i);
            wireList.GetArrayElementAtIndex(i).objectReferenceValue = wires[i];
            anchorList.InsertArrayElementAtIndex(i);
            anchorList.GetArrayElementAtIndex(i).objectReferenceValue = endAnchors[i];
            colorList.InsertArrayElementAtIndex(i);
            colorList.GetArrayElementAtIndex(i).intValue = endColors[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        // 패널 전체 외곽선은 붙이지 않는다: 어느 부품을 조준했는지 알 수 없게 된다 (W8). 모든 부품이 자기 프롬프트를 가진다.

        foreach (MonoBehaviour old in oldStarts)
            Object.DestroyImmediate(old);

        foreach (MonoBehaviour old in oldEnds)
            Object.DestroyImmediate(old);

        foreach (MonoBehaviour old in oldLines)
            Object.DestroyImmediate(old);

        Object.DestroyImmediate(oldLever);
        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2c: 필터 (먼지 = 중첩 NetworkObject 주소 + 스테이션이 상태 관리)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildFilter(GameObject copy)
    {
        MonoBehaviour oldMission = FindLegacy(copy, "FilterCleaningMission");

        if (oldMission == null)
        {
            Debug.LogError("[LegacyMissionConverter] 필터 프리팹에서 FilterCleaningMission 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldMission);
        SerializedProperty oldDusts = oldSO.FindProperty("dusts");
        int count = oldDusts != null ? oldDusts.arraySize : 0;

        if (count == 0 || count > FilterStation.MaxDusts)
        {
            Debug.LogError($"[LegacyMissionConverter] 필터 먼지가 {count}개입니다 (1~{FilterStation.MaxDusts}개 필요).");
            return false;
        }

        FilterStation station = copy.AddComponent<FilterStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.FilterCleaned, CompletionPolicy.Lock, PersonalInteractRange);

        // F8: 먼지 콜라이더는 FilterStation 이 직접 관리한다 → 베이스 목록은 비운다
        so.FindProperty("interactionColliders").ClearArray();

        FilterDust[] newDusts = new FilterDust[count];
        List<MonoBehaviour> oldDustComponents = new List<MonoBehaviour>();

        // 옛 dusts 배열 순서 = 먼지 번호
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour oldDust = oldDusts.GetArrayElementAtIndex(i).objectReferenceValue as MonoBehaviour;

            if (oldDust == null || oldDustComponents.Contains(oldDust))
            {
                Debug.LogError($"[LegacyMissionConverter] 필터 먼지 {i}번이 비어 있거나 중복입니다.");
                return false;
            }

            if (oldDust.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 필터 먼지 '{oldDust.name}' 에 NetworkObject 가 없습니다 (청소기 우클릭이 먼지를 구분할 수 없음).");
                return false;
            }

            SerializedObject oldDustSO = new SerializedObject(oldDust);
            FilterDust dust = oldDust.gameObject.AddComponent<FilterDust>();
            SerializedObject dustSO = new SerializedObject(dust);
            dustSO.FindProperty("station").objectReferenceValue = station;
            dustSO.FindProperty("index").intValue = i;
            CopyReference(oldDustSO, "dustCollider", dustSO, "dustCollider");
            CopyReference(oldDustSO, "outlineRoot", dustSO, "outlineObject");
            dustSO.ApplyModifiedPropertiesWithoutUndo();

            newDusts[i] = dust;
            oldDustComponents.Add(oldDust);
        }

        SerializedProperty dustList = so.FindProperty("dusts");
        dustList.ClearArray();

        for (int i = 0; i < count; i++)
        {
            dustList.InsertArrayElementAtIndex(i);
            dustList.GetArrayElementAtIndex(i).objectReferenceValue = newDusts[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (MonoBehaviour old in oldDustComponents)
            Object.DestroyImmediate(old);

        Object.DestroyImmediate(oldMission);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2c: 청소기 아이템 (1인칭 모델 · 아이템 데이터 · 아이템)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildVacuumFirstPerson(GameObject copy)
    {
        MonoBehaviour oldView = FindLegacy(copy, "FirstPersonVacuumView");

        if (oldView == null)
        {
            Debug.LogError("[LegacyMissionConverter] 청소기 1인칭 프리팹에서 FirstPersonVacuumView 를 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldView);
        VacuumFirstPersonView view = oldView.gameObject.AddComponent<VacuumFirstPersonView>();
        SerializedObject so = new SerializedObject(view);
        CopyReference(oldSO, "mouth", so, "mouth");
        so.ApplyModifiedPropertiesWithoutUndo();

        Object.DestroyImmediate(oldView);
        return true;
    }

    /// <summary>
    /// 팀원의 VacuumData 를 복사해 1인칭 프리팹만 새 것으로 바꾼다 (결정 F9 — 원본을 바꾸면 교체 전까지 옛 청소기가 깨진다).
    /// 이미 있으면 원본 값을 다시 덮어써 아이콘 · 이름 변경을 따라가고, 에셋 GUID 는 유지한다.
    /// </summary>
    private static Object CreateVacuumToolData(GameObject firstPersonPrefab)
    {
        EnsureFolder(ItemDataFolder);

        Object original = AssetDatabase.LoadAssetAtPath<Object>(LegacyVacuumDataPath);

        if (original == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 청소기 아이템 데이터가 없습니다: {LegacyVacuumDataPath}");
            return null;
        }

        Object data = AssetDatabase.LoadAssetAtPath<Object>(VacuumToolDataPath);

        if (data == null)
        {
            if (!AssetDatabase.CopyAsset(LegacyVacuumDataPath, VacuumToolDataPath))
            {
                Debug.LogError($"[LegacyMissionConverter] 아이템 데이터 복사 실패: {VacuumToolDataPath}");
                return null;
            }

            data = AssetDatabase.LoadAssetAtPath<Object>(VacuumToolDataPath);
        }
        else
        {
            EditorUtility.CopySerialized(original, data);
            data.name = System.IO.Path.GetFileNameWithoutExtension(VacuumToolDataPath);
        }

        SerializedObject so = new SerializedObject(data);
        SerializedProperty firstPerson = so.FindProperty("firstPersonPrefab");

        if (firstPerson == null)
        {
            Debug.LogError("[LegacyMissionConverter] 아이템 데이터에 firstPersonPrefab 이 없습니다.");
            return null;
        }

        firstPerson.objectReferenceValue = firstPersonPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        Debug.Log($"[LegacyMissionConverter] 청소기 아이템 데이터 준비 완료: {VacuumToolDataPath}");
        return data;
    }

    private static bool BuildVacuumTool(GameObject copy, Object itemData)
    {
        MonoBehaviour oldItem = FindLegacy(copy, "VacuumItem");

        if (oldItem == null)
        {
            Debug.LogError("[LegacyMissionConverter] 청소기 프리팹에서 VacuumItem 을 찾지 못했습니다.");
            return false;
        }

        SerializedObject oldSO = new SerializedObject(oldItem);
        VacuumTool tool = oldItem.gameObject.AddComponent<VacuumTool>();
        SerializedObject so = new SerializedObject(tool);
        so.FindProperty("data").objectReferenceValue = itemData;
        CopyReference(oldSO, "mouth", so, "mouth");
        CopyFloat(oldSO, "suctionDuration", so, "suctionDuration");
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c 테스트 후 추가 EA16). 옛 청소기 프리팹에는 외곽선이 없어 모델 메시로 만든다
        if (!HasHighlight(copy))
        {
            Transform model = FindDeep(copy.transform, "Vacuum_Model");
            MissionOutlineBuilder.Attach(copy, model != null ? model.gameObject : copy);
        }

        Object.DestroyImmediate(oldItem);
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  2d: 압력 (신규 — 원본 패널 + 피스톤 A/B/C 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildPressure(GameObject copy)
    {
        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        PressureStation station = copy.AddComponent<PressureStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.PressureStabilized, CompletionPolicy.Lock, PersonalInteractRange);

        PressureVisual[] visuals = new PressureVisual[PressureStation.PistonCount];

        // 표시등 재질 (PR14): 원본 네온 재질 Neon_green · Neon_red(발광 켜짐 · 단색)를 글자에 그대로 끼운다
        Material lockedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");
        Material stalledMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");

        if (lockedMaterial == null || stalledMaterial == null)
            Debug.LogWarning("[LegacyMissionConverter] 압력: Neon_green / Neon_red 재질을 찾지 못해 글자 색이 바뀌지 않습니다.");

        for (int i = 0; i < PressureStation.PistonCount; i++)
        {
            string letter = PressureLetters[i];

            // 피스톤 모델 (원본 FBX 를 복사해 자식으로, 테스트 씬과 같은 상대 위치).
            //  PR14: 글자(A/B/C)가 별도 부품으로 분리된 V2 모델을 쓴다 — 글자만 따로 초록 · 빨강으로 바꿀 수 있다
            string pistonPath = $"{SourceModelFolder}/Pressure_piston_{letter}_V2.fbx";
            GameObject pistonModel = AssetDatabase.LoadAssetAtPath<GameObject>(pistonPath);

            if (pistonModel == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 압력 피스톤 모델이 없습니다: {pistonPath}");
                return false;
            }

            GameObject piston = Object.Instantiate(pistonModel, copy.transform);
            piston.name = $"Pressure_piston_{letter}";
            piston.transform.localPosition = PistonOffsets[i];
            piston.transform.localRotation = Quaternion.identity;

            // 노드 찾기: 모델 이름에 오타 · 공백이 있어 "토큰" 단위로 찾는다 (예: Pressure_button_A._nteraction)
            Transform weight = FindByTokens(piston.transform, letter, new[] { "piston", "parts" }, null, false);
            // 원통 본체: V2 의 글자(Pressure_piston_A_Alphabet)도 "piston + A" 라서 alphabet 을 빼야 본체가 잡힌다
            Transform body = FindByTokens(piston.transform, letter, new[] { "piston" }, new[] { "parts", "alphabet" }, true);
            Transform alphabet = FindByTokens(piston.transform, letter, new[] { "alphabet" }, null, true);
            Transform gauge = FindByTokens(copy.transform, letter, new[] { "gauge" }, new[] { "needle", "indicator" }, true);
            Transform needle = FindByTokens(copy.transform, letter, new[] { "needle" }, null, false);
            Transform button = FindByTokens(copy.transform, letter, new[] { "button", "nteraction" }, null, true);

            if (weight == null || body == null || gauge == null || needle == null || button == null)
            {
                Debug.LogError($"[LegacyMissionConverter] 압력 {letter}: 모델 노드를 찾지 못했습니다 " +
                    $"(추 {Found(weight)}, 원통 {Found(body)}, 압력계 {Found(gauge)}, 바늘 {Found(needle)}, 버튼 {Found(button)}).");
                return false;
            }

            // 바늘 회전축 (PR10): 압력계 중심에 만들고 바늘을 그 아래로 옮긴다
            Renderer gaugeRenderer = gauge.GetComponent<Renderer>();
            GameObject pivot = new GameObject($"NeedlePivot_{letter}");
            pivot.transform.SetParent(gauge.parent, false);
            pivot.transform.position = gaugeRenderer.bounds.center;
            pivot.transform.rotation = gauge.rotation;
            needle.SetParent(pivot.transform, true);
            Vector3 needleAxis = ThinnestAxis(gauge);

            // 추 이동 범위 기본값: 원통 높이의 ±30% (부모 로컬 단위로)
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            float parentScale = weight.parent != null ? Mathf.Max(0.0001f, Mathf.Abs(weight.parent.lossyScale.y)) : 1f;
            float travel = bodyRenderer.bounds.size.y * 0.3f / parentScale;
            Vector3 weightAxis = weight.parent != null ? weight.parent.InverseTransformDirection(Vector3.up).normalized : Vector3.up;

            // 표시등 (PR14): 피스톤 글자. 못 찾으면 경고 후 표시등 없이 동작 (게임 규칙에는 영향 없음)
            Renderer letterRenderer = alphabet != null ? alphabet.GetComponent<Renderer>() : null;

            if (letterRenderer == null)
                Debug.LogWarning($"[LegacyMissionConverter] 압력 {letter}: 피스톤 글자(Alphabet)를 찾지 못해 표시등 없이 동작합니다.");

            GameObject visualObject = new GameObject($"Piston_{letter}");
            visualObject.transform.SetParent(copy.transform, false);
            PressureVisual visual = visualObject.AddComponent<PressureVisual>();
            SerializedObject visualSO = new SerializedObject(visual);
            visualSO.FindProperty("needlePivot").objectReferenceValue = pivot.transform;
            visualSO.FindProperty("needleAxis").vector3Value = needleAxis;
            visualSO.FindProperty("weight").objectReferenceValue = weight;
            visualSO.FindProperty("weightAxis").vector3Value = weightAxis;
            visualSO.FindProperty("bottomOffset").floatValue = -travel;
            visualSO.FindProperty("topOffset").floatValue = travel;
            visualSO.FindProperty("lampRenderer").objectReferenceValue = letterRenderer;
            visualSO.FindProperty("lockedMaterial").objectReferenceValue = lockedMaterial;
            visualSO.FindProperty("stalledMaterial").objectReferenceValue = stalledMaterial;
            visualSO.ApplyModifiedPropertiesWithoutUndo();
            visuals[i] = visual;

            // 버튼: 조준 레이어 · 콜라이더 · StationButton · 외곽선 (코드 자판과 같은 공통 함수)
            float pressDepth = SetupPressButton(so, station, button, i);

            Debug.Log($"[LegacyMissionConverter] 압력 {letter} 추정값: 바늘 축 {needleAxis}, 각도 -120~120, 추 범위 ±{travel:0.000} (축 {weightAxis}), 글자 {Found(alphabet)}, 눌림 {pressDepth:0.000}m");
        }

        SerializedProperty pistonList = so.FindProperty("pistons");
        pistonList.ClearArray();

        for (int i = 0; i < visuals.Length; i++)
        {
            pistonList.InsertArrayElementAtIndex(i);
            pistonList.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  3a: 코드 순서 맞추기 (신규 — 원본 자판 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    private static bool BuildCode(GameObject copy)
    {
        // 버튼 11개: 부품 번호 0 ~ 8 = 숫자 1 ~ 9, 9 = 빨강, 10 = 파랑 (CodeStation 상수와 같은 순서)
        string[] buttonNames = new string[CodeStation.BlueButton + 1];

        for (int i = 0; i < CodeRules.DigitCount; i++)
            buttonNames[i] = $"Match_code_button_{i + 1}";

        buttonNames[CodeStation.RedButton] = "Match_code_button_red";
        buttonNames[CodeStation.BlueButton] = "Match_code_button_blue";

        Transform[] buttons = new Transform[buttonNames.Length];
        List<string> missing = new List<string>();

        for (int i = 0; i < buttonNames.Length; i++)
        {
            buttons[i] = FindDeep(copy.transform, buttonNames[i]);

            if (buttons[i] == null || buttons[i].GetComponent<Renderer>() == null)
                missing.Add(buttonNames[i]);
        }

        if (missing.Count > 0)
        {
            Debug.LogError($"[LegacyMissionConverter] 코드: 모델 노드를 찾지 못했습니다 ({string.Join(", ", missing)}).");
            return false;
        }

        // 버튼이 있는 쪽을 스테이션 정면(+Z)으로 — 배치할 때 +Z 를 플레이어 쪽으로 돌리기 때문
        float frontAngle = FaceFront(copy, buttons);

        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        CodeStation station = copy.AddComponent<CodeStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.SecurityCodeEntered, CompletionPolicy.Lock, CodeInteractRange);

        float pressDepth = 0f;

        for (int i = 0; i < buttons.Length; i++)
            pressDepth = SetupPressButton(so, station, buttons[i], i);

        // 불빛: 숫자 1 ~ 9 렌더러 + 원본 네온 재질 3종 (C9)
        Material litMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_blue.mat");
        Material failMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");
        Material doneMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_green.mat");

        if (litMaterial == null || failMaterial == null || doneMaterial == null)
            Debug.LogWarning("[LegacyMissionConverter] 코드: Neon_blue / Neon_red / Neon_green 재질을 찾지 못해 일부 불빛이 보이지 않습니다.");

        CodeKeypadVisual keypad = copy.AddComponent<CodeKeypadVisual>();
        SerializedObject keypadSO = new SerializedObject(keypad);
        SerializedProperty renderers = keypadSO.FindProperty("digitRenderers");
        renderers.ClearArray();

        for (int i = 0; i < CodeRules.DigitCount; i++)
        {
            renderers.InsertArrayElementAtIndex(i);
            renderers.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i].GetComponent<Renderer>();
        }

        keypadSO.FindProperty("litMaterial").objectReferenceValue = litMaterial;
        keypadSO.FindProperty("failMaterial").objectReferenceValue = failMaterial;
        keypadSO.FindProperty("doneMaterial").objectReferenceValue = doneMaterial;
        keypadSO.ApplyModifiedPropertiesWithoutUndo();

        so.FindProperty("keypad").objectReferenceValue = keypad;
        so.FindProperty("display").objectReferenceValue = BuildCodeDisplay(copy);
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 코드: 버튼 {buttons.Length}개 연결, 정면 맞춤 회전 {frontAngle:0}°, 눌림 {pressDepth:0.000}m");
        return true;
    }

    /// <summary>
    /// 코드 자판 화면 글자 (3a 명세 C13): 패널 화면 위에 LCD 글자(LcdDisplay)를 올린다.
    /// 화면을 못 찾으면 경고 후 null — 스테이션은 화면 없이 동작한다.
    /// </summary>
    private static LcdDisplay BuildCodeDisplay(GameObject root)
    {
        MeshFilter panel = root.GetComponent<MeshFilter>();

        if (panel == null || panel.sharedMesh == null ||
            !TryFindScreen(panel, root.transform, CodeScreenAtlasUv, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size))
        {
            Debug.LogWarning("[LegacyMissionConverter] 코드: 패널에서 화면(아틀라스 검은 칸 · 앞을 향한 면)을 찾지 못해 화면 글자 없이 동작합니다.");
            return null;
        }

        // 화면 3mm 앞 (화면 면과 겹쳐 깜빡이지 않게), 글자 앞면(−Z)이 화면 밖(normal)을 보게, 테두리 여백 10%
        LcdDisplay display = CreateLcdText(root.transform, center + normal * 0.003f, Quaternion.LookRotation(-normal, up), size * 0.9f,
            CodeStation.SuccessText, CodeStation.ReadyText);

        TMP_Text text = display.GetComponent<TMP_Text>();
        float tilt = Vector3.Angle(Vector3.forward, Vector3.ProjectOnPlane(normal, Vector3.right));
        Debug.Log($"[LegacyMissionConverter] 코드: 화면 찾음 — 크기 {size.x:0.00} × {size.y:0.00}m, 뒤로 기운 각도 {tilt:0}°, 글자 크기 {text.fontSize:0.00}{(text.enableAutoSizing ? " (자동 크기 유지)" : string.Empty)}");
        return display;
    }

    /// <summary>
    /// LCD 글자 (3a C13 · 3b LS16): parent 아래에 TextMeshPro 글자 + LcdDisplay 를 만든다.
    /// 크기는 가장 긴 문구(longestText)가 영역(size)에 맞도록 한 번 재고 고정한다 (문구마다 글자 크기가 달라지지 않게).
    /// </summary>
    /// <param name="localRotation">글자 앞면(−Z)이 보는 쪽을 정하는 회전</param>
    /// <param name="fontPath">비우면 TextMeshPro 기본 글꼴 (영문 LCD). 한글 문구는 KoreanFontPath</param>
    private static LcdDisplay CreateLcdText(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size,
        string longestText, string initialText, string fontPath = null)
    {
        fontPath ??= TmpDefaultFontPath;
        TextMeshPro text = CreateFittedText(parent, "Display (화면 글자)", localPosition, localRotation, size, longestText, initialText, fontPath, Color.white);
        GameObject textObject = text.gameObject;

        LcdDisplay display = textObject.AddComponent<LcdDisplay>();
        SerializedObject displaySO = new SerializedObject(display);
        displaySO.FindProperty("text").objectReferenceValue = text;
        displaySO.ApplyModifiedPropertiesWithoutUndo();
        return display;
    }

    /// <summary>
    /// 월드 글자(TextMeshPro)를 만든다 (13a 에서 화면 글자와 배관 문구 · 「수리 완료」가 같이 쓰려고 CreateLcdText 에서 떼어 냄 — 동작은 같다):
    /// 칸 크기 · 가운데 정렬 · 줄바꿈 없음, 가장 긴 문구로 글자 크기를 한 번 재서 고정한 뒤 처음 문구를 넣는다. 글꼴이 없으면 경고 후 TMP 기본값.
    /// </summary>
    private static TextMeshPro CreateFittedText(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, Vector2 size,
        string longestText, string initialText, string fontPath, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        // TextMeshPro 를 붙이면 RectTransform 도 함께 생긴다
        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.localPosition = localPosition;
        rect.localRotation = localRotation;
        rect.sizeDelta = size;

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

        if (font != null)
            text.font = font;
        else
            Debug.LogWarning($"[LegacyMissionConverter] 글꼴을 찾지 못해 TextMeshPro 기본값을 씁니다 ({fontPath}).");

        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.color = color;

        // 가장 긴 문구로 크기를 한 번 재고 고정
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.01f;
        text.fontSizeMax = 100f;
        text.text = longestText;
        text.ForceMeshUpdate(true, true);
        float fitted = text.fontSize;

        if (fitted > 0.01f)
        {
            text.enableAutoSizing = false;
            text.fontSize = fitted;
        }

        text.text = initialText;
        return text;
    }

    /// <summary>
    /// 모델 메시에서 화면 면을 찾아 루트 기준 중심 · 바깥 방향(normal) · 화면의 위쪽 · 크기(가로, 세로)를 구한다.
    /// [찾는 규칙 — FBX 분석] 화면은 아틀라스 128×128 의 한 칸(atlasUv — 코드 · 장비는 완전한 검정 칸 UV ≈ 0.3865, 0.5745)을 쓰는 면들 중 앞(+Z)을 향한 면이다.
    ///  코드: 가로 약 127cm · 세로 약 51cm, 뒤로 약 18° 기운 넓은 화면. 화면 둘레의 위아래 경사면도 같은 칸을 쓰지만 앞을 향하지 않아 걸러진다.
    ///  (2026-10-27) 장비 조립 위쪽 검은 패널도 같은 규칙으로 찾는다 — 코드 전용 함수를 메시 · 칸을 받는 공통 함수로 넓혔다.
    /// </summary>
    private static bool TryFindScreen(MeshFilter panel, Transform root, Vector2 atlasUv, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size)
    {
        center = normal = up = Vector3.zero;
        size = Vector2.zero;

        Mesh mesh = panel.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;

        if (uvs.Length != vertices.Length)
            return false;

        List<Vector3> points = new List<Vector3>();
        Vector3 normalSum = Vector3.zero;

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 uvCenter = (uvs[a] + uvs[b] + uvs[c]) / 3f;

            if (Mathf.Abs(uvCenter.x - atlasUv.x) > CodeScreenUvTolerance ||
                Mathf.Abs(uvCenter.y - atlasUv.y) > CodeScreenUvTolerance)
                continue;

            Vector3 pa = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[a]));
            Vector3 pb = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[b]));
            Vector3 pc = root.InverseTransformPoint(panel.transform.TransformPoint(vertices[c]));

            // Unity 메시의 앞면 방향 = Cross(b − a, c − a). 길이는 넓이의 두 배
            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            float doubleArea = cross.magnitude;

            if (doubleArea < 1e-8f)
                continue;

            Vector3 faceNormal = cross / doubleArea;

            if (Vector3.Dot(faceNormal, Vector3.forward) < 0.8f)
                continue;

            normalSum += cross;
            points.Add(pa);
            points.Add(pb);
            points.Add(pc);
        }

        if (points.Count == 0)
            return false;

        normal = normalSum.normalized;
        up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
        Vector3 right = Vector3.Cross(up, normal).normalized;

        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, depth = 0f;

        foreach (Vector3 p in points)
        {
            float r = Vector3.Dot(p, right);
            float u = Vector3.Dot(p, up);
            minR = Mathf.Min(minR, r);
            maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
            depth += Vector3.Dot(p, normal);
        }

        depth /= points.Count;
        center = right * (minR + maxR) * 0.5f + up * (minU + maxU) * 0.5f + normal * depth;
        size = new Vector2(maxR - minR, maxU - minU);
        return size.x > 0.01f && size.y > 0.01f;
    }

    // ═════════════════════════════════════════════════════════════
    //  3b: 생명 유지 장치 · 산소통 (원본 모델에서 조립 — 3b 보강: 화면 상자 · 압력계 덧붙임)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 산소통 1인칭 모델 (보강 명세 LM11): 빈 루트 + 산소통 모델 사본 0.5배를 화면 오른쪽 아래에 세운다. 콜라이더 · 압력계 없음
    /// (1인칭 모델은 아이템 데이터 하나를 같이 써서 통마다 색을 다르게 할 수 없다 — 3b 열린 항목 6). 위치는 에디터 확인 후 조정.
    /// </summary>
    private static bool BuildOxygenTankFirstPerson(GameObject copy)
    {
        GameObject model = LoadSourceModel(OxygenTankModelPath);

        if (model == null)
            return false;

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localPosition = OxygenTankFirstPersonPosition;
        view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one * OxygenTankFirstPersonScale;

        foreach (Collider collider in view.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        return true;
    }

    /// <summary>산소통 아이템 데이터 (팀원 ItemData): 번호 20 · 이름 · 1인칭 모델 (3b).</summary>
    private static ItemData CreateOxygenTankData(GameObject firstPersonPrefab)
    {
        return CreateItemData(OxygenTankDataPath, OxygenTankItemId, "산소통", firstPersonPrefab);
    }

    /// <summary>
    /// 미션 아이템 데이터 (팀원 ItemData — 3b 산소통 · 3c 장비 부품): 번호 · 이름 · 1인칭 모델. 이미 있으면 값만 갱신한다.
    /// 다른 아이템 데이터가 같은 번호를 쓰면 경고한다.
    /// </summary>
    private static ItemData CreateItemData(string path, int id, string itemName, GameObject firstPersonPrefab)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

        ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(path);

        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, path);
        }

        SerializedObject so = new SerializedObject(data);
        so.FindProperty("id").intValue = id;
        so.FindProperty("itemName").stringValue = itemName;
        so.FindProperty("firstPersonPrefab").objectReferenceValue = firstPersonPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            string otherPath = AssetDatabase.GUIDToAssetPath(guid);
            ItemData other = otherPath != path ? AssetDatabase.LoadAssetAtPath<ItemData>(otherPath) : null;

            if (other != null && other.Id == id)
                Debug.LogWarning($"[LegacyMissionConverter] {itemName}: 아이템 번호 {id} 를 {otherPath} 도 쓰고 있습니다.");
        }

        return data;
    }

    /// <summary>
    /// 산소통 (3b 명세 4장 · 보강 명세 LM9 · LM10): 원본 산소통 모델 복사(루트 = 메시, 바닥이 원점) + 밸브 옆 둥근 압력계.
    /// 루트에 NetworkObject · 메시 크기 콜라이더(조준 레이어 — 줍기 · 조준) · ItemWorldView · OxygenTank.
    /// 압력계는 늘 회색 장식이다 — 정상 · 위험이 겉모습으로 구분되지 않게 (7d L3B14, 3b LS1 의 가까이 가면 초록 · 빨강은 없앰).
    /// </summary>
    private static bool BuildOxygenTank(GameObject copy, ItemData data)
    {
        MeshFilter meshFilter = copy.GetComponent<MeshFilter>();
        Renderer bodyRenderer = copy.GetComponent<Renderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null || bodyRenderer == null)
        {
            Debug.LogError("[LegacyMissionConverter] 산소통: 모델 루트에 메시가 없습니다 (모델이 바뀌었는지 확인).");
            return false;
        }

        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();
        BoxCollider box = AddMeshBoxCollider(copy);

        // 조준하면 외곽선 (미션 아이템 공통 — 3c EA16). 루트의 모델 메시로만 — 압력계 같은 작은 덧붙임은 빼서 테두리가 지저분하지 않게
        MissionOutlineBuilder.Attach(copy, copy);

        if (!BuildOxygenTankGauge(copy.transform, meshFilter.sharedMesh, out Renderer[] gaugeParts))
            return false;

        ItemWorldView view = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(view);
        SetObjectArray(viewSO.FindProperty("renderers"), bodyRenderer, gaugeParts[0], gaugeParts[1], gaugeParts[2]);
        SetObjectArray(viewSO.FindProperty("colliders"), box);
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        OxygenTank tank = copy.AddComponent<OxygenTank>();
        SerializedObject so = new SerializedObject(tank);
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 산소통: 아이템 번호 {OxygenTankItemId}, 모델 크기 {meshFilter.sharedMesh.bounds.size}");
        return true;
    }

    /// <summary>
    /// 산소통 압력계 (보강 명세 LM10 — 기획서 그림처럼 밸브 옆 둥근 압력계 1개): 테두리 원판 + 눈금판 + 바늘. 콜라이더 없음.
    /// 자리: 모델 높이의 90%(밸브 머리의 목) ± 3cm 정점 중 덜 튀어나온 옆면(손잡이 반대쪽)에 바깥을 보게 붙인다.
    /// 옆면은 조립 때 메시에서 고른다 — FBX 분석상 손잡이는 +X · 빈 쪽은 −X 인데, Unity 가져오기 때 좌우가 뒤집힐 수 있어 숫자로 정하지 않는다.
    /// </summary>
    /// <param name="parts">테두리 · 눈금판 · 바늘 렌더러 (ItemWorldView 가 들었을 때 함께 숨긴다)</param>
    private static bool BuildOxygenTankGauge(Transform root, Mesh mesh, out Renderer[] parts)
    {
        parts = null;

        Bounds bounds = mesh.bounds;
        float height = bounds.min.y + bounds.size.y * OxygenTankGaugeHeightRatio;
        float minX = float.MaxValue;
        float maxX = float.MinValue;

        foreach (Vector3 vertex in mesh.vertices)
        {
            if (Mathf.Abs(vertex.y - height) > OxygenTankGaugeBand)
                continue;

            minX = Mathf.Min(minX, vertex.x);
            maxX = Mathf.Max(maxX, vertex.x);
        }

        if (minX > maxX)
        {
            Debug.LogError($"[LegacyMissionConverter] 산소통: 높이 {height:0.00}m 근처에 정점이 없어 압력계 자리를 찾지 못했습니다.");
            return false;
        }

        // 덜 튀어나온 쪽 = 비어 있는 옆면 (반대쪽은 손잡이)
        bool negativeSide = bounds.center.x - minX <= maxX - bounds.center.x;
        Vector3 outward = negativeSide ? Vector3.left : Vector3.right;
        Vector3 surfacePoint = new Vector3(negativeSide ? minX : maxX, height, bounds.center.z);
        // 기본 원기둥의 축(위)을 바깥으로 돌린다
        Quaternion discRotation = Quaternion.FromToRotation(Vector3.up, outward);

        Material frame = GetOrCreateColorMaterial("LifeSupport_Frame", LifeSupportFrameColor);
        Material off = GetOrCreateColorMaterial("LifeSupport_Off", GaugeOffColor);
        Material black = GetOrCreateColorMaterial("LifeSupport_Screen", Color.black);

        // 기본 원기둥은 지름 1 · 높이 2 → 크기 (지름, 두께 / 2, 지름). 테두리는 목 표면에서 바깥으로 두께만큼
        GameObject rim = CreateShape(PrimitiveType.Cylinder, "Gauge Rim (압력계 테두리)", root,
            surfacePoint + outward * (OxygenTankGaugeRimThickness * 0.5f),
            new Vector3(OxygenTankGaugeRimDiameter, OxygenTankGaugeRimThickness * 0.5f, OxygenTankGaugeRimDiameter), frame, false);
        rim.transform.localRotation = discRotation;

        // 눈금판: 테두리 바깥면에서 1mm 더 나온 얇은 원판 — 늘 회색 (7d L3B14 — 정상 · 위험을 색으로 드러내지 않는다)
        GameObject gaugeFace = CreateShape(PrimitiveType.Cylinder, "Gauge (압력계 눈금판)", root,
            surfacePoint + outward * (OxygenTankGaugeRimThickness + 0.001f),
            new Vector3(OxygenTankGaugeFaceDiameter, 0.001f, OxygenTankGaugeFaceDiameter), off, false);
        gaugeFace.transform.localRotation = discRotation;

        // 바늘 (장식 — 색은 바뀌지 않는다): 눈금판 위 가는 막대, 가운데에서 30° 기운 위쪽으로. 막대의 Z = 바깥, Y = 길이 방향
        Quaternion needleRotation = Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(0f, 0f, 30f);
        Vector3 needleCenter = surfacePoint + outward * (OxygenTankGaugeRimThickness + 0.004f)
            + needleRotation * Vector3.up * (OxygenTankGaugeFaceDiameter * 0.18f);
        GameObject needle = CreateShape(PrimitiveType.Cube, "Gauge Needle (압력계 바늘)", root, needleCenter,
            new Vector3(0.004f, OxygenTankGaugeFaceDiameter * 0.36f, 0.002f), black, false);
        needle.transform.localRotation = needleRotation;

        parts = new[] { rim.GetComponent<Renderer>(), gaugeFace.GetComponent<Renderer>(), needle.GetComponent<Renderer>() };

        Debug.Log($"[LegacyMissionConverter] 산소통 압력계: 높이 {height:0.00}m, {(negativeSide ? "−X" : "+X")} 옆면 " +
                  $"(목 표면 x {surfacePoint.x:0.000}m, 반대쪽 {(negativeSide ? maxX : minX):0.000}m)");
        return true;
    }

    /// <summary>
    /// 생명 유지 장치 본체 (3b 명세 4장 · 보강 명세 LM2 ~ LM6): 원본 본체 모델 복사(루트 = 메시, 바닥이 원점, 앞 = +Z)
    /// + 자리 노드에 꽂힌 산소통 모습 + 꼭대기 화면 상자 + 폭발 섬광. 경고 램프는 없다 (LM7 — 화면 깜빡임이 대신).
    /// 콜라이더 둘: 몸체(메시 크기, 단단함, 기본 레이어 — 플레이어가 통과하지 않게) / 조준 영역(트리거, 조준 레이어 — 내 미션이 아니면 공통 틀이 끈다).
    /// </summary>
    private static bool BuildLifeSupport(GameObject copy, NetworkObject tankPrefab)
    {
        Material warning = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/Neon_red.mat");

        if (warning == null)
            Debug.LogWarning("[LegacyMissionConverter] 생명 유지 장치: Neon_red 재질을 찾지 못해 폭발 섬광이 보이지 않습니다.");

        GameObject tankModel = LoadSourceModel(OxygenTankModelPath);
        MeshFilter meshFilter = copy.GetComponent<MeshFilter>();

        if (tankModel == null)
            return false;

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("[LegacyMissionConverter] 생명 유지 장치: 모델 루트에 메시가 없습니다 (모델이 바뀌었는지 확인).");
            return false;
        }

        Transform seat = FindDescendant(copy.transform, LifeSupportTankSeatNode);

        if (seat == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 생명 유지 장치: 산소통 자리 노드 '{LifeSupportTankSeatNode}' 를 찾지 못했습니다 (모델이 바뀌었는지 확인).");
            return false;
        }

        Transform root = copy.transform;
        copy.AddComponent<NetworkObject>();
        Bounds bounds = meshFilter.sharedMesh.bounds;

        // 몸체: 메시 크기 상자 (기본 레이어 — 단단함, 조준 검사 대상 아님)
        AddMeshBoxCollider(copy);

        GameObject aim = new GameObject("Aim (조준 영역)");
        aim.layer = InteractableLayer;
        aim.transform.SetParent(root, false);
        aim.transform.localPosition = bounds.center;
        BoxCollider aimCollider = aim.AddComponent<BoxCollider>();
        aimCollider.isTrigger = true;
        aimCollider.size = bounds.size + Vector3.one * 0.04f;

        // 꽂힌 산소통: 자리 노드(홈 바닥)에 산소통 모델 사본 — 판정 중에만 보인다 (LM3)
        GameObject inserted = Object.Instantiate(tankModel, root);
        inserted.name = "Inserted Tank (꽂힌 산소통)";
        inserted.transform.localPosition = root.InverseTransformPoint(seat.position);
        inserted.transform.localRotation = Quaternion.identity;

        foreach (Collider collider in inserted.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        inserted.SetActive(false);

        LcdDisplay display = BuildLifeSupportScreen(root, bounds, out Vector3 screenCenter);

        GameObject flash = CreateShape(PrimitiveType.Sphere, "Flash (폭발 섬광)", root, bounds.center, Vector3.one, warning, false);
        flash.SetActive(false);

        LifeSupportVisual visual = copy.AddComponent<LifeSupportVisual>();
        SerializedObject visualSO = new SerializedObject(visual);
        visualSO.FindProperty("insertedTank").objectReferenceValue = inserted;
        visualSO.FindProperty("flash").objectReferenceValue = flash.transform;
        visualSO.ApplyModifiedPropertiesWithoutUndo();

        LifeSupportStation station = copy.AddComponent<LifeSupportStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.LifeSupportRestored, CompletionPolicy.Lock, LifeSupportInteractRange);
        so.FindProperty("tankPrefab").objectReferenceValue = tankPrefab;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.FindProperty("display").objectReferenceValue = display;
        so.FindProperty("explosionSound").objectReferenceValue = BuildLifeSupportExplosionSound(copy.transform);
        // 본체 한 대 = 산소통 2개 · 정상 1 (7d L3B6 — 옛 프리팹 값이 남지 않게 직접 넣는다)
        so.FindProperty("tankCount").intValue = 2;
        so.FindProperty("safeTanks").intValue = 1;
        AddCollider(so, aimCollider);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준 외곽선: 루트의 모델 메시로만 (화면 상자 · 꽂힌 산소통은 빠진다 — 루트의 메시가 먼저 잡힌다)
        MissionOutlineBuilder.Attach(copy, copy);

        Debug.Log($"[LegacyMissionConverter] 생명 유지 장치: 모델 크기 {bounds.size}, 산소통 자리 {inserted.transform.localPosition}, " +
                  $"화면 중심 {screenCenter} (뒤로 {LifeSupportScreenTilt}°), 산소통 프리팹 {tankPrefab.name}");
        return true;
    }

    /// <summary>
    /// 폭발음 (12a — 7d L3B15): 본체 자식 `Explosion Sound (폭발음)` 에 3D 소리 재생기. 자동 재생 끔 — 장치가 폭발 순간에 한 번 튼다.
    /// 근처 모두가 듣고(사용자 결정 A) 멀수록 작게: 피해 반경 안 최대 크기 → 25m 에서 0 (직선). 소리 파일이 없으면 경고 후 소리 없이.
    /// </summary>
    private static AudioSource BuildLifeSupportExplosionSound(Transform root)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(LifeSupportExplosionSoundPath);

        if (clip == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 생명 유지 장치: 폭발음 파일이 없어 소리 없이 동작합니다 ({LifeSupportExplosionSoundPath}).");
            return null;
        }

        GameObject soundObject = new GameObject("Explosion Sound (폭발음)");
        soundObject.transform.SetParent(root, false);

        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = LifeSupportExplosionFullDistance;
        source.maxDistance = LifeSupportExplosionMaxDistance;
        return source;
    }

    /// <summary>
    /// 본체 화면 상자 (보강 명세 LM4 — 모델에 화면으로 쓸 면이 없다): 꼭대기 위 받침 + 상자(짙은 회색) + 검은 판 + 녹색 LCD 글자,
    /// 뒤로 15° 눕힘. 콜라이더 없음 (조준 · 충돌에 끼지 않게). 위치는 모델 메시 범위(꼭대기 높이 · 앞뒤 중심)에서 계산한다.
    /// </summary>
    private static LcdDisplay BuildLifeSupportScreen(Transform root, Bounds bounds, out Vector3 center)
    {
        Material frame = GetOrCreateColorMaterial("LifeSupport_Frame", LifeSupportFrameColor);
        Material black = GetOrCreateColorMaterial("LifeSupport_Screen", Color.black);
        float top = bounds.max.y;

        CreateShape(PrimitiveType.Cube, "Screen Stand (화면 받침)", root,
            new Vector3(bounds.center.x, top + LifeSupportScreenStandSize.y * 0.5f, bounds.center.z), LifeSupportScreenStandSize, frame, false);

        // 화면 묶음: 크기 1 인 빈 오브젝트를 기울이고 그 아래에 상자 · 판 · 글자를 둔다 (늘린 상자의 자식이 같이 늘어나지 않게)
        center = new Vector3(bounds.center.x, top + LifeSupportScreenStandSize.y + LifeSupportScreenBoxSize.y * 0.5f, bounds.center.z);
        GameObject screen = new GameObject("Screen (화면)");
        screen.transform.SetParent(root, false);
        screen.transform.localPosition = center;
        // X 축 −15°: 화면 앞면(+Z)이 조금 위를 본다 — 선 사람 눈높이에서 읽기 쉽게
        screen.transform.localRotation = Quaternion.Euler(-LifeSupportScreenTilt, 0f, 0f);

        float front = LifeSupportScreenBoxSize.z * 0.5f;
        CreateShape(PrimitiveType.Cube, "Screen Box (화면 상자)", screen.transform, Vector3.zero, LifeSupportScreenBoxSize, frame, false);
        CreateShape(PrimitiveType.Cube, "Screen Panel (화면 판)", screen.transform, new Vector3(0f, 0f, front + 0.002f),
            new Vector3(LifeSupportScreenSize.x, LifeSupportScreenSize.y, 0.004f), black, false);

        // 글자: 판 바로 앞, 글자 앞면(−Z)이 +Z 를 보게. 크기는 가장 긴 문구에 맞춘다
        return CreateLcdText(screen.transform, new Vector3(0f, 0f, front + 0.006f), Quaternion.LookRotation(Vector3.back, Vector3.up),
            LifeSupportScreenSize * 0.9f, LifeSupportStation.LongestScreenText, LifeSupportStation.EmptyText);
    }

    /// <summary>이름으로 자손(자신 포함)을 찾는다. 없으면 null.</summary>
    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }

        return null;
    }

    /// <summary>기본 도형 부품을 만든다. keepCollider 가 false 면 도형의 기본 콜라이더를 지운다 (조준 · 충돌에 끼지 않게).</summary>
    private static GameObject CreateShape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Material material, bool keepCollider)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name;

        if (!keepCollider)
            Object.DestroyImmediate(shape.GetComponent<Collider>());

        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = localPosition;
        shape.transform.localScale = localScale;

        if (material != null)
            shape.GetComponent<Renderer>().sharedMaterial = material;

        return shape;
    }

    /// <summary>
    /// 단색 재질을 우리 폴더(MissionRedesign_Draft/Materials)에서 읽고, 없으면 만든다 (원본 재질은 수정하지 않는다).
    /// URP Lit 이 없으면 Standard. 색은 Material.color(셰이더의 주 색)와 _BaseColor 둘 다 넣는다.
    /// </summary>
    private static Material GetOrCreateColorMaterial(string fileName, Color color)
    {
        string path = $"{DraftMaterialFolder}/{fileName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        material = new Material(shader) { name = fileName };
        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        EnsureFolder(DraftMaterialFolder);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>직렬화된 배열(SerializedProperty)을 items 로 채운다.</summary>
    private static void SetObjectArray(SerializedProperty list, params Object[] items)
    {
        list.ClearArray();

        for (int i = 0; i < items.Length; i++)
        {
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  3c: 고장난 장비 조립 (신규 — 원본 본체 · 부품 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    /// <summary>원본 부품 모델(색)을 읽는다. 없으면 오류.</summary>
    private static GameObject LoadEquipmentPartModel(int color)
    {
        string path = $"{SourceModelFolder}/Equipment_repair_machine_parts_{EquipmentPartColors[color]}.fbx";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (model == null)
            Debug.LogError($"[LegacyMissionConverter] 장비 부품 모델이 없습니다: {path}");

        return model;
    }

    /// <summary>부품 1인칭 모델: 빈 루트 + 부품 모델 사본(크기 0.6, 화면 오른쪽 아래). 콜라이더 없음. 위치는 에디터 확인 후 조정.</summary>
    private static bool BuildEquipmentPartFirstPerson(GameObject copy, int color)
    {
        GameObject model = LoadEquipmentPartModel(color);

        if (model == null)
            return false;

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localPosition = new Vector3(0.25f, -0.25f, 0.55f);
        view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one * 0.6f;
        return true;
    }

    /// <summary>
    /// 부품 아이템 (3c 명세 4장): 빈 루트(바닥 원점) + 부품 모델 사본을 반 높이만큼 위로 (EA11).
    /// 루트 · 모델을 조준 레이어로, 모델 메시 크기 BoxCollider, 루트에 NetworkObject · ItemWorldView · EquipmentPart.
    /// </summary>
    private static bool BuildEquipmentPart(GameObject copy, int color, ItemData data)
    {
        GameObject model = LoadEquipmentPartModel(color);

        if (model == null)
            return false;

        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localRotation = Quaternion.identity;

        MeshFilter meshFilter = view.GetComponentInChildren<MeshFilter>();
        Renderer renderer = view.GetComponentInChildren<Renderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null || renderer == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 부품 ({EquipmentPartKoreanNames[color]}): 모델에 메시가 없습니다.");
            return false;
        }

        // 원점이 부품 중심이라 그대로 두면 반쯤 묻힌다 → 메시 아래쪽이 루트 원점(바닥)에 오게 올린다 (EA11, 메시는 모델 루트에 있다)
        Bounds bounds = meshFilter.sharedMesh.bounds;
        view.transform.localPosition = Vector3.up * -bounds.min.y;

        GameObject colliderObject = meshFilter.gameObject;
        colliderObject.layer = InteractableLayer;
        BoxCollider box = colliderObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;

        ItemWorldView worldView = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(worldView);
        SetObjectArray(viewSO.FindProperty("renderers"), renderer);
        SetObjectArray(viewSO.FindProperty("colliders"), box);
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        EquipmentPart part = copy.AddComponent<EquipmentPart>();
        SerializedObject so = new SerializedObject(part);
        so.FindProperty("data").objectReferenceValue = data;
        so.FindProperty("colorIndex").intValue = color;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c 테스트 후 추가 EA16). 외곽선 복제본은 모델 자식이라 아이템과 같이 움직이고,
        // 들고 있으면 팀원 아이템 코드가 콜라이더를 꺼서 조준되지 않으므로 나타나지 않는다
        MissionOutlineBuilder.Attach(copy, view);

        Debug.Log($"[LegacyMissionConverter] 부품 ({EquipmentPartKoreanNames[color]}): 아이템 번호 {EquipmentPartFirstItemId + color}, 크기 {bounds.size}, 바닥 위로 {-bounds.min.y:0.000}m");
        return true;
    }

    /// <summary>
    /// 장비 (3c 명세 4장): 본체 복사본에 자리 4개(부품 모델 사본, 숨김 — EA9) · 몸체 충돌 상자 · 조립 위치 원 · 날아오기 시작점 · 장치 · 연출.
    /// 정면 맞춤은 하지 않는다 — 자리 노드가 +Z(앞) 쪽에 있다 (명세 1장). 뒤쪽이면 경고한다.
    /// </summary>
    private static bool BuildAssembly(GameObject copy, NetworkObject[] partPrefabs)
    {
        Transform root = copy.transform;
        Transform[] slots = new Transform[AssemblyRules.PartCount];
        List<string> missing = new List<string>();

        for (int c = 0; c < slots.Length; c++)
        {
            string slotName = $"Parts_{EquipmentPartColors[c]}_axes";
            slots[c] = FindDeep(root, slotName);

            if (slots[c] == null)
                missing.Add(slotName);
        }

        if (missing.Count > 0)
        {
            Debug.LogError($"[LegacyMissionConverter] 장비: 자리 노드를 찾지 못했습니다 ({string.Join(", ", missing)}).");
            return false;
        }

        // 몸체 메시는 부품 사본을 붙이기 전에 찾는다 (사본도 메시를 가져서)
        MeshFilter body = root.GetComponentInChildren<MeshFilter>();

        if (body == null || body.sharedMesh == null)
        {
            Debug.LogError("[LegacyMissionConverter] 장비: 본체 메시를 찾지 못했습니다.");
            return false;
        }

        // 몸체 충돌 상자 (기본 레이어 — 플레이어가 통과하지 않게, 조준 대상 아님 — EA14)
        BoxCollider bodyCollider = body.gameObject.AddComponent<BoxCollider>();
        bodyCollider.center = body.sharedMesh.bounds.center;
        bodyCollider.size = body.sharedMesh.bounds.size;

        // 앞면 · 중심 (루트 기준): 복사본은 원점 · 회전 0 이라 몸체 렌더러 경계(월드)를 루트 기준으로 바로 쓴다
        Bounds bodyBounds = body.GetComponent<Renderer>().bounds;
        float front = root.InverseTransformPoint(bodyBounds.center + Vector3.forward * bodyBounds.extents.z).z;
        float centerZ = root.InverseTransformPoint(bodyBounds.center).z;
        float slotZ = 0f;

        foreach (Transform slot in slots)
            slotZ += root.InverseTransformPoint(slot.position).z / slots.Length;

        if (slotZ <= centerZ)
            Debug.LogWarning($"[LegacyMissionConverter] 장비: 자리 노드(z {slotZ:0.00})가 몸체 중심(z {centerZ:0.00})보다 뒤에 있습니다 — 조립 위치가 반대편일 수 있습니다.");

        // 자리마다 그 색 부품 모델 사본 (로컬 0, 숨김 — 붙으면 보임, EA9)
        Transform[] attached = new Transform[slots.Length];
        AssemblySlot[] slotComponents = new AssemblySlot[slots.Length];

        for (int c = 0; c < slots.Length; c++)
        {
            GameObject model = LoadEquipmentPartModel(c);

            if (model == null)
                return false;

            GameObject attachedPart = Object.Instantiate(model, slots[c]);
            attachedPart.name = $"Attached ({EquipmentPartKoreanNames[c]})";
            attachedPart.transform.localPosition = Vector3.zero;
            attachedPart.transform.localRotation = Quaternion.identity;
            attachedPart.transform.localScale = Vector3.one;
            attached[c] = attachedPart.transform;

            // 7c: 자리 = 조준 주소 (중첩 NetworkObject · AssemblySlot · 조준 레이어 상자 · 부품 모양 외곽선 — AR3 · AR7).
            // 상자 · 외곽선은 사본을 숨기기 전에 잰다 (꺼진 렌더러는 경계가 비어 있다)
            slotComponents[c] = BuildAssemblySlot(slots[c], attachedPart, c);
            attachedPart.SetActive(false);
        }

        // 날아오기 시작점: 장비 앞 1m · 자리 평균 높이 (7c AR9 — 바닥 원이 없어져 원 중심 위에서 옮김)
        float slotY = 0f;

        foreach (Transform slot in slots)
            slotY += root.InverseTransformPoint(slot.position).y / slots.Length;

        GameObject flyStart = new GameObject("FlyStart (날아오기 시작)");
        flyStart.transform.SetParent(root, false);
        flyStart.transform.localPosition = new Vector3(0f, slotY, front + AssemblyFlyStartGap);

        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        // 완료 표시 = 검은 패널 글자 (2026-10-27 — 붙은 부품 초록 발광 대신, 3c EA6 바뀜)
        AssemblyVisual visual = copy.AddComponent<AssemblyVisual>();
        SerializedObject visualSO = new SerializedObject(visual);
        SetObjectArray(visualSO.FindProperty("attachedParts"), attached);
        visualSO.FindProperty("flyStart").objectReferenceValue = flyStart.transform;
        visualSO.ApplyModifiedPropertiesWithoutUndo();

        LcdDisplay display = BuildAssemblyDisplay(copy);

        // 범위 = 장비 루트에서 4m (7c AR6 — 기준점 비움). 자리 4개를 장치에 연결하고, 각 자리에 장치를 알려 준다
        AssemblyStation station = copy.AddComponent<AssemblyStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.EquipmentAssembled, CompletionPolicy.Lock, AssemblyInteractRange);
        SetObjectArray(so.FindProperty("partPrefabs"), partPrefabs);
        SetObjectArray(so.FindProperty("slots"), slotComponents);

        foreach (AssemblySlot slotComponent in slotComponents)
        {
            SerializedObject slotSO = new SerializedObject(slotComponent);
            slotSO.FindProperty("station").objectReferenceValue = station;
            slotSO.ApplyModifiedPropertiesWithoutUndo();
        }
        so.FindProperty("visual").objectReferenceValue = visual;
        so.FindProperty("display").objectReferenceValue = display;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 장비: 자리 4개 찾음 (평균 z {slotZ:0.00} > 몸체 중심 z {centerZ:0.00}), 앞면 z {front:0.00}, 자리 4개 조준 주소 · 날아오기 시작 {flyStart.transform.localPosition} · 범위 {AssemblyInteractRange}m, 부품 프리팹 4종");
        return true;
    }

    /// <summary>
    /// 장비 화면 글자 (2026-10-27 — 3c EA6 바뀜): 장비 모델 위쪽 검은 패널(코드 화면과 같은 검정 칸 · 앞을 향한 면)에 LCD 글자를 올린다.
    /// "부품 n / 4" → "정상 작동" (AssemblyRules.ScreenText) — 한글이라 NotoSansKR. 화면을 못 찾으면 경고 후 null — 장비는 글자 없이 동작한다.
    /// </summary>
    private static LcdDisplay BuildAssemblyDisplay(GameObject root)
    {
        MeshFilter body = root.GetComponent<MeshFilter>();

        if (body == null || body.sharedMesh == null ||
            !TryFindScreen(body, root.transform, CodeScreenAtlasUv, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size))
        {
            Debug.LogWarning("[LegacyMissionConverter] 장비: 몸체 모델에서 검은 패널(아틀라스 검은 칸 · 앞을 향한 면)을 찾지 못해 화면 글자 없이 동작합니다.");
            return null;
        }

        // 화면 3mm 앞, 글자 앞면이 화면 밖을 보게, 테두리 여백 10% (코드 화면과 같음). 크기는 가장 긴 "부품 4 / 4" 로 고정
        LcdDisplay display = CreateLcdText(root.transform, center + normal * 0.003f, Quaternion.LookRotation(-normal, up), size * 0.9f,
            AssemblyRules.ScreenText(AssemblyRules.PartCount, false), AssemblyRules.ScreenText(0, false), KoreanFontPath);

        TMP_Text text = display.GetComponent<TMP_Text>();
        Debug.Log($"[LegacyMissionConverter] 장비: 화면 찾음 — 크기 {size.x:0.00} × {size.y:0.00}m, 중심 {center}, 글자 크기 {text.fontSize:0.00}");
        return display;
    }

    /// <summary>
    /// 장비 자리 하나를 조준 주소로 만든다 (7c 명세 AR3 · AR7): 조준 레이어 · 중첩 NetworkObject · 부품 크기 상자(트리거 아님 — 방화문 패널과 같음) ·
    /// AssemblySlot(색) · 부품 모양 외곽선(자리 노드 아래 — 사본을 숨겨도 켜지게). 장치 연결은 BuildAssembly 가 장치를 만든 뒤에 한다.
    /// </summary>
    private static AssemblySlot BuildAssemblySlot(Transform slot, GameObject attachedPart, int color)
    {
        GameObject slotObject = slot.gameObject;
        slotObject.layer = InteractableLayer;

        if (slotObject.GetComponent<NetworkObject>() == null)
            slotObject.AddComponent<NetworkObject>();   // 조준 주소 (AR3 — 팀원 코드의 3m 기준이 자리가 된다)

        Renderer[] renderers = attachedPart.GetComponentsInChildren<Renderer>();
        BoxCollider box = slotObject.AddComponent<BoxCollider>();

        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            box.center = slot.InverseTransformPoint(bounds.center);
            Vector3 size = slot.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        AssemblySlot slotComponent = slotObject.AddComponent<AssemblySlot>();
        SerializedObject slotSO = new SerializedObject(slotComponent);
        slotSO.FindProperty("colorIndex").intValue = color;
        slotSO.FindProperty("slotCollider").objectReferenceValue = box;
        slotSO.ApplyModifiedPropertiesWithoutUndo();

        MissionOutlineBuilder.AttachDetached(slotObject, attachedPart, slot);
        return slotComponent;
    }

    // ═════════════════════════════════════════════════════════════
    //  3d: 대형 방화문 (신규 — 원본 문 · 패널 · 레버 모델에서 조립)
    // ═════════════════════════════════════════════════════════════

    /// <summary>원본 모델을 읽는다. 없으면 오류.</summary>
    private static GameObject LoadSourceModel(string path)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (model == null)
            Debug.LogError($"[LegacyMissionConverter] 원본 모델이 없습니다: {path}");

        return model;
    }

    /// <summary>메시 크기 BoxCollider 를 그 오브젝트에 붙인다 (메시가 없으면 기본 크기).</summary>
    private static BoxCollider AddMeshBoxCollider(GameObject target)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        BoxCollider box = target.AddComponent<BoxCollider>();

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            box.center = meshFilter.sharedMesh.bounds.center;
            box.size = meshFilter.sharedMesh.bounds.size;
        }

        return box;
    }

    /// <summary>방화문 레버 1인칭 모델: 빈 루트 + 레버 모델 사본(크기 0.8, 화면 오른쪽 아래, 세운 모습). 위치는 에디터 확인 후 조정.</summary>
    private static bool BuildFireDoorLeverFirstPerson(GameObject copy)
    {
        GameObject model = LoadSourceModel(FireDoorLeverModelPath);

        if (model == null)
            return false;

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localPosition = new Vector3(0.25f, -0.35f, 0.55f);
        view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one * 0.8f;
        return true;
    }

    /// <summary>
    /// 방화문 레버 아이템 (3d 명세 4장): 빈 루트(바닥 원점) + 레버 모델 사본을 눕혀서(로컬 X 축 90° — 바닥에 떨어진 모습).
    /// 모델 메시 크기 BoxCollider(레이어 6), 루트에 NetworkObject · ItemWorldView · FireDoorLever, 조준 외곽선(3c EA16).
    /// </summary>
    private static bool BuildFireDoorLever(GameObject copy, ItemData data)
    {
        GameObject model = LoadSourceModel(FireDoorLeverModelPath);

        if (model == null)
            return false;

        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();

        GameObject view = Object.Instantiate(model, copy.transform);
        view.name = "Model (모델)";
        view.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        MeshFilter meshFilter = view.GetComponentInChildren<MeshFilter>();
        Renderer renderer = view.GetComponentInChildren<Renderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null || renderer == null)
        {
            Debug.LogError("[LegacyMissionConverter] 방화문 레버: 모델에 메시가 없습니다.");
            return false;
        }

        // 눕히면 모델의 z(두께)가 높이가 된다 (X 축 90°: z → −y) → 두께의 위쪽 끝만큼 올려 바닥에 닿게
        Bounds bounds = meshFilter.sharedMesh.bounds;
        view.transform.localPosition = Vector3.up * bounds.max.z;

        meshFilter.gameObject.layer = InteractableLayer;
        BoxCollider box = AddMeshBoxCollider(meshFilter.gameObject);

        ItemWorldView worldView = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(worldView);
        SetObjectArray(viewSO.FindProperty("renderers"), renderer);
        SetObjectArray(viewSO.FindProperty("colliders"), box);
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        FireDoorLever lever = copy.AddComponent<FireDoorLever>();
        SerializedObject so = new SerializedObject(lever);
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 조준하면 외곽선 (미션 아이템 공통 — 3c EA16)
        MissionOutlineBuilder.Attach(copy, view);

        Debug.Log($"[LegacyMissionConverter] 방화문 레버: 아이템 번호 {FireDoorLeverItemId}, 크기 {bounds.size}");
        return true;
    }

    /// <summary>
    /// 방화문 (3d 명세 4장): 문 복사본(루트)에 문짝 충돌 · 기둥 충돌 · 양쪽 패널(중첩 NetworkObject · 받침 벽 · 화면 · 꽂힌 레버 · 떨어질 자리) · 장치 · 연출.
    /// 정면 맞춤은 하지 않는다 — 모델 앞이 +Z (명세 1장).
    /// (11a BC18) 공통 막는 몸체(문틀 전체 상자)는 출입구를 막아 쓰지 않고, 문짝 · 기둥 충돌을 가로 0.35m 넓히고 양쪽 패널 묶음에만 막는 몸체를 붙인다.
    /// </summary>
    private static bool BuildFireDoor(GameObject copy, NetworkObject leverPrefab)
    {
        Transform root = copy.transform;
        Transform gate = FindDeep(root, "Fire_door_Gate");
        Transform upper = FindDeep(root, "Fire_door_Upper");
        Transform lower = FindDeep(root, "Fire_door_Lower");

        // 문틀 노드는 FBX 의 최상위 노드(문짝 둘의 부모)라 Unity 가 프리팹 루트로 쓰면서 이름을 파일 이름으로 바꾼다.
        // 복사본 이름도 FireDoor_Station 으로 바뀌므로 이름으로는 못 찾는다 → 메시가 달린 루트 자신이 문틀이다 (3d 테스트에서 발견)
        if (gate == null && root.GetComponent<MeshFilter>() != null)
            gate = root;

        if (gate == null || upper == null || lower == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 방화문: 모델 노드를 찾지 못했습니다 (문틀 {Found(gate)}, 위 문짝 {Found(upper)}, 아래 문짝 {Found(lower)}).");
            return false;
        }

        GameObject panelModel = LoadSourceModel(FireDoorPanelModelPath);
        GameObject leverModel = LoadSourceModel(FireDoorLeverModelPath);

        if (panelModel == null || leverModel == null)
            return false;

        // 문짝 충돌 (기본 레이어 — 다 열리면 FireDoorVisual 이 끈다, FD14)
        BoxCollider upperCollider = AddMeshBoxCollider(upper.gameObject);
        BoxCollider lowerCollider = AddMeshBoxCollider(lower.gameObject);

        // 11a BC18 (테스트 후 추가): 문짝 충돌을 가로로 0.35m 넓힌다 — 닫혀 있을 때 얼굴을 대도 안이 안 보이게.
        // 같은 콜라이더라 다 열리면 지금처럼 꺼진다 (출입구 통과 유지)
        MissionBodyBuilder.PadHorizontal(upperCollider, root);
        MissionBodyBuilder.PadHorizontal(lowerCollider, root);

        // 문틀은 양쪽 기둥만 충돌 (문틀 전체 상자면 출입구가 막힌다). 복사본은 원점 · 회전 0 이라 월드 경계를 루트 기준으로 쓴다
        Bounds gateBounds = gate.GetComponent<Renderer>().bounds;
        Bounds doorBounds = lower.GetComponent<Renderer>().bounds;
        Vector3 gateCenter = root.InverseTransformPoint(gateBounds.center);
        float postWidth = Mathf.Max(0.05f, (gateBounds.size.x - doorBounds.size.x) * 0.5f);

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject post = new GameObject(side < 0 ? "Post L (기둥 충돌)" : "Post R (기둥 충돌)");
            post.transform.SetParent(root, false);
            post.transform.localPosition = new Vector3(gateCenter.x + side * (gateBounds.extents.x - postWidth * 0.5f), gateCenter.y, gateCenter.z);
            BoxCollider postCollider = post.AddComponent<BoxCollider>();
            postCollider.size = new Vector3(postWidth, gateBounds.size.y, gateBounds.size.z);

            // 11a BC18: 기둥도 가로로 0.35m 넓힌다 (출입구 폭은 약 0.7m 줄어도 통과에 충분)
            MissionBodyBuilder.PadHorizontal(postCollider, root);
        }

        float front = root.InverseTransformPoint(gateBounds.center + Vector3.forward * gateBounds.extents.z).z;

        if (copy.GetComponent<NetworkObject>() == null)
            copy.AddComponent<NetworkObject>();

        FireDoorStation station = copy.AddComponent<FireDoorStation>();
        FireDoorVisual visual = copy.AddComponent<FireDoorVisual>();

        Material wallMaterial = GetOrCreateColorMaterial("FireDoor_Wall", FireDoorWallColor);
        Material screenMaterial = GetOrCreateColorMaterial("LifeSupport_Screen", Color.black);
        FireDoorPanel[] panels = new FireDoorPanel[FireDoorRules.PanelCount];
        List<Collider> aimColliders = new List<Collider>();

        for (int i = 0; i < panels.Length; i++)
        {
            float x = (i == 0 ? -1f : 1f) * FireDoorPanelOffsetX;
            GameObject panelRoot = new GameObject(i == 0 ? "Panel L (패널)" : "Panel R (패널)");
            panelRoot.transform.SetParent(root, false);
            panelRoot.transform.localPosition = new Vector3(x, 0f, front);
            panelRoot.AddComponent<NetworkObject>();   // 조준 주소 (FD10)

            // 받침 벽: 바닥부터, 패널 뒤 (테스트용 자리 표시 — 실제 맵은 벽, FD16)
            CreateShape(PrimitiveType.Cube, "Wall (받침 벽)", panelRoot.transform, new Vector3(0f, FireDoorWallSize.y * 0.5f, -FireDoorWallSize.z * 0.5f),
                FireDoorWallSize, wallMaterial, true);

            // 패널 모델 (원점 = 바닥 가운데, 앞 = +Z)
            GameObject panelView = Object.Instantiate(panelModel, panelRoot.transform);
            panelView.name = "Panel Model (패널)";
            panelView.transform.localPosition = new Vector3(0f, FireDoorPanelHeight, 0f);
            panelView.transform.localRotation = Quaternion.identity;

            MeshFilter panelMesh = panelView.GetComponentInChildren<MeshFilter>();

            if (panelMesh == null || panelMesh.sharedMesh == null)
            {
                Debug.LogError("[LegacyMissionConverter] 방화문: 패널 모델에 메시가 없습니다.");
                return false;
            }

            panelMesh.gameObject.layer = InteractableLayer;
            aimColliders.Add(AddMeshBoxCollider(panelMesh.gameObject));
            Bounds panelBounds = panelMesh.sharedMesh.bounds;
            float panelFront = panelBounds.max.z;
            float panelTop = FireDoorPanelHeight + panelBounds.max.y;

            // 화면: 패널 위 받침 벽 앞 검은 판 + 3mm 앞 LCD
            float screenY = panelTop + 0.05f + FireDoorScreenSize.y * 0.5f;
            CreateShape(PrimitiveType.Cube, "Screen (화면)", panelRoot.transform, new Vector3(0f, screenY, 0.01f),
                new Vector3(FireDoorScreenSize.x, FireDoorScreenSize.y, 0.02f), screenMaterial, false);
            LcdDisplay display = CreateLcdText(panelRoot.transform, new Vector3(0f, screenY, 0.023f), Quaternion.LookRotation(Vector3.back, Vector3.up),
                FireDoorScreenSize * 0.9f, FireDoorStation.LongestScreenText, FireDoorStation.LockedText);

            // 꽂힌 레버: 회전축 오브젝트(버튼 · 콜라이더 · 외곽선) + 레버 모델 사본(세운 모습), 평소 숨김
            GameObject pivot = new GameObject("Mounted Lever (꽂힌 레버)");
            pivot.transform.SetParent(panelRoot.transform, false);
            pivot.transform.localPosition = new Vector3(0f, FireDoorPanelHeight + FireDoorLeverPivotHeight, panelFront + 0.03f);
            pivot.layer = InteractableLayer;

            GameObject leverView = Object.Instantiate(leverModel, pivot.transform);
            leverView.name = "Lever Model (레버)";
            leverView.transform.localPosition = Vector3.zero;
            leverView.transform.localRotation = Quaternion.identity;

            MeshFilter leverMesh = leverView.GetComponentInChildren<MeshFilter>();
            BoxCollider leverCollider = pivot.AddComponent<BoxCollider>();

            if (leverMesh != null && leverMesh.sharedMesh != null)
            {
                leverCollider.center = leverMesh.sharedMesh.bounds.center;
                leverCollider.size = leverMesh.sharedMesh.bounds.size;
            }

            aimColliders.Add(leverCollider);
            AddStationButton(pivot, station, i);
            MissionOutlineBuilder.Attach(pivot, leverView);

            // 떨어질 자리: 패널 앞 1m 바닥 (FD5)
            GameObject landing = new GameObject("Landing (떨어질 자리)");
            landing.transform.SetParent(panelRoot.transform, false);
            landing.transform.localPosition = new Vector3(0f, 0f, panelFront + FireDoorLandingDistance);

            FireDoorPanel panel = panelRoot.AddComponent<FireDoorPanel>();
            SerializedObject panelSO = new SerializedObject(panel);
            panelSO.FindProperty("station").objectReferenceValue = station;
            panelSO.FindProperty("index").intValue = i;
            panelSO.FindProperty("display").objectReferenceValue = display;
            panelSO.FindProperty("mountedLever").objectReferenceValue = pivot.transform;
            panelSO.FindProperty("landing").objectReferenceValue = landing.transform;
            panelSO.ApplyModifiedPropertiesWithoutUndo();

            // 패널 외곽선 (패널 메시) — 꽂힌 레버는 자기 외곽선이 더 가깝다
            MissionOutlineBuilder.Attach(panelRoot, panelView);

            pivot.SetActive(false);
            panels[i] = panel;
        }

        // 11a BC18: 양쪽 레버 패널 묶음(패널 모델 · 받침 벽 · 화면)에 막는 몸체 — 패널 모델은 조준 콜라이더뿐이라 뚫렸다.
        // 꽂힌 레버 · 외곽선은 꺼져 있어 범위에서 빠지고, 화면 글자도 빠진다. 문틀 전체는 출입구까지 막으므로 쓰지 않는다
        int panelBoxes = 0;

        foreach (FireDoorPanel panel in panels)
            panelBoxes += MissionBodyBuilder.Attach(panel.gameObject);

        SerializedObject visualSO = new SerializedObject(visual);
        visualSO.FindProperty("upperDoor").objectReferenceValue = upper;
        visualSO.FindProperty("lowerDoor").objectReferenceValue = lower;
        visualSO.FindProperty("upperTravel").floatValue = upper.GetComponent<Renderer>().bounds.size.y;
        visualSO.FindProperty("lowerTravel").floatValue = doorBounds.size.y;
        SetObjectArray(visualSO.FindProperty("doorColliders"), upperCollider, lowerCollider);
        visualSO.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject so = ConfigureStation(station, MissionEventType.FireDoorOpened, CompletionPolicy.Lock, FireDoorInteractRange);
        so.FindProperty("leverPrefab").objectReferenceValue = leverPrefab;
        SetObjectArray(so.FindProperty("panels"), panels);
        so.FindProperty("visual").objectReferenceValue = visual;

        // 패널 · 꽂힌 레버 콜라이더 → 이 미션이 없는 사람(범인)과 완료 뒤에는 공통 틀이 꺼서 조준 · 외곽선이 나오지 않는다
        foreach (Collider aimCollider in aimColliders)
            AddCollider(so, aimCollider);

        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 방화문: 문짝 위 · 아래, 패널 2개 (x ±{FireDoorPanelOffsetX}m, 앞면 z {front:0.00}), 기둥 충돌 2 (폭 {postWidth:0.00}m), 레버 프리팹 {leverPrefab.name}, " +
                  $"문짝 · 기둥 충돌 가로 {BodyBoxRules.CameraClearance}m 넓힘 · 패널 몸체 상자 {panelBoxes}개 ({MissionBodyBuilder.Describe(panels[0].gameObject)})");
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  13a: 배관 누수 차단 · 테이프 (15 — 받은 배관 모델 4종 · 테이프 조각 5종 · 롤 모델)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 테이프 롤 모델 (15 NM18 · NM19 — 받은 모델 Tape.fbx): 원본 모델을 parent 아래에 그대로 복사한다 (모델 파일 안의 1.744배 크기 포함 — 지름 약 24cm).
    /// 콜라이더가 있으면 지운다. 모델 기준점은 롤 가운데라, 바닥에 놓을 때는 부르는 쪽이 메시 범위로 올린다.
    /// </summary>
    private static GameObject BuildTapeModel(Transform parent)
    {
        GameObject source = LoadSourceModel(TapeModelPath);

        if (source == null)
            return null;

        GameObject model = Object.Instantiate(source, parent);
        model.name = "Tape Roll (테이프 롤 모델)";

        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        return model;
    }

    /// <summary>
    /// 테이프 1인칭 모델 (15 NM19): 빈 루트 + `Model (모델)` 아래 받은 롤 0.5배 (지름 12cm).
    /// 루트에 ItemWorldView(그리는 대상 = 모델, 충돌 없음)를 붙여 팀원 1인칭 팔이 손 뼈에 붙이게 한다 — 산소통 · 부품 1인칭과 같은 구성 (15 명세 1-5:
    /// 13a 에서는 이 칸이 없어 손이 아니라 팔 루트에 놓였다). 손 기준 위치 · 회전은 0 에서 시작해 Play 중 화면을 보며 맞추고,
    /// 맞춘 값은 프리팹에 남기면 변환 도구가 지킨다 (11a BC13 · BC14 — 기존 값 우선, 다시 만들 때 모델 위치 · 크기도 지킴).
    /// </summary>
    private static bool BuildTapeFirstPerson(GameObject copy)
    {
        GameObject view = new GameObject("Model (모델)");
        view.transform.SetParent(copy.transform, false);
        view.transform.localScale = Vector3.one * TapeFirstPersonScale;

        if (BuildTapeModel(view.transform) == null)
            return false;

        ItemWorldView worldView = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(worldView);
        SetObjectArray(viewSO.FindProperty("renderers"), copy.GetComponentsInChildren<Renderer>(true));
        viewSO.FindProperty("colliders").arraySize = 0;
        viewSO.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>
    /// 테이프 아이템 (13a PL20 · 15 NM18): 빈 루트(바닥 원점) + `Model (모델)` 아래 받은 롤 1배 — 메시 범위로 재서 바닥에 닿게 올린다.
    /// 루트에 NetworkObject · 조준 상자(모델 범위 + 1cm, 레이어 6) · ItemWorldView · TapeTool, 조준 외곽선(롤 메시 — 미션 아이템 공통 3c EA16).
    /// 3인칭 손 기준 위치 · 회전은 기존 프리팹 값을 지킨다 (11a BC13 — 공통 뒷부분).
    /// </summary>
    private static bool BuildTapeItem(GameObject copy, ItemData data)
    {
        copy.layer = InteractableLayer;
        copy.AddComponent<NetworkObject>();

        GameObject view = new GameObject("Model (모델)");
        view.transform.SetParent(copy.transform, false);
        GameObject roll = BuildTapeModel(view.transform);

        if (roll == null)
            return false;

        // 루트 기준 범위 — 복사본은 원점 · 회전 없음이라 월드 범위가 곧 루트 기준. 바닥(가장 낮은 점)이 0 에 오게 모델을 올린다
        Renderer[] renderers = copy.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;

        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

        float lift = -bounds.min.y;
        view.transform.localPosition = new Vector3(0f, lift, 0f);
        bounds.center += new Vector3(0f, lift, 0f);

        BoxCollider box = copy.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size + Vector3.one * (TapeColliderPadding * 2f);

        ItemWorldView worldView = copy.AddComponent<ItemWorldView>();
        SerializedObject viewSO = new SerializedObject(worldView);
        SetObjectArray(viewSO.FindProperty("renderers"), renderers);
        SetObjectArray(viewSO.FindProperty("colliders"), box);
        viewSO.ApplyModifiedPropertiesWithoutUndo();

        TapeTool tape = copy.AddComponent<TapeTool>();
        SerializedObject so = new SerializedObject(tape);
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        MissionOutlineBuilder.Attach(copy, roll);

        Debug.Log($"[LegacyMissionConverter] 테이프: 아이템 번호 {TapeItemId}, 받은 롤 모델 {bounds.size.x:0.00} × {bounds.size.y:0.00} × {bounds.size.z:0.00}m (바닥 맞춤 {lift:0.000}m 올림), 1인칭 {TapeFirstPersonScale}배");
        return true;
    }

    /// <summary>
    /// 배관 (13a 명세 PL13 · PL18 · PL21, 15 명세 NM2 · NM6 · NM12 ~ NM15): 빈 루트 + 받은 배관 모델 4개(0번 구멍 없음 ~ 3번 구멍 3개 — 앞 +Z, 바닥이 원점)
    /// + NetworkObject + PipeLeakStation(TG012 · 잠금 · 범위 3 · F 2초).
    /// 구멍 6자리마다 `Leak Spot n` 을 그 자리가 속한 모델 아래 관 표면에 둔다 (오브젝트 앞 +Z = 바깥): 조준 상자(조준 레이어) + StationHoldPart(번호)
    /// + 조준 고리 외곽선(NM14) + 조준 문구, 자식 가스 연기 · 테이프 조각(NM3 · NM13). 저장은 0번 모델만 켠 채 — 배정이 끝나면 새는 곳 비트 수 번호의 모델이 켜진다 (PipeLeakStation).
    /// 구멍 검사 (NM12): 자리마다 그 모델 메시에 자리 6cm 안 · 관 표면보다 안쪽인 점이 있는지 — 없으면 경고 (모델 담당이 구멍을 옮겼을 때 알게).
    /// 몸체는 공통 뒷부분(FinishPrefab)이 조각 상자로 붙인다 — 켜진 0번 모델만 들어간다 (꺼 둔 1 ~ 3번은 빠지지만 겉모양은 넷이 같다).
    /// [크기] 0번 모델 높이를 재서 FBX 높이(2.994m)와 다르면 자리 · 반지름을 그 비율로 맞춘다 (가져오기 단위가 바뀌어도 표면에 붙게).
    /// </summary>
    private static bool BuildPipeLeak(GameObject copy)
    {
        GameObject[] models = new GameObject[PipeModelPaths.Length];
        MeshFilter[][] modelFilters = new MeshFilter[PipeModelPaths.Length][];

        for (int m = 0; m < PipeModelPaths.Length; m++)
        {
            GameObject source = LoadSourceModel(PipeModelPaths[m]);

            if (source == null)
                return false;

            GameObject model = Object.Instantiate(source, copy.transform);
            model.name = m == 0 ? "Pipe 0 (구멍 없음)" : $"Pipe {m} (구멍 {m})";

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            models[m] = model;
            // 구멍 검사용 — 자리 · 조각을 붙이기 전의 모델 메시만 (조각 안쪽 면이 구멍으로 세어지지 않게)
            modelFilters[m] = model.GetComponentsInChildren<MeshFilter>(true);
        }

        Renderer[] modelRenderers = models[0].GetComponentsInChildren<Renderer>();

        if (modelRenderers.Length == 0)
        {
            Debug.LogError("[LegacyMissionConverter] 배관: 0번 모델에 렌더러가 없습니다 (Pipe_leak_Hole_0.fbx 가 바뀌었는지 확인).");
            return false;
        }

        // 루트 기준 모델 높이 — 복사본은 원점에 있으므로 월드 범위를 루트 기준으로 옮겨 잰다
        Bounds world = modelRenderers[0].bounds;

        foreach (Renderer renderer in modelRenderers)
            world.Encapsulate(renderer.bounds);

        float height = copy.transform.InverseTransformVector(Vector3.up * world.size.y).magnitude;
        float scale = height > 0.01f ? height / PipeModelHeight : 1f;

        if (Mathf.Abs(scale - 1f) > 0.05f)
            Debug.LogWarning($"[LegacyMissionConverter] 배관: 가져온 모델 높이 {height:0.00}m 가 FBX 분석값 {PipeModelHeight}m 와 달라 자리를 {scale:0.00}배로 맞춥니다.");

        GameObject[] tapeSources = new GameObject[PipeTapePaths.Length];

        for (int t = 0; t < PipeTapePaths.Length; t++)
        {
            tapeSources[t] = LoadSourceModel(PipeTapePaths[t]);

            if (tapeSources[t] == null)
                return false;
        }

        copy.AddComponent<NetworkObject>();
        PipeLeakStation station = copy.AddComponent<PipeLeakStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.PipeLeakSealed, CompletionPolicy.Lock, PipeInteractRange);
        so.FindProperty("holdDuration").floatValue = PipeHoldSeconds;

        Material gasMaterial = GetOrCreatePipeGasMaterial();
        Mesh ringMesh = GetOrCreatePipeRingMesh();

        SerializedProperty spotList = so.FindProperty("spots");
        spotList.arraySize = PipeLeakSpots.Length;
        System.Text.StringBuilder spotLog = new System.Text.StringBuilder();
        int holeWarnings = 0;

        for (int i = 0; i < PipeLeakSpots.Length; i++)
        {
            int modelIndex = PipeLeakRules.ModelOf(i);
            float angle = PipeLeakSpots[i].y * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 up = Vector3.up * PipeLeakSpots[i].x;

            // 구멍 검사 (NM12): 관 표면의 구멍 가운데 둘레에 표면보다 안쪽 점이 있는가
            int holeVertices = CountHoleVertices(copy.transform, modelFilters[modelIndex], (outward * PipeHoleSurfaceRadius + up) * scale,
                PipeHoleCheckDistance * scale, PipeHoleInnerRadius * scale);

            if (holeVertices == 0)
            {
                holeWarnings++;
                Debug.LogWarning($"[LegacyMissionConverter] 배관: 자리 {i + 1} ({PipeLeakSpots[i].x:0.000}m · {PipeLeakSpots[i].y:0.0}°) 둘레 {PipeHoleCheckDistance}m 안에 {models[modelIndex].name} 의 구멍(표면보다 안쪽 점)이 없습니다 — 모델의 구멍이 옮겨졌는지 확인하세요 (15 명세 1-2).");
            }

            GameObject spot = new GameObject($"Leak Spot {i + 1} (새는 곳 {i + 1})");
            spot.layer = InteractableLayer;
            spot.transform.SetParent(models[modelIndex].transform, false);
            spot.transform.SetPositionAndRotation(copy.transform.TransformPoint((outward * PipeSurfaceRadius + up) * scale),
                copy.transform.rotation * Quaternion.LookRotation(outward, Vector3.up));

            // 조준 상자: 표면에서 바깥쪽으로 4cm · 안쪽으로 2cm. 새는 중 · 내 미션일 때만 켠다 (PipeLeakStation)
            BoxCollider target = spot.AddComponent<BoxCollider>();
            target.center = new Vector3(0f, 0f, PipeTargetSize.z * 0.5f - 0.02f);
            target.size = PipeTargetSize;
            target.enabled = false;

            StationHoldPart part = spot.AddComponent<StationHoldPart>();
            SerializedObject partSO = new SerializedObject(part);
            partSO.FindProperty("station").objectReferenceValue = station;
            partSO.FindProperty("partIndex").intValue = i;
            partSO.ApplyModifiedPropertiesWithoutUndo();

            ParticleSystem gas = BuildPipeGas(spot.transform, gasMaterial);

            int tapeIndex = PipeSpotTapes[i];
            Transform tape = BuildPipeTape(spot.transform, tapeSources[tapeIndex], $"V{tapeIndex + 1}", scale, out float tapeAngle, out float tapeHeight);
            spotLog.Append($"{(i > 0 ? " · " : string.Empty)}{i + 1}={models[modelIndex].name.Substring(0, 6)} V{tapeIndex + 1}({tapeAngle:0.0}° {tapeHeight:+0.000;-0.000}m, 구멍 점 {holeVertices})");

            // 조준 고리 (NM14): 렌더러 없는 고리 메시 — 외곽선만 켜진다. 모델 구멍은 배관 메시의 일부라 그 부분만 외곽선을 낼 수 없어서
            GameObject ring = new GameObject("Aim Ring (조준 고리 — 외곽선만)");
            ring.layer = InteractableLayer;
            ring.transform.SetParent(spot.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0f, PipeRingOffset * scale);
            ring.transform.localScale = Vector3.one * scale;
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;

            // 조준 외곽선(고리) + 문구 (PromptAndHighlight — 문구는 PipeLeakStation 이 내 손 상태로 바꾼다)
            MissionOutlineBuilder.Attach(spot, ring);
            TextMeshPro prompt = CreateFittedText(spot.transform, "Prompt (조준 문구)", new Vector3(0f, 0.11f, 0.08f), Quaternion.identity,
                new Vector2(0.5f, 0.07f), PipeLeakStation.NeedTapeText, PipeLeakStation.NeedTapeText, KoreanFontPath, Color.white);
            prompt.gameObject.SetActive(false);

            SerializedObject promptSO = new SerializedObject(spot.GetComponent<MissionPrompt>());
            promptSO.FindProperty("mode").intValue = (int)MissionPromptMode.PromptAndHighlight;
            promptSO.FindProperty("promptObject").objectReferenceValue = prompt.gameObject;
            promptSO.ApplyModifiedPropertiesWithoutUndo();

            SerializedProperty element = spotList.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("Target").objectReferenceValue = target;
            element.FindPropertyRelative("Gas").objectReferenceValue = gas;
            element.FindPropertyRelative("Tape").objectReferenceValue = tape;
            element.FindPropertyRelative("Prompt").objectReferenceValue = prompt;
        }

        SetObjectArray(so.FindProperty("pipeModels"), models);

        // 「수리 완료」 (NM15): 구멍 위 · 위 링 아래, 배관 앞(링 0.30m 보다 앞), 보는 사람 쪽을 향하는 글자
        TextMeshPro done = CreateFittedText(copy.transform, "Done Label (수리 완료)", new Vector3(0f, PipeDoneLabelHeight * scale, 0.45f * scale), Quaternion.identity,
            new Vector2(0.6f, 0.16f), PipeLeakStation.DoneText, PipeLeakStation.DoneText, KoreanFontPath, PipeDoneColor);
        done.gameObject.AddComponent<PromptBillboard>();
        done.gameObject.SetActive(false);
        so.FindProperty("doneLabel").objectReferenceValue = done.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 저장은 0번(구멍 없음)만 켠 채 — 몸체도 0번으로 잰다
        for (int m = 1; m < models.Length; m++)
            models[m].SetActive(false);

        Debug.Log($"[LegacyMissionConverter] 배관: 모델 {models.Length}개 (0번 높이 {height:0.00}m · 비율 {scale:0.00}), 구멍 {PipeLeakSpots.Length}자리 — {spotLog}, " +
                  $"구멍 검사 경고 {holeWarnings}, F {PipeHoldSeconds}초, 범위 {PipeInteractRange}m, 「수리 완료」 {PipeDoneLabelHeight}m");
        return true;
    }

    /// <summary>구멍 검사 (NM12): 모델 메시 점 중 center(루트 기준)에서 distance 안 · 관 축(루트 원점의 세로선)에서 innerRadius 보다 가까운 점 수.</summary>
    private static int CountHoleVertices(Transform root, MeshFilter[] filters, Vector3 center, float distance, float innerRadius)
    {
        int count = 0;

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));

                if ((p - center).sqrMagnitude <= distance * distance && new Vector2(p.x, p.z).magnitude < innerRadius)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 테이프 조각 (NM3 · NM4 · NM13): 자리(관 표면점) 아래 `Tape (테이프 조각 Vn)` 기준점 — 진행만큼 커진다 (PipeLeakStation) →
    /// `Tape Pivot (관 축)` → 받은 조각 모델 (모델 파일의 회전 포함). 조각의 바깥 면(관 축에서 0.236m 보다 먼 면) 넓이 가운데를 메시에서 재서,
    /// 관 축으로 (−그 각도) 돌려 자리 앞(+Z = 구멍 쪽)에 오게 하고 그 높이만큼 내려 구멍 높이에 맞춘다. 처음에는 꺼 둔다.
    /// </summary>
    private static Transform BuildPipeTape(Transform spot, GameObject source, string label, float scale, out float centerAngle, out float centerHeight)
    {
        Transform anchor = new GameObject($"Tape (테이프 조각 {label})").transform;
        anchor.SetParent(spot, false);

        Transform pivot = new GameObject("Tape Pivot (관 축)").transform;
        pivot.SetParent(anchor, false);

        GameObject model = Object.Instantiate(source, pivot);
        model.name = $"Tape Model ({label})";

        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        // 넓이 가운데 — pivot 기준 (아직 위치 · 회전 없음 → 관 축이 원점이 되는 조각 자체 좌표)
        Vector3 sum = Vector3.zero;
        float area = 0f;

        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;

            if (mesh == null)
                continue;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int k = 0; k + 2 < triangles.Length; k += 3)
            {
                Vector3 a = pivot.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[k]]));
                Vector3 b = pivot.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[k + 1]]));
                Vector3 c = pivot.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[k + 2]]));
                Vector3 middle = (a + b + c) / 3f;

                if (new Vector2(middle.x, middle.z).magnitude <= PipeTapeOuterRadius * scale)
                    continue;

                float piece = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                sum += middle * piece;
                area += piece;
            }
        }

        Vector3 centroid = area > 0f ? sum / area : Vector3.forward * (PipeSurfaceRadius * scale);
        centerAngle = Mathf.Atan2(centroid.x, centroid.z) * Mathf.Rad2Deg;
        centerHeight = centroid.y;

        // 관 축은 자리(표면점)에서 안쪽(−Z)으로 표면 반지름만큼. Y 축으로 −각도 돌리면 넓이 가운데가 정면(+Z = 구멍 쪽)에 온다
        pivot.localPosition = new Vector3(0f, -centerHeight, -PipeSurfaceRadius * scale);
        pivot.localRotation = Quaternion.Euler(0f, -centerAngle, 0f);
        anchor.gameObject.SetActive(false);
        return anchor;
    }

    /// <summary>
    /// 조준 고리 메시 (NM14 — MissionRedesign_Draft/Meshes/PipeLeakRing.asset): 자리 앞(+Z)을 보는 XY 평면의 납작한 양면 고리
    /// (안 반지름 0.045 · 바깥 0.055m, 32칸). 렌더러 없이 외곽선만 만든다 — 외곽선 재질은 뒷면만 그리므로(Cull Front) 양면이어야 보인다.
    /// 앞 · 뒤 면은 점을 따로 두어 법선이 서로 지워지지 않게 한다. 이미 있으면 그대로 (다시 만들어도 같은 모양 — 무작위 없음).
    /// </summary>
    private static Mesh GetOrCreatePipeRingMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(PipeRingMeshPath);

        if (existing != null)
            return existing;

        EnsureFolder(DraftMeshFolder);

        int n = PipeRingSegments;
        Vector3[] vertices = new Vector3[n * 4];
        Vector3[] normals = new Vector3[n * 4];
        int[] triangles = new int[n * 12];

        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.PI * 2f / n;
            Vector3 direction = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

            // 0 · 1 = 앞면(+Z) 안 · 바깥, 2 · 3 = 뒷면(−Z) 안 · 바깥
            vertices[k * 4] = vertices[k * 4 + 2] = direction * PipeRingInnerRadius;
            vertices[k * 4 + 1] = vertices[k * 4 + 3] = direction * PipeRingOuterRadius;
            normals[k * 4] = normals[k * 4 + 1] = Vector3.forward;
            normals[k * 4 + 2] = normals[k * 4 + 3] = Vector3.back;

            int next = (k + 1) % n * 4;
            int t = k * 12;

            // 앞면: +Z 쪽에서 보면 시계 방향 (Unity 앞면)
            triangles[t] = k * 4;
            triangles[t + 1] = k * 4 + 1;
            triangles[t + 2] = next + 1;
            triangles[t + 3] = k * 4;
            triangles[t + 4] = next + 1;
            triangles[t + 5] = next;

            // 뒷면: 감는 방향을 뒤집음
            triangles[t + 6] = k * 4 + 2;
            triangles[t + 7] = next + 3;
            triangles[t + 8] = k * 4 + 3;
            triangles[t + 9] = k * 4 + 2;
            triangles[t + 10] = next + 2;
            triangles[t + 11] = next + 3;
        }

        Mesh mesh = new Mesh { name = "PipeLeakRing" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, PipeRingMeshPath);
        return mesh;
    }

    /// <summary>가스 연기 (PL17 — 임시): 바깥(+Z)으로 퍼지며 커지고 옅어지는 회색 부드러운 원. 처음에는 멈춰 있다 (PipeLeakStation 이 켠다).</summary>
    private static ParticleSystem BuildPipeGas(Transform spot, Material material)
    {
        GameObject gasObject = new GameObject("Gas (가스 연기)");
        gasObject.transform.SetParent(spot, false);
        gasObject.transform.localPosition = new Vector3(0f, 0f, 0.01f);

        ParticleSystem gas = gasObject.AddComponent<ParticleSystem>();
        gas.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = gas.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = new Color(0.9f, 0.92f, 0.95f, 0.55f);
        main.gravityModifier = -0.03f;   // 살짝 떠오르게
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = gas.emission;
        emission.rateOverTime = 20f;

        // 원뿔은 오브젝트 앞(+Z)으로 뿜는다 — 자리 오브젝트의 앞 = 관 바깥
        ParticleSystem.ShapeModule shape = gas.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.01f;

        ParticleSystem.SizeOverLifetimeModule size = gas.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));

        // 나오자마자 진해졌다가 끝으로 갈수록 투명
        ParticleSystem.ColorOverLifetimeModule color = gas.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer renderer = gasObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return gas;
    }

    /// <summary>
    /// 가스 연기 재질 (우리 폴더 MissionRedesign_Draft/Materials): 코드로 만든 부드러운 원 그림 + URP 파티클(Unlit · 반투명).
    /// 인스펙터의 Surface Type = Transparent · Blending = Alpha 와 같은 값을 넣는다. URP 파티클 셰이더가 없으면 Sprites/Default.
    /// </summary>
    private static Material GetOrCreatePipeGasMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(PipeGasMaterialPath);

        if (material != null)
            return material;

        Texture2D puff = GetOrCreatePuffTexture();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        material = new Material(shader) { name = "PipeGas" };
        Color tint = new Color(0.92f, 0.93f, 0.95f, 1f);
        material.mainTexture = puff;
        material.color = tint;

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", puff);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", tint);

        ApplyTransparent(material);

        EnsureFolder(DraftMaterialFolder);
        AssetDatabase.CreateAsset(material, PipeGasMaterialPath);
        return material;
    }

    /// <summary>URP 재질을 투명(알파 섞기 · 깊이 안 씀)으로 바꾼다. URP 셰이더가 아니면(_Surface 없음) 그대로 — 배관 연기(13a) · 좌표 화면 그림(13b)이 같이 쓴다.</summary>
    private static void ApplyTransparent(Material material)
    {
        if (!material.HasProperty("_Surface"))
            return;

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    /// <summary>부드러운 원 그림 (64 × 64): 가운데 불투명 → 가장자리 투명 (부드러운 계단 곡선). 이미 있으면 그대로.</summary>
    private static Texture2D GetOrCreatePuffTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(PipeGasTexturePath);

        if (existing != null)
            return existing;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        }

        texture.Apply();
        EnsureFolder(DraftMaterialFolder);
        System.IO.File.WriteAllBytes(PipeGasTexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(PipeGasTexturePath);

        if (AssetImporter.GetAtPath(PipeGasTexturePath) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(PipeGasTexturePath);
    }

    // ═════════════════════════════════════════════════════════════
    //  13b: 좌표 설정
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 좌표 장치 (13b 명세 CO30 ~ CO32, 15 명세 NM7 ~ NM11): 받은 패널 모델 복사 (루트 = 몸체 메시 · 자식 초록 · 노랑 버튼, 앞 +Z, 바닥이 원점) + NetworkObject + CoordinateStation.
    /// 버튼은 모델 부품 그대로 — 보는 사람 기준 왼쪽 초록(모델 +X) · 오른쪽 노랑 (기획서 그림 · CO30 과 같은 약속이라 장치 스크립트는 그대로).
    /// 누르는 동안 버튼이 들어가게 버튼 부품을 장치에 넘긴다 (NM9). 화면 그림은 모델 검은 화면 1mm 앞에 화면 세로 비율(1.33배)로 (NM10).
    /// </summary>
    private static bool BuildCoordinate(GameObject copy)
    {
        Transform green = copy.transform.Find(CoordGreenButtonName);
        Transform yellow = copy.transform.Find(CoordYellowButtonName);

        if (green == null || yellow == null)
        {
            Debug.LogError($"[LegacyMissionConverter] 좌표 장치: 모델에 버튼 부품이 없습니다 ({CoordGreenButtonName} · {CoordYellowButtonName}) — Coordinate_setting_panel.fbx 가 바뀌었는지 확인.");
            return false;
        }

        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        copy.AddComponent<NetworkObject>();
        CoordinateStation station = copy.AddComponent<CoordinateStation>();
        SerializedObject so = ConfigureStation(station, MissionEventType.CoordinateSet, CompletionPolicy.Lock, CoordInteractRange);

        TextMeshPro greenPrompt = BuildCoordButton(so, station, copy.transform, green, CoordinateStation.GreenPart, "Green Button (초록 — X)");
        TextMeshPro yellowPrompt = BuildCoordButton(so, station, copy.transform, yellow, CoordinateStation.YellowPart, "Yellow Button (노랑 — Y)");

        // 화면 기준: 모델 검은 화면 1mm 앞. 로컬 −Z = 보는 사람 쪽(화면 밖) · +X = 보는 사람 오른쪽 (13b 와 같은 약속 — 코드 LCD 와 같은 LookRotation(안쪽, 위))
        Vector3 normal = CoordScreenNormal.normalized;
        Vector3 up = Vector3.Cross(normal, Vector3.right);   // 화면 위쪽 (뒤로 기운 방향)
        float screenScale = CoordModelScreenSize.y / CoordScreenSize.y;

        Transform content = new GameObject("Screen (화면)").transform;
        content.SetParent(copy.transform, false);
        content.localPosition = CoordScreenCenter + normal * CoordScreenGap;
        content.localRotation = Quaternion.LookRotation(-normal, up);
        content.localScale = Vector3.one * screenScale;

        CoordinateScreen screen = content.gameObject.AddComponent<CoordinateScreen>();
        BuildCoordScreen(screen, content);

        GameObject soundObject = new GameObject("Beep Sound (신호음 — 소리 칸은 비움)");
        soundObject.transform.SetParent(copy.transform, false);
        soundObject.transform.localPosition = CoordScreenCenter;
        AudioSource beep = soundObject.AddComponent<AudioSource>();
        beep.playOnAwake = false;
        beep.spatialBlend = 1f;
        beep.rolloffMode = AudioRolloffMode.Linear;
        beep.minDistance = CoordBeepMinDistance;
        beep.maxDistance = CoordBeepMaxDistance;

        so.FindProperty("screen").objectReferenceValue = screen;
        so.FindProperty("greenPrompt").objectReferenceValue = greenPrompt;
        so.FindProperty("yellowPrompt").objectReferenceValue = yellowPrompt;
        so.FindProperty("beepSource").objectReferenceValue = beep;
        so.FindProperty("greenButton").objectReferenceValue = green;
        so.FindProperty("yellowButton").objectReferenceValue = yellow;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[LegacyMissionConverter] 좌표 장치: 받은 모델 · 버튼 2개 (초록 x {green.localPosition.x + green.GetComponent<MeshFilter>().sharedMesh.bounds.center.x:0.00} · 윗면 방향 {green.localRotation * Vector3.up}), " +
                  $"화면 {CoordModelScreenSize.x} × {CoordModelScreenSize.y}m · 그림 {screenScale:0.00}배 · 탐색 영역 반지름 {CoordAreaRadius * screenScale:0.00}m, 범위 {CoordInteractRange}m, 신호음 칸 비움 (CO5)");
        return true;
    }

    /// <summary>
    /// 버튼 하나 (NM8): 받은 모델의 버튼 부품에 조준 상자(버튼 메시 범위 + 1cm, 조준 레이어) · StationHoldPart · 장치 콜라이더 목록 · 외곽선(버튼 자기 메시)
    /// · 조준 문구(PromptAndHighlight — 버튼 가운데 위). 문구 글자를 돌려준다.
    /// </summary>
    private static TextMeshPro BuildCoordButton(SerializedObject stationSO, CoordinateStation station, Transform root, Transform button, int part, string label)
    {
        GameObject cap = button.gameObject;
        cap.layer = InteractableLayer;

        MeshFilter filter = cap.GetComponent<MeshFilter>();
        Bounds local = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.15f);
        BoxCollider box = cap.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size + Vector3.one * (CoordButtonPadding * 2f);

        StationHoldPart holdPart = cap.AddComponent<StationHoldPart>();
        SerializedObject partSO = new SerializedObject(holdPart);
        partSO.FindProperty("station").objectReferenceValue = station;
        partSO.FindProperty("partIndex").intValue = part;
        partSO.ApplyModifiedPropertiesWithoutUndo();

        // 내 미션이 아니거나 잠기면 꺼지는 콜라이더 (공통 틀 — 연출)
        AddCollider(stationSO, box);

        MissionOutlineBuilder.Attach(cap, cap);
        string initial = part == CoordinateStation.GreenPart ? CoordinateStation.MoveXText : CoordinateStation.MoveYText;
        Vector3 above = root.InverseTransformPoint(button.TransformPoint(local.center)) + Vector3.up * CoordPromptHeight;
        TextMeshPro prompt = CreateFittedText(root, label + " Prompt (조준 문구)", above, Quaternion.identity,
            new Vector2(0.55f, 0.06f), CoordinateStation.LongestPromptText, initial, KoreanFontPath, Color.white);
        prompt.gameObject.SetActive(false);

        SerializedObject promptSO = new SerializedObject(cap.GetComponent<MissionPrompt>());
        promptSO.FindProperty("mode").intValue = (int)MissionPromptMode.PromptAndHighlight;
        promptSO.FindProperty("promptObject").objectReferenceValue = prompt.gameObject;
        promptSO.ApplyModifiedPropertiesWithoutUndo();
        return prompt;
    }

    /// <summary>
    /// 화면 그림 (13b CO31): 검은 판 앞으로 0.002m 씩 — 탐색 영역 · 십자선 → 목표 · 축 글자 · 막대 → 조작 원 채움 → 고리.
    /// 투명 재질의 그리는 순서는 renderQueue 로 고정한다 (투명은 거리로 정렬돼서 비스듬히 보면 앞뒤가 바뀔 수 있다).
    /// </summary>
    private static void BuildCoordScreen(CoordinateScreen screen, Transform content)
    {
        Texture2D ring = GetOrCreateShapeTexture(CoordRingTexturePath, RingAlpha);
        Texture2D disc = GetOrCreateShapeTexture(CoordDiscTexturePath, DiscAlpha);
        Texture2D targetTexture = GetOrCreateShapeTexture(CoordTargetTexturePath, TargetAlpha);

        Material areaMaterial = GetOrCreateScreenMaterial("CoordArea", ring, CoordAreaColor, 0);
        Material lineMaterial = GetOrCreateScreenMaterial("CoordLine", null, CoordLineColor, 0);
        Material targetMaterial = GetOrCreateScreenMaterial("CoordTarget", targetTexture, CoordTargetColor, 1);
        Material barMaterial = GetOrCreateScreenMaterial("CoordBar", null, Color.white, 1);
        Material fillMaterial = GetOrCreateScreenMaterial("CoordCursorFill", disc, Color.white, 2);
        Material cursorMaterial = GetOrCreateScreenMaterial("CoordCursorRing", ring, Color.white, 3);

        float z1 = -CoordLayerStep;
        float z2 = -CoordLayerStep * 2f;
        float z3 = -CoordLayerStep * 3f;
        float diameter = CoordAreaRadius * 2f;

        Transform drawing = new GameObject("Drawing (켜진 화면)").transform;
        drawing.SetParent(content, false);

        CreateShape(PrimitiveType.Quad, "Search Area (탐색 영역)", drawing, new Vector3(0f, 0f, z1), new Vector3(diameter, diameter, 1f), areaMaterial, false);
        CreateShape(PrimitiveType.Quad, "Cross X (십자선 가로)", drawing, new Vector3(0f, 0f, z1), new Vector3(diameter, 0.003f, 1f), lineMaterial, false);
        CreateShape(PrimitiveType.Quad, "Cross Y (십자선 세로)", drawing, new Vector3(0f, 0f, z1), new Vector3(0.003f, diameter, 1f), lineMaterial, false);
        GameObject target = CreateShape(PrimitiveType.Quad, "Target (목표)", drawing, new Vector3(0f, 0f, z2), new Vector3(CoordTargetSize, CoordTargetSize, 1f), targetMaterial, false);

        Transform cursor = new GameObject("Cursor (조작 원)").transform;
        cursor.SetParent(drawing, false);
        cursor.localPosition = new Vector3(0f, 0f, z3);
        float cursorDiameter = CoordCursorRadius * 2f;
        GameObject fill = CreateShape(PrimitiveType.Quad, "Fill (안 채움)", cursor, Vector3.zero, new Vector3(cursorDiameter * 0.86f, cursorDiameter * 0.86f, 1f), fillMaterial, false);
        GameObject cursorRing = CreateShape(PrimitiveType.Quad, "Ring (고리)", cursor, new Vector3(0f, 0f, -CoordLayerStep), new Vector3(cursorDiameter, cursorDiameter, 1f), cursorMaterial, false);

        // 축 글자: 보는 사람 왼쪽 X(초록) · 오른쪽 Y(노랑) — 그림과 같은 자리. 기호는 한국어 글꼴에 있는 ◀ ▶ ▲ ▼
        TextMeshPro xAxis = CreateFittedText(drawing, "X Axis (X 축)", new Vector3(-CoordAxisLabelX, 0.06f, z2), Quaternion.identity,
            new Vector2(0.2f, 0.06f), "◀ X ▶", "◀ X ▶", KoreanFontPath, CoordGreenColor);
        TextMeshPro yAxis = CreateFittedText(drawing, "Y Axis (Y 축)", new Vector3(CoordAxisLabelX, 0.03f, z2), Quaternion.identity,
            new Vector2(0.08f, 0.2f), "▲\nY\n▼", "▲\nY\n▼", KoreanFontPath, CoordYellowColor);

        GameObject greenBar = CreateShape(PrimitiveType.Quad, "Green Bar (초록 담당 막대)", drawing, new Vector3(-CoordAxisLabelX, CoordBarY, z2),
            new Vector3(CoordBarSize.x, CoordBarSize.y, 1f), barMaterial, false);
        GameObject yellowBar = CreateShape(PrimitiveType.Quad, "Yellow Bar (노랑 담당 막대)", drawing, new Vector3(CoordAxisLabelX, CoordBarY, z2),
            new Vector3(CoordBarSize.x, CoordBarSize.y, 1f), barMaterial, false);
        TextMeshPro greenLabel = CreateFittedText(drawing, "Green Bar Label (초록 담당 글자)", new Vector3(-CoordAxisLabelX, CoordBarY + 0.035f, z2), Quaternion.identity,
            new Vector2(0.22f, 0.032f), "X 비어 있음", "X 비어 있음", KoreanFontPath, CoordGreenColor);
        TextMeshPro yellowLabel = CreateFittedText(drawing, "Yellow Bar Label (노랑 담당 글자)", new Vector3(CoordAxisLabelX, CoordBarY + 0.035f, z2), Quaternion.identity,
            new Vector2(0.22f, 0.032f), "Y 비어 있음", "Y 비어 있음", KoreanFontPath, CoordYellowColor);

        drawing.gameObject.SetActive(false);

        TextMeshPro idle = CreateFittedText(content, "Idle (꺼짐 안내)", new Vector3(0f, 0f, z1), Quaternion.identity,
            new Vector2(0.7f, 0.06f), CoordinateScreen.IdleText, CoordinateScreen.IdleText, KoreanFontPath, CoordIdleColor);
        TextMeshPro done = CreateFittedText(content, "Done (설정 완료)", new Vector3(0f, CoordBarY, z3), Quaternion.identity,
            new Vector2(0.36f, 0.06f), CoordinateScreen.DoneText, CoordinateScreen.DoneText, KoreanFontPath, PipeDoneColor);
        done.gameObject.SetActive(false);

        // 맞았는데 한 색 담당이 비었을 때 안내 (CO38) — 「설정 완료」와 같은 자리, 노랑 글자
        TextMeshPro hint = CreateFittedText(content, "Hint (담당 안내)", new Vector3(0f, CoordBarY, z3), Quaternion.identity,
            new Vector2(0.36f, 0.04f), CoordinateScreen.NeedYellowText, CoordinateScreen.NeedYellowText, KoreanFontPath, CoordYellowColor);
        hint.gameObject.SetActive(false);

        SerializedObject screenSO = new SerializedObject(screen);
        screenSO.FindProperty("areaRadius").floatValue = CoordAreaRadius;
        screenSO.FindProperty("drawing").objectReferenceValue = drawing.gameObject;
        screenSO.FindProperty("cursor").objectReferenceValue = cursor;
        screenSO.FindProperty("cursorRing").objectReferenceValue = cursorRing.GetComponent<Renderer>();
        screenSO.FindProperty("cursorFill").objectReferenceValue = fill.GetComponent<Renderer>();
        screenSO.FindProperty("target").objectReferenceValue = target.transform;
        screenSO.FindProperty("xAxisLabel").objectReferenceValue = xAxis;
        screenSO.FindProperty("yAxisLabel").objectReferenceValue = yAxis;
        screenSO.FindProperty("greenBar").objectReferenceValue = greenBar.GetComponent<Renderer>();
        screenSO.FindProperty("yellowBar").objectReferenceValue = yellowBar.GetComponent<Renderer>();
        screenSO.FindProperty("greenBarLabel").objectReferenceValue = greenLabel;
        screenSO.FindProperty("yellowBarLabel").objectReferenceValue = yellowLabel;
        screenSO.FindProperty("idleLabel").objectReferenceValue = idle.gameObject;
        screenSO.FindProperty("doneLabel").objectReferenceValue = done.gameObject;
        screenSO.FindProperty("hintLabel").objectReferenceValue = hint;
        screenSO.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>화면 그림용 URP Unlit 투명 재질 (빛 · 그림자 영향 없음). 겹치는 순서 = 투명 줄 + queueOffset. 이미 있으면 그대로 (색은 장치가 판마다 따로 넣는다).</summary>
    private static Material GetOrCreateScreenMaterial(string fileName, Texture2D texture, Color color, int queueOffset)
    {
        string path = $"{DraftMaterialFolder}/{fileName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        material = new Material(shader) { name = fileName };
        material.color = color;

        if (texture != null)
        {
            material.mainTexture = texture;

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        ApplyTransparent(material);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + queueOffset;

        EnsureFolder(DraftMaterialFolder);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// 흰색 + 투명도 그림(128 × 128)을 그려 PNG 로 저장한다. alpha(x, y) 는 가운데 0 · 가장자리 ±1 좌표에서 0 또는 1.
    /// 한 픽셀을 3 × 3 으로 나눠 평균을 내서 가장자리 계단을 줄인다. 이미 있으면 그대로.
    /// </summary>
    private static Texture2D GetOrCreateShapeTexture(string path, System.Func<float, float, float> alpha)
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (existing != null)
            return existing;

        const int size = 128;
        const int samples = 3;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sum = 0f;

                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float u = (x + (sx + 0.5f) / samples) / size * 2f - 1f;
                        float v = (y + (sy + 0.5f) / samples) / size * 2f - 1f;
                        sum += alpha(u, v);
                    }
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, sum / (samples * samples)));
            }
        }

        texture.Apply();
        EnsureFolder(DraftMaterialFolder);
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);

        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 고리: 반지름 0.84 ~ 0.98 의 띠 (탐색 영역 · 조작 원이 같이 쓴다)
    private static float RingAlpha(float x, float y)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        return d >= 0.84f && d <= 0.98f ? 1f : 0f;
    }

    // 원판: 조작 원 안 채움
    private static float DiscAlpha(float x, float y)
    {
        return Mathf.Sqrt(x * x + y * y) <= 0.96f ? 1f : 0f;
    }

    // 목표: 네 갈래 별(|x|^⅔ + |y|^⅔ ≤ 0.5^⅔) + 네 모서리 ㄱ자 괄호 — 기획서 그림의 노란 표시
    private static float TargetAlpha(float x, float y)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        const float twoThirds = 2f / 3f;
        bool star = Mathf.Pow(ax, twoThirds) + Mathf.Pow(ay, twoThirds) <= Mathf.Pow(0.5f, twoThirds);
        bool bracket = (ax >= 0.8f && ax <= 0.92f && ay >= 0.6f && ay <= 0.92f) || (ay >= 0.8f && ay <= 0.92f && ax >= 0.6f && ax <= 0.92f);
        return star || bracket ? 1f : 0f;
    }

    // ═════════════════════════════════════════════════════════════
    //  원본 모델 조립 도우미 (압력 · 코드가 같이 쓴다)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 모델의 버튼 메시 하나를 누를 수 있는 부품으로 만든다: 조준 레이어 · 메시 크기 콜라이더 · StationButton · 스테이션 콜라이더 목록 · 외곽선.
    /// 눌림 = 버튼 아래쪽(−up)으로 메시 높이의 30% (0.08초). 압력 · 코드 버튼이 같이 쓴다.
    /// </summary>
    /// <returns>눌림 깊이(m) — 로그용</returns>
    private static float SetupPressButton(SerializedObject stationSO, MissionStation station, Transform button, int partIndex)
    {
        GameObject buttonObject = button.gameObject;
        buttonObject.layer = InteractableLayer;

        MeshFilter buttonMesh = buttonObject.GetComponent<MeshFilter>();
        BoxCollider buttonCollider = buttonObject.AddComponent<BoxCollider>();

        if (buttonMesh != null && buttonMesh.sharedMesh != null)
        {
            buttonCollider.center = buttonMesh.sharedMesh.bounds.center;
            buttonCollider.size = buttonMesh.sharedMesh.bounds.size;
        }

        StationButton stationButton = AddStationButton(buttonObject, station, partIndex);
        SerializedObject buttonSO = new SerializedObject(stationButton);
        buttonSO.FindProperty("buttonVisual").objectReferenceValue = button;

        // 눌림: 버튼의 아래쪽(-up) 으로 메시 높이의 30% (부모 로컬 단위)
        float buttonHeight = buttonMesh != null && buttonMesh.sharedMesh != null ? buttonMesh.sharedMesh.bounds.size.y * Mathf.Abs(button.lossyScale.y) : 0.02f;
        Vector3 pressWorld = -button.up * buttonHeight * 0.3f;
        buttonSO.FindProperty("pressOffset").vector3Value = button.parent != null ? button.parent.InverseTransformVector(pressWorld) : pressWorld;
        buttonSO.FindProperty("pressTime").floatValue = 0.08f;
        buttonSO.FindProperty("returnTime").floatValue = 0.08f;
        buttonSO.ApplyModifiedPropertiesWithoutUndo();

        AddCollider(stationSO, buttonCollider);
        MissionOutlineBuilder.Attach(buttonObject, buttonObject);
        return buttonHeight * 0.3f;
    }

    /// <summary>이름이 정확히 같은 자식(손자 포함)을 찾는다. 없으면 null.</summary>
    private static Transform FindDeep(Transform root, string exactName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t;
        }

        return null;
    }

    /// <summary>
    /// 모델을 루트 기준으로 수평 회전해 "앞쪽 부품이 있는 쪽"이 루트의 +Z(정면)를 보게 한다.
    /// [이유] 테스트 씬 · 맵 배치는 스테이션 루트의 +Z 를 플레이어 쪽으로 돌린다. 원본 모델의 앞뒤 방향은 모델마다 달라서 조립할 때 맞춘다.
    /// [방법] 앞쪽 부품 중심 − 모델 전체 중심(수평 성분) 방향을 +Z 로 돌리는 각도만큼, 루트의 직계 자식을 루트 원점 기준으로 돌린다
    ///  (루트 자체의 회전은 배치할 때 덮어써지므로 자식을 돌린다).
    /// </summary>
    /// <returns>돌린 각도(도). 방향을 알 수 없거나 이미 맞으면 0</returns>
    private static float FaceFront(GameObject root, Transform[] frontParts)
    {
        Renderer[] all = root.GetComponentsInChildren<Renderer>();

        if (all.Length == 0 || frontParts.Length == 0)
            return 0f;

        Bounds bounds = all[0].bounds;

        for (int i = 1; i < all.Length; i++)
            bounds.Encapsulate(all[i].bounds);

        Vector3 front = Vector3.zero;

        foreach (Transform part in frontParts)
            front += part.GetComponent<Renderer>().bounds.center;

        Vector3 direction = front / frontParts.Length - bounds.center;
        direction.y = 0f;

        if (direction.sqrMagnitude < 1e-6f)
            return 0f;

        float angle = Vector3.SignedAngle(direction, Vector3.forward, Vector3.up);

        if (Mathf.Abs(angle) < 1f)
            return 0f;

        foreach (Transform child in root.transform)
            child.RotateAround(root.transform.position, Vector3.up, angle);

        return angle;
    }

    /// <summary>
    /// 이름을 영문 · 숫자 토큰으로 나눠 찾는다: 토큰에 letter(a/b/c)가 정확히 있고, required 는 모두 포함(부분 일치), forbidden 은 하나도 없어야 한다.
    /// 원본 모델 노드 이름의 오타 · 공백(Pressure_button_A._nteraction, "Pressure _gauge_indicator_needle_A")을 견디기 위해서다.
    /// </summary>
    private static Transform FindByTokens(Transform root, string letter, string[] required, string[] forbidden, bool requireRenderer)
    {
        string letterToken = letter.ToLowerInvariant();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string[] tokens = System.Text.RegularExpressions.Regex.Split(t.name.ToLowerInvariant(), "[^a-z0-9]+");

            if (System.Array.IndexOf(tokens, letterToken) < 0)
                continue;

            if (requireRenderer && t.GetComponent<Renderer>() == null)
                continue;

            bool ok = true;

            foreach (string word in required)
            {
                if (!System.Array.Exists(tokens, token => token.Contains(word)))
                {
                    ok = false;
                    break;
                }
            }

            if (ok && forbidden != null)
            {
                foreach (string word in forbidden)
                {
                    if (System.Array.Exists(tokens, token => token.Contains(word)))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (ok)
                return t;
        }

        return null;
    }

    /// <summary>메시의 가장 얇은 축 (원판 모양 압력계의 앞면 방향 = 바늘 회전축). 로컬 단위 벡터.</summary>
    private static Vector3 ThinnestAxis(Transform meshObject)
    {
        MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return Vector3.forward;

        Vector3 size = Vector3.Scale(meshFilter.sharedMesh.bounds.size, meshObject.lossyScale);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        if (size.x <= size.y && size.x <= size.z)
            return Vector3.right;

        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    private static string Found(Transform t) => t != null ? t.name : "없음";

    // ═════════════════════════════════════════════════════════════
    //  부품 · 콜라이더 · 외곽선
    // ═════════════════════════════════════════════════════════════

    private static StationButton AddStationButton(GameObject target, MissionStation station, int partIndex)
    {
        StationButton button = target.AddComponent<StationButton>();
        SerializedObject so = new SerializedObject(button);
        so.FindProperty("station").objectReferenceValue = station;
        so.FindProperty("partIndex").intValue = partIndex;
        so.ApplyModifiedPropertiesWithoutUndo();
        return button;
    }

    /// <summary>interactionColliders 에 없으면 추가한다.</summary>
    private static void AddCollider(SerializedObject stationSO, Collider collider)
    {
        if (collider == null)
            return;

        SerializedProperty list = stationSO.FindProperty("interactionColliders");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == collider)
                return;
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = collider;
    }

    /// <summary>루트에 외곽선 오브젝트가 연결된 MissionPrompt 가 이미 있는가.</summary>
    private static bool HasHighlight(GameObject root)
    {
        MissionPrompt prompt = root.GetComponent<MissionPrompt>();

        if (prompt == null)
            return false;

        return new SerializedObject(prompt).FindProperty("highlightObject").objectReferenceValue != null;
    }

    /// <summary>조준 문구 오브젝트(MissionPrompt.promptObject)마다 PromptBillboard 를 붙인다 (이미 있으면 그대로).</summary>
    private static void AddPromptBillboards(GameObject root)
    {
        foreach (MissionPrompt prompt in root.GetComponentsInChildren<MissionPrompt>(true))
        {
            GameObject promptObject = new SerializedObject(prompt).FindProperty("promptObject").objectReferenceValue as GameObject;

            if (promptObject != null && promptObject.GetComponent<PromptBillboard>() == null)
                promptObject.AddComponent<PromptBillboard>();
        }
    }

    /// <summary>
    /// 부품 자신에게 메시가 있으면 그 메시로 부품 전용 외곽선을 붙인다 (조준한 부품만 켜짐, 2a P7).
    /// 자식 메시는 쓰지 않는다 — 전선처럼 자식에 숨겨진 메시가 있으면 엉뚱한 외곽선이 된다.
    /// 메시가 없으면 외곽선 없이 둔다 (빈 프롬프트 — 부모 스테이션 외곽선이 대신 켜지지 않게).
    /// </summary>
    private static void AttachPartOutline(GameObject part)
    {
        MeshFilter meshFilter = part.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 부품 '{part.name}' 에 메시가 없어 외곽선 없이 둡니다.");
            MissionOutlineBuilder.AttachEmpty(part);
            return;
        }

        MissionOutlineBuilder.Attach(part, part);
    }

    // ─── 값 옮기기 (옛 값이 비어 있으면 경고만 하고 새 컴포넌트의 기본값을 둔다) ───

    private static void CopyReference(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null || source.objectReferenceValue == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 비어 있습니다. 새 컴포넌트의 '{toName}' 을(를) 직접 연결하세요.");
            return;
        }

        to.FindProperty(toName).objectReferenceValue = source.objectReferenceValue;
    }

    private static void CopyFloat(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).floatValue = source.floatValue;
    }

    private static void CopyString(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).stringValue = source.stringValue;
    }

    private static void CopyVector3(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        to.FindProperty(toName).vector3Value = source.vector3Value;
    }

    private static void CopyInt(SerializedObject from, string fromName, SerializedObject to, string toName)
    {
        SerializedProperty source = from.FindProperty(fromName);

        if (source == null)
        {
            Debug.LogWarning($"[LegacyMissionConverter] 옛 값 '{fromName}' 이(가) 없어 기본값을 씁니다.");
            return;
        }

        // enum 도 intValue 로 옮긴다 (값이 같은 enum 끼리 — 예: WireMeshAxis → WireAxis)
        to.FindProperty(toName).intValue = source.intValue;
    }

    /// <summary>옛 interactionColliders 를 옮기고, 비어 있으면 fallbackRoot 아래 콜라이더 전부를 쓴다.</summary>
    private static void CopyColliders(SerializedObject from, SerializedObject to, GameObject fallbackRoot)
    {
        List<Object> colliders = new List<Object>();
        SerializedProperty oldList = from.FindProperty("interactionColliders");

        if (oldList != null && oldList.isArray)
        {
            for (int i = 0; i < oldList.arraySize; i++)
            {
                Object c = oldList.GetArrayElementAtIndex(i).objectReferenceValue;

                if (c != null)
                    colliders.Add(c);
            }
        }

        if (colliders.Count == 0)
        {
            foreach (Collider c in fallbackRoot.GetComponentsInChildren<Collider>(true))
                colliders.Add(c);
        }

        if (colliders.Count == 0)
            Debug.LogWarning("[LegacyMissionConverter] 상호작용 콜라이더가 없습니다. 조준할 수 없습니다.");

        SerializedProperty newList = to.FindProperty("interactionColliders");
        newList.ClearArray();

        for (int i = 0; i < colliders.Count; i++)
        {
            newList.InsertArrayElementAtIndex(i);
            newList.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
        }
    }

    /// <summary>스테이션의 첫 상호작용 콜라이더가 있는 오브젝트 (= 부품을 붙일 곳). 루트라면 스테이션에 가려지므로 null.</summary>
    private static GameObject FindPartObject(SerializedObject stationSO, GameObject root)
    {
        SerializedProperty colliders = stationSO.FindProperty("interactionColliders");

        if (colliders == null || colliders.arraySize == 0)
            return null;

        Collider first = colliders.GetArrayElementAtIndex(0).objectReferenceValue as Collider;

        if (first == null)
            return null;

        if (first.gameObject == root)
        {
            Debug.LogWarning("[LegacyMissionConverter] 버튼 콜라이더가 루트에 있어 부품이 스테이션에 가려집니다. 콜라이더를 버튼 오브젝트로 옮겨 주세요.");
            return null;
        }

        return first.gameObject;
    }

    /// <summary>
    /// 각 콜라이더를 조준했을 때 실제로 무엇이 잡히는지 PlayerTargetDetector 와 같은 규칙으로 확인한다:
    /// 콜라이더에서 위로 올라가며 "처음 만나는 ITargetable" 이 조준 대상이다.
    /// 그 대상이 클릭 · F 홀드 · 드래그를 받지 못하고 놓는 곳도 아니면, 조준은 되는데 아무 입력도 안 먹는 상태가 된다.
    /// (1단계에서 실제로 겪은 문제: 루트의 GeneratorStation 이 버튼보다 먼저 잡혀 클릭이 무시됨)
    /// </summary>
    private static bool VerifyTargetResolution(GameObject root)
    {
        bool ok = true;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            // 조준 감지는 조준 레이어(6)만 본다 → 조준되지 않는 충돌용 콜라이더(문틀 · 장비 몸체 · 받침 벽)는 검사하지 않는다 (3d 명세 FD12)
            if (collider.gameObject.layer != InteractableLayer)
                continue;

            MonoBehaviour resolved = null;

            foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour is ITargetable)
                {
                    resolved = behaviour;
                    break;
                }
            }

            // 클릭 · F 홀드 · 드래그(끄는 부품) · 놓는 곳 · 아이템 우클릭 대상 중 하나면 입력을 받을 수 있다 (2b 3장, 2c F10)
            if (resolved is IInteractable || resolved is IHoldInteractable || resolved is IDragInteractable
                || resolved is StationDropTarget || resolved is IItemUseTarget)
                continue;

            string resolvedName = resolved != null ? resolved.GetType().Name : "없음";
            Debug.LogError($"[LegacyMissionConverter] '{collider.name}' 콜라이더를 조준하면 '{resolvedName}' 가 잡혀 입력을 받을 수 없습니다. 부품 위치를 확인하세요.");
            ok = false;
        }

        return ok;
    }

    private static MonoBehaviour FindLegacy(GameObject root, string typeName)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
                return behaviour;
        }

        return null;
    }

    private static List<MonoBehaviour> FindAllLegacy(GameObject root, string typeName)
    {
        List<MonoBehaviour> found = new List<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
                found.Add(behaviour);
        }

        return found;
    }

    private static bool VerifyNoLegacyScripts(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            {
                Debug.LogError($"[LegacyMissionConverter] '{t.name}' 에 Missing Script 가 있어 저장하지 않습니다.");
                return false;
            }

            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && System.Array.IndexOf(LegacyTypeNames, behaviour.GetType().Name) >= 0)
                {
                    Debug.LogError($"[LegacyMissionConverter] 옛 스크립트 {behaviour.GetType().Name} 가 남아 저장하지 않습니다.");
                    return false;
                }
            }
        }

        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
