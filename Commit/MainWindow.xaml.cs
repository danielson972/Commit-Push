using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Commit;

public partial class MainWindow : Window
{
    private string? _pendingPush;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Root.Loaded += (_, _) => Repository.Focus(FocusState.Programmatic);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 480));
        AppWindow.Closing += (_, e) => e.Cancel = _busy;
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

                Status.Text = "Verificando o repositório…";
                string root = (await Git.RunAsync(Path.GetFullPath(path), "rev-parse", "--show-toplevel"))
                    .RequireSuccess().TrimEnd('\r', '\n');
                if (!await CreateCommitAsync(root, message))
                    return;

                _pendingPush = root;
            }

            Status.Text = "Enviando commit…";
            (await Git.RunAsync(_pendingPush, "push")).RequireSuccess();
            _pendingPush = null;
            Message.Text = string.Empty;
            Status.Text = "Commit enviado com sucesso.";
        }
        catch (Exception error)
        {
            bool pushFailed = _pendingPush is not null;
            Status.Text = pushFailed ? "Commit criado. O push falhou." : "Operação interrompida.";
            await Dialogs.ShowErrorAsync(Root.XamlRoot, pushFailed ? "Erro ao enviar commit" : "Erro", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> CreateCommitAsync(string root, string message)
    {
        Status.Text = "Adicionando alterações…";
        (await Git.RunAsync(root, "add", ".")).RequireSuccess();

        while (true)
        {
            Status.Text = "Criando commit…";
            var commit = await Git.RunAsync(root, "commit", "-m", message);
            if (commit.ExitCode == 0)
                return true;

            var author = await Git.RunAsync(root, "var", "GIT_AUTHOR_IDENT");
            var committer = await Git.RunAsync(root, "var", "GIT_COMMITTER_IDENT");
            if (author.ExitCode == 0 && committer.ExitCode == 0)
                commit.RequireSuccess();

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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Submit.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        Browse.IsEnabled = !busy && _pendingPush is null;
        Repository.IsReadOnly = !Browse.IsEnabled;
        Message.IsReadOnly = !Browse.IsEnabled;
        Submit.Content = _pendingPush is null ? "Commit" : "Tentar push novamente";
    }
}