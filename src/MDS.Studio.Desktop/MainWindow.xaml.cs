using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;

namespace MDS.Studio.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient coreClient = new();
    private readonly HttpClient studioClient = new();
    private string? sessionToken;

    public MainWindow() => InitializeComponent();

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryConfigureCoreClient() || !TryConfigureStudioClient()) return;
        if (string.IsNullOrWhiteSpace(UsernameInput.Text) || string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            StatusText.Text = "Informe o usuário e a senha do MDS Core.";
            return;
        }

        try
        {
            SetBusy(true);
            var response = await coreClient.PostAsJsonAsync("auth/login", new LoginRequest(UsernameInput.Text.Trim(), PasswordInput.Password));
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = "Não foi possível entrar. Confira o usuário, a senha e o endereço do MDS Core.";
                return;
            }

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (login is null || string.IsNullOrWhiteSpace(login.Token) || login.User is null)
            {
                StatusText.Text = "O MDS Core retornou uma resposta de sessão inválida.";
                return;
            }

            sessionToken = login.Token;
            PasswordInput.Clear();
            UserNameText.Text = $"{login.User.Username} — conectado pelo MDS Core";
            RoleText.Text = login.User.Roles?.Length > 0 ? $"Perfil: {string.Join(", ", login.User.Roles)}" : "Perfil não informado.";
            var studioPermissions = (login.User.Permissions ?? [])
                .Where(permission => permission.StartsWith("studio.", StringComparison.Ordinal) || permission == "agent.manage")
                .OrderBy(permission => permission)
                .ToArray();
            PermissionText.Text = studioPermissions.Length > 0 ? $"Acessos do Studio: {string.Join(", ", studioPermissions)}" : "Nenhum acesso ao Studio foi encontrado para esta conta.";
            LoginPanel.Visibility = Visibility.Collapsed;
            SessionPanel.Visibility = Visibility.Visible;
            await ConfirmStudioSessionAsync(login.User);
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "Não foi possível alcançar o MDS Core. Confira se ele está aberto e se o endereço está correto.";
        }
        finally { SetBusy(false); }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                coreClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
                await coreClient.PostAsync("auth/logout", null);
            }
        }
        catch (HttpRequestException) { }
        finally
        {
            sessionToken = null;
            coreClient.DefaultRequestHeaders.Authorization = null;
            studioClient.DefaultRequestHeaders.Authorization = null;
            SessionPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Sessão encerrada.";
        }
    }

    private bool TryConfigureCoreClient()
    {
        if (!Uri.TryCreate(CoreUrlInput.Text.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var coreUrl) ||
            (coreUrl.Scheme != Uri.UriSchemeHttp && coreUrl.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "Informe um endereço HTTP ou HTTPS válido para o MDS Core.";
            return false;
        }

        coreClient.BaseAddress = coreUrl;
        return true;
    }

    private bool TryConfigureStudioClient()
    {
        if (!Uri.TryCreate(StudioUrlInput.Text.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var studioUrl) ||
            (studioUrl.Scheme != Uri.UriSchemeHttp && studioUrl.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "Informe um endereço HTTP ou HTTPS válido para a API do Studio.";
            return false;
        }

        studioClient.BaseAddress = studioUrl;
        return true;
    }

    private async Task ConfirmStudioSessionAsync(CoreUser coreUser)
    {
        try
        {
            studioClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
            var response = await studioClient.GetAsync("me");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = "A sessão do Core está ativa, mas o Studio ainda não a aceitou. Inicie o Studio com a opção de sessão do Core.";
                return;
            }

            var studioUser = await response.Content.ReadFromJsonAsync<StudioUser>();
            StatusText.Text = studioUser?.Id == coreUser.Id
                ? "Sessão confirmada: o Studio reconheceu esta identidade do MDS Core. A senha e o token não são gravados no computador."
                : "A sessão do Core está ativa, mas o Studio ainda está no modo local. Reinicie-o com a opção de sessão do Core.";
            if (studioUser?.Id == coreUser.Id) await RefreshStudioStatusAsync();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "A sessão do Core está ativa. Inicie também a API do Studio para confirmar a integração completa.";
        }
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e) => await RefreshStudioStatusAsync();

    private async Task RefreshStudioStatusAsync()
    {
        try
        {
            var healthResponse = await studioClient.GetAsync("health");
            if (!healthResponse.IsSuccessStatusCode)
            {
                StudioStatusText.Text = "API do Studio indisponível.";
                AgentStatusText.Text = "Não foi possível consultar o agente local.";
                return;
            }

            StudioStatusText.Text = "API do Studio: disponível.";
            var agentsResponse = await studioClient.GetAsync("agents");
            if (!agentsResponse.IsSuccessStatusCode)
            {
                AgentStatusText.Text = $"Não foi possível consultar o agente local (resposta {(int)agentsResponse.StatusCode}).";
                return;
            }

            var agents = await agentsResponse.Content.ReadFromJsonAsync<StudioAgent[]>() ?? [];
            AgentStatusText.Text = agents.Length switch
            {
                0 => "Local Agent: aguardando inscrição.",
                _ => $"Local Agent: {string.Join(" · ", agents.Select(agent => $"{agent.MachineName} ({(agent.IsAuthorized ? "autorizado" : "aguardando autorização")})"))}"
            };
        }
        catch (HttpRequestException)
        {
            StudioStatusText.Text = "API do Studio indisponível.";
            AgentStatusText.Text = "Não foi possível consultar o agente local.";
        }
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        SignInButton.Content = busy ? "Entrando..." : "Entrar";
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record LoginResponse(string? Token, CoreUser? User);
    private sealed record CoreUser(string Id, string Username, string[]? Roles, string[]? Permissions);
    private sealed record StudioUser(string? Id);
    private sealed record StudioAgent(string MachineName, bool IsAuthorized);
}
