using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Movimento sem teclado, como no Among Us:
/// • Mouse: segure o botão esquerdo no mapa e o personagem anda na direção do cursor.
/// • Toque: joystick virtual no canto de baixo à esquerda (aparece sozinho em telas de toque).
/// O PlayerMovement soma ControleToque.Direcao ao teclado/controle.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class ControleToque : MonoBehaviour
{
    /// Direção pedida pelo mouse ou pelo joystick (tamanho 0..1).
    public static Vector2 Direcao { get; private set; }

    [SerializeField] private bool andarComMouse = true;
    [Tooltip("Mostrar o joystick mesmo sem tela de toque (para testar no PC).")]
    [SerializeField] private bool sempreMostrarJoystick = false;
    [SerializeField] private float raioJoystick = 110f;

    private RectTransform baseJoy, pino;
    private int dedo = -1;
    private Vector2 valorJoy;
    private Transform jogador;
    private float proximaBusca;

    private void Start()
    {
        bool toque = Touchscreen.current != null || Application.isMobilePlatform;
        if (toque || sempreMostrarJoystick) CriarJoystick();
    }

    private void OnDestroy() => Direcao = Vector2.zero;

    private void Update()
    {
        Vector2 d = valorJoy;
        if (d == Vector2.zero && andarComMouse) d = DirecaoDoMouse();
        Direcao = (MenuPartida.Aberto || MinigameTarefa.Aberto || HudAmbiental.PainelAberto || Assembleia.Aberto) ? Vector2.zero : Vector2.ClampMagnitude(d, 1f);
    }

    // ------------------------------------------------------------------ mouse
    private Vector2 DirecaoDoMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed) return Vector2.zero;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return Vector2.zero;   // clicando num botão
        if (Camera.main == null) return Vector2.zero;
        if (jogador == null && Time.time >= proximaBusca) { jogador = AcharJogadorLocal(); proximaBusca = Time.time + 1f; }
        if (jogador == null) return Vector2.zero;

        Vector3 alvo = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
        Vector2 dif = (Vector2)(alvo - jogador.position) - new Vector2(0, 0.6f);   // mira no meio do corpo
        if (dif.magnitude < 0.3f) return Vector2.zero;                          // cursor em cima do boneco: para
        return Vector2.ClampMagnitude(dif / 1.5f, 1f);                           // perto do boneco = mais devagar
    }

    private static Transform AcharJogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!PhotonNetwork.InRoom) return pm.transform;
            var pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.AmOwner) return pm.transform;
        }
        return null;
    }

    // ------------------------------------------------------------------ joystick
    private void CriarJoystick()
    {
        var hudT = transform.Find("HUD Ambiental");
        var hud = hudT != null ? hudT.GetComponent<Canvas>() : null;
        if (hud == null) return;
        var area = new GameObject("Joystick", typeof(RectTransform), typeof(Image), typeof(AreaJoystick));
        area.transform.SetParent(hud.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = art.anchorMax = Vector2.zero; art.pivot = Vector2.zero;
        art.anchoredPosition = new Vector2(30, 30); art.sizeDelta = new Vector2(380, 380);
        area.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);           // área invisível que recebe o toque
        area.GetComponent<AreaJoystick>().dono = this;

        baseJoy = Circulo(art, "Base", new Color(0, 0, 0, 0.35f), raioJoystick * 2);
        baseJoy.anchoredPosition = new Vector2(190, 190);
        pino = Circulo(baseJoy, "Pino", new Color(1f, 0.95f, 0.85f, 0.85f), raioJoystick * 0.9f);
    }

    private RectTransform Circulo(RectTransform pai, string nome, Color cor, float tam)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        if (pai != baseJoy) { rt.anchorMin = rt.anchorMax = Vector2.zero; }
        rt.sizeDelta = new Vector2(tam, tam);
        var img = go.GetComponent<Image>(); img.sprite = HudAmbiental.SpriteCirculo(); img.color = cor; img.raycastTarget = false;
        return rt;
    }

    internal void Tocou(PointerEventData e)
    {
        if (dedo != -1) return;
        dedo = e.pointerId;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)baseJoy.parent, e.position, e.pressEventCamera, out var p);
        baseJoy.anchoredPosition = p;                                            // o joystick aparece onde o dedo tocou
        Arrastou(e);
    }

    internal void Arrastou(PointerEventData e)
    {
        if (e.pointerId != dedo) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(baseJoy, e.position, e.pressEventCamera, out var p);
        p = Vector2.ClampMagnitude(p, raioJoystick);
        pino.anchoredPosition = p;
        valorJoy = p / raioJoystick;
    }

    internal void Soltou(PointerEventData e)
    {
        if (e.pointerId != dedo) return;
        dedo = -1; valorJoy = Vector2.zero; pino.anchoredPosition = Vector2.zero;
    }
}

/// Recebe os toques da área do joystick e repassa para o ControleToque.
public class AreaJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    internal ControleToque dono;
    public void OnPointerDown(PointerEventData e) => dono?.Tocou(e);
    public void OnDrag(PointerEventData e) => dono?.Arrastou(e);
    public void OnPointerUp(PointerEventData e) => dono?.Soltou(e);
}
