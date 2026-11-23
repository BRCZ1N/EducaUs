using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mapa da vila (botão MAPA no canto de cima, ou tecla M / Tab), como o mapa do Among Us.
/// Mostra onde você está, as tarefas que faltam (amarelo = individual, azul = coletiva, cadeado = falta a pista),
/// os moradores que ainda têm uma pista para contar e o painel INICIAR da associação.
/// Dá para continuar andando com o mapa aberto. A imagem vem de Resources/Mapa/MapaVila.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class MapaPartida : MonoBehaviour
{
    // o mapa cobre o mundo inteiro: x de 0 a 112, y de -40 a 90 (unidades do Unity)
    private static readonly Rect Mundo = new Rect(0f, -40f, 112f, 130f);
    private const float AlturaJanela = 940f, LarguraJanela = 1380f;   // janela do mapa (o mapa dentro dela tem zoom)
    private static readonly float AlturaBase = AlturaJanela, LarguraBase = AlturaJanela * 112f / 130f;
    [SerializeField] private float zoom = 2f;
    private const float ZoomMin = 1f, ZoomMax = 3.5f;

    public static bool Aberto { get; private set; }

    private static readonly Color Amarelo = new Color32(255, 210, 90, 255);
    private static readonly Color Azul = new Color32(90, 170, 255, 255);
    private static readonly Color Laranja = new Color32(243, 140, 60, 255);
    private static readonly Color Cinza = new Color32(170, 170, 170, 255);
    private static readonly Color Verde = new Color32(110, 200, 90, 255);

    private Font fonte;
    private GameObject painel, botao;
    private RectTransform areaMapa, marcadorEu, janela;
    private Text txtZoom;
    private readonly List<(Transform alvo, RectTransform marca, System.Func<bool> visivel, Image img, System.Func<Color> cor)> marcas =
        new List<(Transform, RectTransform, System.Func<bool>, Image, System.Func<Color>)>();
    private GerenciadorAmbiental gm;
    private Comunidade comunidade;
    private bool montado;
    private CanvasScaler escala;

    private void Awake() => Aberto = false;
    private void OnDestroy() => Aberto = false;

    private void Start()
    {
        gm = GetComponent<GerenciadorAmbiental>();
        comunidade = GetComponent<Comunidade>();
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Construir();
        Fechar();
    }

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        bool bloqueado = MenuPartida.Aberto || MinigameTarefa.Aberto || HudAmbiental.PainelAberto || Assembleia.Aberto;
        if (kb != null && !bloqueado && (kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) Alternar();
        if (bloqueado && Aberto) Fechar();
        botao.SetActive(!bloqueado);
        if (escala != null) escala.matchWidthOrHeight = (float)Screen.width / Mathf.Max(1, Screen.height) < 16f / 9f ? 0f : 1f;   // igual à HUD
        if (!Aberto) return;

        if (!montado) MontarMarcas();

        // zoom: roda do mouse, teclas + e - ou os botões
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f) MudarZoom(mouse.scroll.ReadValue().y > 0 ? 0.25f : -0.25f);
        if (kb != null && (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)) MudarZoom(0.5f);
        if (kb != null && (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)) MudarZoom(-0.5f);
        areaMapa.sizeDelta = new Vector2(LarguraBase * zoom, AlturaBase * zoom);
        foreach (var m in marcas)
        {
            bool ver = m.alvo != null && m.visivel();
            m.marca.gameObject.SetActive(ver);
            if (!ver) continue;
            m.marca.anchoredPosition = ParaMapa(m.alvo.position);
            if (m.cor != null) m.img.color = m.cor();
        }
        var eu = JogadorLocal();
        marcadorEu.gameObject.SetActive(eu != null);
        // centraliza o mapa em você (sem mostrar fora das bordas do mapa)
        Vector2 centro = eu != null ? ParaMapa(eu.position) : Vector2.zero;
        float folgaX = Mathf.Max(0f, (areaMapa.sizeDelta.x - LarguraJanela) / 2f), folgaY = Mathf.Max(0f, (areaMapa.sizeDelta.y - AlturaJanela) / 2f);
        areaMapa.anchoredPosition = new Vector2(Mathf.Clamp(-centro.x, -folgaX, folgaX), Mathf.Clamp(-centro.y, -folgaY, folgaY));
        if (eu != null)
        {
            marcadorEu.anchoredPosition = ParaMapa(eu.position);
            float s = 1f + 0.25f * Mathf.Sin(Time.time * 6f);
            marcadorEu.localScale = new Vector3(s, s, 1f);
            marcadorEu.SetAsLastSibling();
        }
    }

    public void Alternar() { if (Aberto) Fechar(); else Abrir(); }

    private void MudarZoom(float d)
    {
        zoom = Mathf.Clamp(zoom + d, ZoomMin, ZoomMax);
        if (txtZoom != null) txtZoom.text = $"Zoom {zoom:0.#}x";
    }

    public void Abrir()
    {
        Aberto = true; painel.SetActive(true);
        montado = false;                          // remonta: moradores e tarefas podem ter mudado
    }

    public void Fechar() { Aberto = false; if (painel != null) painel.SetActive(false); }

    private Vector2 ParaMapa(Vector3 p)
    {
        var tam = areaMapa.sizeDelta;
        float u = (p.x - Mundo.xMin) / Mundo.width, v = (p.y - Mundo.yMin) / Mundo.height;
        return new Vector2((u - 0.5f) * tam.x, (v - 0.5f) * tam.y);
    }

    private static Transform JogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (PhotonNetwork.InRoom && pm.GetComponent<PhotonView>() is PhotonView v && !v.AmOwner) continue;
            return pm.transform;
        }
        return null;
    }

    // ------------------------------------------------------------------ marcadores
    private void MontarMarcas()
    {
        foreach (var m in marcas) if (m.marca != null) Destroy(m.marca.gameObject);
        marcas.Clear();
        montado = true;

        foreach (var p in FindObjectsByType<PontoDeTarefa>(FindObjectsSortMode.None))
        {
            var ponto = p;
            if (ponto.EhReuniao) continue;
            bool coletiva = ponto.Coletiva;
            var mr = Marca(coletiva ? 26 : 20, coletiva ? "!!" : "!");
            marcas.Add((ponto.transform, mr, () => !ponto.Feita && gm != null && gm.PartidaIniciada, mr.GetComponent<Image>(),
                        () => PrecisaPista(ponto) ? Cinza : coletiva ? Azul : Amarelo));
        }
        foreach (var mor in FindObjectsByType<Morador>(FindObjectsSortMode.None))
        {
            var m = mor;
            var mr = Marca(20, "?");
            marcas.Add((m.transform, mr, () => gm != null && !gm.SaberConhecido(m.Nome), mr.GetComponent<Image>(), () => Laranja));
        }
        var inicio = new GameObject("PosInicio").transform;          // painel INICIAR (só antes de começar)
        inicio.SetParent(transform, false); inicio.position = GerenciadorAmbiental.PosicaoInicio;
        var mi = Marca(24, ">");
        marcas.Add((inicio, mi, () => gm != null && !gm.PartidaIniciada, mi.GetComponent<Image>(), () => Verde));
    }

    private bool PrecisaPista(PontoDeTarefa p)
    {
        if (comunidade == null || gm == null) return false;
        var m = comunidade.SaberNecessario(p.nome);
        return m != null && !gm.SaberConhecido(m.Nome);
    }

    private RectTransform Marca(float tam, string simbolo)
    {
        var rt = Ret(areaMapa, "Marca", Color.white, new Vector2(.5f, .5f), Vector2.zero, new Vector2(tam, tam));
        var img = rt.GetComponent<Image>(); img.sprite = Circulo();
        rt.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
        var t = Txt(rt, Mathf.RoundToInt(tam * 0.7f), FontStyle.Bold, new Color32(42, 30, 26, 255), TextAnchor.MiddleCenter);
        t.text = simbolo;
        return rt;
    }

    // ------------------------------------------------------------------ interface
    private void Construir()
    {
        var cv = new GameObject("Mapa UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cv.transform.SetParent(transform, false);
        var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
        var sc = cv.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f; escala = sc;
        var raiz = (RectTransform)cv.transform;

        // botão MAPA, logo abaixo do MENU
        var b = Ret(raiz, "Botao Mapa", new Color32(30, 26, 22, 230), new Vector2(1, 1), new Vector2(-30, -180), new Vector2(96, 96));
        b.GetComponent<Image>().sprite = Circulo(); b.GetComponent<Image>().raycastTarget = true;
        Txt(b, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = "MAPA";
        b.gameObject.AddComponent<Button>().onClick.AddListener(Alternar);
        botao = b.gameObject;

        // painel do mapa (não bloqueia o jogo: dá para andar com ele aberto)
        painel = Ret(raiz, "Painel Mapa", new Color(0, 0, 0, 0.45f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000)).gameObject;
        painel.GetComponent<Image>().raycastTarget = false;
        var moldura = Ret((RectTransform)painel.transform, "Moldura", new Color32(42, 30, 26, 255), new Vector2(.5f, .5f), new Vector2(-180, 0),
                          new Vector2(LarguraJanela + 16, AlturaJanela + 16));
        janela = Ret(moldura, "Janela", new Color32(60, 110, 50, 255), new Vector2(.5f, .5f), Vector2.zero, new Vector2(LarguraJanela, AlturaJanela));
        janela.gameObject.AddComponent<RectMask2D>();                     // corta o que passa da janela
        var img = new GameObject("Mapa", typeof(RectTransform), typeof(RawImage));
        areaMapa = (RectTransform)img.transform; areaMapa.SetParent(janela, false);
        areaMapa.anchorMin = areaMapa.anchorMax = new Vector2(.5f, .5f);
        areaMapa.sizeDelta = new Vector2(LarguraBase * zoom, AlturaBase * zoom);
        var tex = Resources.Load<Texture2D>("Mapa/MapaVila");
        var raw = img.GetComponent<RawImage>(); raw.texture = tex; raw.raycastTarget = false;
        if (tex == null) { raw.color = new Color32(90, 140, 70, 255); Debug.LogWarning("[Mapa] Resources/Mapa/MapaVila não encontrado."); }
        float larg = 2f * (-180f + LarguraJanela / 2f + 8f + 30f + 150f - 60f);   // coloca a legenda logo à direita da janela

        marcadorEu = Ret(areaMapa, "Voce", new Color32(217, 72, 59, 255), new Vector2(.5f, .5f), Vector2.zero, new Vector2(28, 28));
        marcadorEu.GetComponent<Image>().sprite = Circulo();
        marcadorEu.gameObject.AddComponent<Outline>().effectColor = Color.white;

        // legenda + fechar
        var leg = Ret((RectTransform)painel.transform, "Legenda", new Color32(26, 46, 40, 240), new Vector2(.5f, .5f),
                      new Vector2(larg / 2f + 60, 140), new Vector2(300, 400));
        var tl = Txt(leg, 22, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
        tl.supportRichText = true;
        tl.text = "<b><color=#ffd25a>MAPA DA VILA</color></b>\n\n" +
                  "<color=#d9483b>●</color>  você\n" +
                  "<color=#ffd25a>●</color>  tarefa\n" +
                  "<color=#5aaaff>●</color>  tarefa coletiva\n" +
                  "<color=#aaaaaa>●</color>  falta a pista\n" +
                  "<color=#f38c3c>●</color>  morador com pista\n" +
                  "<color=#6ec85a>●</color>  painel INICIAR\n\n" +
                  "<size=18>M ou Tab abre e fecha\nRoda do mouse ou + e − mudam o zoom</size>";
        var fx = Ret((RectTransform)painel.transform, "Fechar", new Color32(217, 72, 59, 255), new Vector2(.5f, .5f),
                     new Vector2(larg / 2f + 60, -260), new Vector2(200, 64));
        fx.GetComponent<Image>().raycastTarget = true;
        Txt(fx, 26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = "FECHAR";
        fx.gameObject.AddComponent<Button>().onClick.AddListener(Fechar);

        // botões de zoom
        foreach (var (rot, d, x) in new[] { ("−", -0.5f, -55f), ("+", 0.5f, 55f) })
        {
            var bz = Ret((RectTransform)painel.transform, "Zoom" + rot, new Color32(47, 107, 85, 255), new Vector2(.5f, .5f),
                         new Vector2(larg / 2f + 60 + x, -160), new Vector2(90, 64));
            bz.GetComponent<Image>().raycastTarget = true;
            Txt(bz, 36, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = rot;
            float dd = d; bz.gameObject.AddComponent<Button>().onClick.AddListener(() => MudarZoom(dd));
        }
        var zt = Ret((RectTransform)painel.transform, "TxtZoom", new Color(0, 0, 0, 0), new Vector2(.5f, .5f), new Vector2(larg / 2f + 60, -100), new Vector2(240, 36));
        txtZoom = Txt(zt, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        MudarZoom(0f);
    }

    private RectTransform Ret(RectTransform pai, string nome, Color cor, Vector2 ancora, Vector2 pos, Vector2 tam)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = ancora;
        rt.pivot = new Vector2(ancora.x == 1 ? 1 : .5f, ancora.y == 1 ? 1 : .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        var img = go.GetComponent<Image>(); img.color = cor; img.raycastTarget = false;
        return rt;
    }

    private Text Txt(RectTransform pai, int tam, FontStyle estilo, Color cor, TextAnchor al, bool margem = false)
    {
        var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = margem ? new Vector2(20, 20) : Vector2.zero; rt.offsetMax = margem ? new Vector2(-20, -20) : Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = fonte; t.fontSize = tam; t.fontStyle = estilo; t.color = cor; t.alignment = al; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static Sprite circulo;
    private static Sprite Circulo()
    {
        if (circulo != null) return circulo;
        const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f));
                px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(N / 2f - d));
            }
        tex.SetPixels(px); tex.Apply();
        circulo = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100f);
        return circulo;
    }
}
