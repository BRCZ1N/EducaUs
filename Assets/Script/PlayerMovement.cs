using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimento top-down: teclado (WASD / setas), controle, mouse (segurar no mapa) ou joystick de toque.
/// Offline ou fora de sala: você controla. Dentro de uma sala do Photon: só o dono do personagem controla.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviourPun
{
    [SerializeField] private float speed = 4f;
    [SerializeField] private SpriteRenderer sprite;   // opcional, para virar o personagem

    private Rigidbody2D rb;
    private Vector2 input;

    // AmOwner (e não IsMine): quando alguém cai, o dono da sala vira "controlador" do boneco que ficou na sala,
    // e com IsMine ele passava a mexer os dois bonecos ao mesmo tempo.
    private bool PodeControlar => !PhotonNetwork.InRoom || photonView == null || photonView.AmOwner;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;              // movimento liso na tela
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (sprite == null) sprite = GetComponent<SpriteRenderer>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null) sprite.spriteSortPoint = SpriteSortPoint.Pivot;
        // a animação de andar (frente, costas, lados) e a cor de cada jogador
        if (GetComponentInChildren<AnimacaoPersonagem>() == null) gameObject.AddComponent<AnimacaoPersonagem>();
        if (GetComponent<NomeAcima>() == null) gameObject.AddComponent<NomeAcima>();               // nome acima da cabeça
    }

    private void Start()
    {
        // A cópia de outro jogador só segue a rede: sem física local, ninguém empurra ninguém
        if (!PodeControlar)
            rb.bodyType = RigidbodyType2D.Kinematic;

        // Jogadores não colidem entre si (cada um que nasce ignora os que já existem)
        var meus = GetComponentsInChildren<Collider2D>();
        foreach (var outro in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (outro == this) continue;
            foreach (var a in meus)
                foreach (var b in outro.GetComponentsInChildren<Collider2D>())
                    Physics2D.IgnoreCollision(a, b, true);
        }
    }

    private void Update()
    {
        if (!PodeControlar) return;

        input = ReadInput();

    }

    private void FixedUpdate()
    {
        if (!PodeControlar) return;
        rb.linearVelocity = input * speed;
    }

    private static Vector2 ReadInput()
    {
        if (MenuPartida.Aberto || MinigameTarefa.Aberto || HudAmbiental.PainelAberto || Assembleia.Aberto) return Vector2.zero;   // menu ou minigame aberto: o boneco para

        Vector2 v = Vector2.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1;
        }

        var pad = Gamepad.current;
        if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.04f)
            v = pad.leftStick.ReadValue();

        if (v == Vector2.zero) v = ControleToque.Direcao;   // mouse (segurar no mapa) ou joystick de toque

        return Vector2.ClampMagnitude(v, 1f); // diagonal não fica mais rápida
    }
}
