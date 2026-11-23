using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela "Como jogar": tutorial em páginas, aberto pelo botão do menu principal.
/// O VilaMenuBuilder (Tools → Menu principal) cria a tela e liga os campos; o texto das páginas fica aqui.
/// </summary>
public class TutorialScreen : MonoBehaviour
{
    [Header("Referências (preenchidas pelo builder)")]
    [SerializeField] private TMP_Text titulo;
    [SerializeField] private TMP_Text texto;
    [SerializeField] private TMP_Text pagina;
    [SerializeField] private Button anterior;
    [SerializeField] private Button proximo;
    [SerializeField] private Button voltar;
    [Tooltip("Tela que some quando esta abre e volta quando esta fecha.")]
    [SerializeField] private GameObject previousScreen;

    private static readonly (string titulo, string texto)[] Paginas =
    {
        ("O que é o EducaUs",
         "A <b>Vila Beira-Rio</b> é uma vila de pescadores, marisqueiras, agricultores e catadores. Depois que a mineradora e o " +
         "porto chegaram, a água ficou suja, o peixe sumiu e as crianças vivem doentes.\n\n" +
         "Vocês são <b>moradores</b> e precisam se organizar para proteger as <b>7 áreas</b> da vila antes que o tempo acabe " +
         "(20 minutos). Ninguém vem de fora salvar a vila: só a comunidade unida consegue."),
        ("Controles",
         "<b>Andar:</b> W A S D ou setas · no mouse, segure o botão na direção · no celular, use o joystick.\n\n" +
         "<b>USAR</b> (tecla E): faz a tarefa ou conversa com quem está perto.\n" +
         "<b>MAPA</b> (tecla M ou Tab): mostra onde você está e o que falta fazer.\n" +
         "<b>MENU</b> (Esc): código da sala, jogadores, abrir sala online e sair."),
        ("Converse com os vizinhos",
         "Moradores com <b>balão amarelo</b> têm uma <b>pista</b> para contar. Fique perto e segure <b>ESCUTAR</b>.\n\n" +
         "A pista vale para todos os jogadores e libera tarefas: o laudo da lama, por exemplo, só abre depois de ouvir a marisqueira.\n\n" +
         "Conversando de novo, o morador vira um <b>apoio</b>: ele conta como uma pessoa nas tarefas coletivas (dá para jogar sozinho)."),
        ("Tarefas e recursos",
         "<b>!</b> amarelo = tarefa · <b>!</b> azul e maior = tarefa <b>coletiva</b>, que precisa de 2 ou 3 pessoas (ou apoios) no ponto.\n\n" +
         "Cada tarefa abre um <b>minijogo</b> curto. Quando todas as tarefas de uma área ficam prontas, ela é protegida e sobem os " +
         "<b>recursos da comunidade</b>: água, saúde, terra e comida."),
        ("O infiltrado da empresa",
         "Com 3 ou mais jogadores, um é sorteado em segredo como <b>infiltrado</b>. Ele finge ajudar e aperta <b>SABOTAR</b> para desfazer " +
         "o que vocês conquistaram. No modo solo não há infiltrado: o desafio é proteger tudo antes que o tempo acabe.\n\n" +
         "Desconfiou? Toque o sino do barracão (<b>ASSEMBLEIA</b>) ou aperte <b>DENUNCIAR</b> logo depois de uma sabotagem. Todos votam e o " +
         "mais votado é dispensado. Cuidado: dispensar um inocente enfraquece a comunidade."),
        ("Começar e vencer",
         "O <b>dono da sala</b> começa a partida no painel <b>INICIAR</b>, no meio do barracão (o mesmo painel reinicia a rodada).\n\n" +
         "<b>Os moradores vencem</b> se protegerem as 7 áreas ou dispensarem o infiltrado.\n" +
         "<b>A empresa vence</b> se o tempo acabar ou se sobrarem só o infiltrado e mais um jogador.\n\n" +
         "No fim, a tela de resumo abre a <b>roda de conversa</b>: o que aconteceu, quem ganhou, quem perdeu e o que a comunidade fez."),
    };

    private int atual;

    private void Awake()
    {
        if (anterior != null) anterior.onClick.AddListener(() => Ir(atual - 1));
        if (proximo != null) proximo.onClick.AddListener(() => { if (atual < Paginas.Length - 1) Ir(atual + 1); else Close(); });
        if (voltar != null) voltar.onClick.AddListener(Close);
    }

    public void Open()
    {
        if (previousScreen != null) previousScreen.SetActive(false);
        gameObject.SetActive(true);
        Ir(0);
    }

    public void Close()
    {
        gameObject.SetActive(false);
        if (previousScreen != null) previousScreen.SetActive(true);
    }

    private void Ir(int i)
    {
        atual = Mathf.Clamp(i, 0, Paginas.Length - 1);
        if (titulo != null) titulo.text = Paginas[atual].titulo;
        if (texto != null) texto.text = Paginas[atual].texto;
        if (pagina != null) pagina.text = $"{atual + 1} / {Paginas.Length}";
        if (anterior != null) anterior.interactable = atual > 0;
        var rotulo = proximo != null ? proximo.GetComponentInChildren<TMP_Text>() : null;
        if (rotulo != null) rotulo.text = atual < Paginas.Length - 1 ? "Próximo ›" : "Entendi!";
    }
}
