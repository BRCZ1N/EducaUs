using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Anima o personagem (parado / andando para frente, para trás e para os lados) e dá uma COR diferente
/// para cada jogador da sala (6 cores). Coloque no prefab do Player, junto do SpriteRenderer.
/// Funciona igual no seu boneco e na cópia dos outros: a direção vem do movimento na tela,
/// então não precisa sincronizar nada a mais pela rede.
/// Os quadros vêm de Resources/Animacoes/Personagem_0..5 (rode Tools → Vila → Preparar animações uma vez).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AnimacaoPersonagem : MonoBehaviour
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

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        pv = GetComponent<PhotonView>();
    }

    private void Start()
    {
        if (corFixa >= 0) Cor = corFixa % TotalDeCores;
        else if (PhotonNetwork.InRoom && pv != null && pv.OwnerActorNr > 0) Cor = (pv.OwnerActorNr - 1) % TotalDeCores;
        else Cor = 0;

        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Personagem_" + Cor)) quadros[s.name] = s;
        if (quadros.Count == 0)
            Debug.LogWarning("[AnimacaoPersonagem] Quadros não encontrados. Rode Tools → Vila → Preparar animações.");
        ultimaPos = transform.position;
    }

    private void LateUpdate()
    {
        if (quadros.Count == 0) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - ultimaPos;
        ultimaPos = transform.position;

        float vel = delta.magnitude / dt;
        velSuave = Mathf.Lerp(velSuave, vel, 12f * dt);                 // evita piscar entre parado/andando
        bool andando = velSuave > limiarAndando;

        if (delta.sqrMagnitude > 0.00001f)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.1f) { direcao = "lado"; paraEsquerda = delta.x < 0; }
            else if (Mathf.Abs(delta.y) > 0.0001f) direcao = delta.y > 0 ? "costas" : "frente";
        }

        tempo += dt;
        string nome;
        if (andando) nome = $"andar_{direcao}_{(int)(tempo * quadrosPorSegundo) % 4}";
        else if (direcao == "frente") nome = $"parado_frente_{(int)(tempo * 2f) % 2}";   // respira devagar
        else nome = $"parado_{direcao}_0";

        if (quadros.TryGetValue(nome, out var s)) sr.sprite = s;
        sr.flipX = direcao == "lado" && paraEsquerda;                  // o desenho de lado olha para a direita
    }
}
