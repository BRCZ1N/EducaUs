using Photon.Pun;
using UnityEngine;

/// <summary>
/// Sincroniza a posição do jogador pela rede (no lugar do PhotonTransformView / PhotonRigidbody2DView).
///
/// • O dono manda a posição e a velocidade.
/// • Nos outros computadores o boneco vai suavemente até onde o dono está AGORA: a posição recebida é
///   adiantada pelo atraso da mensagem (posição + velocidade × atraso), então a diferença entre as telas fica pequena.
/// • Parado, todos convergem exatamente para o mesmo ponto.
/// • Se a diferença for grande (bueiro, reconexão, reinício), o boneco "pula" direto, sem atravessar o mapa.
///
/// COMO USAR: no prefab do Player, adicione este componente e coloque-o em PhotonView → Observed Components
/// (tirando dali o PhotonTransformView / PhotonRigidbody2DView, que podem ser removidos do prefab).
/// Todas as builds (web, Android, PC) precisam ser geradas de novo com o prefab novo.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class SincronizarPosicao : MonoBehaviourPun, IPunObservable
{
    [Tooltip("Acima desta distância (unidades), o boneco remoto pula direto para a posição certa.")]
    [SerializeField] private float distanciaParaPular = 4f;
    [Tooltip("Quanto maior, mais rápido o boneco remoto alcança a posição recebida.")]
    [SerializeField] private float suavidade = 14f;
    [Tooltip("Máximo de tempo (s) que o boneco remoto continua andando sozinho sem receber mensagem.")]
    [SerializeField] private float maxPrevisao = 0.25f;

    private Rigidbody2D rb;
    private Vector2 alvo, velocidade;
    private float recebidoEm;
    private bool temDado;

    /// Boneco de OUTRO jogador numa sala online. Fora de sala (modo solo) o boneco é sempre do jogador local.
    private bool Remoto => PhotonNetwork.InRoom && photonView != null && !photonView.AmOwner;

    private void Awake() => rb = GetComponent<Rigidbody2D>();

    private void Start()
    {
        // no boneco dos outros quem move é este script: a interpolação do Rigidbody sobrescreveria a posição
        if (Remoto && rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.None;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext((Vector2)transform.position);
            stream.SendNext(rb != null ? rb.linearVelocity : Vector2.zero);
            return;
        }
        Vector2 pos = (Vector2)stream.ReceiveNext();
        Vector2 vel = (Vector2)stream.ReceiveNext();
        float atraso = Mathf.Clamp((float)(PhotonNetwork.Time - info.SentServerTime), 0f, 0.5f);
        velocidade = vel;
        alvo = pos + vel * atraso;                       // onde o dono deve estar agora
        recebidoEm = Time.time;
        if (!temDado || Vector2.Distance(transform.position, alvo) > distanciaParaPular) Colocar(alvo);
        temDado = true;
    }

    private void Update()
    {
        if (!Remoto || !temDado) return;
        // entre uma mensagem e outra, continua andando na mesma direção por um instante
        Vector2 previsto = alvo;
        float desde = Time.time - recebidoEm;
        if (velocidade.sqrMagnitude > 0.0001f) previsto += velocidade * Mathf.Min(desde, maxPrevisao);

        Vector2 atual = transform.position;
        if (Vector2.Distance(atual, previsto) > distanciaParaPular) { Colocar(previsto); return; }
        Vector2 novo = Vector2.Lerp(atual, previsto, 1f - Mathf.Exp(-suavidade * Time.deltaTime));
        Colocar(novo);
    }

    private void Colocar(Vector2 p)
    {
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        if (rb != null) rb.position = p;
    }
}
