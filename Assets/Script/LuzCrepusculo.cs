using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Muda a Global Light 2D conforme o progresso da recuperação:
/// 0 = crepúsculo (avermelhado e escuro), 1 = dia (luz normal).
/// Se houver um GerenciadorAmbiental na cena, segue o progresso dele sozinha.
/// Requer URP com o 2D Renderer (o template "Universal 2D" já vem assim).
/// </summary>
public class LuzCrepusculo : MonoBehaviour
{
    [Tooltip("Vazio = procura a Global Light 2D da cena.")]
    [SerializeField] private Light2D globalLight;
    [SerializeField] private bool seguirGerenciador = true;

    [Header("Início (poluído)")]
    [SerializeField] private Color corCrepusculo = new Color(1f, 0.62f, 0.5f);
    [SerializeField] private float intensidadeCrepusculo = 0.7f;

    [Header("Vitória (recuperado)")]
    [SerializeField] private Color corDia = Color.white;
    [SerializeField] private float intensidadeDia = 1f;

    [Tooltip("Segundos para ir de um extremo ao outro.")]
    [SerializeField] private float suavidade = 2f;

    [Range(0f, 1f)]
    [SerializeField] private float progresso;   // 0..1
    private float atual;

    /// <summary>Chame com (áreas recuperadas / total de áreas) se não usar o GerenciadorAmbiental.</summary>
    public void SetProgresso(float valor) => progresso = Mathf.Clamp01(valor);

    private void Start()
    {
        if (globalLight == null)
            foreach (var l in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
                if (l.lightType == Light2D.LightType.Global) { globalLight = l; break; }
        if (globalLight == null) Debug.LogWarning("[LuzCrepusculo] Nenhuma Global Light 2D na cena.");
        atual = progresso;
        Aplicar();
    }

    private void Update()
    {
        if (seguirGerenciador && GerenciadorAmbiental.Instancia != null)
            progresso = GerenciadorAmbiental.Instancia.ProgressoAreas;
        atual = Mathf.MoveTowards(atual, progresso, Time.deltaTime / suavidade);
        Aplicar();
    }

    private void Aplicar()
    {
        if (globalLight == null) return;
        globalLight.color = Color.Lerp(corCrepusculo, corDia, atual);
        globalLight.intensity = Mathf.Lerp(intensidadeCrepusculo, intensidadeDia, atual);
    }
}
