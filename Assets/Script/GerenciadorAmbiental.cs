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
    [SerializeField] private float recargaSabotagem = 30f;
    [Tooltip("Só para testar sem Photon: você joga como sabotador.")]
    [SerializeField] private bool sabotadorOffline = false;

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

    private readonly List<AreaAmbiental> areas = new List<AreaAmbiental>();
    private readonly List<PontoDeTarefa> pontos = new List<PontoDeTarefa>();
    private readonly Dictionary<AreaAmbiental, List<PontoDeTarefa>> tarefasDaArea = new Dictionary<AreaAmbiental, List<PontoDeTarefa>>();
    private readonly HashSet<string> feitas = new HashSet<string>();

    private HudAmbiental hud;
    private PontoDeTarefa pontoAtual;
    private float segurando;
    private float proximaSabotagem;
    private bool iniciado;

    // ------------------------------------------------------------------ ciclo de vida
    private void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;
        hud = GetComponent<HudAmbiental>();
        if (hud == null) hud = gameObject.AddComponent<HudAmbiental>();
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

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && sortearSabotadorAoIniciar
            && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(CHAVE_SAB))
            SortearSabotador();

        LerEstadoDaSala();
        iniciado = true;
        Recalcular(silencioso: true);
        Debug.Log($"[Ambiental] {areas.Count} áreas, {pontos.Count} tarefas.");
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
        foreach (var kv in mudou) AplicarChave(kv.Key as string, kv.Value);
        if (iniciado) Recalcular(silencioso: false);
    }

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
                    hud.Aviso(recuperada ? $"{a.Nome} foi recuperada!" : $"Sabotagem! {a.Nome} foi poluída de novo.",
                              recuperada ? HudAmbiental.Verde : HudAmbiental.Vermelho);
            }
        }
        AreasRecuperadas = rec; TotalAreas = total;
        TarefasFeitas = pontos.Count(p => p.Feita); TotalTarefas = pontos.Count;
        ProgressoAreas = total > 0 ? (float)rec / total : 0f;
        ProgressoMudou?.Invoke(ProgressoAreas);
        hud.Atualizar(rec, total, TarefasFeitas, TotalTarefas, SouSabotador);

        bool venceu = total > 0 && rec == total;
        if (venceu && !PartidaVencida)
        {
            PartidaVencida = true;
            hud.Grande("Os voluntários recuperaram a vila!");
            Vitoria?.Invoke();
        }
        else if (!venceu && PartidaVencida) { PartidaVencida = false; hud.Grande(null); }
    }

    // ------------------------------------------------------------------ jogador local
    public void Entrou(PontoDeTarefa p) { pontoAtual = p; segurando = 0f; }
    public void Saiu(PontoDeTarefa p) { if (pontoAtual == p) { pontoAtual = null; segurando = 0f; } }

    private void Update()
    {
        if (!iniciado) return;
        var kb = Keyboard.current; var gp = Gamepad.current;
        bool segurandoE = (kb != null && kb.eKey.isPressed) || (gp != null && gp.buttonSouth.isPressed);
        bool apertouQ = (kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame);

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

        if (pontoAtual == null) { hud.Prompt(null, -1f); return; }

        if (!pontoAtual.Feita)
        {
            segurando = segurandoE ? segurando + Time.deltaTime : 0f;
            hud.Prompt($"Segure E: {pontoAtual.nome}", segurando / segundosPorTarefa);
            if (segurando >= segundosPorTarefa)
            {
                segurando = 0f;
                if (SouSabotador) hud.Aviso("(você fingiu fazer a tarefa)", HudAmbiental.Cinza);
                else DefinirTarefa(pontoAtual.Id, true);
            }
        }
        else if (SouSabotador)
        {
            float falta = proximaSabotagem - Time.time;
            hud.Prompt(falta > 0 ? $"Sabotar em {Mathf.CeilToInt(falta)}s" : $"Q: sabotar \"{pontoAtual.nome}\"", -1f);
            if (apertouQ && falta <= 0)
            {
                DefinirTarefa(pontoAtual.Id, false);
                proximaSabotagem = Time.time + recargaSabotagem;
            }
        }
        else hud.Prompt($"Concluída: {pontoAtual.nome}", -1f);
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
            PhotonNetwork.CurrentRoom.SetCustomProperties(limpar);
            SortearSabotador();
        }
        else { feitas.Clear(); Recalcular(silencioso: true); }
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
