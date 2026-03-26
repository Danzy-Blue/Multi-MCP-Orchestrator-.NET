namespace McpGateway;

public sealed class McpPromptRegistry
{
    private readonly Dictionary<string, RegisteredPrompt> _prompts = new(StringComparer.Ordinal);

    public void Extend(IEnumerable<RegisteredPrompt> prompts)
    {
        foreach (var prompt in prompts)
        {
            if (_prompts.TryGetValue(prompt.Name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate prompt name '{prompt.Name}' exposed by both '{existing.ServerAlias}' and '{prompt.ServerAlias}'.");
            }

            _prompts[prompt.Name] = prompt;
        }
    }

    public RegisteredPrompt? Get(string name) => _prompts.GetValueOrDefault(name);

    public IReadOnlyList<RegisteredPrompt> All() =>
        _prompts.Values
            .OrderBy(prompt => prompt.Name, StringComparer.Ordinal)
            .ToArray();

    public void Clear() => _prompts.Clear();

    public int Count => _prompts.Count;
}
