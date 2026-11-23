using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Anima o personagem (parado / andando para frente, para trás e para os lados) e dá uma COR diferente
/// para cada jogador da sala (6 cores). Coloque no prefab do Player, junto do SpriteRenderer.
/// No seu boneco a direção vem do movimento. Nos bonecos dos outros, a direção, o lado e se está andando
/// chegam PELA REDE num evento próprio do Photon (não mexe no PhotonView nem na ordem dos dados de posição):
/// deduzir pelo movimento interpolado fazia o boneco virar para lados errados com os trancos da sincronização.
/// Os quadros vêm de Resources/Animacoes/Personagem_0..5 (rode Tools → Vila → Preparar animações uma vez).
/// O PlayerMovement adiciona este componente sozinho se ele não estiver no prefab.
/// O SpriteRenderer pode estar no próprio objeto ou num filho.
/// </summary>
public class AnimacaoPersonagem : MonoBehaviour, IOnEventCallback
{
    [SerializeField] private float quadrosPorSegundo = 9f;
    [Tooltip("Velocidade mínima (unidades/s) para considerar que está andando.")]
    [SerializeField] private float limiarAndando = 0.4f;
    [Tooltip("-1 = escolhe pela ordem do jogador na sala.")]
    [SerializeField] private int corFixa = -1;

    public const int TotalDeCores = 6;
    public int Cor { get; private set; }

    private SpriteRenderer sr;
    private PhotonView pv;
    private readonly Dictionary<string, Sprite> quadros = new Dictionary<string, Sprite>();
    private Vector3 ultimaPos;
    private float velSuave, tempo;
    private string direcao = "frente";
    private bool paraEsquerda;
    private bool andandoAgora;
    // o que chegou pela rede (bonecos dos outros)
    private bool temRede; private string direcaoRede = "frente"; private bool esquerdaRede, andandoRede;
    private static readonly string[] Direcoes = { "frente", "costas", "lado" };
    private const byte EVENTO_DIRECAO = 77;                              // código livre de evento do Photon
    private string enviadaDir = ""; private bool enviadaEsq, enviadaAnd; private float proximoReenvio;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            var filho = GetComponentInChildren<SpriteRenderer>();
            if (filho != null) sr = filho;
        }
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        pv = GetComponentInParent<PhotonView>();

        // Um Animator antigo (sem as animações certas) trocaria o sprite todo quadro: desliga.
        foreach (var an in GetComponentsInChildren<Animator>())
        {
            an.enabled = false;
            Debug.Log("[AnimacaoPersonagem] Animator desligado: a animação agora vem dos quadros em Resources/Animacoes.");
        }
    }

    private void Start()
    {
        if (corFixa >= 0) Cor = corFixa % TotalDeCores;
        else if (PhotonNetwork.InRoom && pv != null && pv.OwnerActorNr > 0) Cor = (pv.OwnerActorNr - 1) % TotalDeCores;
        else Cor = 0;

        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Personagem_" + Cor)) quadros[s.name] = s;
        if (quadros.Count == 0)
            Debug.LogError("[AnimacaoPersonagem] Nenhum quadro em Resources/Animacoes/Personagem_" + Cor +
                           ". Confira se a pasta Resources/Animacoes está em Assets e rode Tools → Vila → Preparar animações.");
        else if (!quadros.ContainsKey("andar_frente_0"))
            Debug.LogError("[AnimacaoPersonagem] A folha Personagem_" + Cor + " não está fatiada com os nomes certos. Rode Tools → Vila → Preparar animações.");
        ultimaPos = transform.position;
    }

    private void OnEnable() { PhotonNetwork.AddCallbackTarget(this); }
    private void OnDisable() { PhotonNetwork.RemoveCallbackTarget(this); }

    // Dono do boneco: manda a direção quando muda e, por garantia, a cada 1 s (quem entrou depois também recebe).
    private void EnviarDirecao()
    {
        if (!PhotonNetwork.InRoom || pv == null || !pv.AmOwner || pv.ViewID == 0) return;
        bool mudou = direcao != enviadaDir || paraEsquerda != enviadaEsq || andandoAgora != enviadaAnd;
        if (!mudou && Time.time < proximoReenvio) return;
        object[] dados = { pv.ViewID, (byte)Mathf.Max(0, System.Array.IndexOf(Direcoes, direcao)), paraEsquerda, andandoAgora };
        PhotonNetwork.RaiseEvent(EVENTO_DIRECAO, dados,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            mudou ? SendOptions.SendReliable : SendOptions.SendUnreliable);
        enviadaDir = direcao; enviadaEsq = paraEsquerda; enviadaAnd = andandoAgora;
        proximoReenvio = Time.time + 1f;
    }

    // Outros bonecos: recebe a direção mandada pelo dono.
    public void OnEvent(EventData ev)
    {
        if (ev.Code != EVENTO_DIRECAO || pv == null) return;
        if (!(ev.CustomData is object[] d) || d.Length < 4) return;
        if (!(d[0] is int id) || id != pv.ViewID) return;
        int i = d[1] is byte b ? b : 0;
        direcaoRede = Direcoes[Mathf.Clamp(i, 0, Direcoes.Length - 1)];
        esquerdaRede = d[2] is bool e && e;
        andandoRede = d[3] is bool a && a;
        temRede = true;
    }

    private void LateUpdate()
    {
        if (sr != null && pv != null)                                    // dispensado pela assembleia fica transparente
        {
            bool caiu = PhotonNetwork.InRoom && pv.Owner != null && pv.Owner.IsInactive;       // caiu e a sala guarda o lugar
            float alvo = caiu ? 0.25f : Assembleia.Dispensado(pv.OwnerActorNr) ? 0.4f : 1f;
            if (!Mathf.Approximately(sr.color.a, alvo)) sr.color = new Color(1f, 1f, 1f, alvo);
        }
        if (quadros.Count == 0) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - ultimaPos;
        ultimaPos = transform.position;

        bool andando;
        bool remoto = PhotonNetwork.InRoom && pv != null && !pv.AmOwner;
        if (remoto && temRede)
        {   // boneco de outro jogador: usa exatamente o que o dono mandou
            direcao = direcaoRede; paraEsquerda = esquerdaRede; andando = andandoRede;
        }
        else
        {
            float vel = delta.magnitude / dt;
            velSuave = Mathf.Lerp(velSuave, vel, 12f * dt);             // evita piscar entre parado/andando
            andando = velSuave > limiarAndando;
            if (delta.sqrMagnitude > 0.00001f)
            {
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.1f) { direcao = "lado"; paraEsquerda = delta.x < 0; }
                else if (Mathf.Abs(delta.y) > 0.0001f) direcao = delta.y > 0 ? "costas" : "frente";
            }
        }
        andandoAgora = andando;
        EnviarDirecao();

        tempo += dt;
        string nome;
        if (andando) nome = $"andar_{direcao}_{(int)(tempo * quadrosPorSegundo) % 4}";
        else if (direcao == "frente") nome = $"parado_frente_{(int)(tempo * 2f) % 2}";   // respira devagar
        else nome = $"parado_{direcao}_0";

        if (quadros.TryGetValue(nome, out var s)) sr.sprite = s;
        sr.flipX = direcao == "lado" && paraEsquerda;                  // o desenho de lado olha para a direita
    }
}
