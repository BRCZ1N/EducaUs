using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Sincroniza a posição do jogador pela rede usando um EVENTO próprio do Photon
/// (não depende da lista "Observed Components" do PhotonView, então não há o que configurar no prefab).
///
/// • O dono manda posição e velocidade 10 vezes por segundo andando (2 vezes parado).
/// • Nos outros computadores, a posição recebida é adiantada pelo atraso da mensagem
///   (posição + velocidade × atraso) e o boneco vai suavemente até lá.
/// • Parado, todos convergem para o mesmo ponto.
/// • Diferença grande (bueiro, reconexão, reinício): o boneco pula direto, sem atravessar o mapa.
/// • Fora de sala (modo solo) não faz nada: o boneco é do jogador local.
///
/// No prefab do Player: deixe este componente e NENHUM outro de sincronização (sem Photon Transform View).
/// O Photon View pode ficar com "Observed Components" vazio e Synchronization em "Off".
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class SincronizarPosicao : MonoBehaviourPun, IOnEventCallback
{
    private const byte EVENTO_POSICAO = 78;           // código livre (77 é a direção do boneco)

    [Tooltip("Acima desta distância (unidades), o boneco remoto pula direto para a posição certa.")]
    [SerializeField] private float distanciaParaPular = 4f;
    [Tooltip("Quanto maior, mais rápido o boneco remoto alcança a posição recebida.")]
    [SerializeField] private float suavidade = 12f;
    [Tooltip("Máximo de tempo (s) que o boneco remoto continua andando sozinho sem receber mensagem.")]
    [SerializeField] private float maxPrevisao = 0.3f;
    [SerializeField] private float envioAndando = 0.1f;   // 10 por segundo
    [SerializeField] private float envioParado = 0.5f;    // 2 por segundo

    private Rigidbody2D rb;
    private Vector2 alvo, velocidade;
    private float recebidoEm, proximoEnvio;
    private bool temDado, estavaParado = true;

    /// Para o modo de diagnóstico (F3): atraso da última mensagem e há quanto tempo ela chegou.
    public int UltimoAtrasoMs { get; private set; }
    public float SegundosSemMensagem => temDado ? Time.time - recebidoEm : -1f;
    public bool EhRemoto => Remoto;

    /// Boneco de OUTRO jogador numa sala online.
    private bool Remoto => PhotonNetwork.InRoom && photonView != null && !photonView.AmOwner;
    private bool Meu => PhotonNetwork.InRoom && photonView != null && photonView.AmOwner;

    private void Awake() => rb = GetComponent<Rigidbody2D>();
    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    private void Start()
    {
        if (Remoto && rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.None;   // quem move o boneco dos outros é este script
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    private void Update()
    {
        if (Meu) Enviar();
        else if (Remoto) Seguir();
    }

    // ---------------------------------------------------------------- dono: envia
    private void Enviar()
    {
        if (photonView.ViewID == 0) return;
        Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;
        bool parado = vel.sqrMagnitude < 0.0004f;
        bool comecouOuParou = parado != estavaParado;
        if (!comecouOuParou && Time.time < proximoEnvio) return;
        estavaParado = parado;
        proximoEnvio = Time.time + (parado ? envioParado : envioAndando);

        Vector2 pos = transform.position;
        object[] dados = { photonView.ViewID, pos.x, pos.y, vel.x, vel.y, PhotonNetwork.ServerTimestamp };
        PhotonNetwork.RaiseEvent(EVENTO_POSICAO, dados,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            comecouOuParou ? SendOptions.SendReliable : SendOptions.SendUnreliable);
    }

    // ---------------------------------------------------------------- outros: recebem
    public void OnEvent(EventData ev)
    {
        if (ev.Code != EVENTO_POSICAO || photonView == null) return;
        if (!(ev.CustomData is object[] d) || d.Length < 6) return;
        if (!(d[0] is int id) || id != photonView.ViewID || photonView.AmOwner) return;

        Vector2 pos = new Vector2(ToF(d[1]), ToF(d[2]));
        Vector2 vel = new Vector2(ToF(d[3]), ToF(d[4]));
        float atraso = 0f;
        if (d[5] is int enviadoEm)
            atraso = Mathf.Clamp(unchecked(PhotonNetwork.ServerTimestamp - enviadoEm) / 1000f, 0f, 0.5f);
        UltimoAtrasoMs = Mathf.RoundToInt(atraso * 1000f);

        velocidade = vel;
        alvo = pos + vel * atraso;                    // onde o dono deve estar agora
        recebidoEm = Time.time;
        if (!temDado || Vector2.Distance(transform.position, alvo) > distanciaParaPular) Colocar(alvo);
        temDado = true;
    }

    private static float ToF(object o) => o is float f ? f : o is double db ? (float)db : o is int i ? i : 0f;

    private void Seguir()
    {
        if (!temDado) return;
        Vector2 previsto = alvo;
        if (velocidade.sqrMagnitude > 0.0004f)
            previsto += velocidade * Mathf.Min(Time.time - recebidoEm, maxPrevisao);

        Vector2 atual = transform.position;
        if (Vector2.Distance(atual, previsto) > distanciaParaPular) { Colocar(previsto); return; }
        Colocar(Vector2.Lerp(atual, previsto, 1f - Mathf.Exp(-suavidade * Time.deltaTime)));
    }

    private void Colocar(Vector2 p)
    {
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        if (rb != null) rb.position = p;
    }
}
