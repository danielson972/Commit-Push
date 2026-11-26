using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Commit;

public partial class MainWindow : Window
{
    private string? _pendingPush;
    private bool _busy;
    private bool _historyDirty;
    private RepositoryHistory _history = RepositoryHistory.Empty;
    private readonly DispatcherQueueTimer _historyTimer;

    public MainWindow()
    {
        InitializeComponent();
        Root.Loaded += (_, _) => Repository.Focus(FocusState.Programmatic);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 480));
        _historyTimer = DispatcherQueue.CreateTimer();
        _historyTimer.Interval = TimeSpan.FromMilliseconds(400);
        _historyTimer.IsRepeating = false;
        _historyTimer.Tick += (_, _) => SaveHistory();
        LoadHistory();
        Repository.TextChanged += Repository_TextChanged;
        AppWindow.Closing += (_, e) => e.Cancel = _busy;
        Closed += (_, _) =>
        {
            _historyTimer.Stop();
            SaveHistory();
        };
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
                Repository.Text = folder.Path;
        }
        catch (Exception error)
        {
            await Dialogs.ShowErrorAsync(Root.XamlRoot, "Erro ao selecionar o repositório", error.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        SetBusy(true);
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.ShowError = false;
        OperationProgress.ShowPaused = false;
        OperationProgress.Value = _pendingPush is null ? 0 : 3;
        try
        {
            if (_pendingPush is null)
            {
                string path = Repository.Text.Trim().Trim('"');
                if (!Directory.Exists(path))
                    throw new InvalidOperationException("Informe o caminho de uma pasta existente.");

                string message = Message.Text.Trim();
                if (message.Length == 0)
                    throw new InvalidOperationException("Informe a mensagem do commit.");

                ReportProgress(0, "Etapa 1 de 4 · Verificando o repositório…");
                string root = (await Git.RunAsync(Path.GetFullPath(path), "rev-parse", "--show-toplevel"))
                    .RequireSuccess().TrimEnd('\r', '\n');
                _history = _history.WithRepository(root);
                _historyDirty = true;
                SaveHistory();
                RefreshHistory();
                if (!await CreateCommitAsync(root, message))
                    return;

                _pendingPush = root;
            }

            ReportProgress(3, "Etapa 4 de 4 · Enviando commit…");
            (await Git.RunAsync(_pendingPush, "push")).RequireSuccess();
            _pendingPush = null;
            Message.Text = string.Empty;
            ReportProgress(4, "Commit enviado com sucesso.");
        }
        catch (Exception error)
        {
            OperationProgress.ShowPaused = false;
            OperationProgress.ShowError = true;
            bool pushFailed = _pendingPush is not null;
            Status.Text = pushFailed ? "O push falhou. Os commits continuam locais." : "Operação interrompida.";
            if (error is GitFailure failure)
            {
                var (title, explanation) = failure.Describe();
                if (pushFailed)
                    explanation = "Os commits continuam no repositório local. " + explanation;

                await Dialogs.ShowErrorAsync(Root.XamlRoot, title, explanation, failure.TechnicalDetails);
            }
            else
            {
                await Dialogs.ShowErrorAsync(Root.XamlRoot, "Operação interrompida", error.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> CreateCommitAsync(string root, string message)
    {
        ReportProgress(1, "Etapa 2 de 4 · Adicionando alterações…");
        (await Git.RunAsync(root, "add", ".")).RequireSuccess();

        while (true)
        {
            OperationProgress.ShowPaused = false;
            ReportProgress(2, "Etapa 3 de 4 · Criando commit…");
            var commit = await Git.RunAsync(root, "commit", "-m", message);
            if (commit.ExitCode == 0)
                return true;

            if (GitFailure.IsEmptyCommit(commit.Details))
            {
                var ahead = await Git.RunAsync(root, "rev-list", "--count", "@{push}..HEAD", "--");
                if (ahead.ExitCode == 0 && int.TryParse(ahead.Output.Trim(), out int pendingCount) && pendingCount > 0)
                {
                    _pendingPush = root;
                    OperationProgress.ShowPaused = true;
                    ReportProgress(3, "Há commits locais aguardando envio.");
                    string pending = pendingCount == 1 ? "1 commit local aguarda envio" : $"{pendingCount} commits locais aguardam envio";
                    await Dialogs.ShowErrorAsync(Root.XamlRoot, "Envio pendente",
                        $"O Git não encontrou alterações para um novo commit. {pending}. Feche este aviso e clique em Push para enviar.",
                        commit.Details);
                    return false;
                }

                commit.RequireSuccess();
            }

            var author = await Git.RunAsync(root, "var", "GIT_AUTHOR_IDENT");
            var committer = await Git.RunAsync(root, "var", "GIT_COMMITTER_IDENT");
            if (author.ExitCode == 0 && committer.ExitCode == 0)
                commit.RequireSuccess();

            OperationProgress.ShowPaused = true;
            Status.Text = "Configure a identidade Git para continuar.";
            var name = await Git.RunAsync(root, "config", "--get", "user.name");
            var email = await Git.RunAsync(root, "config", "--get", "user.email");
            if (!await Dialogs.EditIdentityAsync(Root.XamlRoot, root, name.Output.Trim(), email.Output.Trim(), commit.Details))
            {
                Status.Text = "Commit interrompido.";
                return false;
            }
        }
    }

    private void LoadHistory()
    {
        try
        {
            _history = RepositoryHistory.Load();
            Repository.Text = _history.LastPath;
        }
        catch (Exception error)
        {
            Status.Text = $"Não foi possível ler o histórico. {error.Message}";
        }
        RefreshHistory();
    }

    private void SaveHistory()
    {
        if (!_historyDirty)
            return;

        try
        {
            _history.Save();
            _historyDirty = false;
        }
        catch (Exception error)
        {
            Status.Text = $"Não foi possível salvar o histórico. {error.Message}";
        }
    }

    private void Repository_TextChanged(object sender, TextChangedEventArgs e)
    {
        _history = _history with { LastPath = Repository.Text };
        _historyDirty = true;
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private void RefreshHistory()
    {
        HistoryMenu.Items.Clear();
        foreach (string path in _history.Paths)
        {
            var item = new MenuFlyoutItem { Text = path };
            item.Click += (_, _) => Repository.Text = path;
            HistoryMenu.Items.Add(item);
        }

        if (_history.Paths.Length > 0)
            HistoryMenu.Items.Add(new MenuFlyoutSeparator());
        else
            HistoryMenu.Items.Add(new MenuFlyoutItem { Text = "Nenhum repositório no histórico", IsEnabled = false });

        var clear = new MenuFlyoutItem { Text = "Limpar histórico" };
        clear.Click += ClearHistory_Click;
        HistoryMenu.Items.Add(clear);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _historyTimer.Stop();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Limpar histórico?",
                Content = "Deseja realmente apagar os caminhos do histórico e esquecer o último caminho salvo?",
                PrimaryButtonText = "Apagar histórico",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            RepositoryHistory.Clear();
            _history = RepositoryHistory.Empty;
            _historyDirty = false;
            RefreshHistory();
        }
        catch (Exception error)
        {
            await Dialogs.ShowErrorAsync(Root.XamlRoot, "Erro ao limpar histórico", error.Message);
        }
        finally
        {
            if (_historyDirty)
                _historyTimer.Start();
        }
    }


    private void ReportProgress(int completedSteps, string status)
    {
        OperationProgress.Value = completedSteps;
        Status.Text = status;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Submit.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        Browse.IsEnabled = !busy && _pendingPush is null;
        HistoryButton.IsEnabled = Browse.IsEnabled;
        Repository.IsReadOnly = !Browse.IsEnabled;
        Message.IsReadOnly = !Browse.IsEnabled;
        Submit.Content = _pendingPush is null ? "Commit" : "Push";
    }
}