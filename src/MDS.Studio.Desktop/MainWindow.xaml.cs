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
            LibraryPanel.Visibility = Visibility.Collapsed;
            PatternsPanel.Visibility = Visibility.Collapsed;
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
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
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
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
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
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Visible;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        await RefreshProjectsAsync();
    }

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e) => await RefreshProjectsAsync();

    private async void Library_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            StatusText.Text = "Entre com sua conta do MDS Core antes de consultar a biblioteca.";
            HomePanel.Visibility = Visibility.Visible;
            LibraryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        PatternsPanel.Visibility = Visibility.Collapsed;
        await RefreshLibraryProjectsAsync();
        await RefreshLibraryAsync();
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private async void Patterns_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            StatusText.Text = "Entre com sua conta do MDS Core antes de consultar os moldes.";
            HomePanel.Visibility = Visibility.Visible;
            PatternsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Visible;
        await RefreshPatternProjectsAsync();
        await RefreshPatternsAsync();
    }

    private async void RefreshPatterns_Click(object sender, RoutedEventArgs e) => await RefreshPatternsAsync();

    private async void ApplyPatternFilters_Click(object sender, RoutedEventArgs e) => await RefreshPatternsAsync();

    private async void CreatePattern_Click(object sender, RoutedEventArgs e)
    {
        if (PatternProjectSelector.SelectedItem is not StudioProject project)
        {
            PatternsStatusText.Text = "Escolha o projeto do molde.";
            return;
        }

        var name = PatternNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            PatternsStatusText.Text = "Informe o nome do molde.";
            return;
        }

        try
        {
            var response = await studioClient.PostAsJsonAsync("patterns", new CreatePatternRequest(project.ProjectId, name, PatternNotesInput.Text.Trim(), SelectedContent(PatternStatusInput, "Rascunho")));
            if (!response.IsSuccessStatusCode)
            {
                PatternsStatusText.Text = $"Não foi possível registrar o molde (resposta {(int)response.StatusCode}).";
                return;
            }

            PatternNameInput.Clear();
            PatternNotesInput.Clear();
            PatternStatusInput.SelectedIndex = 0;
            PatternProjectSelector.SelectedItem = null;
            PatternsStatusText.Text = "Molde registrado.";
            await RefreshPatternsAsync();
        }
        catch (HttpRequestException)
        {
            PatternsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void UpdatePatternStatus_Click(object sender, RoutedEventArgs e)
    {
        if (ExistingPatternSelector.SelectedItem is not StudioPattern pattern)
        {
            PatternsStatusText.Text = "Escolha um molde antes de atualizar a situação.";
            return;
        }
        if (ExistingPatternProjectSelector.SelectedItem is not StudioProject project)
        {
            PatternsStatusText.Text = "Escolha o projeto do molde.";
            return;
        }
        var name = ExistingPatternNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            PatternsStatusText.Text = "Informe o nome do molde.";
            return;
        }

        try
        {
            var response = await studioClient.PutAsJsonAsync($"patterns/{pattern.PatternId}", new UpdatePatternRequest(project.ProjectId, name, ExistingPatternNotesInput.Text.Trim(), SelectedContent(ExistingPatternStatusInput, "Rascunho")));
            if (!response.IsSuccessStatusCode)
            {
                PatternsStatusText.Text = $"Não foi possível atualizar o molde (resposta {(int)response.StatusCode}).";
                return;
            }

            PatternsStatusText.Text = "Molde atualizado.";
            await RefreshPatternsAsync();
        }
        catch (HttpRequestException)
        {
            PatternsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private void ExistingPatternSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ExistingPatternSelector.SelectedItem is not StudioPattern pattern) return;
        ExistingPatternNameInput.Text = pattern.Name;
        ExistingPatternNotesInput.Text = pattern.Notes ?? string.Empty;
        ExistingPatternStatusInput.SelectedIndex = pattern.Status switch
        {
            "Em revisão" => 1,
            "Aprovado" => 2,
            _ => 0
        };
        if (ExistingPatternProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            ExistingPatternProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == pattern.ProjectId);
    }

    private async Task RefreshPatternProjectsAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("projects");
            PatternProjectSelector.ItemsSource = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<StudioProject[]>() ?? []
                : [];
            PatternProjectSelector.SelectedItem = null;
            ExistingPatternProjectSelector.ItemsSource = PatternProjectSelector.ItemsSource;
            ExistingPatternProjectSelector.SelectedItem = null;
            PatternFilterProjectSelector.ItemsSource = PatternProjectSelector.ItemsSource;
            PatternFilterProjectSelector.SelectedItem = null;
        }
        catch (HttpRequestException)
        {
            PatternProjectSelector.ItemsSource = null;
            ExistingPatternProjectSelector.ItemsSource = null;
            PatternFilterProjectSelector.ItemsSource = null;
        }
    }

    private async Task RefreshPatternsAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("patterns");
            if (!response.IsSuccessStatusCode)
            {
                PatternsListText.Text = "A lista de moldes não está disponível.";
                PatternsStatusText.Text = $"O Studio recusou a consulta (resposta {(int)response.StatusCode}).";
                return;
            }

            var allPatterns = await response.Content.ReadFromJsonAsync<StudioPattern[]>() ?? [];
            var selectedProjectId = (PatternFilterProjectSelector.SelectedItem as StudioProject)?.ProjectId;
            var selectedStatus = SelectedContent(PatternFilterStatusInput, "Todas");
            var patterns = allPatterns
                .Where(pattern => selectedProjectId is null || pattern.ProjectId == selectedProjectId)
                .Where(pattern => selectedStatus == "Todas" || pattern.Status == selectedStatus)
                .ToArray();
            ExistingPatternSelector.ItemsSource = patterns;
            ExistingPatternSelector.SelectedItem = null;
            PatternsListText.Text = patterns.Length == 0
                ? "Nenhum molde registrado ainda."
                : string.Join(Environment.NewLine + Environment.NewLine, patterns.Select(pattern => $"{pattern.Name} · {pattern.Status} · Projeto: {pattern.ProjectName}{(string.IsNullOrWhiteSpace(pattern.Notes) ? string.Empty : Environment.NewLine + pattern.Notes)}"));
            PatternsStatusText.Text = patterns.Length == 0
                ? "Nenhum molde encontrado para os filtros escolhidos."
                : $"{patterns.Length} de {allPatterns.Length} molde(s) exibido(s).";
        }
        catch (HttpRequestException)
        {
            PatternsListText.Text = "A lista de moldes não está disponível.";
            ExistingPatternSelector.ItemsSource = null;
            PatternsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void CreateLibraryItem_Click(object sender, RoutedEventArgs e)
    {
        var name = LibraryNameInput.Text.Trim();
        var localPath = LibraryPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(localPath))
        {
            LibraryStatusText.Text = "Informe o nome e o caminho local da referência.";
            return;
        }

        try
        {
            var projectId = (LibraryProjectSelector.SelectedItem as StudioProject)?.ProjectId;
            var response = await studioClient.PostAsJsonAsync("library", new CreateLibraryItemRequest(name, SelectedContent(LibraryTypeInput, "Referência"), localPath, projectId));
            if (!response.IsSuccessStatusCode)
            {
                LibraryStatusText.Text = $"Não foi possível adicionar a referência (resposta {(int)response.StatusCode}). O caminho talvez já esteja cadastrado.";
                return;
            }

            LibraryNameInput.Clear();
            LibraryPathInput.Clear();
            LibraryTypeInput.SelectedIndex = 0;
            LibraryProjectSelector.SelectedItem = null;
            LibraryStatusText.Text = "Referência adicionada à biblioteca.";
            await RefreshLibraryAsync();
        }
        catch (HttpRequestException)
        {
            LibraryStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void ApplyLibraryFilters_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private async void UpdateLibraryLink_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryItemSelector.SelectedItem is not LibraryItem item)
        {
            LibraryStatusText.Text = "Escolha uma referência antes de alterar o vínculo.";
            return;
        }

        try
        {
            var projectId = (LibraryExistingProjectSelector.SelectedItem as StudioProject)?.ProjectId;
            var response = await studioClient.PutAsJsonAsync($"library/{item.AssetId}", new UpdateLibraryItemRequest(LibraryExistingNameInput.Text.Trim(), SelectedContent(LibraryExistingTypeInput, "Referência"), LibraryExistingPathInput.Text.Trim(), projectId));
            if (!response.IsSuccessStatusCode)
            {
                LibraryStatusText.Text = $"Não foi possível salvar o vínculo (resposta {(int)response.StatusCode}).";
                return;
            }

            LibraryStatusText.Text = "Referência atualizada.";
            await RefreshLibraryAsync();
        }
        catch (HttpRequestException)
        {
            LibraryStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private void LibraryItemSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LibraryItemSelector.SelectedItem is not LibraryItem item) return;
        LibraryExistingNameInput.Text = item.OriginalName;
        LibraryExistingPathInput.Text = item.LocalPath;
        SelectLibraryType(item.ContentType);
        if (LibraryExistingProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            LibraryExistingProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == item.ProjectId);
    }

    private async Task RefreshLibraryAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("library");
            if (!response.IsSuccessStatusCode)
            {
                LibraryListText.Text = "A biblioteca não está disponível.";
                LibraryStatusText.Text = $"O Studio recusou a consulta (resposta {(int)response.StatusCode}).";
                return;
            }

            var allItems = await response.Content.ReadFromJsonAsync<LibraryItem[]>() ?? [];
            var selectedProjectId = (LibraryFilterProjectSelector.SelectedItem as StudioProject)?.ProjectId;
            var selectedType = SelectedContent(LibraryFilterTypeInput, "Todos");
            var items = allItems
                .Where(item => selectedProjectId is null || item.ProjectId == selectedProjectId)
                .Where(item => selectedType == "Todos" || item.ContentType == selectedType)
                .ToArray();
            LibraryItemSelector.ItemsSource = items;
            LibraryItemSelector.SelectedItem = null;
            LibraryListText.Text = items.Length == 0
                ? "Nenhuma referência cadastrada ainda."
                : string.Join(Environment.NewLine, items.Select(item => $"{item.OriginalName} · {item.ContentType} — {item.LocalPath}{(string.IsNullOrWhiteSpace(item.ProjectName) ? " · Biblioteca geral" : $" · Projeto: {item.ProjectName}")}"));
            LibraryStatusText.Text = items.Length == 0
                ? "Nenhuma referência encontrada para os filtros escolhidos."
                : $"{items.Length} de {allItems.Length} referência(s) exibida(s).";
        }
        catch (HttpRequestException)
        {
            LibraryListText.Text = "A biblioteca não está disponível.";
            LibraryItemSelector.ItemsSource = null;
            LibraryStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async Task RefreshLibraryProjectsAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("projects");
            LibraryProjectSelector.ItemsSource = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<StudioProject[]>() ?? []
                : [];
            LibraryProjectSelector.SelectedItem = null;
            LibraryExistingProjectSelector.ItemsSource = LibraryProjectSelector.ItemsSource;
            LibraryExistingProjectSelector.SelectedItem = null;
            LibraryFilterProjectSelector.ItemsSource = LibraryProjectSelector.ItemsSource;
            LibraryFilterProjectSelector.SelectedItem = null;
        }
        catch (HttpRequestException)
        {
            LibraryProjectSelector.ItemsSource = null;
            LibraryExistingProjectSelector.ItemsSource = null;
            LibraryFilterProjectSelector.ItemsSource = null;
        }
    }

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
            await RefreshProjectLinksAsync();
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
            await RefreshProjectLinksAsync();
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
            OrganizationItemSelector.ItemsSource = items;
            OrganizationItemSelector.SelectedItem = null;
            OrganizationListText.Text = items.Length == 0
                ? "Nenhum item interno registrado."
                : string.Join(Environment.NewLine + Environment.NewLine, items.Select(item => $"{item.Title} · {item.Status}{(string.IsNullOrWhiteSpace(item.Notes) ? string.Empty : Environment.NewLine + item.Notes)}"));
        }
        catch (HttpRequestException)
        {
            OrganizationListText.Text = "Os itens internos não estão disponíveis.";
        }
    }

    private async Task RefreshProjectLinksAsync()
    {
        if (currentProjectId is null) return;
        try
        {
            var libraryResponse = await studioClient.GetAsync($"library?projectId={currentProjectId}");
            var patternsResponse = await studioClient.GetAsync($"patterns?projectId={currentProjectId}");
            var library = libraryResponse.IsSuccessStatusCode
                ? await libraryResponse.Content.ReadFromJsonAsync<LibraryItem[]>() ?? []
                : [];
            var patterns = patternsResponse.IsSuccessStatusCode
                ? await patternsResponse.Content.ReadFromJsonAsync<StudioPattern[]>() ?? []
                : [];
            ProjectLibraryListText.Text = library.Length == 0
                ? "Nenhuma referência vinculada."
                : string.Join(Environment.NewLine, library.Select(item => $"{item.OriginalName} · {item.ContentType}"));
            ProjectPatternsListText.Text = patterns.Length == 0
                ? "Nenhum molde vinculado."
                : string.Join(Environment.NewLine, patterns.Select(pattern => $"{pattern.Name} · {pattern.Status}"));
        }
        catch (HttpRequestException)
        {
            ProjectLibraryListText.Text = "Não foi possível consultar as referências vinculadas.";
            ProjectPatternsListText.Text = "Não foi possível consultar os moldes vinculados.";
        }
    }

    private async void UpdateOrganizationItem_Click(object sender, RoutedEventArgs e)
    {
        if (currentProjectId is null) return;
        if (OrganizationItemSelector.SelectedItem is not ProjectOrganizationItem item)
        {
            ProjectsStatusText.Text = "Escolha um item interno antes de atualizar a situação.";
            return;
        }
        var title = OrganizationExistingTitleInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ProjectsStatusText.Text = "Informe o título do item interno.";
            return;
        }

        try
        {
            var response = await studioClient.PutAsJsonAsync($"projects/{currentProjectId}/organization/{item.ItemId}", new UpdateProjectOrganizationItemRequest(title, OrganizationExistingNotesInput.Text.Trim(), SelectedContent(OrganizationExistingStatusInput, "Pendente")));
            if (!response.IsSuccessStatusCode)
            {
                ProjectsStatusText.Text = $"Não foi possível atualizar o item (resposta {(int)response.StatusCode}).";
                return;
            }

            ProjectsStatusText.Text = "Item interno atualizado.";
            await RefreshOrganizationAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private void OrganizationItemSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (OrganizationItemSelector.SelectedItem is not ProjectOrganizationItem item) return;
        OrganizationExistingTitleInput.Text = item.Title;
        OrganizationExistingNotesInput.Text = item.Notes ?? string.Empty;
        OrganizationExistingStatusInput.SelectedIndex = item.Status switch
        {
            "Em andamento" => 1,
            "Concluído" => 2,
            _ => 0
        };
    }

    private void SelectProjectStatus(string status) => ProjectStatusInput.SelectedIndex = status switch
    {
        "Em espera" => 1,
        "Concluído" => 2,
        _ => 0
    };

    private static string SelectedContent(System.Windows.Controls.ComboBox comboBox, string fallback)
        => (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? fallback;

    private void SelectLibraryType(string type) => LibraryExistingTypeInput.SelectedIndex = type switch
    {
        "Imagem" => 1,
        "Documento" => 2,
        "Outro" => 3,
        _ => 0
    };

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
            await RefreshWorkspaceSummaryAsync();
        }
        catch (HttpRequestException)
        {
            StudioStatusText.Text = "API do Studio indisponível.";
            AgentStatusText.Text = "Não foi possível consultar o agente local.";
        }
    }

    private async Task RefreshWorkspaceSummaryAsync()
    {
        try
        {
            var response = await studioClient.GetAsync("dashboard");
            if (!response.IsSuccessStatusCode)
            {
                WorkspaceSummaryText.Text = "Resumo indisponível.";
                return;
            }

            var summary = await response.Content.ReadFromJsonAsync<StudioDashboard>();
            WorkspaceSummaryText.Text = summary is null
                ? "Resumo indisponível."
                : $"Projetos: {summary.Projects} · Referências: {summary.LibraryItems} · Moldes: {summary.Patterns}";
        }
        catch (HttpRequestException)
        {
            WorkspaceSummaryText.Text = "Resumo indisponível.";
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
    private sealed record StudioDashboard(int Projects, int LibraryItems, int Patterns);
    private sealed record CreateProjectRequest(string Name, string LocalPath);
    private sealed record CreateLibraryItemRequest(string Name, string Type, string LocalPath, Guid? ProjectId);
    private sealed record UpdateLibraryItemRequest(string Name, string Type, string LocalPath, Guid? ProjectId);
    private sealed record LibraryItem(Guid AssetId, string OriginalName, string ContentType, string LocalPath, Guid? ProjectId, string? ProjectName, DateTimeOffset CreatedAt);
    private sealed record CreatePatternRequest(Guid ProjectId, string Name, string? Notes, string Status);
    private sealed record StudioPattern(Guid PatternId, string Name, string? Notes, string Status, Guid ProjectId, string ProjectName, DateTimeOffset CreatedAt);
    private sealed record UpdatePatternRequest(Guid ProjectId, string Name, string? Notes, string Status);
    private sealed record StudioProject(Guid ProjectId, string Name, string LocalPath, string? Description, string Status, DateTimeOffset CreatedAt);
    private sealed record UpdateProjectRequest(string Name, string LocalPath, string? Description, string Status);
    private sealed record CreateProjectOrganizationItemRequest(string Title, string? Notes, string Status);
    private sealed record ProjectOrganizationItem(Guid ItemId, string Title, string? Notes, string Status, DateTimeOffset CreatedAt);
    private sealed record UpdateProjectOrganizationItemRequest(string Title, string? Notes, string Status);
}
