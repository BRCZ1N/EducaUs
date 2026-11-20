using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Cérebro da partida ambiental. Coloque UM na cena Game (objeto vazio "GerenciadorAmbiental").
///
/// • Acha sozinho todas as AreaAmbiental e todos os pontos "Tarefa - ..." montados pelo MontadorLocal.
/// • Cada tarefa pertence à área do mesmo local (a que contém o ponto ou a mais próxima).
///   Uma área fica RECUPERADA quando todas as suas tarefas estão feitas.
/// • O sabotador, perto de uma tarefa já feita, aperta Q e desfaz essa tarefa: a área volta a ficar poluída.
/// • Progresso = áreas recuperadas / total de áreas  →  a LuzCrepusculo lê esse valor e vai clareando.
/// • Tudo fica nas Room Custom Properties do Photon ("T|Local_X/Nome da tarefa" = true, "SAB" = actor do sabotador),
///   então quem entra no meio da partida já recebe o estado certo. Fora de sala (teste offline) funciona igual, só local.
/// </summary>
public class GerenciadorAmbiental : MonoBehaviourPunCallbacks
{
    public static GerenciadorAmbiental Instancia { get; private set; }

    [Header("Tarefas")]
    [Tooltip("Quanto tempo segurando E para concluir uma tarefa.")]
    [SerializeField] private float segundosPorTarefa = 3f;

    [Header("Sabotador")]
    [Tooltip("O dono da sala sorteia o sabotador quando a cena Game abre (se ainda não houver um).")]
    [SerializeField] private bool sortearSabotadorAoIniciar = true;
    [Tooltip("Com menos jogadores que isso não há sabotador humano: a 'pressão econômica' sabota sozinha.")]
    [SerializeField] private int jogadoresParaSabotador = 3;
    [SerializeField] private float recargaSabotagem = 45f;
    [Tooltip("Só para testar sem Photon: você joga como sabotador.")]
    [SerializeField] private bool sabotadorOffline = false;

    [Header("Partida")]
    [Tooltip("Duração da partida em minutos. Se o tempo acabar com áreas poluídas, o sabotador vence. 0 = sem limite.")]
    [SerializeField] private float minutosDePartida = 10f;

    [Header("Teste")]
    [Tooltip("1 = conclui todas as tarefas · 2 = desfaz todas · 3 = alterna sabotador (só offline)")]
    [SerializeField] private bool atalhosDeTeste = true;

    public event Action<float> ProgressoMudou;          // 0..1
    public event Action<string, bool> AreaMudou;        // nome, recuperada
    public event Action Vitoria;

    public float ProgressoAreas { get; private set; }
    public int AreasRecuperadas { get; private set; }
    public int TotalAreas { get; private set; }
    public int TarefasFeitas { get; private set; }
    public int TotalTarefas { get; private set; }
    public bool PartidaVencida { get; private set; }

    public bool SouSabotador
    {
        get
        {
            if (!PhotonNetwork.InRoom) return sabotadorOffline;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB, out object v)
                   && v is int ator && ator == PhotonNetwork.LocalPlayer.ActorNumber;
        }
    }

    private const string PREFIXO = "T|";
    private const string CHAVE_SAB = "SAB";
    private const string CHAVE_INICIO = "INI";

    /// A sabotagem não é "uma pessoa má": cada aviso mostra a causa política e econômica da degradação.
    private static readonly Dictionary<string, string> AvisoSabotagem = new Dictionary<string, string>
    {
        { "Bacia de rejeitos", "A licença da mineradora foi flexibilizada." },
        { "Lixão", "O contrato da coleta seletiva foi cancelado." },
        { "Rio", "A empresa voltou a despejar esgoto no rio." },
        { "Floresta", "Uma queimada criminosa atingiu a mata." },
        { "Fazenda", "O agrotóxico proibido voltou à lavoura." },
        { "Vila", "O bairro ficou de fora do plano de saneamento." },
        { "Estação", "A verba do saneamento foi cortada." },
    };

    private readonly List<AreaAmbiental> areas = new List<AreaAmbiental>();
    private readonly List<PontoDeTarefa> pontos = new List<PontoDeTarefa>();
    private readonly Dictionary<AreaAmbiental, List<PontoDeTarefa>> tarefasDaArea = new Dictionary<AreaAmbiental, List<PontoDeTarefa>>();
    private readonly HashSet<string> feitas = new HashSet<string>();

    private HudAmbiental hud;
    private PontoDeTarefa pontoAtual;
    private Morador moradorAtual;
    private float dicaColetiva;
    private Comunidade comunidade;
    private float segurando;
    private float proximaSabotagem;
    private bool iniciado;
    private float inicioOffline;
    private bool tempoAcabou;

    /// Segundos que faltam na partida (negativo = sem limite).
    public float TempoRestante
    {
        get
        {
            if (minutosDePartida <= 0f) return -1f;
            double decorrido;
            if (PhotonNetwork.InRoom)
            {
                if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_INICIO, out object v) || !(v is double ini)) return minutosDePartida * 60f;
                decorrido = PhotonNetwork.Time - ini;
            }
            else decorrido = Time.time - inicioOffline;
            return Mathf.Max(0f, minutosDePartida * 60f - (float)decorrido);
        }
    }

    // ------------------------------------------------------------------ ciclo de vida
    private void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;
        hud = GetComponent<HudAmbiental>();
        if (hud == null) hud = gameObject.AddComponent<HudAmbiental>();
        if (GetComponent<MenuPartida>() == null) gameObject.AddComponent<MenuPartida>();   // menu do Esc
        if (GetComponent<LimiteDoMapa>() == null) gameObject.AddComponent<LimiteDoMapa>(); // parede em volta do mapa
        if (GetComponent<AnimarAgua>() == null) gameObject.AddComponent<AnimarAgua>();     // água ondulando
        if (GetComponent<ControleToque>() == null) gameObject.AddComponent<ControleToque>(); // andar com mouse/joystick
        comunidade = GetComponent<Comunidade>();
        if (comunidade == null) comunidade = gameObject.AddComponent<Comunidade>();         // moradores + pressão econômica
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
    }

    private void Start()
    {
        PrepararPontosAntigos();
        areas.AddRange(FindObjectsByType<AreaAmbiental>(FindObjectsSortMode.None));
        pontos.AddRange(FindObjectsByType<PontoDeTarefa>(FindObjectsSortMode.None).Where(p => !p.EhReuniao));
        LigarTarefasAsAreas();
        PrepararTransparencia();

        TentarSortearSabotador();

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(CHAVE_INICIO))
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CHAVE_INICIO, PhotonNetwork.Time } });
        inicioOffline = Time.time;

        LerEstadoDaSala();
        iniciado = true;
        Recalcular(silencioso: true);
        Debug.Log($"[Ambiental] {areas.Count} áreas, {pontos.Count} tarefas.");
    }

    /// Árvores e prédios altos ficam semitransparentes quando o seu personagem passa atrás deles.
    private static readonly string[] Altos = { "arvore", "palmeira", "ipe", "mangueira", "laranjeira", "celeiro", "igreja", "coreto", "chamine", "caminhao" };
    private void PrepararTransparencia()
    {
        foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr.sprite == null || sr.GetComponent<TransparenciaAtras>() != null) continue;
            string n = sr.sprite.name.ToLowerInvariant();
            if (Altos.Any(a => n.Contains(a))) sr.gameObject.AddComponent<TransparenciaAtras>();
        }
    }

    /// Locais montados antes deste script existir: os pontos "Tarefa - X" ainda não têm o componente.
    private void PrepararPontosAntigos()
    {
        foreach (var c in FindObjectsByType<CircleCollider2D>(FindObjectsSortMode.None))
            if (c.isTrigger && c.name.StartsWith("Tarefa - ") && c.GetComponent<PontoDeTarefa>() == null)
                c.gameObject.AddComponent<PontoDeTarefa>();
    }

    private void LigarTarefasAsAreas()
    {
        foreach (var a in areas) tarefasDaArea[a] = new List<PontoDeTarefa>();
        foreach (var p in pontos)
        {
            Transform raiz = PontoDeTarefa.RaizDoLocal(p.transform);
            var candidatas = areas.Where(a => PontoDeTarefa.RaizDoLocal(a.transform) == raiz).ToList();
            if (candidatas.Count == 0) continue;                       // ONG, rua: tarefa geral (sem área)
            var area = candidatas.FirstOrDefault(a => a.Contem(p.transform.position))
                       ?? candidatas.OrderBy(a => a.Distancia(p.transform.position)).First();
            p.Area = area;
            tarefasDaArea[area].Add(p);
        }
        foreach (var kv in tarefasDaArea.Where(kv => kv.Value.Count == 0))
            Debug.LogWarning($"[Ambiental] A área '{kv.Key.Nome}' não tem nenhuma tarefa e não entra na conta.");
    }

    // ------------------------------------------------------------------ estado
    private void LerEstadoDaSala()
    {
        if (!PhotonNetwork.InRoom) return;
        foreach (var kv in PhotonNetwork.CurrentRoom.CustomProperties)
            AplicarChave(kv.Key as string, kv.Value);
    }

    private void AplicarChave(string chave, object valor)
    {
        if (chave == null || !chave.StartsWith(PREFIXO)) return;
        string id = chave.Substring(PREFIXO.Length);
        if (valor is bool b && b) feitas.Add(id); else feitas.Remove(id);
    }

    /// Marca uma tarefa como feita (true) ou desfeita (false) para todos os jogadores.
    public void DefinirTarefa(string id, bool feita)
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PREFIXO + id, feita } });
            return;                                                   // OnRoomPropertiesUpdate aplica (inclusive aqui)
        }
        if (feita) feitas.Add(id); else feitas.Remove(id);
        Recalcular(silencioso: false);
    }

    public override void OnRoomPropertiesUpdate(Hashtable mudou)
    {
        if (iniciado && mudou.ContainsKey(CHAVE_SAB) && mudou[CHAVE_SAB] != null)
            hud.Aviso("Agora há um sabotador entre vocês. Fiquem atentos!", HudAmbiental.Vermelho, 5f);
        foreach (var kv in mudou) AplicarChave(kv.Key as string, kv.Value);
        if (iniciado) Recalcular(silencioso: false);
    }

    /// Dono da sala: quando a sala chega ao número mínimo de jogadores, sorteia o sabotador (uma vez).
    private void TentarSortearSabotador()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && sortearSabotadorAoIniciar
            && PhotonNetwork.CurrentRoom.PlayerCount >= jogadoresParaSabotador
            && !TemSabotadorNaSala)
            SortearSabotador();
    }

    /// Há um sabotador humano presente na sala? (sem ele, a pressão econômica da Comunidade sabota sozinha)
    public bool TemSabotadorNaSala
    {
        get
        {
            if (!PhotonNetwork.InRoom) return sabotadorOffline;
            if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB, out object v) || !(v is int ator)) return false;
            return PhotonNetwork.PlayerList.Any(p => p.ActorNumber == ator);
        }
    }

    /// Pressão econômica: desfaz uma conquista ao acaso (de preferência numa área já recuperada).
    public void SabotarAleatoria()
    {
        var feitasAgora = pontos.Where(p => p.Feita).ToList();
        if (feitasAgora.Count == 0) return;
        var preferidas = feitasAgora.Where(p => p.Area != null && p.Area.Recuperada).ToList();
        var lista = preferidas.Count > 0 ? preferidas : feitasAgora;
        DefinirTarefa(lista[UnityEngine.Random.Range(0, lista.Count)].Id, false);
    }

    public override void OnPlayerEnteredRoom(Player novo) => TentarSortearSabotador();

    public override void OnPlayerLeftRoom(Player saiu)
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB, out object v) && v is int a && a == saiu.ActorNumber)
            hud.Aviso("O sabotador saiu da partida!");
    }

    private void Recalcular(bool silencioso)
    {
        foreach (var p in pontos) p.SetFeita(feitas.Contains(p.Id));

        int rec = 0, total = 0;
        foreach (var a in areas)
        {
            var ts = tarefasDaArea[a];
            if (ts.Count == 0) continue;
            total++;
            bool recuperada = ts.All(t => t.Feita);
            if (recuperada) rec++;
            if (recuperada != a.Recuperada)
            {
                a.SetRecuperada(recuperada);
                AreaMudou?.Invoke(a.Nome, recuperada);
                if (!silencioso)
                    hud.Aviso(recuperada ? $"{a.Nome} foi recuperada pela comunidade!"
                                         : $"Sabotagem! {(AvisoSabotagem.TryGetValue(a.Nome, out var causa) ? causa : a.Nome + " voltou a ser degradada.")}",
                              recuperada ? HudAmbiental.Verde : HudAmbiental.Vermelho);
            }
        }
        AreasRecuperadas = rec; TotalAreas = total;
        TarefasFeitas = pontos.Count(p => p.Feita); TotalTarefas = pontos.Count;
        ProgressoAreas = total > 0 ? (float)rec / total : 0f;
        ProgressoMudou?.Invoke(ProgressoAreas);
        hud.Atualizar(rec, total, TarefasFeitas, TotalTarefas, SouSabotador, TemSabotadorNaSala, comunidade != null ? comunidade.Apoios : 0);

        bool venceu = total > 0 && rec == total;
        if (venceu && !PartidaVencida)
        {
            PartidaVencida = true;
            hud.Grande("A comunidade organizada recuperou a vila!", HudAmbiental.Verde);
            Vitoria?.Invoke();
        }
        else if (!venceu && PartidaVencida) { PartidaVencida = false; hud.Grande(null); }
    }

    // ------------------------------------------------------------------ jogador local
    public void Entrou(PontoDeTarefa p) { pontoAtual = p; segurando = 0f; }
    public void Entrou(Morador m) { moradorAtual = m; segurando = 0f; }
    public void Saiu(Morador m) { if (moradorAtual == m) { moradorAtual = null; segurando = 0f; } }
    public void Saiu(PontoDeTarefa p) { if (pontoAtual == p) { pontoAtual = null; segurando = 0f; } }

    private void Update()
    {
        if (!iniciado) return;
        var kb = Keyboard.current; var gp = Gamepad.current;
        bool segurandoE = (kb != null && kb.eKey.isPressed) || (gp != null && gp.buttonSouth.isPressed);
        bool apertouQ = (kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame);
        segurandoE |= hud.UsarSegurado;          // botão USAR da tela (mouse ou toque)
        apertouQ |= hud.ConsumirSabotar();       // botão SABOTAR da tela
        AtualizarBotoes();

        if (atalhosDeTeste && kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) DefinirTodas(true);
            if (kb.digit2Key.wasPressedThisFrame) DefinirTodas(false);
            if (kb.digit3Key.wasPressedThisFrame && !PhotonNetwork.InRoom)
            {
                sabotadorOffline = !sabotadorOffline;
                Recalcular(silencioso: true);
            }
        }

        float resta = TempoRestante;
        hud.Tempo(resta);
        if (resta == 0f && !PartidaVencida && !tempoAcabou)
        {
            tempoAcabou = true;
            hud.Grande("O tempo acabou: os interesses que lucram com a degradação venceram.", HudAmbiental.Vermelho);
        }

        if (MenuPartida.Aberto) { hud.Prompt(null, -1f); return; }
        if (pontoAtual == null)
        {
            if (moradorAtual != null) ConversarComMorador(segurandoE);
            else hud.Prompt(null, -1f);
            return;
        }

        if (!pontoAtual.Feita)
        {
            int apoiosUsados = 0;
            if (pontoAtual.Coletiva)
            {
                int presentes = pontoAtual.ContarJogadores();
                int faltam = pontoAtual.jogadores - presentes;
                apoiosUsados = Mathf.Clamp(faltam, 0, comunidade.Apoios);   // moradores mobilizados contam como pessoas
                if (faltam > apoiosUsados)
                {
                    segurando = 0f;
                    hud.Prompt($"{pontoAtual.nome}: precisa de {pontoAtual.jogadores} pessoas ({presentes} aqui + {comunidade.Apoios} apoio)", 0f);
                    if (dicaColetiva < Time.time)
                    {
                        hud.Aviso("Chame outros jogadores ou converse com moradores (balão) para ganhar apoios.", HudAmbiental.Cinza, 4f);
                        dicaColetiva = Time.time + 12f;
                    }
                    return;
                }
            }
            segurando = segurandoE ? segurando + Time.deltaTime : 0f;
            hud.Prompt($"Segure USAR (E): {pontoAtual.nome}", segurando / segundosPorTarefa);
            if (segurando >= segundosPorTarefa)
            {
                segurando = 0f;
                if (SouSabotador) hud.Aviso("(você fingiu fazer a tarefa)", HudAmbiental.Cinza);
                else
                {
                    if (apoiosUsados > 0)
                    {
                        comunidade.Gastar(apoiosUsados);
                        hud.Aviso($"{apoiosUsados} morador(es) participaram da ação!", HudAmbiental.Verde);
                    }
                    DefinirTarefa(pontoAtual.Id, true);
                    Recalcular(silencioso: true);
                }
            }
        }
        else if (SouSabotador)
        {
            float falta = proximaSabotagem - Time.time;
            hud.Prompt(falta > 0 ? $"Sabotar em {Mathf.CeilToInt(falta)}s" : $"SABOTAR (Q): \"{pontoAtual.nome}\"", -1f);
            if (apertouQ && falta <= 0)
            {
                DefinirTarefa(pontoAtual.Id, false);
                proximaSabotagem = Time.time + recargaSabotagem;
            }
        }
        else hud.Prompt($"Concluída: {pontoAtual.nome}", -1f);
    }

    private const float SegundosConversa = 1.5f;

    /// Acende/apaga os botões redondos da tela conforme o que está perto do jogador.
    private void AtualizarBotoes()
    {
        bool menu = MenuPartida.Aberto;
        bool usar = false; string rotulo = "USAR"; float prog = 0f;
        if (!menu)
        {
            if (pontoAtual != null && !pontoAtual.Feita) { usar = true; prog = segurando / segundosPorTarefa; }
            else if (pontoAtual == null && moradorAtual != null && moradorAtual.Disponivel && comunidade.Apoios < comunidade.MaximoDeApoios)
            { usar = true; rotulo = "FALAR"; prog = segurando / SegundosConversa; }
        }
        hud.BotaoUsar(usar, rotulo, prog);
        float falta = proximaSabotagem - Time.time;
        hud.BotaoSabotar(SouSabotador, !menu && pontoAtual != null && pontoAtual.Feita && falta <= 0, Mathf.CeilToInt(Mathf.Max(0f, falta)));
    }
    private void ConversarComMorador(bool segurandoE)
    {
        var m = moradorAtual;
        if (!m.Disponivel) { segurando = 0f; hud.Prompt($"{m.Nome} já está mobilizado(a). Volte daqui a pouco.", -1f); return; }
        if (comunidade.Apoios >= comunidade.MaximoDeApoios)
        {
            segurando = 0f;
            hud.Prompt($"Você já tem {comunidade.Apoios} apoios: leve-os a uma tarefa coletiva.", -1f);
            return;
        }
        segurando = segurandoE ? segurando + Time.deltaTime : 0f;
        hud.Prompt($"Segure FALAR (E): {m.Nome}, {m.Papel}", segurando / SegundosConversa);
        if (segurando >= SegundosConversa)
        {
            segurando = 0f;
            if (comunidade.Convidar(m))
            {
                hud.Aviso($"{m.Nome} vai apoiar a mobilização! Apoios: {comunidade.Apoios}", HudAmbiental.Verde);
                Recalcular(silencioso: true);
            }
        }
    }

    // ------------------------------------------------------------------ partida
    /// Dono da sala: escolhe um jogador aleatório como sabotador.
    public void SortearSabotador()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        var jogadores = PhotonNetwork.PlayerList;
        int ator = jogadores[UnityEngine.Random.Range(0, jogadores.Length)].ActorNumber;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CHAVE_SAB, ator } });
    }

    /// Dono da sala: zera todas as tarefas e sorteia outro sabotador (nova rodada).
    public void ReiniciarPartida()
    {
        if (PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var limpar = new Hashtable();
            foreach (var p in pontos) limpar[PREFIXO + p.Id] = null;    // null apaga a propriedade
            limpar[CHAVE_INICIO] = PhotonNetwork.Time;                  // o relógio recomeça
            PhotonNetwork.CurrentRoom.SetCustomProperties(limpar);
            SortearSabotador();
        }
        else { feitas.Clear(); inicioOffline = Time.time; tempoAcabou = false; hud.Grande(null); Recalcular(silencioso: true); }
    }

    private void DefinirTodas(bool feita)
    {
        if (PhotonNetwork.InRoom)
        {
            var h = new Hashtable();
            foreach (var p in pontos) h[PREFIXO + p.Id] = feita;
            PhotonNetwork.CurrentRoom.SetCustomProperties(h);
        }
        else
        {
            foreach (var p in pontos) { if (feita) feitas.Add(p.Id); else feitas.Remove(p.Id); }
            Recalcular(silencioso: false);
        }
    }
}
