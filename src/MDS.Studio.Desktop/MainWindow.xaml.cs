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
    private string? currentUsername;
    private Guid? currentProjectId;

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
            currentUsername = login.User.Username;
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
            currentUsername = null;
            coreClient.DefaultRequestHeaders.Authorization = null;
            studioClient.DefaultRequestHeaders.Authorization = null;
            SessionPanel.Visibility = Visibility.Collapsed;
            ProjectsPanel.Visibility = Visibility.Collapsed;
            ProjectDetailsPanel.Visibility = Visibility.Collapsed;
            currentProjectId = null;
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
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

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        HomePanel.Visibility = Visibility.Visible;
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            StatusText.Text = "Entre com sua conta do MDS Core antes de consultar o diagnóstico.";
            HomePanel.Visibility = Visibility.Visible;
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Visible;
        await RefreshDiagnosticsAsync();
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => await RefreshDiagnosticsAsync();

    private async void Projects_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            StatusText.Text = "Entre com sua conta do MDS Core antes de consultar os projetos.";
            HomePanel.Visibility = Visibility.Visible;
            ProjectsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Visible;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        await RefreshProjectsAsync();
    }

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e) => await RefreshProjectsAsync();

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var name = ProjectNameInput.Text.Trim();
        var localPath = ProjectPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(localPath))
        {
            ProjectsStatusText.Text = "Informe o nome e a pasta local do projeto.";
            return;
        }

        try
        {
            var response = await studioClient.PostAsJsonAsync("projects", new CreateProjectRequest(name, localPath));
            if (!response.IsSuccessStatusCode)
            {
                ProjectsStatusText.Text = $"Não foi possível criar o projeto (resposta {(int)response.StatusCode}). A pasta talvez já esteja cadastrada.";
                return;
            }

            ProjectNameInput.Clear();
            ProjectPathInput.Clear();
            ProjectsStatusText.Text = "Projeto criado.";
            await RefreshProjectsAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async Task RefreshProjectsAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("projects");
            if (!response.IsSuccessStatusCode)
            {
                ProjectSelector.ItemsSource = null;
                ProjectsStatusText.Text = $"O Studio recusou a consulta (resposta {(int)response.StatusCode}).";
                return;
            }

            var projects = await response.Content.ReadFromJsonAsync<StudioProject[]>() ?? [];
            ProjectSelector.ItemsSource = projects;
            ProjectSelector.SelectedItem = null;
            ProjectsStatusText.Text = projects.Length == 0 ? "Lista atualizada." : $"{projects.Length} projeto(s) cadastrado(s).";
        }
        catch (HttpRequestException)
        {
            ProjectSelector.ItemsSource = null;
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectSelector.SelectedItem is not StudioProject selected)
        {
            ProjectsStatusText.Text = "Escolha um projeto da lista primeiro.";
            return;
        }

        try
        {
            var response = await studioClient.GetAsync($"projects/{selected.ProjectId}");
            if (!response.IsSuccessStatusCode)
            {
                ProjectsStatusText.Text = $"Não foi possível abrir o projeto (resposta {(int)response.StatusCode}).";
                return;
            }

            var project = await response.Content.ReadFromJsonAsync<StudioProject>();
            if (project is null) return;
            currentProjectId = project.ProjectId;
            ProjectDetailsHeading.Text = project.Name;
            ProjectDetailsNameInput.Text = project.Name;
            ProjectDetailsPathInput.Text = project.LocalPath;
            ProjectDescriptionInput.Text = project.Description ?? string.Empty;
            SelectProjectStatus(project.Status);
            ProjectDetailsPanel.Visibility = Visibility.Visible;
            await RefreshOrganizationAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void SaveProjectDetails_Click(object sender, RoutedEventArgs e)
    {
        if (currentProjectId is null) return;
        var name = ProjectDetailsNameInput.Text.Trim();
        var path = ProjectDetailsPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
        {
            ProjectsStatusText.Text = "Nome e pasta local são obrigatórios.";
            return;
        }

        try
        {
            var response = await studioClient.PutAsJsonAsync($"projects/{currentProjectId}", new UpdateProjectRequest(name, path, ProjectDescriptionInput.Text.Trim(), SelectedContent(ProjectStatusInput, "Ativo")));
            if (!response.IsSuccessStatusCode)
            {
                ProjectsStatusText.Text = $"Não foi possível salvar os detalhes (resposta {(int)response.StatusCode}).";
                return;
            }

            ProjectDetailsHeading.Text = name;
            ProjectsStatusText.Text = "Detalhes salvos.";
            await RefreshProjectsAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void AddOrganizationItem_Click(object sender, RoutedEventArgs e)
    {
        if (currentProjectId is null) return;
        var title = OrganizationTitleInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ProjectsStatusText.Text = "Informe o título do item interno.";
            return;
        }

        try
        {
            var response = await studioClient.PostAsJsonAsync($"projects/{currentProjectId}/organization", new CreateProjectOrganizationItemRequest(title, OrganizationNotesInput.Text.Trim(), SelectedContent(OrganizationStatusInput, "Pendente")));
            if (!response.IsSuccessStatusCode)
            {
                ProjectsStatusText.Text = $"Não foi possível adicionar o item (resposta {(int)response.StatusCode}).";
                return;
            }

            OrganizationTitleInput.Clear();
            OrganizationNotesInput.Clear();
            OrganizationStatusInput.SelectedIndex = 0;
            ProjectsStatusText.Text = "Item interno adicionado.";
            await RefreshOrganizationAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async Task RefreshOrganizationAsync()
    {
        if (currentProjectId is null) return;
        try
        {
            var response = await studioClient.GetAsync($"projects/{currentProjectId}/organization");
            if (!response.IsSuccessStatusCode)
            {
                OrganizationListText.Text = "Os itens internos não estão disponíveis.";
                return;
            }

            var items = await response.Content.ReadFromJsonAsync<ProjectOrganizationItem[]>() ?? [];
            OrganizationListText.Text = items.Length == 0
                ? "Nenhum item interno registrado."
                : string.Join(Environment.NewLine + Environment.NewLine, items.Select(item => $"{item.Title} · {item.Status}{(string.IsNullOrWhiteSpace(item.Notes) ? string.Empty : Environment.NewLine + item.Notes)}"));
        }
        catch (HttpRequestException)
        {
            OrganizationListText.Text = "Os itens internos não estão disponíveis.";
        }
    }

    private void SelectProjectStatus(string status) => ProjectStatusInput.SelectedIndex = status switch
    {
        "Em espera" => 1,
        "Concluído" => 2,
        _ => 0
    };

    private static string SelectedContent(System.Windows.Controls.ComboBox comboBox, string fallback)
        => (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? fallback;

    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            var diagnosticsResponse = await studioClient.GetAsync("diagnostics");
            if (!diagnosticsResponse.IsSuccessStatusCode)
            {
                DiagnosticStatusText.Text = $"O Studio recusou a consulta de diagnóstico (resposta {(int)diagnosticsResponse.StatusCode}).";
                return;
            }

            var diagnostics = await diagnosticsResponse.Content.ReadFromJsonAsync<StudioDiagnostics>();
            var agentsResponse = await studioClient.GetAsync("agents");
            var agents = agentsResponse.IsSuccessStatusCode
                ? await agentsResponse.Content.ReadFromJsonAsync<StudioAgent[]>() ?? []
                : [];
            DiagnosticIdentityText.Text = $"Usuário: {currentUsername}";
            DiagnosticApiText.Text = $"API do Studio: disponível · versão {diagnostics?.Api ?? "não informada"} · ambiente {diagnostics?.Environment ?? "não informado"}.";
            DiagnosticAgentText.Text = agents.Length == 0
                ? "Local Agent: nenhum agente inscrito."
                : $"Local Agent: {string.Join(" · ", agents.Select(agent => $"{agent.MachineName} ({(agent.IsAuthorized ? "autorizado" : "aguardando autorização")})"))}";
            DiagnosticStatusText.Text = "Consulta atualizada.";
        }
        catch (HttpRequestException)
        {
            DiagnosticStatusText.Text = "Não foi possível consultar a API do Studio.";
        }
    }

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
    private sealed record StudioDiagnostics(string? Environment, string? Api);
    private sealed record CreateProjectRequest(string Name, string LocalPath);
    private sealed record StudioProject(Guid ProjectId, string Name, string LocalPath, string? Description, string Status, DateTimeOffset CreatedAt);
    private sealed record UpdateProjectRequest(string Name, string LocalPath, string? Description, string Status);
    private sealed record CreateProjectOrganizationItemRequest(string Title, string? Notes, string Status);
    private sealed record ProjectOrganizationItem(Guid ItemId, string Title, string? Notes, string Status, DateTimeOffset CreatedAt);
}
