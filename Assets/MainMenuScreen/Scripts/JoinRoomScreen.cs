using System;
using System.Collections;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela "Juntar-se a partida".
/// - Entrar: chama OnlineManager.Instance.JoinRoom(código);
/// - ouve os eventos do OnlineManager (JoinSucceeded, JoinFailed, Disconnected)
///   e mostra o resultado na tela.
/// </summary>
public class JoinRoomScreen : MonoBehaviour
{
    [Header("Referências (preenchidas pelo builder)")]
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text joinButtonLabel;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private RectTransform shakeTarget;
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Tela que some quando esta abre e volta quando esta fecha (ex.: o painel com Novo jogo / Juntar-se / Sair).")]
    [SerializeField] private GameObject previousScreen;

    [Header("Configuração")]
    [Tooltip("Cena aberta depois de entrar na sala.")]
    [SerializeField] private string sceneAfterJoin = "Game";
    [SerializeField] private int minCodeLength = 4;
    [SerializeField] private float fadeDuration = 0.2f;

    [Header("Cores do status")]
    [SerializeField] private Color infoColor = new Color32(243, 230, 216, 255);
    [SerializeField] private Color errorColor = new Color32(255, 110, 80, 255);
    [SerializeField] private Color successColor = new Color32(150, 220, 120, 255);

    /// <summary>Disparado quando o jogador clica em Voltar.</summary>
    public event Action BackRequested;

    public string Code => codeInput.text.Trim().ToUpperInvariant();
    public bool IsBusy { get; private set; }

    private string joinLabelDefault;
    private Vector2 shakeOrigin;
    private Coroutine dotsRoutine, shakeRoutine, fadeRoutine;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        codeInput.characterLimit = 0;
        joinLabelDefault = joinButtonLabel != null ? joinButtonLabel.text : "Entrar";
        if (shakeTarget != null) shakeOrigin = shakeTarget.anchoredPosition;

        codeInput.onValueChanged.AddListener(_ =>
        {
            if (!IsBusy) SetStatus("", infoColor); // apaga o erro quando o jogador volta a digitar
            RefreshJoinButton();
        });
        codeInput.onSubmit.AddListener(_ => Submit());
        joinButton.onClick.AddListener(Submit);
        backButton.onClick.AddListener(Back);
    }

    private void OnEnable()
    {
        var online = OnlineManager.Instance;
        if (online == null) return;

        online.JoinSucceeded += HandleJoinSucceeded;
        online.JoinFailed += HandleJoinFailed;
        online.Disconnected += HandleDisconnected;
    }

    private void OnDisable()
    {
        var online = OnlineManager.Instance;
        if (online != null)
        {
            online.JoinSucceeded -= HandleJoinSucceeded;
            online.JoinFailed -= HandleJoinFailed;
            online.Disconnected -= HandleDisconnected;
        }

        StopAllCoroutines();
        dotsRoutine = shakeRoutine = fadeRoutine = null;
        if (shakeTarget != null) shakeTarget.anchoredPosition = shakeOrigin;
    }

    // ------------------------------------------------------------------ abrir / fechar

    /// <summary>Ligue no OnClick do botão "Juntar-se a partida".</summary>
    public void Open()
    {
        if (previousScreen != null) previousScreen.SetActive(false);
        gameObject.SetActive(true);

        ResetState(clearCode: true);
        Fade(0f, 1f);
        FocusInput();
    }

    public void Close()
    {
        gameObject.SetActive(false);
        if (previousScreen != null) previousScreen.SetActive(true);
    }

    private void Back()
    {
        if (IsBusy) return;
        BackRequested?.Invoke();
        Close();
    }

    // ------------------------------------------------------------------ envio

    private void Submit()
    {
        if (IsBusy) return;

        if (Code.Length < minCodeLength)
        {
            ShowError($"O código tem pelo menos {minCodeLength} caracteres.");
            return;
        }

        var online = OnlineManager.Instance;
        if (online == null)
        {
            Debug.LogWarning("[JoinRoomScreen] OnlineManager não encontrado. O Bootstrap está carregado?");
            ShowError("Serviço online indisponível.");
            return;
        }

        if (!online.IsReady)
        {
            ShowConnectionError();
            return;
        }

        ShowSearching();

        if (!online.JoinRoom(Code))
            ShowError("Não foi possível enviar o pedido. Tente de novo.");
    }

    // ------------------------------------------------------------------ respostas do OnlineManager

    private void HandleJoinSucceeded(string roomCode)
    {
        if (!IsBusy) return; // a entrada não veio desta tela (ex.: o host criando sala)

        ShowSuccess();
        SceneLoader.Instance.ChangeScene(sceneAfterJoin);
    }

    private void HandleJoinFailed(short returnCode, string message)
    {
        if (!IsBusy) return;

        switch (returnCode)
        {
            case ErrorCode.GameDoesNotExist: ShowRoomNotFound(); break;
            case ErrorCode.GameFull: ShowRoomFull(); break;
            case ErrorCode.GameClosed: ShowRoomClosed(); break;
            default: ShowError($"Não foi possível entrar ({message})."); break;
        }
    }

    private void HandleDisconnected(DisconnectCause cause)
    {
        if (IsBusy) ShowConnectionError();
    }

    // ------------------------------------------------------------------ estados da tela

    public void ShowSearching(string message = "Procurando sala")
    {
        IsBusy = true;
        SetInteractable(false);
        if (joinButtonLabel != null) joinButtonLabel.text = "Aguarde";
        StopDots();
        dotsRoutine = StartCoroutine(AnimateDots(message));
    }

    public void ShowError(string message)
    {
        IsBusy = false;
        StopDots();
        if (joinButtonLabel != null) joinButtonLabel.text = joinLabelDefault;
        SetInteractable(true);
        SetStatus(message, errorColor);
        Shake();
        FocusInput();
    }

    public void ShowSuccess(string message = "Sala encontrada! Entrando...")
    {
        IsBusy = true; // continua travado até a cena trocar
        StopDots();
        SetInteractable(false);
        SetStatus(message, successColor);
    }

    // Atalhos para os erros mais comuns
    public void ShowRoomNotFound() => ShowError("Sala não encontrada. Confira o código.");
    public void ShowRoomFull() => ShowError("Essa sala está cheia.");
    public void ShowRoomClosed() => ShowError("Essa partida já começou ou foi fechada.");
    public void ShowConnectionError() => ShowError("Sem conexão com o servidor. Tente de novo.");

    // ------------------------------------------------------------------ helpers

    private void ResetState(bool clearCode)
    {
        IsBusy = false;
        StopDots();
        if (clearCode) codeInput.text = "";
        if (joinButtonLabel != null) joinButtonLabel.text = joinLabelDefault;
        SetStatus("", infoColor);
        SetInteractable(true);
    }

    private void SetInteractable(bool value)
    {
        codeInput.interactable = value;
        backButton.interactable = value;
        RefreshJoinButton();
    }

    private void RefreshJoinButton()
    {
        joinButton.interactable = !IsBusy && Code.Length >= minCodeLength;
    }

    private void SetStatus(string text, Color color)
    {
        statusText.text = text;
        statusText.color = color;
    }

    private void FocusInput()
    {
        if (!isActiveAndEnabled || !codeInput.interactable) return;
        codeInput.Select();
        codeInput.ActivateInputField();
    }

    private void StopDots()
    {
        if (dotsRoutine != null) StopCoroutine(dotsRoutine);
        dotsRoutine = null;
    }

    private IEnumerator AnimateDots(string baseText)
    {
        statusText.color = infoColor;
        int i = 0;
        while (true)
        {
            statusText.text = baseText + new string('.', i % 4);
            i++;
            yield return new WaitForSecondsRealtime(0.35f);
        }
    }

    private void Shake()
    {
        if (shakeTarget == null || !isActiveAndEnabled) return;
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        const float duration = 0.35f, amplitude = 12f, frequency = 40f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float decay = 1f - t / duration;
            shakeTarget.anchoredPosition = shakeOrigin + new Vector2(Mathf.Sin(t * frequency) * amplitude * decay, 0f);
            yield return null;
        }
        shakeTarget.anchoredPosition = shakeOrigin;
        shakeRoutine = null;
    }

    private void Fade(float from, float to)
    {
        if (canvasGroup == null) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(from, to));
    }

    private IEnumerator FadeRoutine(float from, float to)
    {
        float t = 0f;
        canvasGroup.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
        fadeRoutine = null;
    }
}