# EducaUs

**Um jogo digital cooperativo de educação ambiental**, inspirado no *Among Us*.

Os jogadores são moradores da **Vila Beira-Rio**, uma comunidade prejudicada por uma mineradora. Para proteger a vila, eles conversam com os moradores mais antigos, descobrem pistas, realizam ações comunitárias e se reúnem em assembleia. Enquanto isso, um **infiltrado da empresa**, sorteado em segredo, tenta atrapalhar a organização da comunidade.

Projeto desenvolvido para a disciplina **Educação Ambiental (BIO606)** da Universidade Estadual de Feira de Santana (UEFS), semestre 2026.2, sob orientação da Profa. Viviane Martins.

## Jogar agora

**[eda-educaus.vercel.app](https://eda-educaus.vercel.app)**

Funciona direto no navegador, sem instalar nada. Funciona melhor no Chrome ou no Edge, no computador.

## Como jogar

| | |
|---|---|
| **Jogadores** | 1 (modo solo) ou 4 a 6 (online) |
| **Duração** | partida de até 20 minutos |
| **Objetivo dos moradores** | proteger as 7 áreas da vila ou descobrir o infiltrado |
| **Objetivo do infiltrado** | impedir isso sem ser descoberto |

1. **Entrar:** digite seu nome no menu. O botão **Como jogar** explica as regras.
2. **Sozinho:** clique em **Novo jogo**. No modo solo não há infiltrado.
3. **Em grupo:** uma pessoa cria a sala, e as outras entram por **Salas abertas** ou pelo **código da sala**.
4. **Começar:** quem criou a sala inicia a partida no painel **INICIAR**, no meio da associação de moradores. Com 3 ou mais pessoas, uma é sorteada como infiltrado.
5. **Durante a partida:**
   - converse com os moradores (balão amarelo) para descobrir pistas e ganhar apoios;
   - faça as tarefas, que são minijogos curtos (algumas precisam de 2 ou 3 pessoas juntas);
   - desconfiou de alguém? Vá até o **sino da associação** para denunciar ou convocar uma assembleia.
6. **Fim:** a tela final resume a partida e traz perguntas para uma roda de conversa.

### Controles

| Ação | Teclado | Celular |
|---|---|---|
| Andar | Setas ou WASD | Joystick |
| Usar / conversar | E | Botão USAR |
| Mapa | M ou Tab | Botão MAPA |
| Menu da partida | Esc | Botão MENU |
| Sabotar (infiltrado) | Q | Botão SABOTAR |
| Boato (infiltrado) | B | Botão BOATO |
| Ação do lugar (infiltrado) | F | Botão vermelho |

### O infiltrado pode

- **Sabotar:** desfaz uma tarefa já concluída.
- **Comprar:** deixa um morador calado por um tempo (balão cinza).
- **Interditar:** bloqueia uma tarefa com uma obra irregular, que duas pessoas juntas podem embargar.
- **Espalhar um boato:** tira o mapa da tela dos moradores por alguns segundos.
- **Usar os bueiros:** anda pelo esgoto entre pontos da vila.

## Tecnologias

- [Unity 6.3 LTS](https://unity.com/)
- [Photon PUN 2](https://www.photonengine.com/pun) para o modo online
- Input System do Unity (teclado, mouse e toque)

## Abrir o projeto

1. Clone o repositório e abra a pasta no **Unity 6.3 LTS** pelo Unity Hub.
2. Para o modo online, crie um App ID gratuito no [painel do Photon](https://dashboard.photonengine.com/) e coloque em **Window → Photon Unity Networking → Highlight Server Settings → App Id PUN**.
3. Abra a cena do menu principal e aperte **Play**.

### Gerar as builds

Em **File → Build Profiles**:

- **Web:** a pasta gerada pode ser publicada em qualquer hospedagem de site estático (este projeto usa a Vercel).
- **Android:** gera um APK.
- **Windows / Linux:** gera um executável.

Para a versão web, deixe ligadas as opções **Run In Background** e **Name Files As Hashes** em *Player Settings → Web*.

## Contexto educativo

O jogo se apoia numa educação ambiental crítica e comunitária: os problemas ambientais não dependem só das atitudes de cada pessoa, mas de decisões econômicas e políticas, e as comunidades atingidas têm saberes importantes para enfrentá-los. A proposta foi pensada para espaços não formais, como associações de moradores e centros comunitários. Ao final de cada partida, recomenda-se uma roda de conversa sobre o que aconteceu, quem ganhou, quem perdeu e o que a comunidade fez.

Principais referências: Layrargues e Lima (2014), Stortti e Sanchez (2017), Freire (1987), Gohn (2006), Kishimoto (2011) e a Política Nacional de Educação Ambiental (Lei nº 9.795/1999).

## Autor

**Bruno Campos de Oliveira Rocha**, Universidade Estadual de Feira de Santana (UEFS).

*Among Us* é uma marca da InnerSloth. Este projeto é um trabalho acadêmico, sem fins comerciais, e não tem ligação com a InnerSloth.
