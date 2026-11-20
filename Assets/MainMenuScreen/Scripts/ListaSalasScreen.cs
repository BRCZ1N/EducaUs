using System.Collections.Generic;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela "Salas abertas": lista as partidas abertas no servidor (código, anfitrião, jogadores)
/// e entra na que o jogador clicar. A lista se atualiza sozinha (OnlineManager.SalasAtualizadas).
/// O VilaMenuBuilder (Tools → Menu principal) cria a tela e liga os campos.
/// </summary>
public class ListaSalasScreen : MonoBehaviour
{
    [Header("Referências (preenchidas pelo builder)")]
    [SerializeField] private RectTransform conteudo;     // onde os itens são criados (dentro do ScrollRect)
    [SerializeField] private Button modeloItem;          // botão modelo (fica desligado)
    [SerializeField] private TMP_Text textoVazio;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button backButton;
    [SerializeField] private Button atualizarButton;
    [Tooltip("Tela que some quando esta abre e volta quando esta fecha.")]
    [SerializeField] private GameObject previousScreen;

    [Header("Configuração")]
    [SerializeField] private string sceneAfterJoin = "Game";

    private readonly List<GameObject> itens = new List<GameObject>();
    private bool entrando;

    private void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (atualizarButton != null) atualizarButton.onClick.AddListener(Atualizar);
        if (modeloItem != null) modeloItem.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        var online = OnlineManager.Instance;
        if (online == null) return;
        online.SalasAtualizadas += Preencher;
        online.JoinSucceeded += AoEntrar;
        online.JoinFailed += AoFalhar;
    }

    private void OnDisable()
    {
        var online = OnlineManager.Instance;
        if (online == null) return;
        online.SalasAtualizadas -= Preencher;
        online.JoinSucceeded -= AoEntrar;
        online.JoinFailed -= AoFalhar;
    }

    // ------------------------------------------------------------------ abrir / fechar
    public void Open()
    {
        if (previousScreen != null) previousScreen.SetActive(false);
        gameObject.SetActive(true);
        entrando = false;
        SetStatus("");
        var online = OnlineManager.Instance;
        if (online == null || !online.IsReady)
        {
            Preencher(new List<RoomInfo>());
            textoVazio.text = "Sem conexão com o servidor.\nVocê ainda pode jogar sozinho em Novo jogo.";
            return;
        }
        online.PedirListaDeSalas();
        Preencher(online.Salas);
    }

    public void Close()
    {
        if (entrando) return;
        gameObject.SetActive(false);
        if (previousScreen != null) previousScreen.SetActive(true);
    }

    // ------------------------------------------------------------------ lista
    private void Preencher(IReadOnlyList<RoomInfo> salas)
    {
        foreach (var go in itens) Destroy(go);
        itens.Clear();

        int mostradas = 0;
        foreach (var s in salas)
        {
            if (s.RemovedFromList || !s.IsOpen || !s.IsVisible) continue;
            if (s.MaxPlayers > 0 && s.PlayerCount >= s.MaxPlayers) continue;    // cheia

            var item = Instantiate(modeloItem, conteudo);
            item.gameObject.SetActive(true);
            string nomeSala = s.CustomProperties.TryGetValue(OnlineManager.ChaveNomeSala, out object n) && n is string ns && ns.Length > 0 ? ns : "Partida da vila";
            string anfitriao = s.CustomProperties.TryGetValue(OnlineManager.ChaveAnfitriao, out object h) && h is string hs && hs.Length > 0 ? hs : "—";
            string max = s.MaxPlayers > 0 ? s.MaxPlayers.ToString() : "?";
            Coluna(item, "ColNome", nomeSala);
            Coluna(item, "ColAnfitriao", anfitriao);
            Coluna(item, "ColJogadores", $"{s.PlayerCount}/{max}");
            string codigo = s.Name;
            item.onClick.AddListener(() => Entrar(codigo));
            itens.Add(item.gameObject);
            mostradas++;
        }
        textoVazio.gameObject.SetActive(mostradas == 0);
        if (mostradas == 0) textoVazio.text = "Nenhuma sala aberta agora.\nCrie uma em Novo jogo ou espere alguém criar.";
    }

    private static void Coluna(Button item, string nome, string texto)
    {
        var t = item.transform.Find(nome);
        if (t != null && t.TryGetComponent<TMP_Text>(out var tmp)) tmp.text = texto;
    }

    /// Botão Atualizar: pede a lista de novo e redesenha.
    public void Atualizar()
    {
        var online = OnlineManager.Instance;
        if (online == null || !online.IsReady) { SetStatus("Sem conexão com o servidor."); return; }
        online.PedirListaDeSalas();
        Preencher(online.Salas);
        SetStatus(online.Salas.Count == 0 ? "" : $"{online.Salas.Count} sala(s) encontrada(s).");
    }

    private void Entrar(string codigo)
    {
        if (entrando) return;
        var online = OnlineManager.Instance;
        if (online == null || !online.IsReady) { SetStatus("Sem conexão com o servidor."); return; }
        entrando = true;
        SetStatus("Entrando na partida...");
        if (!online.JoinRoom(codigo)) { entrando = false; SetStatus("Não deu para entrar. Tente de novo."); }
    }

    private void AoEntrar(string codigo)
    {
        SetStatus("Entrou! Carregando a partida...");
        SceneLoader.Instance.ChangeScene(sceneAfterJoin);
    }

    private void AoFalhar(short erro, string msg)
    {
        entrando = false;
        SetStatus(erro == ErrorCode.GameFull ? "Essa sala ficou cheia." :
                  erro == ErrorCode.GameClosed ? "Essa sala foi fechada." :
                  erro == ErrorCode.GameDoesNotExist ? "Essa sala não existe mais." : "Não deu para entrar nessa sala.");
    }

    private void SetStatus(string t) { if (statusText != null) statusText.text = t; }
}
