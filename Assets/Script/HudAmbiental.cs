using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// HUD da partida, montada por código (não precisa de prefab nem de TextMeshPro):
/// barra "Vila recuperada 3/8", contador de tarefas, seu papel, aviso de tarefa (Segure E…),
/// mensagens rápidas e a mensagem grande de vitória. O GerenciadorAmbiental cria e controla.
/// </summary>
public class HudAmbiental : MonoBehaviour
{
    public static readonly Color Verde = new Color32(123, 192, 74, 255);
    public static readonly Color Vermelho = new Color32(217, 72, 59, 255);
    public static readonly Color Cinza = new Color32(200, 200, 200, 255);
    private static readonly Color Amarelo = new Color32(255, 210, 90, 255);
    private static readonly Color Fundo = new Color(0.08f, 0.06f, 0.05f, 0.72f);
    private static readonly Color Cal = new Color32(241, 227, 200, 255);

    private Font fonte;
    private Text txtAreas, txtTarefas, txtPapel, txtPrompt, txtAviso, txtGrande, txtTempo;
    private RectTransform barraAreas, barraPrompt;
    private Image painelPapel;
    private GameObject painelPrompt, painelGrande;
    private float avisoAte;

    // botões de toque/clique (estilo Among Us)
    private Image imgUsar, anelUsar, imgSabotar;
    private Text txtUsar, txtSabotar;
    private BotaoSegurar segUsar;
    private bool sabotarClicado;
    private static Sprite circulo, anel;

    /// O jogador está segurando o botão USAR (mouse ou toque).
    public bool UsarSegurado => segUsar != null && segUsar.Segurando && imgUsar.raycastTarget;
    /// O botão SABOTAR foi clicado desde a última vez que alguém perguntou.
    public bool ConsumirSabotar() { bool c = sabotarClicado; sabotarClicado = false; return c; }

    private void Awake() => Construir();

    private void Update()
    {
        if (txtAviso.enabled && Time.time > avisoAte) txtAviso.enabled = false;
    }

    // ------------------------------------------------------------------ API
    public void Atualizar(int areas, int totalAreas, int tarefas, int totalTarefas, bool sabotador, bool temSabotador = true, int apoios = 0)
    {
        txtAreas.text = $"Vila recuperada  {areas}/{totalAreas}";
        float f = totalAreas > 0 ? (float)areas / totalAreas : 0f;
        barraAreas.anchorMax = new Vector2(f, 1f);
        txtTarefas.text = $"Tarefas: {tarefas}/{totalTarefas}    Apoios: {apoios}";
        txtPapel.text = sabotador
            ? "<b>SABOTADOR</b>  Você representa quem lucra com a degradação. Finja ajudar e aperte <b>Q</b> para desfazer conquistas."
            : temSabotador
                ? "<b>VOLUNTÁRIO</b>  Há um sabotador entre vocês. Organizem-se para recuperar a vila."
                : "<b>VOLUNTÁRIO</b>  Converse com os moradores (balão) para ganhar apoios. A pressão econômica pode desfazer conquistas.";
        txtPapel.color = Color.white;
        painelPapel.color = sabotador ? new Color(0.55f, 0.13f, 0.10f, 0.85f) : new Color(0.16f, 0.36f, 0.27f, 0.85f);
    }

    /// texto null esconde; preenchimento < 0 esconde a barrinha.
    public void Prompt(string texto, float preenchimento)
    {
        painelPrompt.SetActive(texto != null);
        if (texto == null) return;
        txtPrompt.text = texto;
        barraPrompt.parent.gameObject.SetActive(preenchimento >= 0f);
        barraPrompt.anchorMax = new Vector2(Mathf.Clamp01(preenchimento), 1f);
    }

    public void Aviso(string texto, Color? cor = null, float segundos = 3.5f)
    {
        txtAviso.text = texto;
        txtAviso.color = cor ?? Color.white;
        txtAviso.enabled = true;
        avisoAte = Time.time + segundos;
    }

    public void Grande(string texto, Color? cor = null)
    {
        painelGrande.SetActive(texto != null);
        if (texto == null) return;
        txtGrande.text = texto;
        txtGrande.color = cor ?? Verde;
    }

    /// Estado do botão USAR: aceso quando há algo para fazer por perto; o anel mostra o progresso.
    public void BotaoUsar(bool ativo, string rotulo, float progresso)
    {
        imgUsar.color = ativo ? new Color32(255, 255, 255, 255) : new Color32(150, 150, 150, 140);
        imgUsar.raycastTarget = ativo;
        txtUsar.text = rotulo;
        txtUsar.color = ativo ? new Color32(42, 30, 26, 255) : new Color32(42, 30, 26, 120);
        anelUsar.fillAmount = ativo ? Mathf.Clamp01(progresso) : 0f;
    }

    /// Botão SABOTAR: só aparece para o sabotador; mostra a recarga.
    public void BotaoSabotar(bool visivel, bool ativo, int recarga)
    {
        imgSabotar.gameObject.SetActive(visivel);
        if (!visivel) return;
        imgSabotar.color = ativo ? Vermelho : new Color(0.45f, 0.2f, 0.18f, 0.6f);
        txtSabotar.text = recarga > 0 ? recarga + "s" : "SABOTAR";
    }

    /// segundos < 0 esconde o relógio.
    public void Tempo(float segundos)
    {
        txtTempo.enabled = segundos >= 0f;
        if (segundos < 0f) return;
        int s = Mathf.CeilToInt(segundos);
        txtTempo.text = $"{s / 60:00}:{s % 60:00}";
        txtTempo.color = segundos <= 60f ? Vermelho : Color.white;
    }

    // ------------------------------------------------------------------ construção
    private void Construir()
    {
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("HUD Ambiental", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raiz = (RectTransform)canvasGo.transform;

        // canto de cima à esquerda: progresso
        var topo = Painel(raiz, "Progresso", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(560, 150));
        txtAreas = Texto(topo, "Areas", 30, FontStyle.Bold, Color.white, new Vector2(20, -14), new Vector2(520, 40), TextAnchor.UpperLeft);
        var trilho = Retangulo(topo, "Barra", new Color(1, 1, 1, 0.15f), new Vector2(20, -62), new Vector2(520, 22));
        barraAreas = Preenchimento(trilho, Verde);
        txtTarefas = Texto(topo, "Tarefas", 22, FontStyle.Normal, Amarelo, new Vector2(20, -96), new Vector2(380, 30), TextAnchor.UpperLeft);
        txtTempo = Texto(topo, "Tempo", 30, FontStyle.Bold, Color.white, new Vector2(400, -90), new Vector2(140, 40), TextAnchor.UpperRight);
        // faixa do papel, logo abaixo do progresso: fundo colorido e texto que quebra linha (lê bem sobre o mapa)
        var papel = Retangulo(raiz, "Papel", Fundo, new Vector2(24, -182), new Vector2(560, 64));
        painelPapel = papel.GetComponent<Image>();
        txtPapel = Texto(papel, "Texto", 19, FontStyle.Normal, Color.white, new Vector2(16, -6), new Vector2(528, 52), TextAnchor.MiddleLeft);
        txtPapel.horizontalOverflow = HorizontalWrapMode.Wrap;
        txtPapel.supportRichText = true;

        // embaixo, no centro: tarefa perto de você
        painelPrompt = Painel(raiz, "Prompt", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-450, 150), new Vector2(900, 96)).gameObject;
        var rp = (RectTransform)painelPrompt.transform;
        txtPrompt = Texto(rp, "Texto", 26, FontStyle.Bold, Color.white, new Vector2(20, -10), new Vector2(860, 44), TextAnchor.MiddleCenter);
        var trilhoP = Retangulo(rp, "Barra", new Color(1, 1, 1, 0.15f), new Vector2(40, -62), new Vector2(820, 18));
        barraPrompt = Preenchimento(trilhoP, Amarelo);
        painelPrompt.SetActive(false);

        // avisos rápidos (área recuperada / sabotagem)
        txtAviso = Texto(raiz, "Aviso", 34, FontStyle.Bold, Color.white, new Vector2(-600, -40), new Vector2(1200, 50), TextAnchor.MiddleCenter,
                         new Vector2(0.5f, 1));
        Sombra(txtAviso);
        txtAviso.enabled = false;

        // botões redondos (clique ou toque), canto de baixo à direita
        imgUsar = BotaoRedondo(raiz, "Usar", new Vector2(-40, 40), 190, Color.white, out txtUsar, "USAR");
        anelUsar = Anel(imgUsar.rectTransform, Amarelo);
        segUsar = imgUsar.gameObject.AddComponent<BotaoSegurar>();
        imgSabotar = BotaoRedondo(raiz, "Sabotar", new Vector2(-250, 40), 150, Vermelho, out txtSabotar, "SABOTAR");
        txtSabotar.color = Color.white; txtSabotar.fontSize = 24;
        imgSabotar.gameObject.AddComponent<Button>().onClick.AddListener(() => sabotarClicado = true);
        imgSabotar.gameObject.SetActive(false);
        BotaoUsar(false, "USAR", 0f);

        // botão MENU (no lugar do Esc, para quem joga com mouse ou toque)
        var menu = BotaoRedondo(raiz, "Menu", new Vector2(-30, -70), 96, new Color(0.08f, 0.06f, 0.05f, 0.8f), out var txtMenu, "MENU", topo: true);
        txtMenu.color = Cal; txtMenu.fontSize = 20;
        menu.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            var m = FindFirstObjectByType<MenuPartida>();
            if (m != null) { if (MenuPartida.Aberto) m.Fechar(); else m.Abrir(); }
        });

        // vitória
        painelGrande = Painel(raiz, "Vitoria", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-520, 90), new Vector2(1040, 180)).gameObject;
        txtGrande = Texto((RectTransform)painelGrande.transform, "Texto", 40, FontStyle.Bold, Verde, new Vector2(20, -20), new Vector2(1000, 140), TextAnchor.MiddleCenter);
        txtGrande.horizontalOverflow = HorizontalWrapMode.Wrap;
        painelGrande.SetActive(false);
    }

    private Image BotaoRedondo(RectTransform pai, string nome, Vector2 pos, float tam, Color cor, out Text rotulo, string texto, bool topo = false)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = topo ? new Vector2(1, 1) : new Vector2(1, 0);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(tam, tam);
        var img = go.GetComponent<Image>(); img.sprite = SpriteCirculo(); img.color = cor;
        rotulo = Texto(rt, "Texto", Mathf.RoundToInt(tam * 0.17f), FontStyle.Bold, new Color32(42, 30, 26, 255), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        var trt = rotulo.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        rotulo.text = texto;
        return img;
    }

    private Image Anel(RectTransform pai, Color cor)
    {
        var go = new GameObject("Progresso", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-10, -10); rt.offsetMax = new Vector2(10, 10);
        var img = go.GetComponent<Image>(); img.sprite = SpriteAnel(); img.color = cor; img.raycastTarget = false;
        img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = (int)Image.Origin360.Top; img.fillClockwise = true;
        img.fillAmount = 0f;
        return img;
    }

    /// Círculo branco com contorno escuro (cor aplicada pelo Image.color).
    public static Sprite SpriteCirculo()
    {
        if (circulo != null) return circulo;
        circulo = CriarCirculo(128, 0f, 7f);
        return circulo;
    }
    private static Sprite SpriteAnel()
    {
        if (anel != null) return anel;
        anel = CriarCirculo(128, 0.80f, 0f);
        return anel;
    }
    private static Sprite CriarCirculo(int n, float furo, float contorno)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[n * n]; float r = n / 2f - 1;
        Color tinta = new Color32(42, 30, 26, 255);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f));
                float a = Mathf.Clamp01(r - d + 0.5f);                                   // borda lisa
                if (furo > 0) a *= Mathf.Clamp01(d - r * furo + 0.5f);
                Color c = (contorno > 0 && d > r - contorno) ? tinta : Color.white;
                c.a = a; px[y * n + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
    }

    private RectTransform Painel(RectTransform pai, string nome, Vector2 ancora, Vector2 pivo, Vector2 pos, Vector2 tam)
    {
        var rt = Retangulo(pai, nome, Fundo, pos, tam, ancora);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        return rt;
    }

    private RectTransform Retangulo(RectTransform pai, string nome, Color cor, Vector2 pos, Vector2 tam, Vector2? ancora = null)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = ancora ?? new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = tam;
        var img = go.GetComponent<Image>();
        img.color = cor;
        img.raycastTarget = false;
        return rt;
    }

    private RectTransform Preenchimento(RectTransform trilho, Color cor)
    {
        var go = new GameObject("Cheio", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(trilho, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>(); img.color = cor; img.raycastTarget = false;
        return rt;
    }

    private Text Texto(RectTransform pai, string nome, int tamanho, FontStyle estilo, Color cor, Vector2 pos, Vector2 tam,
                       TextAnchor alinhamento, Vector2? ancora = null)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = ancora ?? new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = tam;
        var t = go.GetComponent<Text>();
        t.font = fonte; t.fontSize = tamanho; t.fontStyle = estilo; t.color = cor;
        t.alignment = alinhamento; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    private static void Sombra(Text t)
    {
        var s = t.gameObject.AddComponent<Outline>();
        s.effectColor = new Color(0, 0, 0, 0.85f);
        s.effectDistance = new Vector2(2, -2);
    }
}

/// Guarda se o botão está sendo segurado (mouse ou dedo).
public class BotaoSegurar : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool Segurando { get; private set; }
    public void OnPointerDown(PointerEventData e) => Segurando = true;
    public void OnPointerUp(PointerEventData e) => Segurando = false;
    public void OnPointerExit(PointerEventData e) => Segurando = false;
    private void OnDisable() => Segurando = false;
}
