using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MDS.Studio.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient coreClient = new();
    private readonly HttpClient studioClient = new();
    private string? sessionToken;
    private string? currentUsername;
    private Guid? currentProjectId;
    private Guid? preferredLibraryItemId;
    private const string SavedCoreCredentialName = "MDSStudio:CoreLogin";

    public MainWindow()
    {
        InitializeComponent();
        LoadSavedCredentials();
    }

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
            if (RememberCredentialsCheckBox.IsChecked == true)
                SaveCredentials(UsernameInput.Text.Trim(), PasswordInput.Password);
            else
                DeleteSavedCredentials();
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
            SettingsPanel.Visibility = Visibility.Collapsed;
            currentProjectId = null;
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
            LoginPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Sessão encerrada.";
        }
    }

    private void ForgetSavedCredentials_Click(object sender, RoutedEventArgs e)
    {
        DeleteSavedCredentials();
        UsernameInput.Clear();
        PasswordInput.Clear();
        RememberCredentialsCheckBox.IsChecked = false;
        StatusText.Text = "O acesso salvo foi removido deste computador.";
    }

    private void LoadSavedCredentials()
    {
        if (!CredRead(SavedCoreCredentialName, CredentialTypeGeneric, 0, out var credentialPointer)) return;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            UsernameInput.Text = credential.UserName ?? string.Empty;
            PasswordInput.Password = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char)) ?? string.Empty;
            RememberCredentialsCheckBox.IsChecked = true;
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void SaveCredentials(string username, string password)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var passwordPointer = Marshal.AllocCoTaskMem(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, passwordPointer, passwordBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = SavedCoreCredentialName,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = passwordPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = username
            };

            CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(passwordPointer);
        }
    }

    private static void DeleteSavedCredentials() => CredDelete(SavedCoreCredentialName, CredentialTypeGeneric, 0);

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

    private async void Home_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        HomePanel.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(sessionToken)) await RefreshHomeProjectsAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        RefreshSettings();
    }

    private void RefreshSettings_Click(object sender, RoutedEventArgs e) => RefreshSettings();

    private void RefreshSettings()
    {
        SettingsCoreUrlText.Text = CoreUrlInput.Text.Trim();
        SettingsStudioUrlText.Text = StudioUrlInput.Text.Trim();
        SettingsStatusText.Text = string.IsNullOrWhiteSpace(sessionToken)
            ? "Sessão não iniciada. Entre pelo início para usar o MDS Core."
            : $"Sessão atual: {currentUsername ?? "usuário identificado"}.";
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
        SettingsPanel.Visibility = Visibility.Collapsed;
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
        SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Visible;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        await RefreshProjectsAsync();
    }

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e) => await RefreshProjectsAsync();

    private async void OpenWaitingProjects_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return;
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Visible;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        currentProjectId = null;
        ProjectSearchInput.Clear();
        ProjectFilterStatusInput.SelectedIndex = 2;
        await RefreshProjectsAsync();
    }

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
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        PatternsPanel.Visibility = Visibility.Collapsed;
        await RefreshLibraryProjectsAsync();
        await RefreshLibraryAsync();
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private async void OpenProjectLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (currentProjectId is null) return;
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        await RefreshLibraryProjectsAsync();
        if (LibraryFilterProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            LibraryFilterProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == currentProjectId);
        LibraryFilterTypeInput.SelectedIndex = 0;
        await RefreshLibraryAsync();
    }

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
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Visible;
        await RefreshPatternProjectsAsync();
        await RefreshPatternsAsync();
    }

    private async void RefreshPatterns_Click(object sender, RoutedEventArgs e) => await RefreshPatternsAsync();

    private async void OpenReviewPatterns_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return;
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Visible;
        await RefreshPatternProjectsAsync();
        PatternFilterProjectSelector.SelectedItem = null;
        PatternFilterStatusInput.SelectedIndex = 2;
        await RefreshPatternsAsync();
    }

    private async void OpenProjectPatterns_Click(object sender, RoutedEventArgs e)
    {
        if (currentProjectId is null) return;
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Visible;
        await RefreshPatternProjectsAsync();
        if (PatternFilterProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            PatternFilterProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == currentProjectId);
        PatternFilterStatusInput.SelectedIndex = 0;
        await RefreshPatternsAsync();
    }

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
            var response = await studioClient.PutAsJsonAsync($"patterns/{pattern.PatternId}", new UpdatePatternRequest(project.ProjectId, name, ExistingPatternNotesInput.Text.Trim(), ExistingPatternTechnicalFilePathInput.Text.Trim(), ExistingPatternTechnicalVersionInput.Text.Trim(), SelectedContent(ExistingPatternStatusInput, "Rascunho")));
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

    private async void ExistingPatternSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ExistingPatternSelector.SelectedItem is not StudioPattern pattern) return;
        ExistingPatternNameInput.Text = pattern.Name;
        ExistingPatternNotesInput.Text = pattern.Notes ?? string.Empty;
        ExistingPatternTechnicalFilePathInput.Text = pattern.TechnicalFilePath ?? string.Empty;
        ExistingPatternTechnicalVersionInput.Text = pattern.TechnicalVersion ?? string.Empty;
        ExistingPatternStatusInput.SelectedIndex = pattern.Status switch
        {
            "Em revisão" => 1,
            "Aprovado" => 2,
            _ => 0
        };
        if (ExistingPatternProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            ExistingPatternProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == pattern.ProjectId);
        await RefreshPatternTechnicalHistoryAsync(pattern.PatternId);
    }

    private void OpenPatternFile_Click(object sender, RoutedEventArgs e)
        => OpenLocalFile(ExistingPatternTechnicalFilePathInput.Text, "arquivo técnico", PatternsStatusText);

    private void ChoosePatternTechnicalFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher arquivo técnico do molde",
            Filter = "Arquivos de molde|*.dst;*.pes;*.exp;*.jef;*.vp3|Todos os arquivos|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            ExistingPatternTechnicalFilePathInput.Text = dialog.FileName;
    }

    private async Task RefreshPatternTechnicalHistoryAsync(Guid patternId)
    {
        try
        {
            var response = await studioClient.GetAsync($"patterns/{patternId}/technical-revisions");
            if (!response.IsSuccessStatusCode)
            {
                PatternTechnicalHistoryText.Text = "O histórico técnico não está disponível.";
                return;
            }
            var revisions = await response.Content.ReadFromJsonAsync<PatternTechnicalRevision[]>() ?? [];
            PatternTechnicalHistoryText.Text = revisions.Length == 0
                ? "Nenhuma versão técnica registrada ainda. Salve o molde para registrar a versão atual."
                : string.Join(Environment.NewLine, revisions.Select(revision => $"{revision.Version} · {revision.FilePath} · registrado em {revision.RecordedAt.LocalDateTime:g}"));
        }
        catch (HttpRequestException)
        {
            PatternTechnicalHistoryText.Text = "Não foi possível consultar o histórico técnico.";
        }
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
            var searchTerm = PatternSearchInput.Text.Trim();
            var patterns = allPatterns
                .Where(pattern => string.IsNullOrWhiteSpace(searchTerm) || pattern.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Where(pattern => selectedProjectId is null || pattern.ProjectId == selectedProjectId)
                .Where(pattern => selectedStatus == "Todas" || pattern.Status == selectedStatus)
                .ToArray();
            ExistingPatternSelector.ItemsSource = patterns;
            ExistingPatternSelector.SelectedItem = null;
            PatternsListText.Text = patterns.Length == 0
                ? "Nenhum molde registrado ainda."
                : string.Join(Environment.NewLine + Environment.NewLine, patterns.Select(pattern => $"{pattern.Name} · {pattern.Status} · Projeto: {pattern.ProjectName}{(string.IsNullOrWhiteSpace(pattern.TechnicalFilePath) ? string.Empty : Environment.NewLine + $"Arquivo técnico: {pattern.TechnicalFilePath}{(string.IsNullOrWhiteSpace(pattern.TechnicalVersion) ? string.Empty : $" · Versão: {pattern.TechnicalVersion}")}")}{(string.IsNullOrWhiteSpace(pattern.Notes) ? string.Empty : Environment.NewLine + pattern.Notes)}"));
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
            preferredLibraryItemId = item.AssetId;
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
        RefreshLibraryPreview(item);
        if (LibraryExistingProjectSelector.ItemsSource is IEnumerable<StudioProject> projects)
            LibraryExistingProjectSelector.SelectedItem = projects.FirstOrDefault(project => project.ProjectId == item.ProjectId);
    }

    private void RefreshLibraryPreview(LibraryItem item)
    {
        LibraryPreviewPanel.Visibility = Visibility.Collapsed;
        LibraryImagePreview.Source = null;
        if (!string.Equals(item.ContentType, "Imagem", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(item.LocalPath))
        {
            LibraryPreviewCaptionText.Text = "A imagem não foi encontrada no caminho local registrado.";
            LibraryPreviewPanel.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(item.LocalPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            LibraryImagePreview.Source = image;
            LibraryPreviewCaptionText.Text = item.OriginalName;
            LibraryPreviewPanel.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            LibraryPreviewCaptionText.Text = "Não foi possível gerar a prévia desta imagem.";
            LibraryPreviewPanel.Visibility = Visibility.Visible;
        }
    }

    private void OpenLibraryFile_Click(object sender, RoutedEventArgs e)
        => OpenLocalFile(LibraryExistingPathInput.Text, "referência", LibraryStatusText);

    private void ChooseNewLibraryImage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryChooseImage(out var path)) return;
        LibraryPathInput.Text = path;
        LibraryTypeInput.SelectedIndex = 1;
    }

    private void ChooseExistingLibraryImage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryChooseImage(out var path)) return;
        LibraryExistingPathInput.Text = path;
        LibraryExistingTypeInput.SelectedIndex = 1;
    }

    private static bool TryChooseImage(out string path)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            path = dialog.FileName;
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static void OpenLocalFile(string filePath, string label, System.Windows.Controls.TextBlock statusText)
    {
        var path = filePath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            statusText.Text = $"Informe o caminho local da {label}.";
            return;
        }

        if (!File.Exists(path))
        {
            statusText.Text = $"O arquivo da {label} não foi encontrado no caminho informado.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            statusText.Text = $"Abrindo {label} no programa padrão do Windows.";
        }
        catch (Exception)
        {
            statusText.Text = $"Não foi possível abrir a {label} no programa padrão do Windows.";
        }
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
            var searchTerm = LibrarySearchInput.Text.Trim();
            var items = allItems
                .Where(item => string.IsNullOrWhiteSpace(searchTerm) || item.OriginalName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Where(item => selectedProjectId is null || item.ProjectId == selectedProjectId)
                .Where(item => selectedType == "Todos" || item.ContentType == selectedType)
                .ToArray();
            LibraryPreviewPanel.Visibility = Visibility.Collapsed;
            LibraryImagePreview.Source = null;
            LibraryItemSelector.ItemsSource = items;
            var preferredItem = preferredLibraryItemId is Guid preferredId
                ? items.FirstOrDefault(candidate => candidate.AssetId == preferredId)
                : null;
            LibraryItemSelector.SelectedItem = preferredItem;
            preferredLibraryItemId = null;
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
            LibraryPreviewPanel.Visibility = Visibility.Collapsed;
            LibraryImagePreview.Source = null;
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

    private async void ApplyProjectFilter_Click(object sender, RoutedEventArgs e) => await RefreshProjectsAsync();

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

            var allProjects = await response.Content.ReadFromJsonAsync<StudioProject[]>() ?? [];
            var selectedStatus = SelectedContent(ProjectFilterStatusInput, "Todas");
            var searchTerm = ProjectSearchInput.Text.Trim();
            var projects = allProjects
                .Where(project => string.IsNullOrWhiteSpace(searchTerm) || project.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Where(project => selectedStatus == "Todas" || project.Status == selectedStatus)
                .ToArray();
            ProjectSelector.ItemsSource = projects;
            ProjectSelector.SelectedItem = null;
            ProjectsStatusText.Text = projects.Length == 0
                ? "Nenhum projeto encontrado para o filtro escolhido."
                : $"{projects.Length} de {allProjects.Length} projeto(s) exibido(s).";
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

        await OpenProjectDetailsAsync(selected);
    }

    private async Task OpenProjectDetailsAsync(StudioProject selected)
    {
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
            await RefreshProjectVisualReferencesAsync();
            await RefreshOrganizationAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private async void OpenHomeProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StudioProject project }) return;
        HomePanel.Visibility = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PatternsPanel.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Visible;
        ProjectDetailsPanel.Visibility = Visibility.Collapsed;
        ProjectSelector.ItemsSource = new[] { project };
        ProjectSelector.SelectedItem = project;
        await OpenProjectDetailsAsync(project);
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
            await RefreshProjectVisualReferencesAsync();
        }
        catch (HttpRequestException)
        {
            ProjectsStatusText.Text = "Não foi possível alcançar a API do Studio.";
        }
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = ProjectDetailsPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ProjectsStatusText.Text = "A pasta local do projeto não foi encontrada no caminho informado.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            ProjectsStatusText.Text = "Abrindo a pasta do projeto no Explorador de Arquivos.";
        }
        catch (Exception)
        {
            ProjectsStatusText.Text = "Não foi possível abrir a pasta do projeto no Explorador de Arquivos.";
        }
    }

    private void ChooseNewProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseProjectFolder(out var folder)) ProjectPathInput.Text = folder;
    }

    private void ChooseExistingProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseProjectFolder(out var folder)) ProjectDetailsPathInput.Text = folder;
    }

    private static bool TryChooseProjectFolder(out string folder)
    {
        var dialog = new OpenFolderDialog { Title = "Escolher pasta do projeto", Multiselect = false };
        if (dialog.ShowDialog() == true)
        {
            folder = dialog.FolderName;
            return true;
        }

        folder = string.Empty;
        return false;
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
                ProjectProgressText.Text = "Não foi possível calcular o progresso dos itens internos.";
                return;
            }

            var items = await response.Content.ReadFromJsonAsync<ProjectOrganizationItem[]>() ?? [];
            OrganizationItemSelector.ItemsSource = items;
            OrganizationItemSelector.SelectedItem = null;
            OrganizationListText.Text = items.Length == 0
                ? "Nenhum item interno registrado."
                : string.Join(Environment.NewLine + Environment.NewLine, items.Select(item => $"{item.Title} · {item.Status}{(string.IsNullOrWhiteSpace(item.Notes) ? string.Empty : Environment.NewLine + item.Notes)}"));
            var pending = items.Count(item => string.Equals(item.Status, "Pendente", StringComparison.OrdinalIgnoreCase));
            var inProgress = items.Count(item => string.Equals(item.Status, "Em andamento", StringComparison.OrdinalIgnoreCase));
            var completed = items.Count(item => string.Equals(item.Status, "Concluído", StringComparison.OrdinalIgnoreCase));
            ProjectProgressText.Text = items.Length == 0
                ? "Ainda não há etapas internas registradas neste projeto."
                : $"{completed} concluído(s) · {inProgress} em andamento · {pending} pendente(s)";
        }
        catch (HttpRequestException)
        {
            OrganizationListText.Text = "Os itens internos não estão disponíveis.";
            ProjectProgressText.Text = "Não foi possível calcular o progresso dos itens internos.";
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

    private async Task RefreshProjectVisualReferencesAsync()
    {
        if (currentProjectId is null) return;
        ProjectVisualReferencesPanel.Children.Clear();
        ProjectVisualReferencesStatusText.Text = string.Empty;

        try
        {
            var response = await studioClient.GetAsync("library");
            if (!response.IsSuccessStatusCode)
            {
                ProjectVisualReferencesStatusText.Text = "Não foi possível consultar as referências visuais.";
                return;
            }

            var projectPath = ProjectDetailsPathInput.Text.Trim().TrimEnd('\\') + "\\";
            var visualReferences = (await response.Content.ReadFromJsonAsync<LibraryItem[]>() ?? [])
                .Where(item => string.Equals(item.ContentType, "Imagem", StringComparison.OrdinalIgnoreCase))
                .Where(item => item.ProjectId == currentProjectId || item.LocalPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var shown = 0;
            foreach (var reference in visualReferences)
            {
                if (!File.Exists(reference.LocalPath)) continue;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(reference.LocalPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var card = new Border
                    {
                        Width = 180,
                        Background = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 242)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 0, 12, 12)
                    };
                    var content = new StackPanel();
                    content.Children.Add(new Image { Source = bitmap, Height = 120, Stretch = Stretch.Uniform });
                    content.Children.Add(new TextBlock { Text = reference.OriginalName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0), FontWeight = FontWeights.SemiBold });
                    var openImageButton = new Button
                    {
                        Content = content,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        ToolTip = "Clique para ampliar a imagem"
                    };
                    openImageButton.Click += (_, _) => ShowVisualReference(reference.OriginalName, bitmap);
                    card.Child = openImageButton;
                    ProjectVisualReferencesPanel.Children.Add(card);
                    shown++;
                }
                catch (Exception) { }
            }

            ProjectVisualReferencesStatusText.Text = shown > 0
                ? $"{shown} referência(s) visual(is) disponível(is) para consulta."
                : "Nenhuma imagem disponível para este projeto. Vincule uma imagem na Biblioteca ou registre uma imagem dentro da pasta do projeto.";
        }
        catch (HttpRequestException)
        {
            ProjectVisualReferencesStatusText.Text = "Não foi possível alcançar a API do Studio para carregar as referências visuais.";
        }
    }

    private void ShowVisualReference(string title, BitmapImage image)
    {
        var preview = new Window
        {
            Title = title,
            Owner = this,
            Width = 900,
            Height = 700,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Content = new ScrollViewer
            {
                Padding = new Thickness(20),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = image, Stretch = Stretch.Uniform }
            }
        };

        preview.ShowDialog();
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
            await RefreshHomeProjectsAsync();
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

    private async Task RefreshHomeProjectsAsync()
    {
        HomeProjectCardsPanel.Children.Clear();
        try
        {
            var projectsTask = studioClient.GetAsync("projects");
            var patternsTask = studioClient.GetAsync("patterns");
            await Task.WhenAll(projectsTask, patternsTask);
            var response = await projectsTask;
            var patternsResponse = await patternsTask;
            if (!response.IsSuccessStatusCode)
            {
                HomeProjectsStatusText.Text = "Projetos indisponíveis no momento.";
                HomeAttentionText.Text = "Não foi possível carregar os itens que precisam de acompanhamento.";
                return;
            }

            var projects = await response.Content.ReadFromJsonAsync<StudioProject[]>() ?? [];
            var patterns = patternsResponse.IsSuccessStatusCode
                ? await patternsResponse.Content.ReadFromJsonAsync<StudioPattern[]>() ?? []
                : [];
            var waitingProjects = projects.Count(project => string.Equals(project.Status, "Em espera", StringComparison.OrdinalIgnoreCase));
            var reviewingPatterns = patterns.Count(pattern => string.Equals(pattern.Status, "Em revisão", StringComparison.OrdinalIgnoreCase));
            HomeAttentionText.Text = waitingProjects == 0 && reviewingPatterns == 0
                ? "Nenhum projeto em espera ou molde em revisão no momento."
                : $"Projetos em espera: {waitingProjects} · Moldes em revisão: {reviewingPatterns}";
            foreach (var project in projects.Take(6))
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = project.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = project.Status, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), Margin = new Thickness(0, 5, 0, 0) });
                var button = new Button
                {
                    Tag = project,
                    Content = content,
                    Width = 185,
                    Background = Brushes.White,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(13),
                    Margin = new Thickness(0, 0, 12, 12),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    ToolTip = "Abrir detalhes do projeto"
                };
                button.Click += OpenHomeProject_Click;
                HomeProjectCardsPanel.Children.Add(button);
            }

            HomeProjectsStatusText.Text = projects.Length == 0
                ? "Nenhum projeto cadastrado ainda."
                : "Clique em um projeto para abrir seus detalhes.";
        }
        catch (HttpRequestException)
        {
            HomeProjectsStatusText.Text = "Projetos indisponíveis no momento.";
            HomeAttentionText.Text = "Não foi possível carregar os itens que precisam de acompanhamento.";
        }
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        SignInButton.Content = busy ? "Entrando..." : "Entrar";
    }

    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPointer);

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
    private sealed record StudioPattern(Guid PatternId, string Name, string? Notes, string? TechnicalFilePath, string? TechnicalVersion, string Status, Guid ProjectId, string ProjectName, DateTimeOffset CreatedAt);
    private sealed record UpdatePatternRequest(Guid ProjectId, string Name, string? Notes, string? TechnicalFilePath, string? TechnicalVersion, string Status);
    private sealed record PatternTechnicalRevision(Guid RevisionId, string FilePath, string Version, DateTimeOffset RecordedAt);
    private sealed record StudioProject(Guid ProjectId, string Name, string LocalPath, string? Description, string Status, DateTimeOffset CreatedAt);
    private sealed record UpdateProjectRequest(string Name, string LocalPath, string? Description, string Status);
    private sealed record CreateProjectOrganizationItemRequest(string Title, string? Notes, string Status);
    private sealed record ProjectOrganizationItem(Guid ItemId, string Title, string? Notes, string Status, DateTimeOffset CreatedAt);
    private sealed record UpdateProjectOrganizationItemRequest(string Title, string? Notes, string Status);
}
