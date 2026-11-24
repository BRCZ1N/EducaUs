using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Cérebro da partida ambiental (versão decolonial: a comunidade atingida defende o próprio território). Coloque UM na cena Game (objeto vazio "GerenciadorAmbiental").
///
/// • Acha sozinho todas as AreaAmbiental e todos os pontos "Tarefa - ..." montados pelo MontadorLocal.
/// • Cada tarefa pertence à área do mesmo local (a que contém o ponto ou a mais próxima).
///   Uma área fica RECUPERADA quando todas as suas tarefas estão feitas.
/// • O sabotador, perto de uma tarefa já feita, aperta Q e desfaz essa tarefa: a área volta a ficar poluída.
/// • Progresso = áreas recuperadas / total de áreas  →  a LuzCrepusculo lê esse valor e vai clareando.
/// • Tudo fica nas Room Custom Properties do Photon ("T|Local_X/Nome da tarefa" = true, "SAB" = actor do sabotador),
///   então quem entra no meio da partida já recebe o estado certo. Fora de sala (teste offline) funciona igual, só local.
/// • Saberes dos moradores ("S|Nome do morador" = true) são da comunidade inteira: um escuta, todos podem usar.
/// • No fim (vitória ou tempo), abre a roda de conversa no ciclo de Paulo Freire: Realidade → Consciência crítica → Práxis.
/// </summary>
public class GerenciadorAmbiental : MonoBehaviourPunCallbacks
{
    public static GerenciadorAmbiental Instancia { get; private set; }


    [Header("Sabotador")]
    [Tooltip("O dono da sala sorteia o sabotador quando a cena Game abre (se ainda não houver um).")]
    [SerializeField] private bool sortearSabotadorAoIniciar = true;
    [Tooltip("Com menos jogadores que isso não há sabotador humano: a 'pressão econômica' sabota sozinha.")]
    [SerializeField] private int jogadoresParaSabotador = 3;
    [SerializeField] private float recargaSabotagem = 45f;
    [Tooltip("Só para testar sem Photon: você joga como sabotador.")]
    [SerializeField] private bool sabotadorOffline = false;

    [Header("Partida")]
    [Tooltip("Duração da partida em minutos (padrão 20). Se o tempo acabar com áreas atingidas, a empresa vence. 0 = sem limite.")]
    [SerializeField] private float duracaoEmMinutos = 20f;

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
    private const string PREFIXO_SABER = "S|";
    private const string CHAVE_REINICIO = "REI";

    /// A sabotagem não é "uma pessoa má": cada aviso mostra como a colonialidade opera no território,
    /// nas três dimensões (Quijano): PODER (terra, água, trabalho), SABER (quem pode dizer o que é verdade) e SER (quem é desumanizado).
    private static readonly Dictionary<string, string[]> AvisoSabotagem = new Dictionary<string, string[]>
    {
        { "Bacia de rejeitos", new[] { "A licença da barragem foi renovada sem consultar a comunidade.",
                                       "O laudo da empresa chamou a lama de \"inerte\" e ignorou as marisqueiras.",
                                       "Na TV, os moradores foram chamados de invasores da área da empresa." } },
        { "Lixão", new[] { "Os caminhões da empresa voltaram a despejar rejeito no lixão de madrugada.",
                           "Uma campanha culpou a \"falta de educação\" dos moradores pelo lixo da indústria.",
                           "Os catadores foram tratados como lixo e expulsos do galpão." } },
        { "Rio", new[] { "O porto ganhou a outorga da água: o rio virou \"área da empresa\".",
                         "O órgão ambiental só aceitou o laudo da empresa: \"água dentro do padrão\".",
                         "Os pescadores foram chamados de atraso para o progresso." } },
        { "Floresta", new[] { "A cerca da empresa avançou sobre a mata da comunidade.",
                              "O mapa oficial apagou os nomes que os mais velhos dão aos lugares.",
                              "Disseram que a comunidade \"não usa\" a terra e por isso não tem direito a ela." } },
        { "Fazenda", new[] { "O crédito agrícola só sai para quem compra semente patenteada e veneno.",
                             "O técnico de fora disse que a semente guardada pela comunidade é \"atrasada\".",
                             "Os agricultores ouviram que \"pobre não sabe plantar\"." } },
        { "Vila", new[] { "A empresa comprou o horário da rádio comunitária.",
                          "Uma reportagem disse que os moradores \"exageram\" os problemas.",
                          "Chamaram a vila de \"atrasada\" para justificar a remoção." } },
        { "Estação", new[] { "A verba do saneamento foi desviada para a estrada do minério.",
                             "A audiência pública foi marcada longe e em linguagem técnica: ninguém entendeu.",
                             "Disseram que o bairro \"não precisa\" de saneamento porque \"sempre foi assim\"." } },
    };

    /// Recursos da comunidade no HUD: 0 Água, 1 Saúde, 2 Terra, 3 Comida (no relatório: os danos que atravessam a vida).
    private static readonly Dictionary<string, int[]> DimensoesDaArea = new Dictionary<string, int[]>
    {
        { "Bacia de rejeitos", new[] { 0, 1 } }, { "Lixão", new[] { 1, 2 } }, { "Rio", new[] { 0, 3 } },
        { "Floresta", new[] { 2 } }, { "Fazenda", new[] { 3, 2 } }, { "Vila", new[] { 2, 1 } }, { "Estação", new[] { 0, 1 } },
    };

    private readonly List<AreaAmbiental> areas = new List<AreaAmbiental>();
    private readonly List<PontoDeTarefa> pontos = new List<PontoDeTarefa>();
    private readonly Dictionary<AreaAmbiental, List<PontoDeTarefa>> tarefasDaArea = new Dictionary<AreaAmbiental, List<PontoDeTarefa>>();
    private readonly HashSet<string> feitas = new HashSet<string>();
    private readonly HashSet<string> saberes = new HashSet<string>();
    private int sabotagens;
    private bool rodaMostrada;
    private readonly float[] dimensoes = new float[4];

    /// A partida acabou (vitória da comunidade ou fim do tempo).
    public bool PartidaEncerrada => PartidaVencida || tempoAcabou || fimAssembleia;
    private bool fimAssembleia;
    private string tituloFim;

    /// Última vez (Time.time) que uma conquista foi desfeita, e onde: a Assembleia usa para liberar o DENUNCIAR.
    public float UltimaSabotagem { get; private set; } = -999f;
    public string UltimaAreaSabotada { get; private set; } = "";

    /// Ator do infiltrado (0 = nenhum).
    public int AtorSabotador =>
        PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB, out object v) && v is int a ? a : 0;

    /// Fim decidido na assembleia: o infiltrado foi descoberto (comunidade vence) ou a comunidade se desarticulou.
    public void EncerrarPorAssembleia(bool comunidadeVenceu, string titulo)
    {
        if (PartidaEncerrada) return;
        fimAssembleia = true; tituloFim = titulo;
        hud.Grande(titulo, comunidadeVenceu ? HudAmbiental.Verde : HudAmbiental.Vermelho);
        Invoke(comunidadeVenceu ? nameof(RodaVitoria) : nameof(RodaDerrota), 3f);
    }
    public bool SaberConhecido(string morador) => saberes.Contains(morador);
    public int Saberes => saberes.Count;

    private HudAmbiental hud;
    private PontoDeTarefa pontoAtual;
    private Morador moradorAtual;
    private float dicaColetiva;
    private Comunidade comunidade;
    private MinigameTarefa minigame;
    private bool usarAntes;
    private float segurando;
    private float proximaSabotagem;
    private bool iniciado;
    private float inicioOffline;
    private bool iniciadaOffline;
    private string papelMostrado;

    /// A partida só começa quando o dono da sala usa o painel INICIAR no meio da associação de moradores.
    public bool PartidaIniciada => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(CHAVE_INICIO) : iniciadaOffline;
    private static bool SouDono => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
    public static readonly Vector2 PosicaoInicio = new Vector2(50f, 70.6f);   // recepção da associação de moradores
    private SpriteRenderer srInicio; private Sprite[] sprInicio;
    private bool pertoDoInicio; private float segurandoInicio;
    private const float SegundosInicio = 1.5f;
    private bool tempoAcabou;
    private float tempoCongelado = -1f;     // o relógio para quando a comunidade vence

    /// Segundos que faltam na partida (negativo = sem limite).
    public float TempoRestante
    {
        get
        {
            if (duracaoEmMinutos <= 0f) return -1f;
            double decorrido;
            if (PhotonNetwork.InRoom)
            {
                if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_INICIO, out object v) || !(v is double ini)) return duracaoEmMinutos * 60f;
                decorrido = PhotonNetwork.Time - ini;
            }
            else { if (!iniciadaOffline) return duracaoEmMinutos * 60f; decorrido = Time.time - inicioOffline; }
            return Mathf.Max(0f, duracaoEmMinutos * 60f - (float)decorrido);
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
        if (GetComponent<MapaPartida>() == null) gameObject.AddComponent<MapaPartida>();   // mapa (M / Tab / botão MAPA)
        if (GetComponent<LimiteDoMapa>() == null) gameObject.AddComponent<LimiteDoMapa>(); // parede em volta do mapa
        if (GetComponent<AnimarAgua>() == null) gameObject.AddComponent<AnimarAgua>();     // água ondulando
        if (GetComponent<ControleToque>() == null) gameObject.AddComponent<ControleToque>(); // andar com mouse/joystick
        minigame = GetComponent<MinigameTarefa>();
        if (minigame == null) minigame = gameObject.AddComponent<MinigameTarefa>();         // minigames das tarefas
        comunidade = GetComponent<Comunidade>();
        if (comunidade == null) comunidade = gameObject.AddComponent<Comunidade>();
        if (GetComponent<Assembleia>() == null) gameObject.AddComponent<Assembleia>();      // assembleia e votação (multiplayer)         // moradores + pressão econômica
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

        if (PartidaIniciada) TentarSortearSabotador();       // (a partida só começa pelo painel INICIAR)
        CriarPainelInicio();

        LerEstadoDaSala();
        iniciado = true;
        Recalcular(silencioso: true);
        Debug.Log($"[Ambiental] {areas.Count} áreas, {pontos.Count} tarefas.");
        MostrarApresentacao();
    }

    /// Apresentação do território ao entrar na partida.
    private void MostrarApresentacao()
    {
        hud.PainelInfo("Vila Beira-Rio",
            "Na Vila Beira-Rio vivem pescadores, marisqueiras, agricultores e catadores. A família de muita gente mora aqui " +
            "há gerações. Um dia chegaram a mineradora e o porto, prometendo progresso e emprego. Hoje a água está suja, " +
            "o peixe sumiu, a roça foi cercada e as crianças vivem doentes.\n\n" +
            "Vocês são moradores da vila e vão lutar por ela juntos.\n\n" +
            "•  <b>Converse com os vizinhos</b> (balão amarelo). Cada um tem uma pista que libera tarefas para todos.\n" +
            "•  <b>Ações coletivas</b> (marca azul) precisam de mais gente: chame outros jogadores ou mobilize moradores.\n" +
            "•  Cuide dos <b>recursos</b> da comunidade: água, saúde, terra e comida.\n" +
            "•  Com 3 ou mais jogadores, um <b>infiltrado da empresa</b> vai tentar desfazer o que vocês conquistarem.\n\n" +
            "Quando todos estiverem prontos, o <b>dono da sala</b> inicia a partida no painel <b>INICIAR</b>, no meio da associação de moradores.",
            null, "COMEÇAR");
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
        if (chave == null) return;
        if (chave.StartsWith(PREFIXO_SABER))
        {
            string quem = chave.Substring(PREFIXO_SABER.Length);
            if (valor is bool sb && sb) saberes.Add(quem); else saberes.Remove(quem);
            return;
        }
        if (!chave.StartsWith(PREFIXO)) return;
        string id = chave.Substring(PREFIXO.Length);
        if (valor is bool b && b) feitas.Add(id);
        else
        {
            if (iniciado && valor is bool && feitas.Contains(id))                   // false = desfeita (null = reinício)
            {
                sabotagens++;
                var pt = pontos.FirstOrDefault(x => x.Id == id);
                UltimaSabotagem = Time.time;
                UltimaAreaSabotada = pt != null && pt.Area != null ? pt.Area.Nome : "algum lugar";
            }
            feitas.Remove(id);
        }
    }

    /// Um morador contou o seu saber: ele passa a ser de toda a comunidade (todos os jogadores).
    public void RegistrarSaber(string morador)
    {
        if (PhotonNetwork.InRoom) { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PREFIXO_SABER + morador, true } }); return; }
        saberes.Add(morador);
        Recalcular(silencioso: true);
    }

    /// Marca uma tarefa como feita (true) ou desfeita (false) para todos os jogadores.
    public void DefinirTarefa(string id, bool feita)
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PREFIXO + id, feita } });
            return;                                                   // OnRoomPropertiesUpdate aplica (inclusive aqui)
        }
        if (feita) feitas.Add(id);
        else { if (feitas.Contains(id)) sabotagens++; feitas.Remove(id); }
        Recalcular(silencioso: false);
    }

    public override void OnRoomPropertiesUpdate(Hashtable mudou)
    {
        if (iniciado && mudou.ContainsKey(CHAVE_SAB) && mudou[CHAVE_SAB] != null)
            hud.Aviso("Um infiltrado da empresa está entre vocês. Fiquem atentos!", HudAmbiental.Vermelho, 5f);
        if (iniciado && mudou.ContainsKey(CHAVE_REINICIO))
        {   // o dono da sala reiniciou a partida
            sabotagens = 0; rodaMostrada = false; tempoAcabou = false; fimAssembleia = false; tituloFim = null; hud.Grande(null);
            CancelInvoke(); if (HudAmbiental.PainelAberto) hud.FecharInfo();
            hud.Aviso("A partida começou! Converse com os vizinhos e protejam a vila.", HudAmbiental.Verde, 5f);
        }
        foreach (var kv in mudou) AplicarChave(kv.Key as string, kv.Value);
        if (iniciado && mudou.ContainsKey(CHAVE_REINICIO)) sabotagens = 0;
        if (iniciado) Recalcular(silencioso: false);
    }

    /// Dono da sala: quando a sala chega ao número mínimo de jogadores, sorteia o sabotador (uma vez).
    private const string CHAVE_SAB_ANTERIOR = "SABU";   // quem foi o infiltrado na rodada anterior (para não repetir)

    /// Jogadores que podem ser sorteados: presentes (não caídos) e não dispensados pela assembleia.
    private static List<Player> Elegiveis() =>
        PhotonNetwork.PlayerList.Where(p => !p.IsInactive && !Assembleia.Dispensado(p.ActorNumber)).ToList();

    /// Sorteia entre os elegíveis, evitando repetir o infiltrado da rodada anterior (se houver outra opção).
    private static int SortearAtor(List<Player> elegiveis)
    {
        if (elegiveis.Count == 0) return 0;
        int anterior = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB_ANTERIOR, out object v) && v is int a ? a : 0;
        var opcoes = elegiveis.Count > 1 ? elegiveis.Where(p => p.ActorNumber != anterior).ToList() : elegiveis;
        return opcoes[UnityEngine.Random.Range(0, opcoes.Count)].ActorNumber;
    }

    private void TentarSortearSabotador()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && sortearSabotadorAoIniciar && PartidaIniciada && !PartidaEncerrada
            && Elegiveis().Count >= jogadoresParaSabotador
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
            return PhotonNetwork.PlayerList.Any(p => p.ActorNumber == ator && !p.IsInactive);   // caiu = não conta
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

    /// Entrou numa sala com a cena já aberta (voltou depois de cair, ou abriu a sala pelo menu): relê o estado.
    public override void OnJoinedRoom()
    {
        if (!iniciado) return;
        LerEstadoDaSala();
        Recalcular(silencioso: true);
    }

    /// Partida offline virando sala online (botão do menu): leva o progresso para a sala, que passa a ser a referência.
    public void LevarEstadoParaSala()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        var h = new Hashtable();
        foreach (var id in feitas) h[PREFIXO + id] = true;
        foreach (var sb in saberes) h[PREFIXO_SABER + sb] = true;
        if (iniciadaOffline) h[CHAVE_INICIO] = PhotonNetwork.Time - (Time.time - inicioOffline);   // o relógio continua de onde estava
        if (h.Count > 0) PhotonNetwork.CurrentRoom.SetCustomProperties(h);
        TentarSortearSabotador();
    }

    public override void OnPlayerLeftRoom(Player saiu)
    {
        if (!(PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CHAVE_SAB, out object v) && v is int a && a == saiu.ActorNumber)) return;
        if (saiu.IsInactive)
        {   // caiu: a sala guarda o lugar dele por um tempo; se não voltar, outro é sorteado
            hud.Aviso("O infiltrado da empresa caiu da partida...");
            if (PhotonNetwork.IsMasterClient) StartCoroutine(EsperarVoltaDoInfiltrado(saiu.ActorNumber));
            return;
        }
        hud.Aviso("O infiltrado da empresa saiu da partida!");
        if (PhotonNetwork.IsMasterClient) TrocarInfiltrado();
    }

    private System.Collections.IEnumerator EsperarVoltaDoInfiltrado(int ator)
    {
        yield return new WaitForSeconds(32f);
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(ator);
        if (p == null || p.IsInactive) TrocarInfiltrado();
    }

    /// O infiltrado saiu de vez: o dono da sala sorteia outro (se ainda houver gente suficiente).
    private void TrocarInfiltrado()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PartidaEncerrada) return;
        var elegiveis = Elegiveis();
        if (sortearSabotadorAoIniciar && PartidaIniciada && elegiveis.Count >= jogadoresParaSabotador)
        {
            int novo = SortearAtor(elegiveis);
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CHAVE_SAB, novo }, { CHAVE_SAB_ANTERIOR, novo } });
        }
        else PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CHAVE_SAB, null } });
    }

    private void Recalcular(bool silencioso)
    {
        foreach (var p in pontos) p.SetFeita(feitas.Contains(p.Id));

        int rec = 0, total = 0;
        var somaDim = new float[4]; var contaDim = new int[4];
        foreach (var a in areas)
        {
            var ts = tarefasDaArea[a];
            if (ts.Count == 0) continue;
            total++;
            if (DimensoesDaArea.TryGetValue(a.Nome, out var dims))
                foreach (int d in dims) { somaDim[d] += (float)ts.Count(t => t.Feita) / ts.Count; contaDim[d]++; }
            bool recuperada = ts.All(t => t.Feita);
            if (recuperada) rec++;
            if (recuperada != a.Recuperada)
            {
                a.SetRecuperada(recuperada);
                AreaMudou?.Invoke(a.Nome, recuperada);
                if (!silencioso)
                {
                    if (recuperada) hud.Aviso($"{a.Nome} foi protegida pela comunidade!", HudAmbiental.Verde);
                    else
                    {
                        int k = Mathf.Abs(sabotagens) % 3;     // igual em todos os computadores
                        string causa = AvisoSabotagem.TryGetValue(a.Nome, out var lista) ? lista[k] : a.Nome + " voltou a ser degradada.";
                        hud.Aviso($"Sabotagem! {causa}", HudAmbiental.Vermelho, 6f);
                    }
                }
            }
        }
        for (int d = 0; d < 4; d++) dimensoes[d] = contaDim[d] > 0 ? somaDim[d] / contaDim[d] : 0f;
        hud.Recursos(dimensoes);
        if (comunidade != null) comunidade.AtualizarOutdoors(nome => areas.Any(x => x.Nome == nome && x.Recuperada));
        AreasRecuperadas = rec; TotalAreas = total;
        TarefasFeitas = pontos.Count(p => p.Feita); TotalTarefas = pontos.Count;
        ProgressoAreas = total > 0 ? (float)rec / total : 0f;
        ProgressoMudou?.Invoke(ProgressoAreas);
        hud.Atualizar(rec, total, TarefasFeitas, TotalTarefas, SouSabotador, TemSabotadorNaSala, comunidade != null ? comunidade.Apoios : 0,
                      saberes.Count, comunidade != null ? comunidade.TotalDeSaberes : 6);

        bool venceu = total > 0 && rec == total;
        if (venceu && !PartidaVencida)
        {
            PartidaVencida = true;
            hud.Grande("A comunidade unida protegeu a vila!", HudAmbiental.Verde);
            Vitoria?.Invoke();
            if (!silencioso) Invoke(nameof(RodaVitoria), 3f);
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
        bool usarTela = hud.UsarSegurado;
        bool apertouUsar = (kb != null && kb.eKey.wasPressedThisFrame) || (gp != null && gp.buttonSouth.wasPressedThisFrame)
                           || (usarTela && !usarAntes);   // clique no botão USAR abre o minigame
        usarAntes = usarTela;
        segurandoE |= usarTela;                  // botão USAR da tela (mouse ou toque)
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

        // revela o papel quando a partida começa (e de novo se o sorteio mudar)
        if (PartidaIniciada)
        {
            object ini = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.CustomProperties[CHAVE_INICIO] : (object)inicioOffline;
            string chave = $"{ini}|{SouSabotador}|{TemSabotadorNaSala}";
            if (chave != papelMostrado) { papelMostrado = chave; hud.RevelarPapel(SouSabotador, TemSabotadorNaSala); }
        }
        float resta = TempoRestante;
        if (!PartidaVencida) tempoCongelado = -1f;
        else if (tempoCongelado < 0f) tempoCongelado = resta;
        hud.Tempo(PartidaVencida ? tempoCongelado : resta);
        if (resta == 0f && !PartidaVencida && !tempoAcabou)
        {
            tempoAcabou = true;
            hud.Grande("O tempo acabou: a empresa passou por cima da vila.", HudAmbiental.Vermelho);
            Invoke(nameof(RodaDerrota), 3f);
        }

        if (MenuPartida.Aberto || MinigameTarefa.Aberto || HudAmbiental.PainelAberto || Assembleia.Aberto) { hud.Prompt(null, -1f); AtualizarBotoes(); return; }
        if (Assembleia.LocalDispensado)
        {   // dispensado na assembleia: só acompanha a partida
            hud.Prompt("Você foi dispensado(a) pela assembleia: acompanhe e torça pela comunidade.", -1f);
            hud.BotaoSabotar(false, false, 0);
            return;
        }
        // painel INICIAR / REINICIAR no meio da associação (só o dono da sala usa)
        pertoDoInicio = PertoDoPainelInicio();
        if (srInicio != null && sprInicio[1] != null) srInicio.sprite = PartidaIniciada ? sprInicio[1] : sprInicio[0];
        if (pertoDoInicio && pontoAtual == null)
        {
            if (!SouDono) { segurandoInicio = 0f; hud.Prompt("Só o dono da sala pode iniciar ou reiniciar a partida.", -1f); return; }
            segurandoInicio = segurandoE ? segurandoInicio + Time.deltaTime : 0f;
            hud.Prompt(PartidaIniciada ? "Segure USAR (E): REINICIAR a partida (tudo volta ao começo)" : "Segure USAR (E): INICIAR a partida",
                       segurandoInicio / SegundosInicio);
            if (segurandoInicio >= SegundosInicio) { segurandoInicio = 0f; IniciarPartida(); }
            return;
        }
        segurandoInicio = 0f;
        if (!PartidaIniciada)
        {   // antes de começar dá para andar e conhecer o mapa, mas sem tarefas, conversas ou sabotagem
            hud.Prompt(SouDono ? "Com todos prontos, use o painel INICIAR na associação de moradores."
                               : "Aguardando o dono da sala iniciar a partida na associação de moradores...", -1f);
            hud.BotaoSabotar(false, false, 0);
            return;
        }
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
            segurando = 0f;
            var precisa = comunidade.SaberNecessario(pontoAtual.nome);
            if (precisa != null && !SaberConhecido(precisa.Nome))
            {
                hud.Prompt($"{pontoAtual.nome}: falta uma pista de {precisa.Nome} ({precisa.Papel})", -1f);
                if (apertouUsar || dicaColetiva < Time.time)
                {
                    hud.Aviso($"Converse primeiro com {precisa.Nome} ({precisa.Papel}): ela/ele sabe o que fazer aqui.", HudAmbiental.Cinza, 4f);
                    dicaColetiva = Time.time + 12f;
                }
                return;
            }
            hud.Prompt($"USAR (E): {pontoAtual.nome}", -1f);
            if (apertouUsar)
            {
                var ponto = pontoAtual; int apoios = apoiosUsados; bool fingindo = SouSabotador;
                minigame.Abrir(ponto.nome, () =>
                {
                    if (fingindo) { hud.Aviso("(você fingiu fazer a tarefa)", HudAmbiental.Cinza); return; }
                    if (apoios > 0)
                    {
                        comunidade.Gastar(apoios);
                        hud.Aviso($"{apoios} morador(es) participaram da ação!", HudAmbiental.Verde);
                    }
                    DefinirTarefa(ponto.Id, true);
                    Recalcular(silencioso: true);
                });
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
        if (!menu && pertoDoInicio && pontoAtual == null && SouDono)
        { usar = true; rotulo = PartidaIniciada ? "REINICIAR" : "INICIAR"; prog = segurandoInicio / SegundosInicio; }
        else if (!menu && !PartidaIniciada) { }
        else if (!menu)
        {
            if (pontoAtual != null && !pontoAtual.Feita) { usar = true; prog = 0f; }
            else if (pontoAtual == null && moradorAtual != null)
            {
                bool novo = !SaberConhecido(moradorAtual.Nome);
                if (novo || (moradorAtual.Disponivel && comunidade.Apoios < comunidade.MaximoDeApoios))
                { usar = true; rotulo = novo ? "ESCUTAR" : "FALAR"; prog = segurando / SegundosConversa; }
            }
        }
        hud.BotaoUsar(usar, rotulo, prog);
        float falta = proximaSabotagem - Time.time;
        hud.BotaoSabotar(SouSabotador, !menu && pontoAtual != null && pontoAtual.Feita && falta <= 0, Mathf.CeilToInt(Mathf.Max(0f, falta)));
    }
    private void ConversarComMorador(bool segurandoE)
    {
        var m = moradorAtual;
        bool novo = !SaberConhecido(m.Nome);
        bool podeApoiar = m.Disponivel && comunidade.Apoios < comunidade.MaximoDeApoios;
        if (!novo && !podeApoiar)
        {
            segurando = 0f;
            hud.Prompt(!m.Disponivel ? $"{m.Nome} já está mobilizado(a). Volte daqui a pouco."
                                     : $"Você já tem {comunidade.Apoios} apoios: leve-os a uma ação coletiva.", -1f);
            return;
        }
        segurando = segurandoE ? segurando + Time.deltaTime : 0f;
        hud.Prompt(novo ? $"Segure ESCUTAR (E): {m.Nome}, {m.Papel}" : $"Segure FALAR (E): mobilizar {m.Nome}", segurando / SegundosConversa);
        if (segurando < SegundosConversa) return;
        segurando = 0f;
        if (novo)
        {
            RegistrarSaber(m.Nome);
            hud.Fala($"{m.Nome}, {m.Papel}", m.Fala, HudAmbiental.Verde, 10f);
            string libera = string.Join(", ", comunidade.TarefasQueUsam(m.Nome));
            hud.Aviso($"Pista nova: {m.Saber}" + (libera.Length > 0 ? $"  (libera: {libera})" : ""), HudAmbiental.Verde, 6f);
        }
        if (podeApoiar && comunidade.Convidar(m) && !novo)
            hud.Aviso($"{m.Nome} vai apoiar a mobilização! Apoios: {comunidade.Apoios}", HudAmbiental.Verde);
        Recalcular(silencioso: true);
    }

    // ------------------------------------------------------------------ roda de conversa (Paulo Freire)
    private void RodaVitoria() => MostrarRodaDeConversa(true);
    private void RodaDerrota() => MostrarRodaDeConversa(false);

    /// Fim da partida: Realidade do oprimido → Consciência crítica → Práxis emancipatória.
    private void MostrarRodaDeConversa(bool vitoria)
    {
        if (rodaMostrada) return;
        rodaMostrada = true;
        int totalSab = comunidade != null ? comunidade.TotalDeSaberes : 6;
        int mob = comunidade != null ? comunidade.ApoiosMobilizados : 0;
        string recursos = string.Join("\n", Enumerable.Range(0, 4).Select(i => $"•  {HudAmbiental.NomesRecursos[i]}: {Mathf.RoundToInt(dimensoes[i] * 100)}%"));

        string aconteceu =
            $"Vocês conversaram com <b>{saberes.Count} de {totalSab}</b> moradores.\n\n" +
            $"A empresa desfez conquistas <b>{sabotagens}</b> vezes.\n\nRecursos no fim:\n{recursos}";
        string quemGanhou =
            "Quem lucrou com a mineradora e o porto? E quem ficou com a água suja, a doença e a falta de peixe?\n\n" +
            "Por que o laudo da empresa valia mais que a palavra da marisqueira?\n\n" +
            "Bastava cada um \"fazer a sua parte\"?";
        string fizemos =
            $"<b>{TarefasFeitas}/{TotalTarefas}</b> ações e <b>{mob}</b> moradores mobilizados.\n\n" +
            "Conversar, informar, cobrar direitos, juntar comunidades e criar trabalho: foi isso que protegeu a vila.\n\n" +
            "<b>E perto de nós?</b> Existe algum lugar parecido com a Vila Beira-Rio?";
        float media = dimensoes.Average();
        int estrelas = vitoria ? 3 : media >= 0.6f ? 2 : media >= 0.25f ? 1 : 0;
        string nota = new string('★', estrelas) + new string('☆', 3 - estrelas);
        hud.PainelInfo((tituloFim ?? (vitoria ? "A comunidade unida protegeu a vila!" : "A empresa passou por cima da vila")) + "   " + nota,
            vitoria ? "Ninguém veio de fora salvar a vila: foram os próprios moradores, juntos. Agora, conversem em grupo sobre as perguntas abaixo:"
                    : "O tempo acabou antes de a comunidade se organizar. Antes de jogar de novo, conversem em grupo sobre as perguntas abaixo:",
            new[] { ("O QUE ACONTECEU", aconteceu), ("QUEM GANHOU, QUEM PERDEU", quemGanhou), ("O QUE A COMUNIDADE FEZ", fizemos) },
            "FECHAR");
    }

    // ------------------------------------------------------------------ partida
    /// Dono da sala: escolhe um jogador aleatório como sabotador.
    public void SortearSabotador()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        int ator = SortearAtor(Elegiveis());
        if (ator == 0) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CHAVE_SAB, ator }, { CHAVE_SAB_ANTERIOR, ator } });
    }

    // ------------------------------------------------------------------ painel de início (associação de moradores)
    private void CriarPainelInicio()
    {
        sprInicio = new Sprite[2];
        foreach (var sp in Resources.LoadAll<Sprite>("Animacoes/Inicio"))
            if (sp.name == "inicio_0") sprInicio[0] = sp; else if (sp.name == "inicio_1") sprInicio[1] = sp;
        if (sprInicio[0] == null) Debug.LogWarning("[Ambiental] Sprite do painel INICIAR não encontrado. Rode Tools → Vila → Preparar animações.");
        var go = new GameObject("Painel de início");
        go.transform.SetParent(transform, false);
        go.transform.position = PosicaoInicio;
        srInicio = go.AddComponent<SpriteRenderer>();
        srInicio.sprite = sprInicio[0]; srInicio.sortingOrder = 10; srInicio.spriteSortPoint = SpriteSortPoint.Pivot;
    }

    private bool PertoDoPainelInicio()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (PhotonNetwork.InRoom && pm.GetComponent<PhotonView>() is PhotonView v && !v.AmOwner) continue;
            return Vector2.Distance(pm.transform.position, PosicaoInicio + new Vector2(0, 0.4f)) <= 1.8f;
        }
        return false;
    }

    /// Começa a partida (relógio, sorteio do infiltrado). Só o dono da sala.
    public void IniciarPartida()
    {
        if (!SouDono) return;
        ReiniciarPartida();
        if (!PhotonNetwork.InRoom) hud.Aviso("A partida começou! Converse com os vizinhos e protejam a vila.", HudAmbiental.Verde, 5f);
    }

    /// Dono da sala: zera todas as tarefas e sorteia outro sabotador (nova rodada).
    public void ReiniciarPartida()
    {
        if (PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var limpar = new Hashtable();
            foreach (var p in pontos) limpar[PREFIXO + p.Id] = null;    // null apaga a propriedade
            foreach (var sb in saberes) limpar[PREFIXO_SABER + sb] = null;
            limpar[CHAVE_INICIO] = PhotonNetwork.Time;                  // o relógio recomeça
            limpar[CHAVE_REINICIO] = PhotonNetwork.Time;                // avisa todos que é uma nova rodada
            foreach (var k in new[] { Assembleia.CH_ATIVA, Assembleia.CH_DISPENSADOS, Assembleia.CH_USOU, Assembleia.CH_RESULTADO, Assembleia.CH_FIM })
                limpar[k] = null;                                        // a assembleia também recomeça
            var elegiveis = PhotonNetwork.PlayerList.Where(p => !p.IsInactive).ToList();   // nova rodada: ninguém está dispensado
            int novoSab = sortearSabotadorAoIniciar && elegiveis.Count >= jogadoresParaSabotador ? SortearAtor(elegiveis) : 0;
            limpar[CHAVE_SAB] = novoSab != 0 ? (object)novoSab : null;
            if (novoSab != 0) limpar[CHAVE_SAB_ANTERIOR] = novoSab;
            PhotonNetwork.CurrentRoom.SetCustomProperties(limpar);
        }
        else { iniciadaOffline = true; feitas.Clear(); saberes.Clear(); inicioOffline = Time.time; tempoAcabou = false; hud.Grande(null); Recalcular(silencioso: true); }
        sabotagens = 0; rodaMostrada = false; fimAssembleia = false; tituloFim = null;
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
