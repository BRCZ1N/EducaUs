using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// HUD da partida, montada por código (não precisa de prefab nem de TextMeshPro):
/// barra "Território defendido 3/7", tarefas, apoios e saberes, seu papel, os 4 danos que atravessam a vida
/// (Água, Corpo, Território, Alimento), a fala dos moradores e da Rádio Progresso, avisos rápidos,
/// e os painéis de tela cheia (apresentação do território e roda de conversa final, no ciclo de Paulo Freire).
/// O GerenciadorAmbiental cria e controla.
/// </summary>
public class HudAmbiental : MonoBehaviour
{
    public static readonly Color Verde = new Color32(123, 192, 74, 255);
    public static readonly Color Vermelho = new Color32(217, 72, 59, 255);
    public static readonly Color Cinza = new Color32(200, 200, 200, 255);
    public static readonly Color Amarelo = new Color32(255, 210, 90, 255);
    private static readonly Color Fundo = new Color(0.08f, 0.06f, 0.05f, 0.72f);
    public static readonly Color Cal = new Color32(241, 227, 200, 255);

    /// Recursos da comunidade (ícones em Resources/Animacoes/Recursos).
    public static readonly string[] NomesRecursos = { "ÁGUA", "SAÚDE", "TERRA", "COMIDA" };
    private static readonly string[] IconesRecursos = { "agua", "saude", "terra", "comida" };

    /// Um painel de tela cheia (apresentação ou roda de conversa) está aberto: o personagem fica parado.
    public static bool PainelAberto { get; private set; }

    private Font fonte;
    private CanvasScaler escala;
    private RectTransform[] barrasDim = new RectTransform[4];
    private Text[] txtDim = new Text[4];
    private Image[] fundoRec = new Image[4];
    private float[] valorRec = { -1, -1, -1, -1 };
    private float[] piscaRec = new float[4];
    private Color[] corPisca = new Color[4];
    private GameObject painelFala, painelInfo;
    private Text txtFalaQuem, txtFalaTexto, txtInfoTitulo, txtInfoTexto, txtInfoBotao;
    private Text[] txtInfoColunas = new Text[3], txtInfoCabecalhos = new Text[3];
    private GameObject colunasInfo;
    private System.Action aoFecharInfo;
    private float falaAte;
    private Text txtAreas, txtTarefas, txtPapel, txtPrompt, txtAviso, txtGrande, txtTempo;
    private RectTransform barraAreas, barraPrompt;
    private Image painelPapel;
    private GameObject painelPrompt, painelGrande, painelAviso;
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

    private void Awake() { PainelAberto = false; Construir(); }
    private void OnDestroy() => PainelAberto = false;

    private void Update()
    {
        if (painelAviso.activeSelf && Time.time > avisoAte) painelAviso.SetActive(false);
        if (painelFala.activeSelf && Time.time > falaAte) painelFala.SetActive(false);
        // tela mais estreita que 16:9: mantém a largura de 1920 (os painéis não se sobrepõem);
        // mais larga: mantém a altura de 1080 (sobra espaço dos lados)
        if (escala != null) escala.matchWidthOrHeight = (float)Screen.width / Mathf.Max(1, Screen.height) < 16f / 9f ? 0f : 1f;
        for (int i = 0; i < 4; i++)
        {
            if (fundoRec[i] == null) continue;
            piscaRec[i] = Mathf.Max(0f, piscaRec[i] - Time.deltaTime * 0.8f);
            float p = piscaRec[i] * (0.6f + 0.4f * Mathf.Sin(Time.time * 18f));
            fundoRec[i].color = Color.Lerp(new Color(1, 1, 1, 0.06f), new Color(corPisca[i].r, corPisca[i].g, corPisca[i].b, 0.6f), p);
        }
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (PainelAberto && kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) FecharInfo();
    }

    // ------------------------------------------------------------------ API
    public void Atualizar(int areas, int totalAreas, int tarefas, int totalTarefas, bool sabotador, bool temSabotador = true, int apoios = 0,
                          int saberes = 0, int totalSaberes = 0)
    {
        txtAreas.text = $"Vila protegida  {areas}/{totalAreas}";
        float f = totalAreas > 0 ? (float)areas / totalAreas : 0f;
        barraAreas.anchorMax = new Vector2(f, 1f);
        txtTarefas.text = $"Tarefas {tarefas}/{totalTarefas}   Apoios {apoios}   Pistas {saberes}/{totalSaberes}";
        txtPapel.text = sabotador
            ? "<b>INFILTRADO DA EMPRESA</b>  Finja que ajuda a comunidade e desfaça o que eles conquistarem (<b>Q</b>). Não seja descoberto!"
            : temSabotador
                ? "<b>MORADOR</b>  Tem um infiltrado da empresa entre vocês. Converse com os vizinhos e protejam a vila juntos."
                : "<b>MORADOR</b>  Converse com os vizinhos (balão amarelo = pista nova). A empresa vai tentar desfazer o que vocês conquistarem.";
        txtPapel.color = Color.white;
        painelPapel.color = sabotador ? new Color(0.55f, 0.13f, 0.10f, 0.85f) : new Color(0.16f, 0.36f, 0.27f, 0.85f);
    }

    /// Recursos da comunidade (água, saúde, terra, comida), de 0 a 1. Pisca vermelho quando cai e verde quando sobe.
    public void Recursos(float[] v)
    {
        for (int i = 0; i < 4 && i < v.Length; i++)
        {
            float x = Mathf.Clamp01(v[i]);
            if (valorRec[i] >= 0f && Mathf.Abs(x - valorRec[i]) > 0.001f)
            { piscaRec[i] = 1f; corPisca[i] = x < valorRec[i] ? Vermelho : Verde; }
            valorRec[i] = x;
            barrasDim[i].anchorMax = new Vector2(x, 1f);
            barrasDim[i].GetComponent<Image>().color = Color.Lerp(Vermelho, Verde, x);
            txtDim[i].text = $"<b>{Mathf.RoundToInt(x * 100)}%</b>";
        }
    }

    /// Fala de um morador ou da Rádio Progresso (topo da tela).
    public void Fala(string quem, string texto, Color cor, float segundos = 7f)
    {
        txtFalaQuem.text = quem; txtFalaQuem.color = cor;
        txtFalaTexto.text = "\u201C" + texto + "\u201D";
        painelFala.SetActive(true);
        falaAte = Time.time + segundos;
    }

    /// Painel de tela cheia. colunas = null mostra só o texto; com 3 colunas mostra (cabeçalho, conteúdo) lado a lado.
    public void PainelInfo(string titulo, string texto, (string cab, string txt)[] colunas, string botao, System.Action aoFechar = null)
    {
        txtInfoTitulo.text = titulo;
        txtInfoTexto.text = texto ?? "";
        bool temColunas = colunas != null && colunas.Length > 0;
        colunasInfo.SetActive(temColunas);
        for (int i = 0; i < 3; i++)
        {
            bool tem = temColunas && i < colunas.Length;
            txtInfoCabecalhos[i].transform.parent.parent.gameObject.SetActive(tem);
            if (!tem) continue;
            txtInfoCabecalhos[i].text = colunas[i].cab;
            txtInfoColunas[i].text = colunas[i].txt;
        }
        var rt = txtInfoTexto.rectTransform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, temColunas ? 120 : 520);
        txtInfoBotao.text = botao;
        aoFecharInfo = aoFechar;
        painelInfo.SetActive(true);
        painelGrande.SetActive(false);          // a tela da roda substitui a mensagem grande
        PainelAberto = true;
    }

    public void FecharInfo()
    {
        painelInfo.SetActive(false);
        PainelAberto = false;
        var cb = aoFecharInfo; aoFecharInfo = null;
        cb?.Invoke();
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
        painelAviso.SetActive(true);
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
        escala = scaler;
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

        // recursos da comunidade: ícone, porcentagem e barra (estilo jogo de sobrevivência)
        var icones = new System.Collections.Generic.Dictionary<string, Sprite>();
        foreach (var sp in Resources.LoadAll<Sprite>("Animacoes/Recursos")) icones[sp.name] = sp;
        var rec = Retangulo(raiz, "Recursos", Fundo, new Vector2(24, -254), new Vector2(560, 132));
        Texto(rec, "Titulo", 17, FontStyle.Bold, Amarelo, new Vector2(16, -8), new Vector2(528, 24), TextAnchor.UpperLeft).text = "RECURSOS DA COMUNIDADE";
        for (int i = 0; i < 4; i++)
        {
            var cel = Retangulo(rec, "Recurso" + i, new Color(1, 1, 1, 0.06f), new Vector2(12 + i * 136, -36), new Vector2(128, 88));
            fundoRec[i] = cel.GetComponent<Image>();
            var ic = Retangulo(cel, "Icone", Color.white, new Vector2(6, -6), new Vector2(48, 48));
            var img = ic.GetComponent<Image>(); img.preserveAspect = true;
            if (icones.TryGetValue(IconesRecursos[i], out var spr)) img.sprite = spr; else img.color = new Color(1, 1, 1, 0.3f);
            Texto(cel, "Nome", 15, FontStyle.Bold, Cal, new Vector2(58, -8), new Vector2(68, 20), TextAnchor.UpperLeft).text = NomesRecursos[i];
            txtDim[i] = Texto(cel, "Valor", 22, FontStyle.Normal, Color.white, new Vector2(58, -28), new Vector2(68, 28), TextAnchor.UpperLeft);
            txtDim[i].supportRichText = true;
            var tr = Retangulo(cel, "Trilho", new Color(1, 1, 1, 0.15f), new Vector2(6, -66), new Vector2(116, 12));
            barrasDim[i] = Preenchimento(tr, Vermelho);
        }
        Recursos(new float[4]);

        // fala dos moradores / Rádio Progresso (topo, no centro)
        var pf = Painel(raiz, "Fala", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-450, -96), new Vector2(900, 124));
        painelFala = pf.gameObject;
        txtFalaQuem = Texto(pf, "Quem", 22, FontStyle.Bold, Verde, new Vector2(24, -10), new Vector2(852, 30), TextAnchor.UpperLeft);
        txtFalaTexto = Texto(pf, "Texto", 22, FontStyle.Italic, Cal, new Vector2(24, -42), new Vector2(852, 76), TextAnchor.UpperLeft);
        txtFalaTexto.horizontalOverflow = HorizontalWrapMode.Wrap;
        // ocupa só o espaço entre os painéis da esquerda (até x = 584) e o botão MENU, em qualquer proporção de tela
        Esticar(pf, 604, 150); Esticar(txtFalaQuem.rectTransform, 24, 24); Esticar(txtFalaTexto.rectTransform, 24, 24);
        painelFala.SetActive(false);

        // embaixo, no centro: tarefa perto de você
        painelPrompt = Painel(raiz, "Prompt", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-450, 150), new Vector2(900, 96)).gameObject;
        var rp = (RectTransform)painelPrompt.transform;
        txtPrompt = Texto(rp, "Texto", 26, FontStyle.Bold, Color.white, new Vector2(20, -10), new Vector2(860, 44), TextAnchor.MiddleCenter);
        var trilhoP = Retangulo(rp, "Barra", new Color(1, 1, 1, 0.15f), new Vector2(40, -62), new Vector2(820, 18));
        barraPrompt = Preenchimento(trilhoP, Amarelo);
        painelPrompt.SetActive(false);

        // avisos rápidos (área recuperada / sabotagem)
        // fica numa faixa própria, logo acima da fala (sem encostar nela nem nos painéis da esquerda)
        var pa = Painel(raiz, "Aviso", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-450, -24), new Vector2(900, 62));
        painelAviso = pa.gameObject;
        txtAviso = Texto(pa, "Texto", 28, FontStyle.Bold, Color.white, new Vector2(16, -6), new Vector2(868, 50), TextAnchor.MiddleCenter);
        txtAviso.horizontalOverflow = HorizontalWrapMode.Wrap;
        txtAviso.resizeTextForBestFit = true; txtAviso.resizeTextMinSize = 16; txtAviso.resizeTextMaxSize = 28;
        Esticar(pa, 604, 150); Esticar(txtAviso.rectTransform, 16, 16);
        Sombra(txtAviso);
        painelAviso.SetActive(false);

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
        // mensagem grande no alto da tela (não cobre o personagem, que fica no centro)
        painelGrande = Painel(raiz, "Vitoria", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-470, -236), new Vector2(940, 96)).gameObject;
        txtGrande = Texto((RectTransform)painelGrande.transform, "Texto", 34, FontStyle.Bold, Verde, new Vector2(20, -10), new Vector2(900, 76), TextAnchor.MiddleCenter);
        Esticar((RectTransform)painelGrande.transform, 604, 150); Esticar(txtGrande.rectTransform, 20, 20);
        txtGrande.resizeTextForBestFit = true; txtGrande.resizeTextMinSize = 20; txtGrande.resizeTextMaxSize = 34;
        txtGrande.horizontalOverflow = HorizontalWrapMode.Wrap;
        painelGrande.SetActive(false);

        ConstruirPainelInfo(raiz);
    }

    private void ConstruirPainelInfo(RectTransform raiz)
    {
        painelInfo = new GameObject("Painel Info", typeof(RectTransform), typeof(Image));
        var prt = (RectTransform)painelInfo.transform; prt.SetParent(raiz, false);
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
        painelInfo.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);          // escurece e bloqueia cliques

        var cx = Retangulo(prt, "Caixa", new Color32(26, 46, 40, 248), new Vector2(-760, 440), new Vector2(1520, 880), new Vector2(.5f, .5f));
        txtInfoTitulo = Texto(cx, "Titulo", 40, FontStyle.Bold, Amarelo, new Vector2(40, -30), new Vector2(1440, 56), TextAnchor.UpperCenter);
        txtInfoTexto = Texto(cx, "Texto", 24, FontStyle.Normal, Cal, new Vector2(70, -100), new Vector2(1380, 520), TextAnchor.UpperLeft);
        txtInfoTexto.horizontalOverflow = HorizontalWrapMode.Wrap; txtInfoTexto.supportRichText = true;

        colunasInfo = new GameObject("Colunas", typeof(RectTransform));
        var crt = (RectTransform)colunasInfo.transform; crt.SetParent(cx, false);
        crt.anchorMin = crt.anchorMax = new Vector2(0, 1); crt.pivot = new Vector2(0, 1); crt.anchoredPosition = new Vector2(40, -230); crt.sizeDelta = new Vector2(1440, 520);
        Color[] cores = { new Color32(58, 127, 196, 255), new Color32(232, 131, 58, 255), new Color32(63, 143, 90, 255) };
        for (int i = 0; i < 3; i++)
        {
            var col = Retangulo(crt, "Coluna" + i, new Color(1, 1, 1, 0.07f), new Vector2(i * 486, 0), new Vector2(468, 520));
            var cab = Retangulo(col, "Cab", cores[i], Vector2.zero, new Vector2(468, 54));
            txtInfoCabecalhos[i] = Texto(cab, "T", 24, FontStyle.Bold, Color.white, new Vector2(0, 0), new Vector2(468, 54), TextAnchor.MiddleCenter);
            txtInfoColunas[i] = Texto(col, "Txt", 21, FontStyle.Normal, Cal, new Vector2(20, -68), new Vector2(428, 440), TextAnchor.UpperLeft);
            txtInfoColunas[i].horizontalOverflow = HorizontalWrapMode.Wrap; txtInfoColunas[i].supportRichText = true;
        }

        var bt = Retangulo(cx, "Botao", new Color32(232, 131, 58, 255), new Vector2(610, -790), new Vector2(300, 70));
        bt.GetComponent<Image>().raycastTarget = true;
        txtInfoBotao = Texto(bt, "T", 28, FontStyle.Bold, Color.white, Vector2.zero, new Vector2(300, 70), TextAnchor.MiddleCenter);
        bt.gameObject.AddComponent<Button>().onClick.AddListener(FecharInfo);
        painelInfo.SetActive(false);
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

    /// Estica na horizontal dentro do pai, com margens fixas à esquerda e à direita (mantém altura e posição vertical).
    private static void Esticar(RectTransform rt, float esquerda, float direita)
    {
        float y = rt.anchoredPosition.y, h = rt.sizeDelta.y;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
        rt.offsetMin = new Vector2(esquerda, y - h);
        rt.offsetMax = new Vector2(-direita, y);
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
