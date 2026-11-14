using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

internal enum PlayerAppearanceSlot
{
    Color,
    Face,
    Head,
    Body
}

internal struct PlayerAppearanceSelection : INetworkStruct, IEquatable<PlayerAppearanceSelection>
{
    public int color; //캐릭터 색상 인덱스
    public int face; //바이저 스티커 ID
    public int head; //머리 장식 ID
    public int body; //등 장식 ID

    internal int get(PlayerAppearanceSlot slot) //슬롯의 현재 선택값 조회
    {
        return slot switch
        {
            PlayerAppearanceSlot.Color => color,
            PlayerAppearanceSlot.Face => face,
            PlayerAppearanceSlot.Head => head,
            _ => body
        };
    }

    internal void set(PlayerAppearanceSlot slot, int value) //해당 슬롯만 변경
    {
        switch (slot)
        {
            case PlayerAppearanceSlot.Color: color = value; break;
            case PlayerAppearanceSlot.Face: face = value; break;
            case PlayerAppearanceSlot.Head: head = value; break;
            case PlayerAppearanceSlot.Body: body = value; break;
        }
    }

    public bool Equals(PlayerAppearanceSelection other) //외형 전체가 같은지 확인
    {
        return color == other.color && face == other.face && head == other.head && body == other.body;
    }
}

[CreateAssetMenu(menuName = "Trust No One/Player Cosmetics Catalog")]
public class PlayerCosmeticsCatalog : ScriptableObject
{
    [Serializable]
    internal sealed class Entry
    {
        [SerializeField] internal int id; //아이템 데이터 테이블 ID
        [SerializeField] internal PlayerAppearanceSlot slot; //장착 부위
        [SerializeField] internal string displayName; //설정의 선택 항목 이름
        [SerializeField] internal Sprite sticker; //바이저에 붙일 원본 그림
        [SerializeField] internal Sprite thumbnail; //설정 목록에 보여줄 모델 미리보기
        [SerializeField] internal Vector4 stickerTiling = new(1f, 1f, 0f, 0f); //바이저 표면에 투영할 크기와 중심
        [SerializeField] internal GameObject model; //머리와 등의 원본 장식 모델
        [SerializeField] internal Vector3 localPosition; //장착 본 기준 위치
        [SerializeField] internal Vector3 localRotation; //장착 본 기준 회전
        [SerializeField] internal float scale = 0.01f; //원본 모델과 장착 본의 배율 환산

        internal bool isAvailable => id > 0 && (slot == PlayerAppearanceSlot.Face ? sticker != null : model != null); //리소스가 연결된 항목만 노출
    }

    [SerializeField] private string headPath; //캐릭터 모델에서 머리 본까지의 경로
    [SerializeField] private string backPath; //캐릭터 모델에서 등 장착용 본까지의 경로
    [SerializeField] private Mesh stickerSurface; //원본 바이저 표면에 맞춘 스티커 메시
    [SerializeField] private Material stickerMaterial; //알파를 적용할 스티커 재질
    [SerializeField] private Entry[] entries; //슬롯별로 선택할 수 있는 데이터 테이블 항목

    internal IReadOnlyList<Entry> items => entries; //설정 UI가 읽는 목록
    internal Mesh surface => stickerSurface; //스티커를 붙일 바이저 메시
    internal Material material => stickerMaterial; //스티커를 그리는 공유 재질

    internal Entry find(PlayerAppearanceSlot slot, int id) //슬롯에 올바른 ID와 리소스가 있는지 확인
    {
        if (id == 0 || entries == null || !hasResources(slot)) return null;
        foreach (Entry entry in entries)
            if (entry != null && entry.slot == slot && entry.id == id && entry.isAvailable)
                return entry;
        return null;
    }

    internal bool hasResources(PlayerAppearanceSlot slot) //목록과 서버 검증에서 같은 리소스 검사 사용
    {
        return slot switch
        {
            PlayerAppearanceSlot.Face => !string.IsNullOrEmpty(headPath) && stickerSurface != null && stickerMaterial != null,
            PlayerAppearanceSlot.Head => !string.IsNullOrEmpty(headPath),
            PlayerAppearanceSlot.Body => !string.IsNullOrEmpty(backPath),
            _ => false
        };
    }

    internal Transform findAttachment(Transform model, PlayerAppearanceSlot slot) //공통 캐릭터 리그의 장착 본 조회
    {
        return model != null ? model.Find(slot == PlayerAppearanceSlot.Body ? backPath : headPath) : null;
    }
}
