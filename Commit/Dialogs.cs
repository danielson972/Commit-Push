using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Commit;

internal static class Dialogs
{
    public static async Task ShowErrorAsync(XamlRoot root, string title, string explanation, string? technicalDetails = null)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(DetailsBox(explanation));
        if (technicalDetails is not null)
        {
            content.Children.Add(new TextBlock { Text = "Detalhes técnicos" });
            content.Children.Add(DetailsBox(technicalDetails));
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    public static async Task<bool> EditIdentityAsync(XamlRoot root, string repository,
        string currentName, string currentEmail, string details)
    {
        var name = new TextBox { Header = "Nome do autor", Text = currentName };
        var email = new TextBox { Header = "E-mail do autor", Text = currentEmail };
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Os dados serão salvos apenas neste repositório.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(name);
        panel.Children.Add(email);
        panel.Children.Add(DetailsBox(details));
        panel.Children.Add(validation);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Identidade Git do repositório",
            Content = panel,
            PrimaryButtonText = "Salvar e tentar novamente",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        bool saving = false;
        dialog.Closing += (_, e) => e.Cancel = saving;
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            string author = name.Text.Trim();
            string address = email.Text.Trim();
            if (author.Length == 0 || address.Length == 0)
            {
                validation.Text = "Preencha o nome e o e-mail do autor.";
                e.Cancel = true;
                return;
            }

            var deferral = e.GetDeferral();
            saving = true;
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                (await Git.RunAsync(repository, "config", "--local", "--", "user.name", author)).RequireSuccess();
                (await Git.RunAsync(repository, "config", "--local", "--", "user.email", address)).RequireSuccess();
            }
            catch (Exception error)
            {
                validation.Text = error.Message;
                e.Cancel = true;
            }
            finally
            {
                saving = false;
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static ScrollViewer DetailsBox(string details) => new()
    {
        MaxHeight = 155,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = new TextBlock
        {
            Text = details,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        }
    };
}