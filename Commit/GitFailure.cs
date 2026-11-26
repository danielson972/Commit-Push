namespace Commit;

internal sealed class GitFailure(string operation, int exitCode, string details)
    : Exception($"git {operation} terminou com código {exitCode}.")
{
    internal string TechnicalDetails => string.IsNullOrWhiteSpace(details)
        ? $"{Message}\nO Git não retornou uma mensagem."
        : $"{Message}\n\n{details}";

    internal static bool IsEmptyCommit(string output) =>
        output.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);

    internal (string Title, string Explanation) Describe()
    {
        bool Contains(string text) => details.Contains(text, StringComparison.OrdinalIgnoreCase);

        if (operation == "commit" && IsEmptyCommit(details))
            return ("Sem alterações para commitar",
                "O Git não encontrou alterações preparadas para um novo commit. Para criar outro commit, altere um arquivo rastreado ou verifique se o arquivo novo está no .gitignore.");

        if (Contains("not a git repository"))
            return ("Pasta fora de um repositório Git",
                "Selecione uma pasta dentro do repositório que deseja enviar.");

        if (Contains("unmerged files") || Contains("unmerged paths") || Contains("needs merge"))
            return ("Conflitos de merge pendentes",
                "Resolva os conflitos nos arquivos indicados pelo Git antes de tentar criar o commit novamente.");

        if (Contains("no upstream branch"))
            return ("Branch sem destino de push",
                "A branch local ainda não está vinculada a uma branch remota. Configure o upstream com o comando sugerido pelo Git nos detalhes.");

        if (Contains("no configured push destination") || Contains("does not appear to be a git repository"))
            return ("Destino do push indisponível",
                "Confira se há um remoto configurado e se o endereço está correto.");

        if (Contains("authentication failed") || Contains("permission denied (publickey)") ||
            Contains("could not read username") || Contains("terminal prompts disabled") ||
            Contains("returned error: 403"))
            return ("Falha de autenticação ou acesso",
                "Confira as credenciais usadas pelo Git e se sua conta tem permissão para enviar alterações a esse repositório.");

        if (Contains("non-fast-forward") || Contains("(fetch first)"))
            return ("A branch remota tem alterações pendentes",
                "Integre as alterações da branch remota à sua branch local e tente o push novamente.");

        if (Contains("remote rejected"))
            return ("Push rejeitado pelo servidor",
                "O servidor recusou o envio. Confira nos detalhes a regra da branch ou a validação que impediu o push.");

        if (Contains("index.lock") || Contains("unable to lock"))
            return ("Repositório bloqueado pelo Git",
                "Verifique se outro processo Git está usando o repositório. Aguarde ele terminar e tente novamente.");

        if (Contains("could not resolve host") || Contains("failed to connect") ||
            Contains("connection timed out"))
            return ("Falha de conexão com o remoto",
                "Confira a conexão de rede e o endereço do repositório remoto, depois tente novamente.");

        string title = operation switch
        {
            "rev-parse" => "Não foi possível verificar o repositório",
            "add" => "Não foi possível adicionar as alterações",
            "commit" => "Não foi possível criar o commit",
            "push" => "Não foi possível enviar o commit",
            _ => "Falha ao executar o Git"
        };
        return (title, string.IsNullOrWhiteSpace(details)
            ? "O comando falhou sem informar a causa. Rode o comando indicado nos detalhes em um terminal na pasta do repositório para investigar."
            : "Confira a mensagem original do Git nos detalhes abaixo para identificar a causa.");
    }
}