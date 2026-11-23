using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Minigames das tarefas (como no Among Us): ao usar uma tarefa abre uma janela com um jogo curto.
/// Tipos: Separar (arrastar para o lugar certo), Clicar (todos os alvos), Encher (soltar na faixa),
/// Escolher (marcar as opções certas) e Ordem (passos na ordem certa).
/// Cada tarefa tem o seu conteúdo na tabela Config abaixo. O GerenciadorAmbiental adiciona este componente sozinho.
/// Ícones: Resources/Animacoes/Minigame (rode Tools → Vila → Preparar animações).
/// </summary>
public class MinigameTarefa : MonoBehaviour
{
    public static bool Aberto { get; private set; }

    enum Tipo { Separar, Clicar, Encher, Escolher, Ordem }

    class Config
    {
        public Tipo tipo; public string instrucao;
        public (string icone, int destino)[] itens;     // Separar: ícone e índice do destino certo
        public string[] destinos;                       // Separar: nomes dos destinos
        public Color[] coresDestino;
        public string alvo, alvoFeito; public int quantidade;   // Clicar
        public (string texto, bool certo)[] opcoes;     // Escolher
        public string[] passos;                         // Ordem (já na ordem certa)
    }

    static readonly Color Azul = new Color32(58, 127, 196, 255), Vermelho = new Color32(217, 72, 59, 255),
        Amarelo = new Color32(232, 179, 60, 255), Verde = new Color32(63, 143, 90, 255), Marrom = new Color32(122, 82, 50, 255),
        Cinza = new Color32(110, 110, 110, 255);

    // Conteúdo de cada tarefa. Versão decolonial: as respostas certas valorizam o saber de quem vive no território
    // e apontam os responsáveis; as erradas repetem o discurso do "Desenvolvimento" ou culpam a comunidade.
    static readonly Dictionary<string, Config> Tarefas = new Dictionary<string, Config>
    {
        // ---------- conversar e se organizar
        ["Roda de conversa com os moradores"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "Por que a água, a pesca e a saúde pioraram? Marque o que os moradores descobriram conversando.",
            opcoes = new[] { ("A barragem e o porto da empresa", true), ("Licenças dadas sem consultar a comunidade", true),
                             ("Laudos que ignoram quem vive aqui", true), ("O lucro vai para fora, o dano fica aqui", true),
                             ("Os moradores não sabem cuidar do ambiente", false), ("É só cada um fazer a sua parte", false) },
        },
        ["Assembleia da associação de moradores"] = new Config
        {
            tipo = Tipo.Ordem, instrucao = "Organize a assembleia na ordem certa.",
            passos = new[] { "Abrir a roda com os mais velhos", "Ouvir quem foi prejudicado", "Decidir juntos as ações", "Escolher quem leva às comunidades vizinhas" },
        },
        // ---------- informar
        ["Laudo da comunidade"] = new Config
        { tipo = Tipo.Encher, instrucao = "Dona Lia mostrou onde o marisco morreu. Segure para colher a amostra e solte na faixa verde." },
        ["Rastrear o lixo industrial da empresa"] = new Config
        {
            tipo = Tipo.Separar, instrucao = "Dona Cida conhece a rota dos caminhões. Separe o lixo das casas do rejeito que a empresa despeja no lixão.",
            destinos = new[] { "LIXO DA COMUNIDADE (reciclar)", "REJEITO DA EMPRESA (denunciar)" }, coresDestino = new[] { Verde, Vermelho },
            itens = new[] { ("garrafa", 0), ("lata", 0), ("papel", 0), ("casca", 0), ("tambor", 1), ("tambor", 1), ("agrotoxico", 1) },
        },
        ["Monitoramento comunitário da água"] = new Config
        { tipo = Tipo.Clicar, instrucao = "Marque no mapa da água cada ponto com peixe morto. Quem monitora o rio é a comunidade.", alvo = "peixe_morto", alvoFeito = "registro", quantidade = 7 },
        ["Rádio comunitária"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "Joca trouxe as gravações. O que vai ao ar na rádio comunitária?",
            opcoes = new[] { ("Depoimento da marisqueira sobre a lama", true), ("Convite para a assembleia", true),
                             ("Resultado do laudo da comunidade", true), ("Os direitos de quem foi prejudicado", true),
                             ("Propaganda da Rádio Progresso", false), ("Boato sem fonte", false) },
        },
        // ---------- cobrar direitos
        ["Ato cultural na portaria da mineradora"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "A cultura também marcha. O que a comunidade leva ao ato?",
            opcoes = new[] { ("Samba de roda e cantigas da comunidade", true), ("Faixa: Território não se vende", true),
                             ("Depoimentos dos moradores no microfone", true), ("Faixa: Lama não é progresso", true),
                             ("Faixa: Obrigado pelos empregos, empresa", false), ("Pedir desculpas por atrapalhar", false) },
        },
        ["Denúncia ao Ministério Público"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "Seu Tonho contou a memória do rio. O que vai na denúncia?",
            opcoes = new[] { ("Depoimentos de pescadores e marisqueiras", true), ("O laudo da comunidade", true),
                             ("Fotos do cano e dos peixes mortos", true), ("Pedido de reparação às famílias", true),
                             ("Só o laudo da empresa", false), ("Pedido para a comunidade sair dali", false) },
        },
        ["Demarcar as terras da comunidade"] = new Config
        { tipo = Tipo.Clicar, instrucao = "Dona Marta lembra onde terminam as terras da comunidade. Finque um marco em cada lugar.", alvo = "marco_vazio", alvoFeito = "marco", quantidade = 6 },
        ["Brigada comunitária"] = new Config
        { tipo = Tipo.Clicar, instrucao = "O fogo começou na cerca da empresa. A brigada da comunidade apaga todos os focos.", alvo = "chama", alvoFeito = "", quantidade = 7 },
        ["Fiscalizar a obra de saneamento"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "O que o conselho popular confere na obra de saneamento?",
            opcoes = new[] { ("Se a rede chega até a vila", true), ("Quanto dinheiro foi gasto", true), ("O prazo prometido", true),
                             ("Se a comunidade foi consultada", true), ("A cor do capacete dos operários", false), ("Se o prefeito saiu bem na foto", false) },
        },
        // ---------- juntar comunidades
        ["Audiência pública com as comunidades vizinhas"] = new Config
        {
            tipo = Tipo.Escolher, instrucao = "Várias comunidades prejudicadas pela empresa se juntaram. O que levar à audiência?",
            opcoes = new[] { ("Comunidades vizinhas, lado a lado", true), ("Depoimentos e o laudo da comunidade", true),
                             ("Exigir que a comunidade seja consultada antes", true), ("Pedido de reparação e saneamento", true),
                             ("Aceitar a proposta da empresa sem discutir", false), ("Deixar só os técnicos falarem", false) },
        },
        // ---------- trabalho e renda
        ["Banco de sementes da comunidade"] = new Config
        {
            tipo = Tipo.Separar, instrucao = "Seu Zé guarda as sementes da avó. O que fica no banco da comunidade e o que volta para a empresa?",
            destinos = new[] { "BANCO DA COMUNIDADE", "DEVOLVER À EMPRESA" }, coresDestino = new[] { Verde, Vermelho },
            itens = new[] { ("semente_crioula", 0), ("semente_crioula", 0), ("composto", 0), ("esterco", 0),
                            ("semente_patente", 1), ("semente_patente", 1), ("agrotoxico", 1) },
        },
        ["Cooperativa de catadores"] = new Config
        {
            tipo = Tipo.Ordem, instrucao = "Clique nos passos na ordem certa para criar a cooperativa.",
            passos = new[] { "Reunir os catadores", "Decidir as regras em assembleia", "Registrar a cooperativa", "Cobrar um contrato justo da prefeitura" },
        },
        ["Cooperativa agroecológica"] = new Config
        {
            tipo = Tipo.Ordem, instrucao = "Clique nos passos na ordem certa.",
            passos = new[] { "Reunir as famílias", "Trocar sementes e saberes", "Montar a feira agroecológica", "Vender direto, sem atravessador" },
        },
    };

    // ------------------------------------------------------------------ estado
    private Canvas canvas;
    private RectTransform area;
    private Text txtTitulo, txtInstrucao, txtAviso;
    private GameObject janela;
    private Action aoConcluir;
    private Font fonte;
    private readonly Dictionary<string, Sprite> icones = new Dictionary<string, Sprite>();
    private int restantes;

    private void Awake()
    {
        Aberto = false;
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Minigame")) icones[s.name] = s;
        Construir();
        janela.SetActive(false);
    }

    private void OnDestroy() => Aberto = false;

    /// Abre o minigame da tarefa. aoConcluir é chamado quando o jogador vence.
    public void Abrir(string tarefa, Action aoConcluir)
    {
        this.aoConcluir = aoConcluir;
        if (!Tarefas.TryGetValue(tarefa, out var cfg))
            cfg = new Config { tipo = Tipo.Encher, instrucao = "Segure e solte na faixa verde." };
        foreach (Transform f in area) Destroy(f.gameObject);
        txtTitulo.text = tarefa;
        txtInstrucao.text = cfg.instrucao;
        txtAviso.text = "";
        janela.SetActive(true);
        Aberto = true;
        switch (cfg.tipo)
        {
            case Tipo.Separar: MontarSeparar(cfg); break;
            case Tipo.Clicar: MontarClicar(cfg); break;
            case Tipo.Encher: MontarEncher(); break;
            case Tipo.Escolher: MontarEscolher(cfg); break;
            case Tipo.Ordem: MontarOrdem(cfg); break;
        }
    }

    public void Fechar()
    {
        StopAllCoroutines();
        janela.SetActive(false);
        Aberto = false;
    }

    private void Vencer()
    {
        txtAviso.color = new Color32(150, 220, 120, 255);
        txtAviso.text = "Feito!";
        StartCoroutine(FecharDepois());
    }

    private IEnumerator FecharDepois()
    {
        yield return new WaitForSeconds(0.8f);
        Fechar();
        aoConcluir?.Invoke();
    }

    private void Errou(string msg)
    {
        txtAviso.color = new Color32(255, 140, 110, 255);
        txtAviso.text = msg;
    }

    // ------------------------------------------------------------------ SEPARAR (arrastar)
    private void MontarSeparar(Config c)
    {
        int n = c.destinos.Length;
        float larg = 780f / n;
        var alvos = new List<RectTransform>();
        for (int i = 0; i < n; i++)
        {
            var d = Caixa(area, "Destino " + c.destinos[i], c.coresDestino[i], new Vector2(-390 + larg * i + 6, -200), new Vector2(larg - 12, 140));
            Rotulo(d, c.destinos[i], 20, Color.white);
            alvos.Add(d);
        }
        restantes = c.itens.Length;
        var rng = new System.Random();
        var ordem = c.itens.OrderBy(_ => rng.Next()).ToArray();
        float passo = 760f / ordem.Length;
        for (int i = 0; i < ordem.Length; i++)
        {
            var (icone, destino) = ordem[i];
            var it = Icone(area, icone, new Vector2(-380 + passo * i + passo / 2 - 50, 60), 100);
            var arr = it.gameObject.AddComponent<Arrastavel>();
            arr.Configurar(this, alvos, destino);
        }
    }

    internal void Soltou(Arrastavel a, bool certo)
    {
        if (certo)
        {
            Destroy(a.gameObject);
            if (--restantes <= 0) Vencer();
        }
        else Errou("Esse não vai aí. Tente outro lugar.");
    }

    // ------------------------------------------------------------------ CLICAR
    private void MontarClicar(Config c)
    {
        restantes = c.quantidade;
        var rng = new System.Random();
        var usados = new List<Vector2>();
        for (int i = 0; i < c.quantidade; i++)
        {
            Vector2 p = Vector2.zero;
            for (int t = 0; t < 30; t++)
            {
                p = new Vector2(rng.Next(-370, 270), rng.Next(-230, 90));
                if (usados.All(q => Vector2.Distance(q, p) > 105)) break;
            }
            usados.Add(p);
            var img = Icone(area, c.alvo, p, 100);
            var b = img.gameObject.AddComponent<Button>();
            string feito = c.alvoFeito;
            b.onClick.AddListener(() =>
            {
                if (!b.interactable) return;
                b.interactable = false;
                if (string.IsNullOrEmpty(feito)) img.gameObject.SetActive(false);
                else if (icones.TryGetValue(feito, out var s)) img.sprite = s;
                if (--restantes <= 0) Vencer();
            });
        }
    }

    // ------------------------------------------------------------------ ENCHER (segurar e soltar na faixa)
    private void MontarEncher()
    {
        var frasco = Caixa(area, "Frasco", new Color(1, 1, 1, 0.15f), new Vector2(-60, -230), new Vector2(120, 300));
        var faixa = Caixa(frasco, "Faixa", new Color(0.3f, 0.8f, 0.3f, 0.45f), new Vector2(0, 300 * 0.70f), new Vector2(120, 300 * 0.15f));
        var nivel = Caixa(frasco, "Lama", new Color32(124, 58, 32, 255), Vector2.zero, new Vector2(120, 0));
        faixa.anchorMin = faixa.anchorMax = Vector2.zero;           // medidas a partir do fundo do frasco
        nivel.anchorMin = nivel.anchorMax = Vector2.zero;
        faixa.anchoredPosition = new Vector2(0, 300 * 0.70f); nivel.anchoredPosition = Vector2.zero;
        nivel.SetSiblingIndex(0);
        var botao = Caixa(area, "Segurar", new Color32(232, 131, 58, 255), new Vector2(120, -120), new Vector2(240, 90));
        Rotulo(botao, "SEGURAR", 28, Color.white);
        var seg = botao.gameObject.AddComponent<BotaoSegurar>();
        StartCoroutine(LoopEncher(seg, nivel));
    }

    private IEnumerator LoopEncher(BotaoSegurar seg, RectTransform nivel)
    {
        float v = 0; bool segurou = false;
        while (true)
        {
            bool segurando = seg.Segurando || (UnityEngine.InputSystem.Keyboard.current?.eKey.isPressed ?? false);
            if (segurando) { v += Time.deltaTime * 0.45f; segurou = true; }
            else if (segurou)
            {
                if (v >= 0.70f && v <= 0.85f) { nivel.sizeDelta = new Vector2(120, 300 * v); Vencer(); yield break; }
                Errou(v < 0.70f ? "Pouca amostra! Encha até a faixa verde." : "Passou da faixa! Tente de novo.");
                v = 0; segurou = false;
            }
            if (v > 1f) { Errou("Transbordou! Tente de novo."); v = 0; segurou = false; }
            nivel.sizeDelta = new Vector2(120, 300 * v);
            yield return null;
        }
    }

    // ------------------------------------------------------------------ ESCOLHER (marcar as certas)
    private void MontarEscolher(Config c)
    {
        var rng = new System.Random();
        var ops = c.opcoes.OrderBy(_ => rng.Next()).ToArray();
        var marcados = new bool[ops.Length];
        var imgs = new Image[ops.Length];
        for (int i = 0; i < ops.Length; i++)
        {
            int k = i;
            var cx = Caixa(area, "Opcao" + i, new Color(1, 1, 1, 0.12f), new Vector2(-390, 110 - i * 58), new Vector2(780, 50));
            var t = Rotulo(cx, "[  ]  " + ops[i].texto, 24, Color.white); t.alignment = TextAnchor.MiddleLeft;
            t.rectTransform.offsetMin = new Vector2(20, 0);
            imgs[i] = cx.GetComponent<Image>();
            var b = cx.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                marcados[k] = !marcados[k];
                imgs[k].color = marcados[k] ? new Color(0.3f, 0.7f, 0.4f, 0.6f) : new Color(1, 1, 1, 0.12f);
                t.text = (marcados[k] ? "[X]  " : "[  ]  ") + ops[k].texto;
                txtAviso.text = "";
            });
        }
        var conf = Caixa(area, "Confirmar", new Color32(232, 131, 58, 255), new Vector2(-120, -260), new Vector2(240, 70));
        Rotulo(conf, "CONFIRMAR", 26, Color.white);
        conf.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            bool ok = true;
            for (int i = 0; i < ops.Length; i++) if (marcados[i] != ops[i].certo) ok = false;
            if (ok) Vencer();
            else Errou(Enumerable.Range(0, ops.Length).Any(i => marcados[i] && !ops[i].certo)
                ? "Alguma opção marcada não ajuda. Pense em quem é responsável." : "Faltou marcar alguma opção.");
        });
    }

    // ------------------------------------------------------------------ ORDEM (passos)
    private void MontarOrdem(Config c)
    {
        var rng = new System.Random();
        var embaralhado = Enumerable.Range(0, c.passos.Length).OrderBy(_ => rng.Next()).ToArray();
        int proximo = 0;
        var botoes = new List<(Image img, Text txt, int idx)>();
        for (int i = 0; i < embaralhado.Length; i++)
        {
            int idx = embaralhado[i];
            var cx = Caixa(area, "Passo" + i, new Color(1, 1, 1, 0.12f), new Vector2(-390, 90 - i * 72), new Vector2(780, 62));
            var t = Rotulo(cx, c.passos[idx], 26, Color.white);
            var img = cx.GetComponent<Image>();
            botoes.Add((img, t, idx));
            var b = cx.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                if (idx == proximo)
                {
                    proximo++;
                    img.color = new Color(0.3f, 0.7f, 0.4f, 0.6f);
                    t.text = proximo + ". " + c.passos[idx];
                    b.interactable = false;
                    if (proximo >= c.passos.Length) Vencer();
                }
                else
                {
                    Errou("Ainda não é a vez desse passo. Recomece pensando na ordem.");
                    proximo = 0;
                    foreach (var (im, tx, ix) in botoes)
                    {
                        im.color = new Color(1, 1, 1, 0.12f); tx.text = c.passos[ix];
                        im.GetComponent<Button>().interactable = true;
                    }
                }
            });
        }
    }

    // ------------------------------------------------------------------ construção da janela
    private void Construir()
    {
        var cgo = new GameObject("Minigame", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

        janela = new GameObject("Janela", typeof(RectTransform), typeof(Image));
        janela.transform.SetParent(cgo.transform, false);
        var jrt = (RectTransform)janela.transform; jrt.anchorMin = Vector2.zero; jrt.anchorMax = Vector2.one; jrt.offsetMin = jrt.offsetMax = Vector2.zero;
        janela.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);          // escurece o jogo e bloqueia cliques

        var painel = Caixa((RectTransform)janela.transform, "Painel", new Color32(26, 46, 40, 245), new Vector2(-460, -350), new Vector2(920, 700), centro: true);
        txtTitulo = Texto(painel, 38, FontStyle.Bold, new Color32(255, 210, 90, 255), new Vector2(0, 300), new Vector2(860, 60));
        txtInstrucao = Texto(painel, 22, FontStyle.Normal, new Color32(241, 227, 200, 255), new Vector2(0, 245), new Vector2(860, 60));
        txtInstrucao.horizontalOverflow = HorizontalWrapMode.Wrap;
        txtAviso = Texto(painel, 24, FontStyle.Bold, Color.white, new Vector2(0, -320), new Vector2(860, 40));

        var ar = new GameObject("Area", typeof(RectTransform));
        ar.transform.SetParent(painel, false);
        area = (RectTransform)ar.transform;
        area.anchorMin = area.anchorMax = area.pivot = new Vector2(.5f, .5f); area.anchoredPosition = new Vector2(0, -10); area.sizeDelta = new Vector2(820, 520);

        var x = Caixa(painel, "Fechar", new Color32(217, 72, 59, 255), new Vector2(395, 285), new Vector2(50, 50));
        Rotulo(x, "X", 28, Color.white);
        x.gameObject.AddComponent<Button>().onClick.AddListener(Fechar);
    }

    private RectTransform Caixa(RectTransform pai, string nome, Color cor, Vector2 pos, Vector2 tam, bool centro = false)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        go.GetComponent<Image>().color = cor;
        return rt;
    }

    private Image Icone(RectTransform pai, string nome, Vector2 pos, float tam)
    {
        var rt = Caixa(pai, nome, Color.white, pos, new Vector2(tam, tam));
        var img = rt.GetComponent<Image>();
        if (icones.TryGetValue(nome, out var s)) img.sprite = s; else img.color = Cinza;
        img.preserveAspect = true;
        return img;
    }

    private Text Rotulo(RectTransform pai, string texto, int tam, Color cor)
    {
        var t = Texto(pai, tam, FontStyle.Bold, cor, Vector2.zero, Vector2.zero);
        var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        t.text = texto; t.raycastTarget = false;
        return t;
    }

    private Text Texto(RectTransform pai, int tam, FontStyle est, Color cor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
        var t = go.GetComponent<Text>(); t.font = fonte; t.fontSize = tam; t.fontStyle = est; t.color = cor;
        t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        return t;
    }
}

/// Item que pode ser arrastado até um destino (minigame Separar).
public class Arrastavel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private MinigameTarefa dono;
    private List<RectTransform> alvos;
    private int certo;
    private RectTransform rt;
    private Vector2 inicio;

    public void Configurar(MinigameTarefa d, List<RectTransform> a, int c) { dono = d; alvos = a; certo = c; rt = (RectTransform)transform; }

    public void OnBeginDrag(PointerEventData e) { inicio = rt.anchoredPosition; transform.SetAsLastSibling(); }

    public void OnDrag(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, e.position, e.pressEventCamera, out var p);
        rt.anchoredPosition = p - rt.sizeDelta / 2f;
    }

    public void OnEndDrag(PointerEventData e)
    {
        for (int i = 0; i < alvos.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(alvos[i], e.position, e.pressEventCamera))
            {
                if (i == certo) { dono.Soltou(this, true); return; }
                dono.Soltou(this, false); break;
            }
        rt.anchoredPosition = inicio;                       // volta para o lugar
    }
}
