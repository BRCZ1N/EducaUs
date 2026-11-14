using UnityEngine;
using UnityEngine.EventSystems;

// Faz o botão deslizar para a direita no hover/foco e toca sons opcionais.
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler, IPointerClickHandler
{
    public float slide = 24f;
    public float speed = 14f;
    public AudioClip hoverClip;
    public AudioClip clickClip;

    RectTransform rt;
    float baseX, offset;

    void Awake() { rt = (RectTransform)transform; baseX = rt.anchoredPosition.x; }

    void Update()
    {
        var p = rt.anchoredPosition;
        p.x = Mathf.Lerp(p.x, baseX + offset, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        rt.anchoredPosition = p;
    }

    void Hover(bool on)
    {
        offset = on ? slide : 0f;
        if (on && hoverClip) AudioSource.PlayClipAtPoint(hoverClip, Vector3.zero);
    }

    public void OnPointerEnter(PointerEventData e) => Hover(true);
    public void OnPointerExit(PointerEventData e)  => Hover(false);
    public void OnSelect(BaseEventData e)          => Hover(false);
    public void OnDeselect(BaseEventData e)        => Hover(false);
    public void OnPointerClick(PointerEventData e)
    {
        if (clickClip) AudioSource.PlayClipAtPoint(clickClip, Vector3.zero);
    }
}
