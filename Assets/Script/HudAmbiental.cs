using UnityEngine;
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

    private Font fonte;
    private Text txtAreas, txtTarefas, txtPapel, txtPrompt, txtAviso, txtGrande;
    private RectTransform barraAreas, barraPrompt;
    private GameObject painelPrompt, painelGrande;
    private float avisoAte;

    private void Awake() => Construir();

    private void Update()
    {
        if (txtAviso.enabled && Time.time > avisoAte) txtAviso.enabled = false;
    }

    // ------------------------------------------------------------------ API
    public void Atualizar(int areas, int totalAreas, int tarefas, int totalTarefas, bool sabotador)
    {
        txtAreas.text = $"Vila recuperada  {areas}/{totalAreas}";
        float f = totalAreas > 0 ? (float)areas / totalAreas : 0f;
        barraAreas.anchorMax = new Vector2(f, 1f);
        txtTarefas.text = $"Tarefas feitas: {tarefas}/{totalTarefas}";
        txtPapel.text = sabotador ? "Você é o SABOTADOR — finja tarefas e aperte Q para sabotar" : "Você é VOLUNTÁRIO — faça as tarefas para recuperar a vila";
        txtPapel.color = sabotador ? Vermelho : Verde;
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

    public void Grande(string texto)
    {
        painelGrande.SetActive(texto != null);
        if (texto != null) txtGrande.text = texto;
    }

    // ------------------------------------------------------------------ construção
    private void Construir()
    {
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("HUD Ambiental", typeof(Canvas), typeof(CanvasScaler));
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
        txtTarefas = Texto(topo, "Tarefas", 22, FontStyle.Normal, Amarelo, new Vector2(20, -96), new Vector2(520, 30), TextAnchor.UpperLeft);
        txtPapel = Texto(raiz, "Papel", 22, FontStyle.Bold, Verde, new Vector2(24, -184), new Vector2(900, 30), TextAnchor.UpperLeft,
                         new Vector2(0, 1));
        Sombra(txtPapel);

        // embaixo, no centro: tarefa perto de você
        painelPrompt = Painel(raiz, "Prompt", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-330, 150), new Vector2(660, 96)).gameObject;
        var rp = (RectTransform)painelPrompt.transform;
        txtPrompt = Texto(rp, "Texto", 28, FontStyle.Bold, Color.white, new Vector2(20, -10), new Vector2(620, 44), TextAnchor.MiddleCenter);
        var trilhoP = Retangulo(rp, "Barra", new Color(1, 1, 1, 0.15f), new Vector2(40, -62), new Vector2(580, 18));
        barraPrompt = Preenchimento(trilhoP, Amarelo);
        painelPrompt.SetActive(false);

        // avisos rápidos (área recuperada / sabotagem)
        txtAviso = Texto(raiz, "Aviso", 34, FontStyle.Bold, Color.white, new Vector2(-600, -40), new Vector2(1200, 50), TextAnchor.MiddleCenter,
                         new Vector2(0.5f, 1));
        Sombra(txtAviso);
        txtAviso.enabled = false;

        // vitória
        painelGrande = Painel(raiz, "Vitoria", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-520, 90), new Vector2(1040, 180)).gameObject;
        txtGrande = Texto((RectTransform)painelGrande.transform, "Texto", 54, FontStyle.Bold, Verde, new Vector2(20, -20), new Vector2(1000, 140), TextAnchor.MiddleCenter);
        painelGrande.SetActive(false);
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
