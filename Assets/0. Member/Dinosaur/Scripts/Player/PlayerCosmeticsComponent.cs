using UnityEngine;
using UnityEngine.Rendering;

internal sealed class PlayerCosmeticsComponent : MonoBehaviour
{
    private static readonly int baseMap = Shader.PropertyToID("_BaseMap"); //스티커의 원본 그림
    private static readonly int mapTiling = Shader.PropertyToID("_BaseMap_ST"); //바이저 위 스티커의 배치
    private readonly GameObject[] attachments = new GameObject[3]; //슬롯별 현재 장식 오브젝트
    private readonly int[] displayedIds = new int[3]; //이미 적용한 슬롯 ID
    private PlayerCosmeticsCatalog catalog; //장식 원본과 장착 위치
    private Transform model; //본을 찾을 캐릭터 모델 루트
    private bool hidden; //본인 1인칭에서 장식 표시 여부

    internal void initialize(PlayerCosmeticsCatalog source, Transform characterModel) //실제 캐릭터와 미리보기에서 같은 배치 사용
    {
        catalog = source;
        model = characterModel;
    }

    internal void setSelection(PlayerAppearanceSelection selection) //바뀐 장식만 교체
    {
        if (catalog == null || model == null) return;
        for (int index = 0; index < displayedIds.Length; index++)
        {
            PlayerAppearanceSlot slot = (PlayerAppearanceSlot)(index + 1); //현재 검사할 장착 부위
            int id = selection.get(slot); //이 슬롯의 승인 또는 미리보기 선택
            if (displayedIds[index] == id) continue;
            displayedIds[index] = id;
            if (attachments[index] != null)
            {
                attachments[index].SetActive(false);
                Destroy(attachments[index]);
                attachments[index] = null;
            }
            PlayerCosmeticsCatalog.Entry entry = catalog.find(slot, id); //슬롯에 맞는 원본 장식
            if (entry == null) continue;
            Transform anchor = catalog.findAttachment(model, slot); //애니메이션과 시선을 따라 움직일 본
            if (anchor == null) continue;

            GameObject attachment = slot == PlayerAppearanceSlot.Face ? createSticker(entry, anchor) : createModel(entry, anchor); //해당 본에 표시할 장식
            foreach (Transform part in attachment.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = model.gameObject.layer;
            attachment.SetActive(!hidden);
            attachments[index] = attachment;
        }
    }

    private GameObject createSticker(PlayerCosmeticsCatalog.Entry entry, Transform anchor) //바이저 곡면을 따라 스티커 표시
    {
        GameObject sticker = new(entry.displayName, typeof(MeshFilter), typeof(MeshRenderer)); //장착한 스티커 메시
        sticker.transform.SetParent(anchor, false);
        sticker.GetComponent<MeshFilter>().sharedMesh = catalog.surface;
        MeshRenderer renderer = sticker.GetComponent<MeshRenderer>(); //플레이어별 텍스처를 유지할 렌더러
        renderer.sharedMaterial = catalog.material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        MaterialPropertyBlock properties = new(); //공유 재질을 변경하지 않는 텍스처 설정
        properties.SetTexture(baseMap, entry.sticker.texture);
        properties.SetVector(mapTiling, entry.stickerTiling);
        renderer.SetPropertyBlock(properties);
        return sticker;
    }

    private GameObject createModel(PlayerCosmeticsCatalog.Entry entry, Transform anchor) //장식 원본 비율을 유지한 장착
    {
        GameObject attachment = Instantiate(entry.model, anchor, false); //머리띠·모자·가방 모델
        attachment.name = entry.displayName;
        Transform pose = attachment.transform; //장착 본 기준으로 맞춘 자세
        pose.SetLocalPositionAndRotation(entry.localPosition, Quaternion.Euler(entry.localRotation));
        pose.localScale = entry.model.transform.localScale * entry.scale;
        return attachment;
    }

    internal void setHidden(bool value) //본인 1인칭과 외부 카메라의 표시 전환
    {
        if (hidden == value) return;
        hidden = value;
        foreach (GameObject attachment in attachments)
            if (attachment != null) attachment.SetActive(!hidden);
    }
}
