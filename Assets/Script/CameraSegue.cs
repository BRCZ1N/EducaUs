using Photon.Pun;
using UnityEngine;

/// <summary>
/// Câmera que segue o jogador local suavemente e não mostra o fundo fora do mapa.
/// Coloque na câmera da cena Game (Orthographic). Os limites são os do mapa inteiro, em unidades do mundo.
/// </summary>
public class CameraSegue : MonoBehaviour
{
    [SerializeField] private Transform alvo;
    [SerializeField] private float suavidade = 6f;

    [Header("Limites do mapa (unidades do mundo)")]
    [SerializeField] private bool limitarAoMapa = true;
    [SerializeField] private Vector2 minimo = new Vector2(0f, -40f);     // canto de baixo à esquerda (bairro)
    [SerializeField] private Vector2 maximo = new Vector2(112f, 90f);    // canto de cima à direita

    private Camera cam;

    private void Awake() => cam = GetComponent<Camera>();

    private void LateUpdate()
    {
        if (alvo == null || !EhMeu(alvo))
        {
            alvo = AcharJogadorLocal();
            if (alvo == null) return;
            transform.position = Limitar(new Vector3(alvo.position.x, alvo.position.y, transform.position.z));
        }

        Vector3 destino = Limitar(new Vector3(alvo.position.x, alvo.position.y, transform.position.z));
        transform.position = Vector3.Lerp(transform.position, destino, suavidade * Time.deltaTime);
    }

    private Vector3 Limitar(Vector3 p)
    {
        if (!limitarAoMapa || cam == null || !cam.orthographic) return p;
        float meiaAltura = cam.orthographicSize, meiaLargura = meiaAltura * cam.aspect;
        float x0 = minimo.x + meiaLargura, x1 = maximo.x - meiaLargura;
        float y0 = minimo.y + meiaAltura, y1 = maximo.y - meiaAltura;
        p.x = x0 <= x1 ? Mathf.Clamp(p.x, x0, x1) : (minimo.x + maximo.x) / 2f;
        p.y = y0 <= y1 ? Mathf.Clamp(p.y, y0, y1) : (minimo.y + maximo.y) / 2f;
        return p;
    }

    /// <summary>Para o spawner online dizer qual é o jogador local.</summary>
    public void SetAlvo(Transform t) => alvo = t;

    private static bool EhMeu(Transform t)
    {
        if (!PhotonNetwork.InRoom) return true;
        var pv = t.GetComponent<PhotonView>();
        return pv == null || pv.AmOwner;
    }

    private static Transform AcharJogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!PhotonNetwork.InRoom) return pm.transform;
            var pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.AmOwner) return pm.transform;
        }
        return null;
    }
}
