using Photon.Pun;
using UnityEngine;

/// <summary>Câmera que segue o jogador local suavemente. Coloque na câmera da cena Game.</summary>
public class CameraSegue : MonoBehaviour
{
    [SerializeField] private Transform alvo;
    [SerializeField] private float suavidade = 6f;

    private void LateUpdate()
    {
        if (alvo == null)
        {
            alvo = AcharJogadorLocal();
            if (alvo == null) return;
            transform.position = new Vector3(alvo.position.x, alvo.position.y, transform.position.z);   // pula direto na 1ª vez
        }

        Vector3 destino = new Vector3(alvo.position.x, alvo.position.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, destino, suavidade * Time.deltaTime);
    }

    /// <summary>Para o spawner online dizer qual é o jogador local.</summary>
    public void SetAlvo(Transform t) => alvo = t;

    private static Transform AcharJogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!PhotonNetwork.InRoom) return pm.transform;
            var pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine) return pm.transform;
        }
        return null;
    }
}