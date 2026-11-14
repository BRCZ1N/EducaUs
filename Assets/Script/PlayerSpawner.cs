using System.Linq;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Cria o personagem do jogador quando a cena Game abre.
/// Online: PhotonNetwork.Instantiate (todos veem). Offline: Instantiate normal (para testar o mapa).
/// Usa os pontos "Spawn_1..6" da ONG; cada jogador pega um pelo número dele na sala.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [Tooltip("O prefab precisa estar numa pasta Resources e ter PhotonView.")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("Vazio = procura os objetos Spawn_1, Spawn_2... da cena.")]
    [SerializeField] private Transform[] spawns;

    private void Start()
    {
        PhotonNetwork.IsMessageQueueRunning = true;   // a Game abriu: libera as mensagens seguradas

        if (spawns == null || spawns.Length == 0)
            spawns = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                     .Where(t => t.name.StartsWith("Spawn_"))
                     .OrderBy(t => t.name)
                     .ToArray();

        Vector3 pos = EscolherPosicao();
        GameObject jogador = PhotonNetwork.InRoom
            ? PhotonNetwork.Instantiate(playerPrefab.name, pos, Quaternion.identity)
            : Instantiate(playerPrefab, pos, Quaternion.identity);

        var cam = FindFirstObjectByType<CameraSegue>();
        if (cam != null) cam.SetAlvo(jogador.transform);
    }

    private Vector3 EscolherPosicao()
    {
        if (spawns.Length == 0)
            return new Vector3(UnityEngine.Random.Range(45f, 50f), UnityEngine.Random.Range(80f, 85f), 0f);

        int i = PhotonNetwork.InRoom ? (PhotonNetwork.LocalPlayer.ActorNumber - 1) % spawns.Length : 0;
        return spawns[i].position;
    }
}