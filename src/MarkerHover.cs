using UnityEngine;
using UnityEngine.EventSystems;

namespace LunariumItemCollectionMod;

public sealed class MarkerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject? Tooltip { get; set; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Tooltip != null)
        {
            transform.SetAsLastSibling();
            Tooltip.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Tooltip != null)
        {
            Tooltip.SetActive(false);
        }
    }
}
