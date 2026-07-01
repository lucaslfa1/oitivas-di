using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SinistroAPI.Data;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Models.Entities;

namespace SinistroAPI.Controllers;

/// <summary>
/// Controlador de autenticação do Sentinel: expõe os endpoints de login e auto-cadastro de usuários.
/// </summary>
/// <remarks>
/// COMO funciona:
/// 1. O atributo [ApiController] habilita comportamentos automáticos do ASP.NET Core
///    (binding do corpo via [FromBody], validação de model state e respostas 400 padronizadas).
/// 2. O atributo [Route("api/[controller]")] gera a rota base a partir do nome da classe:
///    "AuthController" -> token "[controller]" vira "Auth" -> rota final "api/Auth".
///    Combinada com os [HttpPost] de cada ação, resulta em "POST api/Auth/login" e "POST api/Auth/register".
/// 3. A única dependência é o <see cref="AppDbContext"/> (Entity Framework Core), injetado via construtor.
///    Toda persistência/consulta de usuários passa por ele, mantendo o backend Azure-only (sem provedores externos).
///
/// PONTO DE ATENÇÃO (segurança): este controlador trata SENHAS EM TEXTO PURO — a comparação no login
/// e o armazenamento no cadastro usam o valor cru de <c>Password</c>, sem hash/salt. Ver detalhes em
/// cada método. Isto é uma dívida de segurança a ser corrigida (ex.: PBKDF2/BCrypt/Argon2).
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Contexto do Entity Framework Core usado para consultar e persistir usuários (tabela Users).
    /// </summary>
    private readonly AppDbContext _context;

    /// <summary>
    /// Recebe o <see cref="AppDbContext"/> por injeção de dependência e o guarda para uso nas ações.
    /// </summary>
    /// <param name="context">
    /// Contexto de banco resolvido pelo container de DI do ASP.NET Core; tipicamente registrado
    /// com escopo por requisição (scoped), de modo que cada chamada HTTP recebe sua própria instância.
    /// </param>
    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Autentica um usuário a partir de username + senha e devolve seus dados de sessão (username e papel).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Recebe <see cref="LoginRequest"/> no corpo da requisição (JSON), desserializado por [FromBody].
    /// 2. Faz UMA única consulta ao banco com <c>FirstOrDefaultAsync</c>, filtrando por
    ///    Username E Password ao mesmo tempo. Ou seja, a verificação de credenciais é delegada
    ///    ao próprio banco: se nenhuma linha casar, o usuário/senha é considerado inválido.
    /// 3. Se a consulta não retornar registro (<c>user == null</c>), responde 401 Unauthorized
    ///    com mensagem genérica ("Usuário ou senha incorretos"). A mensagem é propositalmente
    ///    genérica para não revelar se foi o usuário ou a senha que falhou (evita enumeração de contas).
    /// 4. Se encontrar, responde 200 OK com <see cref="LoginResponse"/> contendo o flag de sucesso,
    ///    o Username e o Role (papel/perfil) — usados pelo front para autorização e exibição.
    ///
    /// PONTO A CORRIGIR (senha em texto puro): a comparação <c>u.Password == request.Password</c>
    /// confronta a senha enviada diretamente contra o valor armazenado, SEM hash nem salt. Isso significa
    /// que as senhas trafegam/ficam comparáveis em claro e qualquer vazamento do banco expõe todas elas.
    /// O correto seria armazenar apenas um hash forte (PBKDF2/BCrypt/Argon2) e, no login, carregar o
    /// usuário só por Username e validar a senha com a verificação de hash (que também mitiga timing attacks).
    ///
    /// OBSERVAÇÃO: este endpoint NÃO emite token (JWT/cookie) — apenas confirma as credenciais e devolve
    /// o papel. A sessão/autorização efetiva é responsabilidade da camada que consome esta resposta.
    /// </remarks>
    /// <param name="request">Credenciais informadas pelo usuário: <c>Username</c> e <c>Password</c>.</param>
    /// <returns>
    /// 200 OK com <see cref="LoginResponse"/> (sucesso, mensagem, username e role) quando as credenciais
    /// conferem; 401 Unauthorized com mensagem genérica quando não há usuário correspondente.
    /// </returns>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        // Verificação de credenciais delegada ao banco: a senha em texto puro é comparada diretamente
        // (ponto a corrigir — ver <remarks>). Sem registro casando, trata-se de login inválido.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Password == request.Password);

        if (user == null)
        {
            // Mensagem genérica de propósito: não diz se errou usuário ou senha (anti-enumeração de contas).
            return Unauthorized(new LoginResponse(false, "Usuário ou senha incorretos"));
        }

        // Sucesso: devolve identidade mínima (username) e papel (role) para o front aplicar autorização.
        return Ok(new LoginResponse(true, "Login bem-sucedido", user.Username, user.Role));
    }

    /// <summary>
    /// Realiza o auto-cadastro de um novo usuário, garantindo unicidade do username e atribuindo papel padrão.
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline passo a passo):
    /// 1. Recebe <see cref="LoginRequest"/> no corpo (reaproveita o mesmo DTO do login: username + senha).
    /// 2. Checa duplicidade com <c>AnyAsync(u =&gt; u.Username == request.Username)</c> — consulta booleana
    ///    barata que retorna apenas se já existe alguém com aquele username.
    /// 3. Se já existir, responde 400 BadRequest ("Este nome de usuário já está em uso") e encerra,
    ///    sem tocar no banco. (Nota: há uma janela de corrida entre o AnyAsync e o SaveChanges; em
    ///    concorrência alta a unicidade definitiva deve ser garantida por índice UNIQUE em Username.)
    /// 4. Caso o username esteja livre, monta um <see cref="UserModel"/> e persiste com
    ///    <c>Add</c> + <c>SaveChangesAsync</c> (INSERT efetivado nesse ponto).
    /// 5. Sempre responde 200 OK com mensagem pedindo ativação pelo administrador — ou seja, o cadastro
    ///    NÃO concede acesso imediato; o usuário fica pendente até liberação manual (gate de segurança).
    ///
    /// Justificativa dos valores fixos (magic values):
    /// - <c>Role = "Membro"</c>: papel padrão de MENOR privilégio. Atribuído no servidor (e não vindo do
    ///   request) de propósito, para impedir que o cliente se auto-promova a Admin via payload manipulado.
    ///
    /// PONTO A CORRIGIR (senha em texto puro): <c>Password = request.Password</c> grava a senha
    /// EXATAMENTE como recebida, sem hash nem salt. O comentário inline original ("Ideally hashed...")
    /// reconhece a dívida: o correto é nunca armazenar a senha em claro — gerar e salvar somente o hash
    /// forte (PBKDF2/BCrypt/Argon2), de modo que um vazamento do banco não exponha as senhas reais.
    /// </remarks>
    /// <param name="request">Dados do novo usuário: <c>Username</c> desejado e <c>Password</c>.</param>
    /// <returns>
    /// 200 OK com <see cref="LoginResponse"/> de sucesso (cadastro pendente de ativação) quando o username
    /// está disponível; 400 BadRequest quando o username já está em uso.
    /// </returns>
    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] LoginRequest request)
    {
        // Pré-checagem de unicidade do username (consulta booleana barata via AnyAsync).
        // Garantia definitiva contra corrida deve vir de índice UNIQUE em Username (ver <remarks>).
        var existingUser = await _context.Users.AnyAsync(u => u.Username == request.Username);
        if (existingUser)
        {
            return BadRequest(new LoginResponse(false, "Este nome de usuário já está em uso"));
        }

        var newUser = new UserModel
        {
            Username = request.Username,
            Password = request.Password, // Ideally hashed, but keeping consistency with existing logic
            Role = "Membro" // Default safe role
        };

        // Persiste o novo usuário; o INSERT só é efetivado no SaveChangesAsync.
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        // Cadastro não libera acesso imediato: usuário fica pendente de ativação pelo administrador.
        return Ok(new LoginResponse(true, "Usuário registrado com sucesso. Aguarde a ativação pelo administrador."));
    }
}
