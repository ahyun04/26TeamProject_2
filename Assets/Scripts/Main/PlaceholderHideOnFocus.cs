using TMPro;
using UnityEngine;

/// <summary>
/// TMP_InputField 에 붙이면, 칸을 클릭(선택)하는 순간 Placeholder 를 숨기고
/// 선택이 풀렸을 때 내용이 비어 있으면 Placeholder 를 다시 보여준다.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
public class PlaceholderHideOnFocus : MonoBehaviour
{
    private TMP_InputField input;

    private void Awake()
    {
        input = GetComponent<TMP_InputField>();
    }

    private void OnEnable()
    {
        input.onSelect.AddListener(onSelect);
        input.onDeselect.AddListener(onDeselect);
        refresh(false);
    }

    private void OnDisable()
    {
        input.onSelect.RemoveListener(onSelect);
        input.onDeselect.RemoveListener(onDeselect);
    }

    private void onSelect(string _) { refresh(true); }
    private void onDeselect(string _) { refresh(false); }

    private void refresh(bool focused)
    {
        if (input.placeholder == null) return;
        input.placeholder.gameObject.SetActive(!focused && string.IsNullOrEmpty(input.text));
    }
}